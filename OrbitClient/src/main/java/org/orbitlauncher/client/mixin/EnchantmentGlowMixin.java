package org.orbitlauncher.client.mixin;

import com.holdmylua.source.mixin.render.RenderTypeFeatureRendererAccessor;
import net.minecraft.client.renderer.feature.ItemFeatureRenderer;
import org.orbitlauncher.client.EnchantmentGlow;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ItemFeatureRenderer.class)
public abstract class EnchantmentGlowMixin {
    @Inject(method="prepareSubmit",at=@At("TAIL"))
    private void orbit$outline(ItemFeatureRenderer.Submit submit,boolean foil,CallbackInfo ci){
        if(foil)EnchantmentGlow.render(submit,((RenderTypeFeatureRendererAccessor)this)::hmi$getVertexBuilder,false);
    }
}
