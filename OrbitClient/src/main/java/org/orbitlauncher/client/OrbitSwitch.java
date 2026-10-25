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
        this(x,y,w,h,label,checked,action,label);
    }
    OrbitSwitch(int x,int y,int w,int h,String label,boolean checked,Runnable action,String motionKey) {
        super(x,y,w,h,label+": "+(checked?tr("Включён","Enabled"):tr("Выключен","Disabled")),false,action);
        this.label=label;this.checked=checked;
        var screen=Minecraft.getInstance().gui.screen();
        thumb=screen==null?new OrbitMotion(checked):TOGGLES.computeIfAbsent(screen,s->new java.util.HashMap<>()).computeIfAbsent(motionKey,key->new OrbitMotion(checked));
        setTooltip(OrbitTheme.tooltip(getMessage()));
        setTooltipDelay(java.time.Duration.ofMillis(450));
    }
    @Override protected void extractWidgetRenderState(GuiGraphicsExtractor g,int mx,int my,float delta) {
        if(compact){
            g.fill(getX(),getY(),getX()+width,getY()+height,OrbitMotion.mix(OrbitTheme.translucent(OrbitTheme.BASE,242),OrbitTheme.HOVER,hover.toward(isHoveredOrFocused())));
            if(isFocused())g.fill(getX(),getY()+height-1,getX()+width,getY()+height,OrbitTheme.PRIMARY);
            ButtonLabel.left(g,OrbitFonts.ui(),label,getX()+6,getY()+3,width-34,height-6,0xFFE4EDF5);
            int trackX=getX()+width-23,trackY=getY()+(height-10)/2;
            float amount=thumb.toward(checked);
            // A switch stays a pill even when menu corner rounding is zero.
            g.blitSprite(net.minecraft.client.renderer.RenderPipelines.GUI_TEXTURED,net.minecraft.resources.Identifier.fromNamespaceAndPath("orbit_client","ui/pill"),trackX,trackY,18,10,OrbitMotion.mix(OrbitTheme.CONTROL,OrbitTheme.SELECTED,amount));
            OrbitTheme.disc(g,trackX+2+Math.round(8*amount),trackY+2,6,OrbitMotion.mix(OrbitTheme.MUTED,OrbitTheme.PRIMARY,amount));
            return;
        }
        if(width>50) {
            OrbitTheme.rounded(g,getX(),getY(),width,height,OrbitMotion.mix(OrbitTheme.CARD,OrbitTheme.HOVER,hover.toward(isHoveredOrFocused())));
            if(isFocused())OrbitTheme.outline(g,getX(),getY(),width,height,OrbitTheme.FOCUS);
            ButtonLabel.left(g,OrbitFonts.ui(),label,getX()+10,getY()+3,width-50,height-6,OrbitTheme.TEXT);
        }
        OrbitTheme.switchTrack(g,trackX(getX(),width),getY()+(height-14)/2,thumb.toward(checked),isFocused());
    }
    static int trackX(int x,int width) {return width>50?x+width-36:x+(width-26)/2;}
    @Override net.minecraft.client.gui.Font labelFont(){return OrbitFonts.ui();}
}
