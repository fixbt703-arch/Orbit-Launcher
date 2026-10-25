package org.orbitlauncher.client;

import com.mojang.blaze3d.pipeline.RenderTarget;
import com.mojang.blaze3d.buffers.GpuBuffer;
import com.mojang.blaze3d.platform.NativeImage;
import com.mojang.blaze3d.systems.RenderSystem;
import com.mojang.blaze3d.textures.GpuTextureView;
import java.nio.file.Files;
import java.util.concurrent.CompletableFuture;
import net.minecraft.client.Minecraft;
import net.minecraft.client.Screenshot;

/** Development-only GPU readback compares the same frame before/after the border pass. */
final class GlowMaskVerification {
    static boolean requested;
    static boolean expectOccluded;
    static int minimumCoverage=1000;
    static volatile int passed,occlusionPassed;
    private static volatile Throwable failure;
    record Capture(CompletableFuture<NativeImage> before,CompletableFuture<NativeImage> mask,float extent,boolean occluded,int minimum){}
    static Capture before(GpuTextureView destination,RenderTarget mask,float extent){
        var mc=Minecraft.getInstance();
        if(!requested||!Boolean.getBoolean("orbit.client.glow-smoke")||RenderSystem.getProjectionType()!=com.mojang.blaze3d.ProjectionType.PERSPECTIVE
            ||destination.getWidth(0)<400||destination.getHeight(0)<300)return null;
        requested=false;
        return new Capture(read(new ViewTarget(destination)),readMask(mask),extent,expectOccluded,minimumCoverage);
    }
    private static CompletableFuture<NativeImage> read(RenderTarget target){
        var result=new CompletableFuture<NativeImage>();
        Screenshot.takeScreenshot(target,result::complete);return result;
    }
    private static CompletableFuture<NativeImage> readMask(RenderTarget target){
        var result=new CompletableFuture<NativeImage>();int width=target.width,height=target.height;
        int pixelBytes=target.getColorTexture().getFormat().blockSize();
        var buffer=RenderSystem.getDevice().createBuffer(()->"Orbit mask verification",GpuBuffer.USAGE_MAP_READ|GpuBuffer.USAGE_COPY_DST,(long)width*height*pixelBytes);
        RenderSystem.getDevice().createCommandEncoder().copyTextureToBuffer(target.getColorTexture(),buffer,0,()->{
            try(var mapped=buffer.map(true,false)){
                var image=new NativeImage(width,height,false);
                var bytes=mapped.data().order(java.nio.ByteOrder.nativeOrder());
                for(int y=0;y<height;y++)for(int x=0;x<width;x++){
                    int offset=(y*width+x)*pixelBytes;
                    if(pixelBytes==4)image.setPixelABGR(x,height-y-1,bytes.getInt(offset));
                    else {
                        int color=0;for(int c=0;c<4;c++)color|=Math.round(Math.clamp(Float.float16ToFloat(bytes.getShort(offset+c*2)),0,1)*255)<<(c*8);
                        image.setPixelABGR(x,height-y-1,color);
                    }
                }
                result.complete(image);
            }catch(Throwable error){result.completeExceptionally(error);}finally{buffer.close();}
        },0);return result;
    }
    static void after(Capture capture,GpuTextureView destination,RenderTarget horizontal){
        if(capture==null)return;
        var after=read(new ViewTarget(destination));
        var distance=readMask(horizontal);
        CompletableFuture.allOf(capture.before,capture.mask,after,distance).thenRun(()->{
            try(var before=capture.before.join();var mask=capture.mask.join();var result=after.join();var field=distance.join()){
                var directory=Minecraft.getInstance().gameDirectory.toPath().resolve("screenshots");Files.createDirectories(directory);
                String prefix=capture.occluded?"perimeter-occluded":"perimeter-"+(passed+1);
                before.writeToFile(directory.resolve(prefix+"-before.png"));mask.writeToFile(directory.resolve(prefix+"-mask.png"));result.writeToFile(directory.resolve(prefix+"-after.png"));field.writeToFile(directory.resolve(prefix+"-distance.png"));
                int changed=0,covered=0;
                for(int y=0;y<mask.getHeight();y++)for(int x=0;x<mask.getWidth();x++){
                    boolean inside=(mask.getPixel(x,y)>>>24)!=0;
                    if(inside)covered++;
                    if(before.getPixel(x,y)==result.getPixel(x,y))continue;
                    if(inside)throw new AssertionError("Outline changed model interior at "+x+","+y);
                    boolean near=false;int radius=(int)Math.ceil(capture.extent);
                    for(int yy=Math.max(0,y-radius);yy<=Math.min(mask.getHeight()-1,y+radius)&&!near;yy++)
                        for(int xx=Math.max(0,x-radius);xx<=Math.min(mask.getWidth()-1,x+radius);xx++)
                            if((mask.getPixel(xx,yy)>>>24)!=0&&(xx-x)*(xx-x)+(yy-y)*(yy-y)<=capture.extent*capture.extent+.01f){near=true;break;}
                    if(!near)throw new AssertionError("Outline escaped exterior pixel width at "+x+","+y);
                    changed++;
                }
                if(capture.occluded){
                    if(covered!=0||changed!=0)throw new AssertionError("Occluded armor left a mask/border: covered="+covered+", changed="+changed);
                    occlusionPassed++;OrbitClient.LOG.info("GPU occlusion passed: later opaque block display hides the full armor mask and border");return;
                }
                if(changed==0||covered<capture.minimum)throw new AssertionError("GPU perimeter test needs visible coverage and border: covered="+covered+", minimum="+capture.minimum+", changed="+changed);
                passed++;OrbitClient.LOG.info("GPU perimeter passed: {} exterior pixels, {} model pixels unchanged, no face seams or enlarged-model overlay",changed,covered);
            }catch(Throwable error){failure=error;}
        });
    }
    static void verify(){
        if(failure!=null)throw new AssertionError("GPU exterior perimeter failed",failure);
        if(passed==0)throw new AssertionError("GPU exterior perimeter readback did not complete");
    }
    static void verifyOcclusion(){verify();if(occlusionPassed==0)throw new AssertionError("GPU occlusion readback did not complete");}
    private static final class ViewTarget extends RenderTarget {
        ViewTarget(GpuTextureView view){super("Orbit perimeter verification",false,view.texture().getFormat());width=view.getWidth(0);height=view.getHeight(0);colorTexture=view.texture();colorTextureView=view;}
    }
    private GlowMaskVerification(){}
}
