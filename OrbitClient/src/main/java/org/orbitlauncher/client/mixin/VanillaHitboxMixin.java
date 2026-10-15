package org.orbitlauncher.client.mixin;
import net.minecraft.client.renderer.debug.EntityHitboxDebugRenderer;
import org.orbitlauncher.client.OrbitClient;
import org.orbitlauncher.client.Module;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
/** Avoid drawing a second set of vanilla bounds when F3+B is also enabled. */
@Mixin(EntityHitboxDebugRenderer.class)
public abstract class VanillaHitboxMixin {
    @Inject(method="emitGizmos",at=@At("HEAD"),cancellable=true)
    private void orbit$singleOutline(CallbackInfo ci){if(OrbitClient.enabled(Module.HITBOX))ci.cancel();}
}
