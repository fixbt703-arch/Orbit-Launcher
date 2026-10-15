package org.orbitlauncher.client.mixin;
import net.minecraft.client.gui.Hud;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.DeltaTracker;
import org.orbitlauncher.client.OrbitClient;
import org.orbitlauncher.client.Module;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(Hud.class)
public class HudMixin {
    @Inject(method="extractCrosshair",at=@At("HEAD"),cancellable=true)
    private void orbit$crosshair(GuiGraphicsExtractor g,DeltaTracker delta,CallbackInfo ci) {
        if(org.orbitlauncher.client.HudRenderer.customCrosshairVisible()) ci.cancel();
    }
}
