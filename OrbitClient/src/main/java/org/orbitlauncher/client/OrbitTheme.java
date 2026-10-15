package org.orbitlauncher.client;

import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.resources.Identifier;

/** Shared Orbit design system, including tintable, antialiased nine-slice surfaces. */
final class OrbitTheme {
    // Palette shared with the launcher's Themes/OrbitTheme.xaml.
    static final int TEXT=0xFFF4F6FF, MUTED=0xFFB5C2D8, ACCENT=0xFFD9CEFF, CYAN=0xFF79D7EF;
    static final int BASE=0xFF0C111C, CARD=0xFF171E2E, HOVER=0xFF222C40, BORDER=0xFF303D55, CONTROL=0xFF222C42;
    static final int PRIMARY=0xFFB39CFF, PRIMARY_HOVER=0xFFC8B7FF, SELECTED=0xFF2C3155, FOCUS=0xFF6B618C, DISABLED=0xFF748198;
    static final int ON_PRIMARY=0xFF201735, SUCCESS=0xFF74DDB7, HEART=0xFFFF879C, GOLD=0xFFF4CD87;
    private static final Identifier PANEL=id("panel"),WINDOW=id("window"),WINDOW_OUTLINE=id("window_outline"),OUTLINE=id("outline");
    private static Identifier id(String name){return Identifier.fromNamespaceAndPath("orbit_client","ui/"+name);}
    static net.minecraft.client.gui.components.Tooltip tooltip(net.minecraft.network.chat.Component text){
        return net.minecraft.client.gui.components.Tooltip.create(text,java.util.Optional.empty(),Identifier.fromNamespaceAndPath("orbit_client","ui"));
    }
    static void rounded(GuiGraphicsExtractor g,int x,int y,int w,int h,int color) {
        if(w<=0 || h<=0 || (color>>>24)==0)return;
        if(w<=8||h<=8){g.fill(x,y,x+w,y+h,color);return;}
        g.blitSprite(RenderPipelines.GUI_TEXTURED,PANEL,x,y,w,h,color);
    }
    static void outline(GuiGraphicsExtractor g,int x,int y,int w,int h,int color) {
        if(w>0 && h>0)g.blitSprite(RenderPipelines.GUI_TEXTURED,OUTLINE,x,y,w,h,color);
    }
    static void surface(GuiGraphicsExtractor g,int x,int y,int w,int h,int fill,int border) {
        rounded(g,x,y,w,h,fill);outline(g,x,y,w,h,border);
    }
    static void window(GuiGraphicsExtractor g,int screenW,int screenH,int x,int y,int w,int h) {
        g.fill(0,0,screenW,screenH,0x9C070B12);
        g.blitSprite(RenderPipelines.GUI_TEXTURED,WINDOW,x-2,y+3,w+4,h+3,0x30000000);
        g.blitSprite(RenderPipelines.GUI_TEXTURED,WINDOW,x,y,w,h,0xFC0C111C);
        g.blitSprite(RenderPipelines.GUI_TEXTURED,WINDOW_OUTLINE,x,y,w,h,BORDER);
    }
    static void switchTrack(GuiGraphicsExtractor g,int x,int y,float progress,boolean focused) {
        g.blitSprite(RenderPipelines.GUI_TEXTURED,id("pill"),x,y,26,14,OrbitMotion.mix(0xFF344158,PRIMARY,progress));
        if(focused)g.blitSprite(RenderPipelines.GUI_TEXTURED,id("pill_outline"),x,y,26,14,ACCENT);
        g.pose().pushMatrix();g.pose().translate(x+3+12*progress,y+3);
        disc(g,0,0,8,0xFFF4F0FF);g.pose().popMatrix();
    }
    static void disc(GuiGraphicsExtractor g,int x,int y,int size,int color) {
        g.blitSprite(RenderPipelines.GUI_TEXTURED,id("circle"),x,y,size,size,color);
    }
    static void colorPicker(GuiGraphicsExtractor g,int x,int y,int w,int h,int hueY,float hue) {
        for(int col=0;col<w;col++) {
            int top=java.awt.Color.HSBtoRGB(hue,col/(float)Math.max(1,w-1),1);
            g.fillGradient(x+col,y,x+col+1,y+h,top,0xFF000000);
            g.fill(x+col,hueY,x+col+1,hueY+12,java.awt.Color.HSBtoRGB(col/(float)Math.max(1,w-1),1,1));
        }
    }
    static void pickerMarker(GuiGraphicsExtractor g,int x,int y,int color){
        g.fill(x-3,y-3,x+4,y+4,BASE);g.outline(x-2,y-2,5,5,0xFFFFFFFF);g.fill(x-1,y-1,x+2,y+2,color);
    }
    static String ellipsize(net.minecraft.client.gui.Font font,String text,int width){
        return font.width(text)<=width?text:font.plainSubstrByWidth(text,Math.max(0,width-font.width("…"))).stripTrailing()+"…";
    }
    private OrbitTheme() { }
}
