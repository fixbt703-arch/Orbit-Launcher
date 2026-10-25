package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.input.KeyEvent;
import org.lwjgl.glfw.GLFW;

/** Captures real focused controls at maximum theme rounding in the UI smoke world. */
final class FocusShapeChecks {
    static void tick(Minecraft mc,int tick){
        if(tick==108){
            var menu=new OrbitScreen();mc.gui.setScreen(menu);
            var row=menu.children().stream().filter(c->c instanceof OrbitButton b&&b.getMessage().getString().equals("FPS")).map(AbstractWidget.class::cast).findFirst().orElseThrow();
            menu.setFocused(row);
            OrbitClient.LOG.info("Focus shape capture: rectangular module row, radius {}",OrbitTheme.options().cornerRadius);
        }
        if(tick==114&&mc.gui.screen() instanceof OrbitScreen menu){
            var host=menu.inlineForTest(Module.INTERFACE);
            var slider=host.children().stream().filter(OrbitSlider.class::isInstance).map(OrbitSlider.class::cast).findFirst().orElseThrow();
            for(int i=0;i<500&&(menu.activeInlineForTest()!=host||host.getFocused()!=slider);i++)menu.keyPressed(new KeyEvent(GLFW.GLFW_KEY_TAB,0,0));
            if(menu.activeInlineForTest()!=host||host.getFocused()!=slider||!menu.inlineFocusFullyVisibleForTest(Module.INTERFACE))throw new AssertionError("Focus shape slider is unreachable");
            OrbitClient.LOG.info("Focus shape capture: rectangular compact slider, radius {}",OrbitTheme.options().cornerRadius);
        }
    }
}
