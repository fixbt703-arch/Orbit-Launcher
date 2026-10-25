package org.orbitlauncher.client;

import com.mojang.blaze3d.vertex.PoseStack;
import com.mojang.blaze3d.vertex.VertexConsumer;
import net.minecraft.client.model.Model;
import net.minecraft.client.model.geom.ModelPart;
import net.minecraft.client.renderer.OrderedSubmitNodeCollector;
import net.minecraft.client.renderer.feature.ModelFeatureRenderer;
import net.minecraft.resources.Identifier;
import org.orbitlauncher.client.mixin.ArmorGlowPartAccessor;
import java.util.LinkedHashMap;
import java.util.Map;

/** Exact-size posed armor masks; the original armor and glint submissions remain intact. */
public final class ArmorGlow {
    private static final Map<Model<?>,HullModel> MODELS=new LinkedHashMap<>(16,.75f,true){
        @Override protected boolean removeEldestEntry(Map.Entry<Model<?>,HullModel> entry){return size()>64;}
    };
    private static int guiDepth;
    public static int renderedForTest;
    public static void enterGui(){guiDepth++;}
    public static void leaveGui(){guiDepth--;}
    public static <S> void queue(OrderedSubmitNodeCollector collector,Model<? super S> model,S state,PoseStack pose,Identifier texture,int light,int overlay,int outline){
        if(OrbitClient.config==null||!OrbitClient.enabled(Module.GLINT)||outline!=0)return;
        var settings=OrbitClient.config.state(Module.GLINT);
        if(!settings.glintArmor||!settings.glowOutline||settings.glowOutlineOpacity<=0||!(guiDepth>0?settings.glowInventory:settings.glowThirdPerson))return;
        var hull=MODELS.computeIfAbsent(model,HullModel::new);
        collector.submitModel(hull,state,pose,GlowRenderTypes.armor(texture),light,overlay,-1,null,0,null);
    }
    public static boolean isHull(Model<?> model){return model instanceof HullModel;}
    public static <S> void render(ModelFeatureRenderer.Submit<S> submit,VertexConsumer buffer,PoseStack pose){
        var settings=OrbitClient.config.state(Module.GLINT);
        pose.last().set(submit.pose());
        submit.model().setupAnim(submit.state());
        int rgb=settings.glowMatchGlint?settings.glintColor:settings.glowOutlineColor;
        renderPart(submit.model().root(),pose,buffer,settings,rgb,System.nanoTime()/1_000_000_000.0);
        renderedForTest++;
    }
    private static void renderPart(ModelPart part,PoseStack pose,VertexConsumer buffer,ClientConfig.State settings,int rgb,double time){
        if(!part.visible)return;
        pose.pushPose();part.translateAndRotate(pose);
        var access=(ArmorGlowPartAccessor)(Object)part;
        if(!part.skipDraw)for(var cube:access.orbit$armorCubes()){
            for(var polygon:cube.polygons){
                var vertices=polygon.vertices();var normal=polygon.normal();
                for(int i=0;i<vertices.length;i++){
                    var vertex=vertices[i];
                    float x=vertex.worldX(),y=vertex.worldY(),z=vertex.worldZ();
                    var sample=GlowStyle.sample(rgb,settings.glowRainbow,false,x,y,z,time);
                    buffer.addVertex(pose.last(),x,y,z).setColor(0xFF000000|sample.core()).setUv(vertex.u(),vertex.v())
                        .setLight(0xF000F0).setNormal(pose.last(),normal.x(),normal.y(),normal.z());
                }
            }
        }
        for(var child:access.orbit$armorChildren().values())renderPart(child,pose,buffer,settings,rgb,time);
        pose.popPose();
    }
    private static final class HullModel extends Model<Object>{
        private final Model<Object> original;
        @SuppressWarnings("unchecked") HullModel(Model<?> original){super(original.root(),original.renderType());this.original=(Model<Object>)original;}
        @Override public void setupAnim(Object state){original.setupAnim(state);}
    }
    private ArmorGlow(){}
}
