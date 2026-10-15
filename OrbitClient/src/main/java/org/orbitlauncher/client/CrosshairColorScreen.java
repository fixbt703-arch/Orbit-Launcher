package org.orbitlauncher.client;

import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;
import java.awt.Color;
import static org.orbitlauncher.client.OrbitClient.tr;

/** Mouse/drag HSV picker with an exact HEX alternative and live target previews. */
final class CrosshairColorScreen extends Screen {
    private final Screen parent;
    private int x,y,w,h,leftW,rightX,rightW,bodyY,svHeight,hueY,footer,role,drag=-1;
    private float hue,saturation,value;
    private EditBox hex;
    private boolean syncing;
    private final OrbitPageTransition pageTransition=new OrbitPageTransition();
    CrosshairColorScreen(Screen parent){super(net.minecraft.client.Minecraft.getInstance(),OrbitFonts.ui(),Component.literal(tr("Цвет прицела","Crosshair color")));this.parent=parent;}
    private ClientConfig.State state(){return OrbitClient.config.state(Module.CROSSHAIR);}
    private int color(){return role==1?state().enemyColor:role==2?state().friendlyColor:ClientConfig.accent(state())&0xFFFFFF;}
    private void readColor(){int rgb=color();float[] hsv=Color.RGBtoHSB(rgb>>16&255,rgb>>8&255,rgb&255,null);hue=hsv[0];saturation=hsv[1];value=hsv[2];}
    @Override protected void init(){
        w=Math.min(width-24,520);h=Math.min(height-20,320);x=(width-w)/2;y=(height-h)/2;
        leftW=(w-36)*3/5;rightX=x+24+leftW;rightW=w-leftW-36;bodyY=y+64;footer=y+h-28;
        svHeight=Math.max(20,footer-bodyY-22);hueY=bodyY+svHeight+5;
        if(state().crosshairColorMode!=1)role=0;
        if(role==2&&!state().separateFriendlyColor)role=1;
        readColor();
        String[] modes={tr("Обычный цвет","Static color"),tr("По цели","By target"),tr("Инверсия","Inversion")};
        addRenderableWidget(new OrbitButton(x+w-116,y+9,104,22,modes[state().crosshairColorMode],false,()->{state().crosshairColorMode=(state().crosshairColorMode+1)%3;OrbitClient.save();rebuildWidgets();}));
        int count=state().crosshairColorMode==1?(state().separateFriendlyColor?3:2):1,tw=(w-24-(count-1)*4)/count;
        String[] names={tr("Без цели","No target"),tr("При наведении","On target"),tr("Союзник","Friendly")};
        for(int i=0;i<count;i++){final int id=i;addRenderableWidget(new OrbitButton(x+12+i*(tw+4),y+36,tw,20,count==1?tr("Цвет прицела","Crosshair color"):names[i],role==i,()->{role=id;rebuildWidgets();pageTransition.start();}).asTab());}
        hex=new EditBox(font,rightX+29,bodyY+7,rightW-34,14,Component.literal("HEX #RRGGBB"));hex.setBordered(false);
        hex.setMaxLength(7);hex.setTextColor(0xFFE6EDFA);hex.setValue(String.format("#%06X",color()));
        hex.setResponder(text->{if(syncing)return;boolean valid=text.matches("#?[0-9a-fA-F]{6}");hex.setTextColor(valid?0xFFE6EDFA:0xFFFF718B);if(valid){setColor(Integer.parseInt(text.replace("#",""),16));readColor();}});
        hex.setEditable(state().crosshairColorMode!=2);addRenderableWidget(hex);
        addRenderableWidget(new OrbitSlider(rightX,bodyY+30,rightW,tr("Непрозрачность","Opacity"),0,1,state().opacity,v->state().opacity=(float)v,v->Math.round(v*100)+"%"));
        addRenderableWidget(new OrbitButton(x+w-76,footer,64,22,tr("Готово","Done"),true,this::onClose));
    }
    private void setColor(int rgb){
        if(role==1)state().enemyColor=rgb;
        else if(role==2)state().friendlyColor=rgb;
        else {state().customColor=rgb;state().crosshairChroma=false;}
    }
    private void changed(){setColor(Color.HSBtoRGB(hue,saturation,value)&0xFFFFFF);syncing=true;hex.setValue(String.format("#%06X",color()));hex.setTextColor(0xFFE6EDFA);syncing=false;}
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){
        OrbitTheme.window(g,width,height,x,y,w,h);
        OrbitTheme.surface(g,rightX+24,bodyY,rightW-24,24,OrbitTheme.CARD,hex.isFocused()?OrbitTheme.ACCENT:OrbitTheme.BORDER);
        g.text(OrbitFonts.heading(),OrbitTheme.ellipsize(OrbitFonts.heading(),tr("Цвет прицела","Crosshair color"),w-144),x+12,y+13,OrbitTheme.TEXT,false);
        OrbitTheme.colorPicker(g,x+12,bodyY,leftW,svHeight,hueY,hue);
        int sx=x+12+Math.round(saturation*(leftW-1)),sy=bodyY+Math.round((1-value)*(svHeight-1));
        OrbitTheme.pickerMarker(g,sx,sy,0xFF000000|color());
        int hx=x+12+Math.round(hue*(leftW-1));OrbitTheme.rounded(g,hx-2,hueY-1,5,14,0xFFFFFFFF);OrbitTheme.rounded(g,hx-1,hueY,3,12,java.awt.Color.HSBtoRGB(hue,1,1));
        OrbitTheme.rounded(g,rightX,bodyY,20,24,0xFF000000|color());
        if(state().crosshairColorMode==2){g.fill(x+12,bodyY,x+12+leftW,hueY+12,0xCE101B2D);g.textWithWordWrap(font,Component.literal(tr("Инверсия использует цвет фона. Для выбора цвета переключите режим.","Inversion uses the background. Switch modes to pick a color.")),x+18,bodyY+6,leftW-12,OrbitTheme.TEXT);}
        int normalWidth=previewWidth(tr("Обычный","Normal"));
        preview(g,x+12,footer,normalWidth,0,tr("Обычный","Normal"));
        if(state().crosshairColorMode==1)preview(g,x+16+normalWidth,footer,previewWidth(tr("Цель","Target")),role==2?2:1,tr("Цель","Target"));
        super.extractRenderState(g,mx,my,delta);pageTransition.finish(g,x+12,bodyY,w-24,footer-bodyY-4);
    }
    private int previewWidth(String label){return Math.min(Math.max(72,font.width(label)+32),(w-100)/2);}
    private void preview(GuiGraphicsExtractor g,int px,int py,int pw,int target,String label){
        HudRenderer.panel(g,px,py,pw,22,OrbitTheme.CARD);ButtonLabel.draw(g,font,label,px+4,py+3,pw-30,16,OrbitTheme.TEXT);
        g.enableScissor(px+pw-25,py+1,px+pw-1,py+21);g.pose().pushMatrix();g.pose().translate(px+pw-13,py+11);g.pose().scale(.4f,.4f);CrosshairRenderer.draw(g,0,0,state(),false,target);g.pose().popMatrix();g.disableScissor();
    }
    private void pick(double mx,double my){
        if(drag==0){saturation=(float)Math.clamp((mx-x-12)/(leftW-1),0,1);value=1-(float)Math.clamp((my-bodyY)/(svHeight-1),0,1);}
        else if(drag==1)hue=(float)Math.clamp((mx-x-12)/(leftW-1),0,1);
        changed();
    }
    @Override public boolean mouseClicked(MouseButtonEvent e,boolean twice){
        if(e.button()==0 && state().crosshairColorMode!=2 && e.x()>=x+12&&e.x()<x+12+leftW){
            if(e.y()>=bodyY&&e.y()<bodyY+svHeight)drag=0;
            else if(e.y()>=hueY&&e.y()<hueY+12)drag=1;
            if(drag>=0){pick(e.x(),e.y());return true;}
        }return super.mouseClicked(e,twice);
    }
    @Override public boolean mouseDragged(MouseButtonEvent e,double dx,double dy){if(drag>=0){pick(e.x(),e.y());return true;}return super.mouseDragged(e,dx,dy);}
    @Override public boolean mouseReleased(MouseButtonEvent e){if(drag>=0){drag=-1;OrbitClient.save();return true;}return super.mouseReleased(e);}
    void verifyLayouts(){int old=state().crosshairColorMode;boolean friendly=state().separateFriendlyColor;for(int mode=0;mode<3;mode++){state().crosshairColorMode=mode;state().separateFriendlyColor=true;rebuildWidgets();SmokeChecks.verifyWidgets(this);}state().crosshairColorMode=old;state().separateFriendlyColor=friendly;}
    void verifyInput(){
        var s=state();s.crosshairColorMode=1;role=0;rebuildWidgets();hex.setValue("#12AB34");
        if(s.customColor!=0x12AB34)throw new IllegalStateException("Base color input failed");
        role=1;rebuildWidgets();hex.setValue("#FA6611");
        if(s.enemyColor!=0xFA6611 || s.customColor!=0x12AB34)throw new IllegalStateException("Target color is not independent");
        hex.setValue("#bad");if(s.enemyColor!=0xFA6611)throw new IllegalStateException("Invalid HEX changed color");
        role=0;rebuildWidgets();drag=0;pick(x+12+(leftW-1)*.5,bodyY+(svHeight-1)*.5);drag=-1;
        int selected=s.customColor;OrbitClient.save();
        var loaded=ClientConfig.load().state(Module.CROSSHAIR);
        if(loaded.customColor!=selected || loaded.enemyColor!=0xFA6611)throw new IllegalStateException("Colors did not survive save/load");
        OrbitClient.LOG.info("Crosshair color: HSV input, independent target color, invalid HEX and persistence passed");
    }
    @Override public void onClose(){OrbitClient.save();minecraft.gui.setScreen(parent);}
    @Override public boolean isPauseScreen(){return false;}
}
