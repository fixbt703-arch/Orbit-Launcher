package org.orbitlauncher.client;

import com.holdmylua.source.global.GlobalsStorage;
import com.mojang.blaze3d.vertex.PoseStack;
import com.mojang.blaze3d.vertex.VertexConsumer;
import net.minecraft.client.Minecraft;
import net.minecraft.client.model.geom.builders.UVPair;
import net.minecraft.client.renderer.feature.ItemFeatureRenderer;
import net.minecraft.client.renderer.item.ItemStackRenderState.FoilType;
import net.minecraft.client.renderer.rendertype.RenderType;
import net.minecraft.client.renderer.rendertype.RenderTypes;
import net.minecraft.client.resources.model.geometry.BakedQuad;
import net.minecraft.world.item.ItemDisplayContext;
import org.joml.Vector3f;
import org.joml.Vector3fc;
import java.util.*;
import java.util.function.Function;

/** Independently implemented silhouette glow, emitted through Minecraft's depth-tested line renderer. */
public final class EnchantmentGlow {
    private record Edge(Vector3f a,Vector3f b){}
    private static final Map<BakedQuad,List<Edge>> CACHE=new LinkedHashMap<>(128,.75f,true){
        @Override protected boolean removeEldestEntry(Map.Entry<BakedQuad,List<Edge>> entry){return size()>1024;}
    };
    public static int renderedForTest,segmentsForTest;
    public static void invalidate(){CACHE.clear();}
    static boolean visible(ItemDisplayContext context,boolean hmi,ClientConfig.State s){
        if(!s.glowOutline || s.glowOutlineOpacity<=0)return false;
        if(context==ItemDisplayContext.GUI)return s.glowInventory;
        if(context.firstPerson() || hmi)return s.glowFirstPerson;
        return (context==ItemDisplayContext.THIRD_PERSON_LEFT_HAND || context==ItemDisplayContext.THIRD_PERSON_RIGHT_HAND) && s.glowThirdPerson;
    }
    public static void render(ItemFeatureRenderer.Submit submit,Function<RenderType,VertexConsumer> buffers,boolean hmi){
        if(OrbitClient.config==null || !OrbitClient.enabled(Module.GLINT) || submit.foilType()==FoilType.NONE || submit.outlineColor()!=0)return;
        var s=OrbitClient.config.state(Module.GLINT);
        if(!visible(submit.displayContext(),hmi,s))return;
        int rgb=s.glowMatchGlint?s.glintColor:s.glowOutlineColor;
        double time=System.nanoTime()/1_000_000_000.0;
        if(s.glowRainbow)rgb=java.awt.Color.HSBtoRGB((float)((time/8)%1),.65f,1)&0xFFFFFF;
        float alpha=s.glowOutlineOpacity*(s.glowPulse?(float)(.82+.18*Math.sin(time*2)):1);
        VertexConsumer buffer=buffers.apply(RenderTypes.linesTranslucent());
        var posed=new PoseStack();int index=0,segments=0;
        var seen=new HashSet<String>();
        for(var quad:submit.quads()){
            var edges=CACHE.computeIfAbsent(quad,EnchantmentGlow::mesh);
            posed.last().set(submit.pose());
            if(hmi)GlobalsStorage.modelPartAnimator.applyPoses(index,posed);
            for(var edge:edges){
                Vector3f a=posed.last().pose().transformPosition(edge.a,new Vector3f());
                Vector3f b=posed.last().pose().transformPosition(edge.b,new Vector3f());
                String ka=key(a),kb=key(b),key=ka.compareTo(kb)<0?ka+":"+kb:kb+":"+ka;
                if(!seen.add(key))continue;
                Vector3f tangent=new Vector3f(b).sub(a);if(tangent.lengthSquared()<1e-10f)continue;tangent.normalize();
                if(s.glowSoftness>0){
                    line(buffer,a,b,tangent,rgb,alpha*.07f,s.glowWidth+7*s.glowSoftness);
                    line(buffer,a,b,tangent,rgb,alpha*.16f,s.glowWidth+3.5f*s.glowSoftness);
                }
                line(buffer,a,b,tangent,rgb,alpha,s.glowWidth);
                segments++;
            }
            index++;
        }
        if(segments>0){renderedForTest++;segmentsForTest+=segments;}
    }
    private static String key(Vector3fc v){return Math.round(v.x()*100000)+","+Math.round(v.y()*100000)+","+Math.round(v.z()*100000);}
    private static void line(VertexConsumer buffer,Vector3f a,Vector3f b,Vector3f tangent,int rgb,float alpha,float width){
        int color=Math.round(Math.clamp(alpha,0,1)*255)<<24|rgb;
        buffer.addVertex(a).setColor(color).setNormal(tangent.x(),tangent.y(),tangent.z()).setLineWidth(width);
        buffer.addVertex(b).setColor(color).setNormal(tangent.x(),tangent.y(),tangent.z()).setLineWidth(width);
    }
    private static List<Edge> mesh(BakedQuad quad){
        var sprite=quad.materialInfo().sprite();var contents=sprite.contents();
        int width=contents.width(),height=contents.height();
        float[] u=new float[4],v=new float[4];
        float minU=1,minV=1,maxU=0,maxV=0;
        for(int i=0;i<4;i++){
            u[i]=(UVPair.unpackU(quad.packedUV(i))-sprite.getU0())/(sprite.getU1()-sprite.getU0());
            v[i]=(UVPair.unpackV(quad.packedUV(i))-sprite.getV0())/(sprite.getV1()-sprite.getV0());
            minU=Math.min(minU,u[i]);maxU=Math.max(maxU,u[i]);minV=Math.min(minV,v[i]);maxV=Math.max(maxV,v[i]);
        }
        float du1=u[1]-u[0],dv1=v[1]-v[0],du3=u[3]-u[0],dv3=v[3]-v[0],det=du1*dv3-dv1*du3;
        if(Math.abs(det)<1e-8f)return List.of(); // Degenerate UV strips are covered by the front/back silhouettes.
        int x0=Math.clamp((int)Math.floor(minU*width+.001),0,width-1),y0=Math.clamp((int)Math.floor(minV*height+.001),0,height-1);
        int x1=Math.clamp((int)Math.ceil(maxU*width-.001),x0+1,width),y1=Math.clamp((int)Math.ceil(maxV*height-.001),y0+1,height);
        int w=x1-x0,h=y1-y0;if((long)w*h>1_048_576)return List.of();
        boolean[] solid=new boolean[w*h];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)solid[y*w+x]=!contents.isTransparent(0,x+x0,y+y0);
        var result=new ArrayList<Edge>();
        for(var edge:GlowContour.trace(solid,w,h)){
            float ua=Math.clamp((edge.x0()+x0)/(float)width,minU,maxU),va=Math.clamp((edge.y0()+y0)/(float)height,minV,maxV);
            float ub=Math.clamp((edge.x1()+x0)/(float)width,minU,maxU),vb=Math.clamp((edge.y1()+y0)/(float)height,minV,maxV);
            result.add(new Edge(point(quad,ua-u[0],va-v[0],du1,dv1,du3,dv3,det),point(quad,ub-u[0],vb-v[0],du1,dv1,du3,dv3,det)));
        }
        return List.copyOf(result);
    }
    private static Vector3f point(BakedQuad q,float du,float dv,float du1,float dv1,float du3,float dv3,float det){
        float s=(du*dv3-dv*du3)/det,t=(dv*du1-du*dv1)/det;
        return new Vector3f(q.position0()).fma(s,new Vector3f(q.position1()).sub(q.position0())).fma(t,new Vector3f(q.position3()).sub(q.position0()));
    }
    private EnchantmentGlow(){}
}
