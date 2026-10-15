package org.orbitlauncher.client;

import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.narration.NarrationElementOutput;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.network.chat.Component;
import org.lwjgl.glfw.GLFW;

final class OrbitColorButton extends AbstractWidget {
    private final int color;private final boolean selected;private final Runnable action;
    OrbitColorButton(int x,int y,int w,int color,boolean selected,Runnable action) {
        super(x,y,w,30,Component.literal(String.format("#%06X",color&0xFFFFFF)));
        this.color=color;this.selected=selected;this.action=action;
    }
    @Override protected void extractWidgetRenderState(GuiGraphicsExtractor g,int mx,int my,float delta) {
        HudRenderer.panel(g,getX(),getY(),width,height,selected?OrbitTheme.ACCENT:isHoveredOrFocused()?OrbitTheme.FOCUS:OrbitTheme.BORDER);
        HudRenderer.panel(g,getX()+3,getY()+3,width-6,height-6,color);
        if(selected) {OrbitTheme.rounded(g,getX()+width/2-7,getY()+height/2-7,14,14,0xCC0B111C);ModuleIcons.ui(g,"check",getX()+width/2-6,getY()+height/2-6,12,0xFFFFFFFF);}
    }
    @Override public void onClick(MouseButtonEvent e,boolean twice) {action.run();}
    @Override public boolean keyPressed(KeyEvent e) {if(e.key()==GLFW.GLFW_KEY_ENTER || e.key()==GLFW.GLFW_KEY_SPACE){action.run();return true;}return super.keyPressed(e);}
    @Override protected void updateWidgetNarration(NarrationElementOutput output) {defaultButtonNarrationText(output);}
}
