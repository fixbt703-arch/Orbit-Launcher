package org.orbitlauncher.client;

import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;
import java.awt.Color;
import static org.orbitlauncher.client.OrbitClient.tr;

/** One precise HSV/HEX editor for all colors of an individual HUD card. */
final class HudColorScreen extends Screen {
    private final Screen parent;private final Module module;
    private int x,y,w,h,leftW,rightX,rightW,bodyY,svHeight,hueY,footer,role,drag=-1;
    private float hue,saturation,value;private EditBox hex;private boolean syncing;
    private final OrbitPageTransition pageTransition=new OrbitPageTransition();
    HudColorScreen(Screen parent,Module module){super(net.minecraft.client.Minecraft.getInstance(),OrbitFonts.ui(),Component.literal(tr("Цвета HUD","HUD colors")));this.parent=parent;this.module=module;}
    private ClientConfig.State state(){return OrbitClient.config.state(module);}
    private int color(){return role==1?state().backgroundColor:role==2?state().textColor:ClientConfig.accent(state())&0xFFFFFF;}
    private void setColor(int rgb){if(role==1)state().backgroundColor=rgb;else if(role==2)state().textColor=rgb;else state().customColor=rgb;}
    private void readColor(){int rgb=color();float[] hsv=Color.RGBtoHSB(rgb>>16&255,rgb>>8&255,rgb&255,null);hue=hsv[0];saturation=hsv[1];value=hsv[2];}
    @Override protected void init(){
        w=Math.min(width-24,520);h=Math.min(height-20,320);x=(width-w)/2;y=(height-h)/2;
        leftW=(w-36)*3/5;rightX=x+24+leftW;rightW=w-leftW-36;bodyY=y+64;footer=y+h-28;
        svHeight=Math.max(20,footer-bodyY-22);hueY=bodyY+svHeight+5;readColor();
        String[] names={tr("Акцент","Accent"),tr("Фон","Background"),tr("Текст","Text")};int tw=(w-32)/3;
        for(int i=0;i<3;i++){final int id=i;addRenderableWidget(new OrbitButton(x+12+i*(tw+4),y+36,tw,20,names[i],role==i,()->{role=id;rebuildWidgets();pageTransition.start();}).asTab());}
        hex=new EditBox(font,rightX+29,bodyY+7,rightW-34,14,Component.literal("HEX #RRGGBB"));hex.setBordered(false);hex.setMaxLength(7);hex.setTextColor(OrbitTheme.TEXT);hex.setValue(String.format("#%06X",color()));
        hex.setResponder(text->{if(syncing)return;boolean valid=text.matches("#?[0-9a-fA-F]{6}");hex.setTextColor(valid?OrbitTheme.TEXT:0xFFFF718B);if(valid){setColor(Integer.parseInt(text.replace("#",""),16));readColor();}});addRenderableWidget(hex);
        addRenderableWidget(new OrbitButton(x+w-76,footer,64,22,tr("Готово","Done"),true,this::onClose));
    }
    private void changed(){setColor(Color.HSBtoRGB(hue,saturation,value)&0xFFFFFF);syncing=true;hex.setValue(String.format("#%06X",color()));hex.setTextColor(OrbitTheme.TEXT);syncing=false;}
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){
        OrbitTheme.window(g,width,height,x,y,w,h);g.text(OrbitFonts.heading(),tr("Цвета HUD","HUD colors"),x+12,y+13,OrbitTheme.TEXT,false);
        OrbitTheme.surface(g,rightX+24,bodyY,rightW-24,24,OrbitTheme.CARD,hex.isFocused()?OrbitTheme.ACCENT:OrbitTheme.BORDER);
        OrbitTheme.colorPicker(g,x+12,bodyY,leftW,svHeight,hueY,hue);
        int sx=x+12+Math.round(saturation*(leftW-1)),sy=bodyY+Math.round((1-value)*(svHeight-1));OrbitTheme.pickerMarker(g,sx,sy,0xFF000000|color());
        int hx=x+12+Math.round(hue*(leftW-1));OrbitTheme.rounded(g,hx-2,hueY-1,5,14,0xFFFFFFFF);OrbitTheme.rounded(g,hx-1,hueY,3,12,java.awt.Color.HSBtoRGB(hue,1,1));OrbitTheme.rounded(g,rightX,bodyY,20,24,0xFF000000|color());
        preview(g,rightX,bodyY+36,rightW,Math.max(12,footer-bodyY-44));super.extractRenderState(g,mx,my,delta);pageTransition.finish(g,x+12,bodyY,w-24,footer-bodyY-4);
    }
    private void preview(GuiGraphicsExtractor g,int px,int py,int pw,int ph){
        var s=state();OrbitTheme.rounded(g,px,py,pw,ph,OrbitTheme.CARD);int alpha=(int)(s.opacity*255)<<24;
        if(s.background)HudRenderer.panel(g,px,py,pw,ph,alpha|s.backgroundColor);
        if(s.hudBorder)OrbitTheme.outline(g,px,py,pw,ph,OrbitTheme.BORDER);
        if(s.accentBar)g.fill(px+3,py+7,px+5,py+ph-7,ClientConfig.accent(s));
        int textAlpha=(int)(s.textOpacity*255)<<24;g.text(OrbitFonts.strong(),OrbitFonts.strong().plainSubstrByWidth(module.title(),pw-14),px+8,py+7,textAlpha|(s.accentTitle?ClientConfig.accent(s)&0xFFFFFF:s.textColor),false);
        if(ph>=34)g.text(font,font.plainSubstrByWidth(tr("Пример HUD","HUD preview"),pw-14),px+8,py+21,textAlpha|s.textColor,false);
    }
    private void pick(double mx,double my){if(drag==0){saturation=(float)Math.clamp((mx-x-12)/(leftW-1),0,1);value=1-(float)Math.clamp((my-bodyY)/(svHeight-1),0,1);}else if(drag==1)hue=(float)Math.clamp((mx-x-12)/(leftW-1),0,1);changed();}
    @Override public boolean mouseClicked(MouseButtonEvent e,boolean twice){if(e.button()==0&&e.x()>=x+12&&e.x()<x+12+leftW){if(e.y()>=bodyY&&e.y()<bodyY+svHeight)drag=0;else if(e.y()>=hueY&&e.y()<hueY+12)drag=1;if(drag>=0){pick(e.x(),e.y());return true;}}return super.mouseClicked(e,twice);}
    @Override public boolean mouseDragged(MouseButtonEvent e,double dx,double dy){if(drag>=0){pick(e.x(),e.y());return true;}return super.mouseDragged(e,dx,dy);}
    @Override public boolean mouseReleased(MouseButtonEvent e){if(drag>=0){drag=-1;OrbitClient.save();return true;}return super.mouseReleased(e);}
    void verifyLayouts(){for(role=0;role<3;role++){rebuildWidgets();SmokeChecks.verifyWidgets(this);}role=0;rebuildWidgets();}
    @Override public void onClose(){OrbitClient.save();minecraft.gui.setScreen(parent);}
    @Override public boolean isPauseScreen(){return false;}
}
