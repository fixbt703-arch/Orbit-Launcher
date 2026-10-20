package org.orbitlauncher.client.mixin;

import com.llamalad7.mixinextras.injector.ModifyExpressionValue;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import com.mojang.blaze3d.pipeline.RenderPipeline;
import net.minecraft.client.AttackIndicatorStatus;
import net.minecraft.client.gui.Hud;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.resources.Identifier;
import org.orbitlauncher.client.CriticalIndicator;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/** Keep vanilla's complete attack-indicator extraction; replace only the crosshair artwork. */
@Mixin(Hud.class)
public class HudMixin {
    @ModifyExpressionValue(method="extractCrosshair",at=@At(value="INVOKE",target="Lnet/minecraft/client/OptionInstance;get()Ljava/lang/Object;"))
    private Object orbit$indicator(Object original){
        return CriticalIndicator.customized()?(CriticalIndicator.enabled()?AttackIndicatorStatus.CROSSHAIR:AttackIndicatorStatus.OFF):original;
    }
    @WrapOperation(method="extractCrosshair",at=@At(value="INVOKE",target="Lnet/minecraft/client/gui/GuiGraphicsExtractor;blitSprite(Lcom/mojang/blaze3d/pipeline/RenderPipeline;Lnet/minecraft/resources/Identifier;IIII)V"))
    private void orbit$sprite(GuiGraphicsExtractor g,RenderPipeline pipeline,Identifier sprite,int x,int y,int w,int h,Operation<Void> original){
        CriticalIndicator.sprite(g,sprite,x,y,w,h,()->original.call(g,pipeline,sprite,x,y,w,h));
    }
    @WrapOperation(method="extractCrosshair",at=@At(value="INVOKE",target="Lnet/minecraft/client/gui/GuiGraphicsExtractor;blitSprite(Lcom/mojang/blaze3d/pipeline/RenderPipeline;Lnet/minecraft/resources/Identifier;IIIIIIII)V"))
    private void orbit$progress(GuiGraphicsExtractor g,RenderPipeline pipeline,Identifier sprite,int spriteW,int spriteH,int u,int v,int x,int y,int w,int h,Operation<Void> original){
        CriticalIndicator.sprite(g,sprite,x,y,w,h,()->original.call(g,pipeline,sprite,spriteW,spriteH,u,v,x,y,w,h));
    }
}
