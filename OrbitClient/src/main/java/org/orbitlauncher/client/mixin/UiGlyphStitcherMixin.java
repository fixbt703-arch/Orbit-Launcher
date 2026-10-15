package org.orbitlauncher.client.mixin;

import com.mojang.blaze3d.font.GlyphBitmap;
import com.mojang.blaze3d.font.GlyphInfo;
import net.minecraft.client.gui.font.GlyphStitcher;
import net.minecraft.client.gui.font.glyphs.BakedSheetGlyph;
import net.minecraft.network.chat.Style;
import net.minecraft.resources.Identifier;
import org.orbitlauncher.client.SmoothUiText;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(GlyphStitcher.class)
public abstract class UiGlyphStitcherMixin {
    @Shadow @Final private Identifier texturePrefix;
    @Inject(method="stitch",at=@At("RETURN"))
    private void orbit$registerFontAtlas(GlyphInfo info,GlyphBitmap bitmap,CallbackInfoReturnable<BakedSheetGlyph> result){
        var glyph=result.getReturnValue();
        if(glyph!=null&&texturePrefix.getNamespace().equals("orbit_client")
            &&(texturePrefix.getPath().equals("ui")||texturePrefix.getPath().startsWith("ui_")))
            SmoothUiText.register(glyph.createGlyph(0,0,-1,0,Style.EMPTY,0,0).textureView());
    }
}
