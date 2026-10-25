package org.orbitlauncher.client.mixin;

import com.holdmylua.source.mixin.render.RenderTypeFeatureRendererAccessor;
import com.mojang.blaze3d.vertex.PoseStack;
import net.minecraft.client.renderer.feature.ModelFeatureRenderer;
import org.orbitlauncher.client.ArmorGlow;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ModelFeatureRenderer.class)
public abstract class ArmorGlowModelMixin {
    @Shadow @Final private PoseStack poseStack;
    @Inject(method="prepareModel",at=@At("HEAD"),cancellable=true)
    private <S> void orbit$armorHull(ModelFeatureRenderer.Submit<S> submit,CallbackInfo ci){
        if(!ArmorGlow.isHull(submit.model()))return;
        ArmorGlow.render(submit,((RenderTypeFeatureRendererAccessor)this).hmi$getVertexBuilder(submit.renderType()),poseStack);
        ci.cancel();
    }
}
