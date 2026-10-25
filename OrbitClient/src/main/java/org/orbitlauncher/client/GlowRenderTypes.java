package org.orbitlauncher.client;

import com.mojang.blaze3d.PrimitiveTopology;
import com.mojang.blaze3d.pipeline.*;
import com.mojang.blaze3d.platform.CompareOp;
import com.mojang.blaze3d.vertex.DefaultVertexFormat;
import java.util.*;
import net.minecraft.client.renderer.BindGroupLayouts;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.client.renderer.rendertype.RenderSetup;
import net.minecraft.client.renderer.rendertype.RenderType;
import net.minecraft.client.renderer.rendertype.LayeringTransform;
import net.minecraft.resources.Identifier;

/** Exact-size geometry writes binary silhouette coverage to a separate texture, never to the scene. */
public final class GlowRenderTypes {
    private static final RenderPipeline MASK=RenderPipelines.register(RenderPipeline.builder(RenderPipelines.MATRICES_FOG_SNIPPET)
        .withLocation(id("pipeline/enchantment_mask"))
        .withVertexShader(id("core/enchantment_outline")).withFragmentShader(id("core/enchantment_outline"))
        .withBindGroupLayout(BindGroupLayouts.SAMPLER0).withVertexBinding(0,DefaultVertexFormat.BLOCK)
        .withPrimitiveTopology(PrimitiveTopology.QUADS).withCull(false)
        .withColorTargetState(ColorTargetState.DEFAULT)
        .withDepthStencilState(new DepthStencilState(CompareOp.GREATER_THAN_OR_EQUAL,false)).build());
    private record Key(Identifier texture,boolean armor){}
    private static final Map<Key,RenderType> CACHE=new LinkedHashMap<>(32,.75f,true){
        @Override protected boolean removeEldestEntry(Map.Entry<Key,RenderType> entry){return size()>128;}
    };
    static Identifier id(String path){return Identifier.fromNamespaceAndPath("orbit_client",path);}
    public static boolean isMaskPipeline(RenderPipeline pipeline){return pipeline==MASK;}
    public static RenderType outline(Identifier texture){
        return layer(texture,false);
    }
    public static RenderType armor(Identifier texture){return layer(texture,true);}
    private static RenderType layer(Identifier texture,boolean armor){
        return CACHE.computeIfAbsent(new Key(texture,armor),key->RenderType.create("orbit_silhouette_mask",
            RenderSetup.builder(MASK).withTexture("Sampler0",texture)
                // Match vanilla armor's own depth layering, rather than putting its mask behind it.
                .setLayeringTransform(armor?LayeringTransform.VIEW_OFFSET_Z_LAYERING:LayeringTransform.NO_LAYERING).createRenderSetup()));
    }
    private GlowRenderTypes(){}
}
