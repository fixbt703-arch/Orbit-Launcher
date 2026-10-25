package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.network.chat.Component;

/** Shared appearance scope for the workspace, HUD editor and color editors. */
abstract class OrbitUiScreen extends Screen {
    protected OrbitUiScreen(Minecraft minecraft,Font font,Component title){super(minecraft,font,title);}
    protected static String focusKey(AbstractWidget widget){
        return widget instanceof OrbitSwitch toggle?"switch:"+toggle.label:
            widget instanceof OrbitSlider slider?"slider:"+slider.label():widget instanceof OrbitButton button?button.focusKey():widget.getClass().getName()+":"+widget.getMessage().getString();
    }
    @Override protected void rebuildWidgets(){
        String previous=getFocused() instanceof AbstractWidget widget?focusKey(widget):null;
        super.rebuildWidgets();
        if(previous!=null)for(var child:children())if(child instanceof AbstractWidget widget&&focusKey(widget).equals(previous)){setFocused(widget);break;}
    }
    @Override public void setFocused(GuiEventListener child){
        if(child instanceof AbstractWidget old&&!children().contains(child)){
            String key=focusKey(old);child=null;
            for(var candidate:children())if(candidate instanceof AbstractWidget widget&&focusKey(widget).equals(key)){child=widget;break;}
            if(child==null&&children().contains(getFocused()))child=getFocused();
        }
        super.setFocused(child);
    }
    @Override public void extractBackground(GuiGraphicsExtractor g,int mx,int my,float delta){
        OrbitTheme.update();
        if(minecraft.level==null)extractPanorama(g,delta);
        var ui=OrbitTheme.options();
        if(ui.effectiveBlurRadius()>0)g.blurBeforeThisStratum();
        int alpha=ui.backgroundAlpha();
        if(alpha>0)g.fill(0,0,g.guiWidth(),g.guiHeight(),OrbitTheme.translucent(OrbitTheme.BASE,alpha));
        minecraft.gui.hud.extractDeferredSubtitles();
    }
}
