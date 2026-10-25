package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSliderButton;
import net.minecraft.network.chat.Component;
import java.util.function.DoubleConsumer;
import java.util.function.DoubleFunction;

final class OrbitSlider extends AbstractSliderButton {
    private final String label;
    private final double min;
    private double max;
    private final DoubleConsumer changed;
    private final DoubleFunction<String> format;
    private final OrbitMotion hover=new OrbitMotion();
    private boolean compact;
    OrbitSlider compact(){compact=true;setHeight(30);return this;}
    String label(){return label;}
    OrbitSlider(int x,int y,int w,String label,double min,double max,double current,DoubleConsumer changed,DoubleFunction<String> format) {
        super(x,y,w,32,Component.literal(label),Math.clamp((current-min)/(max-min),0,1));
        this.label=label;this.min=min;this.max=max;this.changed=changed;this.format=format;updateMessage();
    }
    @Override protected void updateMessage() {
        if(format!=null) {setMessage(Component.literal(label+": "+format.apply(min+value*(max-min))));setTooltip(OrbitTheme.tooltip(getMessage()));}
    }
    @Override protected void applyValue() { changed.accept(min+value*(max-min)); }
    @Override public void extractWidgetRenderState(GuiGraphicsExtractor g,int mx,int my,float delta) {
        var font=OrbitFonts.ui();
        int textColor=OrbitTheme.TEXT,accent=OrbitTheme.ACCENT;
        int background=OrbitMotion.mix(compact?OrbitTheme.translucent(OrbitTheme.BASE,242):OrbitTheme.CARD,OrbitTheme.HOVER,hover.toward(isHoveredOrFocused()));
        if(compact)g.fill(getX(),getY(),getX()+width,getY()+height,background);
        else OrbitTheme.rounded(g,getX(),getY(),width,height,background);
        if(isFocused()){
            if(compact)g.outline(getX(),getY(),width,height,accent);
            else OrbitTheme.outline(g,getX(),getY(),width,height,OrbitTheme.FOCUS);
        }
        String amount=format.apply(min+value*(max-min));
        int amountWidth=font.width(amount)+8;
        ButtonLabel.left(g,font,label,getX()+10,getY()+2,Math.max(1,width-24-amountWidth),19,textColor);
        g.text(font,amount,getX()+width-10-font.width(amount),getY()+7,accent,false);
        int start=getX()+10,end=getX()+width-10,y=getY()+24,knob=start+(int)((end-start)*value);
        g.fill(start,y-1,end,y+1,OrbitTheme.CONTROL);g.fill(start,y-1,knob,y+1,compact?accent:OrbitTheme.PRIMARY);
        OrbitTheme.disc(g,knob-4,y-4,8,isHoveredOrFocused()?0xFFFFFFFF:accent);
    }
    private void setFromMouse(double mouseX){value=Math.clamp((mouseX-getX()-10)/(width-20),0,1);applyValue();updateMessage();}
    void maximum(double maximum){double current=min+value*(max-min);max=Math.max(min+.001,maximum);value=Math.clamp((current-min)/(max-min),0,1);applyValue();updateMessage();}
    @Override public void onClick(net.minecraft.client.input.MouseButtonEvent event,boolean twice){setFromMouse(event.x());}
    @Override protected void onDrag(net.minecraft.client.input.MouseButtonEvent event,double dx,double dy){setFromMouse(event.x());}
    static void verifyPointerMapping(){
        double[] output={-1};var slider=new OrbitSlider(20,20,200,"Test",0,100,50,v->output[0]=v,v->"50");
        for(double fraction:new double[]{0,.25,.5,.75,1}){
            slider.setFromMouse(30+fraction*180);
            if(Math.abs(output[0]-fraction*100)>.001)throw new IllegalStateException("Slider hit position differs from its knob");
        }
    }
}
