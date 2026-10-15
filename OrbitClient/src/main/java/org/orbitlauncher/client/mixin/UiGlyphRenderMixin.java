package org.orbitlauncher.client.mixin;

import com.mojang.blaze3d.systems.RenderSystem;
import com.mojang.blaze3d.textures.FilterMode;
import net.minecraft.client.gui.font.TextRenderable;
import net.minecraft.client.gui.render.TextureSetup;
import net.minecraft.client.renderer.state.gui.GlyphRenderState;
import org.orbitlauncher.client.SmoothUiText;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(GlyphRenderState.class)
public abstract class UiGlyphRenderMixin {
    @Shadow @Final private TextRenderable renderable;
    @Inject(method="textureSetup",at=@At("HEAD"),cancellable=true)
    private void orbit$smoothFont(CallbackInfoReturnable<TextureSetup> result){
        if(SmoothUiText.contains(renderable.textureView()))result.setReturnValue(TextureSetup.singleTextureWithLightmap(
            renderable.textureView(),RenderSystem.getSamplerCache().getClampToEdge(FilterMode.LINEAR)));
    }
}
