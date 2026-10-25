package org.orbitlauncher.client.mixin;

import com.llamalad7.mixinextras.injector.ModifyReturnValue;
import com.llamalad7.mixinextras.injector.wrapmethod.WrapMethod;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import org.orbitlauncher.client.OrbitScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

@Mixin(Screen.class)
public abstract class OrbitViewportMixin {
    @WrapMethod(method={"init(II)V","resize(II)V"})
    private void orbit$viewportSize(int width,int height,Operation<Void> original){
        if((Object)this instanceof OrbitScreen menu){menu.configureViewport(width,height);original.call(menu.viewportWidth(),menu.viewportHeight());}
        else original.call(width,height);
    }
    @WrapMethod(method="extractRenderStateWithTooltipAndSubtitles")
    private void orbit$viewportDraw(GuiGraphicsExtractor g,int mouseX,int mouseY,float delta,Operation<Void> original){
        if(!((Object)this instanceof OrbitScreen menu)){original.call(g,mouseX,mouseY,delta);return;}
        menu.beginViewport(g);
        try{original.call(g,menu.viewportMouse(mouseX),menu.viewportMouse(mouseY),delta);}
        finally{menu.endViewport(g);}
    }

    /** Deferred vanilla tooltips use the same bounds and transform as the menu. */
    @Mixin(GuiGraphicsExtractor.class)
    public static abstract class Metrics {
        @WrapMethod(method="containsPointInScissor")
        private boolean orbit$scissor(int x,int y,Operation<Boolean> original){return original.call(OrbitScreen.scissorCoordinate(x),OrbitScreen.scissorCoordinate(y));}
        @ModifyReturnValue(method="guiWidth",at=@At("RETURN"))
        private int orbit$width(int original){return OrbitScreen.renderWidth(original);}
        @ModifyReturnValue(method="guiHeight",at=@At("RETURN"))
        private int orbit$height(int original){return OrbitScreen.renderHeight(original);}
    }
}
