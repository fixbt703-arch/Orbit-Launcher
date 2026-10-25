package org.orbitlauncher.client.mixin;

import com.llamalad7.mixinextras.injector.wrapmethod.WrapMethod;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.mojang.blaze3d.vertex.PoseStack;
import net.minecraft.client.gui.render.pip.GuiEntityRenderer;
import net.minecraft.client.renderer.SubmitNodeCollector;
import net.minecraft.client.renderer.state.gui.pip.GuiEntityRenderState;
import org.orbitlauncher.client.ArmorGlow;
import org.spongepowered.asm.mixin.Mixin;

@Mixin(GuiEntityRenderer.class)
public abstract class ArmorGlowGuiMixin {
    @WrapMethod(method="renderToTexture(Lnet/minecraft/client/renderer/state/gui/pip/GuiEntityRenderState;Lcom/mojang/blaze3d/vertex/PoseStack;Lnet/minecraft/client/renderer/SubmitNodeCollector;)V")
    private void orbit$guiArmor(GuiEntityRenderState state,PoseStack pose,SubmitNodeCollector collector,Operation<Void> original){
        ArmorGlow.enterGui();
        try{original.call(state,pose,collector);}finally{ArmorGlow.leaveGui();}
    }
}
