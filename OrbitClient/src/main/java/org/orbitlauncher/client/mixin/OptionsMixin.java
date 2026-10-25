package org.orbitlauncher.client.mixin;

import net.minecraft.client.Options;
import org.orbitlauncher.client.ExtendedChunks;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(Options.class)
abstract class OptionsMixin {
    @Inject(method="getMenuBackgroundBlurriness",at=@At("RETURN"),cancellable=true)
    private void orbit$menuBlur(CallbackInfoReturnable<Integer> result){
        result.setReturnValue(org.orbitlauncher.client.OrbitClient.menuBlurRadius(result.getReturnValue()));
    }
    @Inject(method="getEffectiveRenderDistance",at=@At("RETURN"),cancellable=true)
    private void orbit$ignoreServerRenderCap(CallbackInfoReturnable<Integer> result) {
        result.setReturnValue(ExtendedChunks.effectiveRenderDistance(result.getReturnValue()));
    }
}
