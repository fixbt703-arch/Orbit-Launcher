package org.orbitlauncher.client.mixin;

import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import org.orbitlauncher.client.OrbitClient;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Suppress the render pass, retaining tooltip text for narration and later re-enabling. */
@Mixin(AbstractWidget.class)
public abstract class InterfaceTooltipMixin {
    @Inject(method="extractTooltipForNextRenderPass",at=@At("HEAD"),cancellable=true)
    private void orbit$tooltip(GuiGraphicsExtractor g,int mouseX,int mouseY,CallbackInfo ci){
        if(!OrbitClient.tooltipsAllowed())ci.cancel();
    }
}
