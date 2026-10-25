package org.orbitlauncher.client;

import com.mojang.blaze3d.GpuFormat;
import com.mojang.blaze3d.PrimitiveTopology;
import com.mojang.blaze3d.buffers.GpuBuffer;
import com.mojang.blaze3d.pipeline.*;
import com.mojang.blaze3d.shaders.UniformType;
import com.mojang.blaze3d.systems.RenderSystem;
import com.mojang.blaze3d.textures.*;
import java.util.*;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.RenderPipelines;
import org.joml.Vector4f;
import org.lwjgl.system.MemoryStack;

/** Union silhouette -> horizontal distance -> exterior-only composite. Scene geometry is never enlarged. */
public final class GlowOutline {
    private record Size(int width,int height){}
    private record Targets(TextureTarget mask,TextureTarget horizontal){
        void close(){mask.destroyBuffers();horizontal.destroyBuffers();}
    }
    private static final Map<Size,Targets> TARGETS=new LinkedHashMap<>(4,.75f,true){
        @Override protected boolean removeEldestEntry(Map.Entry<Size,Targets> entry){
            if(size()<=4)return false;
            entry.getValue().close();return true;
        }
    };
    private static final RenderPipeline HORIZONTAL=pipeline("outline_horizontal",GpuFormat.RGBA16_FLOAT,false);
    private static final RenderPipeline COMPOSITE=pipeline("outline_perimeter",GpuFormat.RGBA8_UNORM,true);
    private static int scopeDepth;
    private record MaskDraw(Runnable draw,GpuTextureView color,GpuTextureView depth){}
    private static final List<MaskDraw> PENDING=new ArrayList<>();
    private static Targets active;
    private static GpuTextureView destination,depth;
    public static int compositesForTest;
    private static RenderPipeline pipeline(String name,GpuFormat format,boolean blend){
        var layout=BindGroupLayout.builder().withSampler("MaskSampler").withUniform("OutlineParams",UniformType.UNIFORM_BUFFER);
        if(blend)layout.withSampler("HorizontalSampler");
        return RenderPipelines.register(RenderPipeline.builder().withLocation(GlowRenderTypes.id("pipeline/"+name))
            .withVertexShader("core/screenquad").withFragmentShader(GlowRenderTypes.id("core/"+name))
            .withBindGroupLayout(layout.build()).withPrimitiveTopology(PrimitiveTopology.TRIANGLES).withCull(false)
            .withColorTargetState(new ColorTargetState(blend?Optional.of(BlendFunction.TRANSLUCENT):Optional.empty(),format,ColorTargetState.WRITE_ALL))
            .withDepthStencilState(Optional.empty()).build());
    }
    public static void beginScope(){scopeDepth++;}
    public static void endScope(){
        if(--scopeDepth!=0)return;
        try{
            // Test masks against final opaque depth, including objects submitted after the enchanted model.
            for(var draw:PENDING)executeMask(draw);
            composite();
        }finally{PENDING.clear();}
    }
    public static void drawMask(Runnable draw){
        boolean standalone=scopeDepth==0;
        if(standalone)beginScope();
        var main=Minecraft.getInstance().gameRenderer.mainRenderTarget();
        PENDING.add(new MaskDraw(draw,
            RenderSystem.outputColorTextureOverride!=null?RenderSystem.outputColorTextureOverride:main.getColorTextureView(),
            RenderSystem.outputDepthTextureOverride!=null?RenderSystem.outputDepthTextureOverride:main.getDepthTextureView()));
        if(standalone)endScope();
    }
    private static void executeMask(MaskDraw draw){
        var savedColor=RenderSystem.outputColorTextureOverride;
        var savedDepth=RenderSystem.outputDepthTextureOverride;
        var color=draw.color;
        var sceneDepth=draw.depth;
        try{
            if(active!=null&&destination!=color)composite();
            if(active==null){
                destination=color;depth=sceneDepth;
                var size=new Size(color.getWidth(0),color.getHeight(0));
                active=TARGETS.computeIfAbsent(size,key->new Targets(
                    new TextureTarget("Orbit silhouette",key.width,key.height,false,GpuFormat.RGBA8_UNORM),
                    new TextureTarget("Orbit outline distance",key.width,key.height,false,GpuFormat.RGBA16_FLOAT)));
                RenderSystem.getDevice().createCommandEncoder().clearColorTexture(active.mask.getColorTexture(),new Vector4f(0,0,0,0));
            }
            RenderSystem.outputColorTextureOverride=active.mask.getColorTextureView();
            RenderSystem.outputDepthTextureOverride=depth;
            draw.draw.run();
        }finally{
            RenderSystem.outputColorTextureOverride=savedColor;
            RenderSystem.outputDepthTextureOverride=savedDepth;
        }
    }
    private static void composite(){
        if(active==null)return;
        try{
            var state=OrbitClient.config.state(Module.GLINT);
            var encoder=RenderSystem.getDevice().createCommandEncoder();
            float scale=(float)Minecraft.getInstance().getWindow().getGuiScale();
            // Bound the pixel kernel even at extreme GUI scales; normal settings stay below this limit.
            float width=Math.min(24,state.glowWidth*scale),softness=Math.min(4,state.glowSoftness*scale);
            float pulse=GlowStyle.sample(0,false,state.glowPulse,0,0,0,System.nanoTime()/1_000_000_000.0).intensity();
            var capture=GlowMaskVerification.before(destination,active.mask,width+softness+.5f);
            try(var stack=MemoryStack.stackPush()){
                var data=stack.malloc(16).putFloat(width).putFloat(softness).putFloat(state.glowOutlineOpacity*pulse).putFloat(64).flip();
                var uniforms=encoder.transientMemory().uploadGpu(data,RenderSystem.getDevice().getDeviceInfo().limits().minUniformOffsetAlignment(),GpuBuffer.USAGE_UNIFORM);
                var sampler=RenderSystem.getSamplerCache().getClampToEdge(FilterMode.NEAREST);
                try(var pass=encoder.createRenderPass(()->"Orbit outline horizontal distance",active.horizontal.getColorTextureView(),Optional.empty())){
                    pass.setPipeline(HORIZONTAL);pass.setUniform("OutlineParams",uniforms);
                    pass.bindTexture("MaskSampler",active.mask.getColorTextureView(),sampler);
                    pass.draw(3,1,0,0);
                }
                try(var pass=encoder.createRenderPass(()->"Orbit exterior perimeter",destination,Optional.empty())){
                    pass.setPipeline(COMPOSITE);pass.setUniform("OutlineParams",uniforms);
                    pass.bindTexture("MaskSampler",active.mask.getColorTextureView(),sampler);
                    pass.bindTexture("HorizontalSampler",active.horizontal.getColorTextureView(),sampler);
                    pass.draw(3,1,0,0);
                }
            }
            GlowMaskVerification.after(capture,destination,active.horizontal);
            compositesForTest++;
        }finally{active=null;destination=null;depth=null;}
    }
    public static void close(){for(var targets:TARGETS.values())targets.close();TARGETS.clear();}
    private GlowOutline(){}
}
