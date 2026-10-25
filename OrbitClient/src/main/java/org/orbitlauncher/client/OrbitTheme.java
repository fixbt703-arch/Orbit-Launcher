package org.orbitlauncher.client;

import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.resources.Identifier;

/** Shared Orbit design system, including tintable, antialiased nine-slice surfaces. */
final class OrbitTheme {
    // Shared workspace and HUD palette. Feature-specific colors remain independent.
    static final int TEXT=0xFFE4EDF5;
    static int MUTED=0xFFACB4C3;
    static int ACCENT=0xFF71E6EB, CYAN=0xFF49D4DE,PRIMARY=0xFF49D4DE,PRIMARY_HOVER=0xFF78E4EB;
    static int BASE=0xFF0D1924, CARD=0xFF142432, CONTROL=0xFF1C3040;
    static final int DISABLED=0xFF718B9B;
    static int HOVER=0xFF21384A,BORDER=0xFF284151,SELECTED=0xFF20424E,FOCUS=0xFF3B7985;
    static final int ON_PRIMARY=0xFF09232C, SUCCESS=0xFF74DDB7, HEART=0xFFFF879C, GOLD=0xFFF4CD87;
    private static final Identifier PANEL=id("panel"),WINDOW=id("window"),WINDOW_OUTLINE=id("window_outline"),OUTLINE=id("outline");
    private static final InterfaceOptions DEFAULTS=new InterfaceOptions();
    private static final Identifier[] PANELS=new Identifier[13],OUTLINES=new Identifier[13];
    private static int secondary=DEFAULTS.secondary();
    private static long frame;
    static {for(int i=1;i<=12;i++){PANELS[i]=id("radius_"+i);OUTLINES[i]=id("radius_"+i+"_outline");}}
    static InterfaceOptions options(){return OrbitClient.config==null?DEFAULTS:OrbitClient.config.interfaceOptions;}
    static void update(){
        long now=System.nanoTime();var options=options();
        float amount=frame==0?1:(float)options.blend((now-frame)/1e9,14);frame=now;
        double seconds=now/1e9;
        PRIMARY=CYAN=OrbitMotion.mix(PRIMARY,options.primary(seconds),amount);
        BASE=OrbitMotion.mix(BASE,options.base(seconds),amount);
        CARD=OrbitMotion.mix(CARD,options.card(seconds),amount);
        CONTROL=OrbitMotion.mix(CONTROL,options.control(seconds),amount);
        MUTED=OrbitMotion.mix(0xFFACB4C3,PRIMARY,.12f);
        secondary=OrbitMotion.mix(secondary,options.secondary(seconds),amount);
        ACCENT=PRIMARY_HOVER=OrbitMotion.mix(PRIMARY,0xFFFFFFFF,.3f);
        HOVER=OrbitMotion.mix(CARD,PRIMARY,.12f);BORDER=OrbitMotion.mix(CARD,PRIMARY,.25f);
        SELECTED=OrbitMotion.mix(CARD,PRIMARY,.2f);FOCUS=OrbitMotion.mix(CARD,PRIMARY,.5f);
    }
    static float elapsed(long start,double milliseconds){double duration=options().duration(milliseconds);return duration==0?1:(float)Math.clamp((System.nanoTime()-start)/(duration*1_000_000),0,1);}
    static int translucent(int color,int alpha){return (Math.clamp(alpha,0,255)<<24)|(color&0xFFFFFF);}
    static void accentLine(GuiGraphicsExtractor g,int x,int y,int width){
        var options=options();
        if(options.glow)for(int i=3;i>0;i--)g.fill(x,y-i,x+width,y+i+1,translucent(PRIMARY,5+(3-i)*4));
        double phase=options.gradient==2&&options.animations?(System.nanoTime()/1e9)*options.animationSpeed*.45:0;
        for(int col=0;col<width;col+=3){
            float blend=options.gradient==0?0:options.gradient==1?col/(float)Math.max(1,width-1):(float)(.5-.5*Math.cos(phase+col/(double)Math.max(1,width)*Math.PI*2));
            g.fill(x+col,y,x+Math.min(width,col+3),y+1,options.theme==InterfaceOptions.RAINBOW&&options.gradient!=0?options.rainbow(options.gradient==2?System.nanoTime()/1e9:0,col/(double)Math.max(1,width)*.8,.55f,.95f):OrbitMotion.mix(PRIMARY,secondary,blend));
        }
    }
    private static Identifier id(String name){return Identifier.fromNamespaceAndPath("orbit_client","ui/"+name);}
    static net.minecraft.client.gui.components.Tooltip tooltip(net.minecraft.network.chat.Component text){
        return net.minecraft.client.gui.components.Tooltip.create(text,java.util.Optional.empty(),Identifier.fromNamespaceAndPath("orbit_client","ui"));
    }
    static void rounded(GuiGraphicsExtractor g,int x,int y,int w,int h,int color) {
        if(w<=0 || h<=0 || (color>>>24)==0)return;
        int radius=Math.min(options().cornerRadius,Math.min(w,h)/2);
        if(radius==0){g.fill(x,y,x+w,y+h,color);return;}
        g.blitSprite(RenderPipelines.GUI_TEXTURED,PANELS[radius],x,y,w,h,color);
    }
    static void outline(GuiGraphicsExtractor g,int x,int y,int w,int h,int color) {
        if(w<=0||h<=0)return;
        int radius=Math.min(options().cornerRadius,Math.min(w,h)/2);
        if(radius==0)g.outline(x,y,w,h,color);
        else g.blitSprite(RenderPipelines.GUI_TEXTURED,OUTLINES[radius],x,y,w,h,color);
    }
    static void surface(GuiGraphicsExtractor g,int x,int y,int w,int h,int fill,int border) {
        rounded(g,x,y,w,h,fill);outline(g,x,y,w,h,border);
    }
    static void window(GuiGraphicsExtractor g,int screenW,int screenH,int x,int y,int w,int h) {
        windowFrame(g,x,y,w,h);
    }
    static void windowFrame(GuiGraphicsExtractor g,int x,int y,int w,int h) {
        rounded(g,x-2,y+3,w+4,h+3,0x30000000);
        surface(g,x,y,w,h,translucent(BASE,252),BORDER);
    }
    static void switchTrack(GuiGraphicsExtractor g,int x,int y,float progress,boolean focused) {
        g.blitSprite(RenderPipelines.GUI_TEXTURED,id("pill"),x,y,26,14,OrbitMotion.mix(CONTROL,PRIMARY,progress));
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
