package org.orbitlauncher.client;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;
import java.awt.Color;
import static org.orbitlauncher.client.OrbitClient.tr;

final class FeatureColorScreen extends Screen {
    static final int OUTLINE=0,TARGET=1,FILL=2,TARGET_FILL=3,FRIENDLY=4,HURT=5,GLOW_OUTLINE=6;
    private final Screen parent;private final Module module;private final int role;
    private int x,y,w,h,pickerW,pickerH,bodyY,hueY,rightX,rightW,drag=-1;
    private float hue,saturation,value;private boolean syncing;private EditBox hex;
    FeatureColorScreen(Screen parent,Module module,boolean target){this(parent,module,target?TARGET:OUTLINE);}
    FeatureColorScreen(Screen parent,Module module,int role){super(net.minecraft.client.Minecraft.getInstance(),OrbitFonts.ui(),Component.literal(module.title()));this.parent=parent;this.module=module;this.role=role;}
    private ClientConfig.State state(){return OrbitClient.config.state(module);}
    private int color(){return color(state());}
    private int color(ClientConfig.State s){return module==Module.GLINT?(role==GLOW_OUTLINE?s.glowOutlineColor:s.glintColor):switch(role){case TARGET->s.hitboxTargetColor;case FILL->s.hitboxFillColor;case TARGET_FILL->s.hitboxTargetFillColor;case FRIENDLY->s.hitboxFriendlyColor;case HURT->s.hitboxHurtColor;default->s.hitboxColor;};}
    private void set(int rgb){var s=state();if(module==Module.GLINT){if(role==GLOW_OUTLINE)s.glowOutlineColor=rgb;else s.glintColor=rgb;}else switch(role){case TARGET->s.hitboxTargetColor=rgb;case FILL->s.hitboxFillColor=rgb;case TARGET_FILL->s.hitboxTargetFillColor=rgb;case FRIENDLY->s.hitboxFriendlyColor=rgb;case HURT->s.hitboxHurtColor=rgb;default->s.hitboxColor=rgb;}}
    private float opacity(){var s=state();return module==Module.GLINT?(role==GLOW_OUTLINE?s.glowOutlineOpacity:s.glintOpacity):switch(role){case TARGET->s.hitboxTargetOpacity;case FILL->s.hitboxFillOpacity;case TARGET_FILL->s.hitboxTargetFillOpacity;default->s.hitboxOpacity;};}
    private void opacity(double value){var s=state();if(module==Module.GLINT){if(role==GLOW_OUTLINE)s.glowOutlineOpacity=(float)value;else s.glintOpacity=(float)value;}else switch(role){case TARGET->s.hitboxTargetOpacity=(float)value;case FILL->s.hitboxFillOpacity=(float)value;case TARGET_FILL->s.hitboxTargetFillOpacity=(float)value;default->s.hitboxOpacity=(float)value;}}
    private String roleName(){return module==Module.GLINT?(role==GLOW_OUTLINE?tr("Обводка","Outline"):tr("Сияние","Glint")):switch(role){case TARGET->tr("Цель","Target");case FILL->tr("Заливка","Fill");case TARGET_FILL->tr("Заливка цели","Target fill");case FRIENDLY->tr("Союзники","Allies");case HURT->tr("Получение урона","Damage");default->tr("Обводка","Outline");};}
    private void read(){int c=color();float[] hsv=Color.RGBtoHSB(c>>16&255,c>>8&255,c&255,null);hue=hsv[0];saturation=hsv[1];value=hsv[2];}
    @Override protected void init(){
        w=Math.min(width-24,520);h=Math.min(height-20,320);x=(width-w)/2;y=(height-h)/2;bodyY=y+44;pickerW=(w-36)*3/5;rightX=x+24+pickerW;rightW=w-pickerW-36;pickerH=Math.max(24,h-96);hueY=bodyY+pickerH+8;read();
        hex=new EditBox(font,rightX+8,bodyY+8,rightW-16,14,Component.literal("HEX #RRGGBB"));hex.setBordered(false);hex.setMaxLength(7);hex.setTextColor(OrbitTheme.TEXT);hex.setValue(String.format("#%06X",color()));
        hex.setResponder(v->{if(syncing)return;boolean valid=v.matches("#?[0-9a-fA-F]{6}");hex.setTextColor(valid?OrbitTheme.TEXT:0xFFFF718B);if(valid){set(Integer.parseInt(v.replace("#",""),16));read();}});addRenderableWidget(hex);
        addRenderableWidget(new OrbitSlider(rightX,bodyY+34,rightW,tr("Непрозрачность","Opacity"),0,module==Module.HITBOX&&(role==FILL||role==TARGET_FILL)?.5:1,opacity(),this::opacity,v->Math.round(v*100)+"%"));
        addRenderableWidget(new OrbitButton(x+w-76,y+h-28,64,22,tr("Готово","Done"),true,this::onClose));
    }
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){
        OrbitTheme.window(g,width,height,x,y,w,h);ButtonLabel.left(g,OrbitFonts.strong(),module.title()+" / "+roleName(),x+12,y+10,w-24,24,OrbitTheme.TEXT);
        OrbitTheme.surface(g,rightX,bodyY,rightW,28,OrbitTheme.CARD,hex.isFocused()?OrbitTheme.FOCUS:OrbitTheme.BORDER);
        OrbitTheme.colorPicker(g,x+12,bodyY,pickerW,pickerH,hueY,hue);OrbitTheme.pickerMarker(g,x+12+Math.round(saturation*(pickerW-1)),bodyY+Math.round((1-value)*(pickerH-1)),0xFF000000|color());
        int hx=x+12+Math.round(hue*(pickerW-1));g.outline(hx-1,hueY-1,3,14,OrbitTheme.TEXT);
        if(h>=240){OrbitTheme.rounded(g,rightX,bodyY+80,rightW,52,0xFF000000|color());g.text(font,tr("Выбранный цвет","Selected color"),rightX,bodyY+143,OrbitTheme.MUTED,false);}
        super.extractRenderState(g,mx,my,delta);
    }
    private void pick(double mx,double my){if(drag==0){saturation=(float)Math.clamp((mx-x-12)/(pickerW-1),0,1);value=1-(float)Math.clamp((my-bodyY)/(pickerH-1),0,1);}else hue=(float)Math.clamp((mx-x-12)/(pickerW-1),0,1);set(Color.HSBtoRGB(hue,saturation,value)&0xFFFFFF);syncing=true;hex.setValue(String.format("#%06X",color()));hex.setTextColor(OrbitTheme.TEXT);syncing=false;}
    @Override public boolean mouseClicked(MouseButtonEvent e,boolean twice){if(e.button()==0&&e.x()>=x+12&&e.x()<x+12+pickerW){if(e.y()>=bodyY&&e.y()<bodyY+pickerH)drag=0;else if(e.y()>=hueY&&e.y()<hueY+12)drag=1;if(drag>=0){pick(e.x(),e.y());return true;}}return super.mouseClicked(e,twice);}
    @Override public boolean mouseDragged(MouseButtonEvent e,double dx,double dy){if(drag>=0){pick(e.x(),e.y());return true;}return super.mouseDragged(e,dx,dy);}
    @Override public boolean mouseReleased(MouseButtonEvent e){if(drag>=0){drag=-1;OrbitClient.save();return true;}return super.mouseReleased(e);}
    void verifyInput(){
        int original=color();hex.setValue("#12AB34");
        if(color()!=0x12AB34)throw new IllegalStateException("Feature HEX input failed: "+module);
        hex.setValue("#invalid");if(color()!=0x12AB34)throw new IllegalStateException("Invalid feature HEX accepted: "+module);
        drag=0;pick(x+12+(pickerW-1)*.5,bodyY+(pickerH-1)*.5);drag=-1;
        OrbitClient.save();var loaded=ClientConfig.load().state(module);
        int persisted=color(loaded);
        if(persisted!=color())throw new IllegalStateException("Feature color not persisted: "+module);
        set(original);OrbitClient.save();
    }
    @Override public void onClose(){OrbitClient.save();minecraft.gui.setScreen(parent);}
    @Override public boolean isPauseScreen(){return false;}
}
