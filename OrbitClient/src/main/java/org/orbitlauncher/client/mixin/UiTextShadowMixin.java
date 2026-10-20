package org.orbitlauncher.client.mixin;

import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.util.FormattedCharSequence;
import org.orbitlauncher.client.OrbitClient;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyVariable;

/** Also covers vanilla tooltip and editor text hosted by an Orbit screen. */
@Mixin(GuiGraphicsExtractor.class)
abstract class UiTextShadowMixin {
    @ModifyVariable(method="text(Lnet/minecraft/client/gui/Font;Lnet/minecraft/util/FormattedCharSequence;IIIZ)V",at=@At("HEAD"),argsOnly=true,ordinal=0)
    private boolean orbit$flatText(boolean shadow,Font font,FormattedCharSequence text,int x,int y,int color,boolean original){
        return shadow&&!OrbitClient.flatText(font);
    }
}
