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
    String label(){return label;}
    OrbitSlider(int x,int y,int w,String label,double min,double max,double current,DoubleConsumer changed,DoubleFunction<String> format) {
        super(x,y,w,32,Component.literal(label),Math.clamp((current-min)/(max-min),0,1));
        this.label=label;this.min=min;this.max=max;this.changed=changed;this.format=format;updateMessage();
    }
    @Override protected void updateMessage() {
        if(format!=null) setMessage(Component.literal(label+": "+format.apply(min+value*(max-min))));
    }
    @Override protected void applyValue() { changed.accept(min+value*(max-min)); }
    @Override public void extractWidgetRenderState(GuiGraphicsExtractor g,int mx,int my,float delta) {
        var font=OrbitFonts.ui();
        OrbitTheme.rounded(g,getX(),getY(),width,height,OrbitMotion.mix(OrbitTheme.CARD,OrbitTheme.HOVER,hover.toward(isHoveredOrFocused())));
        if(isFocused())OrbitTheme.outline(g,getX(),getY(),width,height,OrbitTheme.FOCUS);
        String amount=format.apply(min+value*(max-min));
        int amountWidth=font.width(amount)+8;
        if(OrbitFonts.strong().width(label)+amountWidth+24<=width) {
            g.text(OrbitFonts.strong(),label,getX()+10,getY()+6,OrbitTheme.TEXT,false);
            g.text(font,amount,getX()+width-10-font.width(amount),getY()+6,OrbitTheme.ACCENT,false);
        } else ButtonLabel.draw(g,font,getMessage().getString(),getX()+8,getY()+3,width-16,13,OrbitTheme.TEXT);
        int start=getX()+10,end=getX()+width-10,y=getY()+24,knob=start+(int)((end-start)*value);
        g.fill(start,y-1,end,y+1,0xFF344158);g.fill(start,y-1,knob,y+1,OrbitTheme.PRIMARY);
        OrbitTheme.disc(g,knob-4,y-4,8,isHoveredOrFocused()?0xFFFFFFFF:OrbitTheme.ACCENT);
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
