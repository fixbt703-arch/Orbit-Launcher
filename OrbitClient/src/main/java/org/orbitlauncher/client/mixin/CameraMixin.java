package org.orbitlauncher.client.mixin;
import net.minecraft.client.Camera;
import org.orbitlauncher.client.OrbitClient;
import org.orbitlauncher.client.Module;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(Camera.class)
public class CameraMixin {
    @Inject(method="calculateFov",at=@At("RETURN"),cancellable=true)
    private void orbit$zoom(float delta,CallbackInfoReturnable<Float> cir) {
        if(OrbitClient.zooming()) cir.setReturnValue(cir.getReturnValue()/OrbitClient.config.state(Module.ZOOM).zoomFactor);
    }
}
