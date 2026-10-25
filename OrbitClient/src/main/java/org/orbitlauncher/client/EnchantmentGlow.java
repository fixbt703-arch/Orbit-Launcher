package org.orbitlauncher.client;

import com.holdmylua.source.global.GlobalsStorage;
import com.mojang.blaze3d.vertex.PoseStack;
import com.mojang.blaze3d.vertex.VertexConsumer;
import net.minecraft.client.model.geom.builders.UVPair;
import net.minecraft.client.renderer.feature.ItemFeatureRenderer;
import net.minecraft.client.renderer.item.ItemStackRenderState.FoilType;
import net.minecraft.client.renderer.rendertype.RenderType;
import net.minecraft.world.item.ItemDisplayContext;
import java.util.function.Function;

/** The original geometry is only a mask; the visible border is computed from its screen-space perimeter. */
public final class EnchantmentGlow {
    public static int renderedForTest,segmentsForTest;
    public static void invalidate(){}
    static boolean visible(ItemDisplayContext context,boolean hmi,ClientConfig.State s){
        if(!s.glowOutline || s.glowOutlineOpacity<=0)return false;
        if(context==ItemDisplayContext.GUI)return s.glowInventory;
        if(context.firstPerson() || hmi)return s.glowFirstPerson;
        return (context==ItemDisplayContext.THIRD_PERSON_LEFT_HAND || context==ItemDisplayContext.THIRD_PERSON_RIGHT_HAND) && s.glowThirdPerson;
    }
    public static void render(ItemFeatureRenderer.Submit submit,Function<RenderType,VertexConsumer> buffers,boolean hmi){
        if(OrbitClient.config==null || !OrbitClient.enabled(Module.GLINT) || submit.foilType()==FoilType.NONE || submit.outlineColor()!=0)return;
        var s=OrbitClient.config.state(Module.GLINT);
        if(!s.glintItems||!visible(submit.displayContext(),hmi,s))return;
        int rgb=s.glowMatchGlint?s.glintColor:s.glowOutlineColor;
        double time=System.nanoTime()/1_000_000_000.0;
        var posed=new PoseStack();int index=0;
        for(var quad:submit.quads()){
            posed.last().set(submit.pose());
            if(hmi)GlobalsStorage.modelPartAnimator.applyPoses(index,posed);
            var buffer=buffers.apply(GlowRenderTypes.outline(quad.materialInfo().sprite().atlasLocation()));
            var normal=quad.direction().step();
            for(int vertex=0;vertex<4;vertex++){
                var p=quad.position(vertex);
                var sample=GlowStyle.sample(rgb,s.glowRainbow,false,p.x(),p.y(),p.z(),time);
                long uv=quad.packedUV(vertex);
                buffer.addVertex(posed.last().pose(),p.x(),p.y(),p.z()).setColor(0xFF000000|sample.core())
                    .setUv(UVPair.unpackU(uv),UVPair.unpackV(uv)).setUv2(240,240).setNormal(posed.last(),normal.x(),normal.y(),normal.z());
            }
            index++;
        }
        if(index>0){renderedForTest++;segmentsForTest+=index;}
    }
    private EnchantmentGlow(){}
}
