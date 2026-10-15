package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.network.chat.Component;
import static org.orbitlauncher.client.OrbitClient.tr;

final class OrbitSwitch extends OrbitButton {
    final String label;
    private final boolean checked;
    private final OrbitMotion hover=new OrbitMotion();
    private static final java.util.Map<net.minecraft.client.gui.screens.Screen,java.util.Map<String,OrbitMotion>> TOGGLES=new java.util.WeakHashMap<>();
    private final OrbitMotion thumb;
    OrbitSwitch(int x,int y,int w,int h,String label,boolean checked,Runnable action) {
        super(x,y,w,h,label+": "+(checked?tr("Включён","Enabled"):tr("Выключен","Disabled")),false,action);
        this.label=label;this.checked=checked;
        var screen=Minecraft.getInstance().gui.screen();
        thumb=screen==null?new OrbitMotion(checked):TOGGLES.computeIfAbsent(screen,s->new java.util.HashMap<>()).computeIfAbsent(label,key->new OrbitMotion(checked));
        setTooltip(OrbitTheme.tooltip(getMessage()));
        setTooltipDelay(java.time.Duration.ofMillis(450));
    }
    @Override protected void extractWidgetRenderState(GuiGraphicsExtractor g,int mx,int my,float delta) {
        if(width>50) {
            OrbitTheme.rounded(g,getX(),getY(),width,height,OrbitMotion.mix(OrbitTheme.CARD,OrbitTheme.HOVER,hover.toward(isHoveredOrFocused())));
            if(isFocused())OrbitTheme.outline(g,getX(),getY(),width,height,OrbitTheme.FOCUS);
            ButtonLabel.left(g,OrbitFonts.strong(),label,getX()+10,getY()+3,width-50,height-6,OrbitTheme.TEXT);
        }
        OrbitTheme.switchTrack(g,trackX(getX(),width),getY()+(height-14)/2,thumb.toward(checked),isFocused());
    }
    static int trackX(int x,int width) {return width>50?x+width-36:x+(width-26)/2;}
    @Override net.minecraft.client.gui.Font labelFont(){return OrbitFonts.strong();}
}
