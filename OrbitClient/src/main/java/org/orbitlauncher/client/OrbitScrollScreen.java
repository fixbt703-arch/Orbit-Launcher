package org.orbitlauncher.client;

import java.util.*;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Renderable;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.gui.narration.NarratableEntry;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.gui.components.EditBox;
import org.lwjgl.glfw.GLFW;
import net.minecraft.network.chat.Component;

/** A clipped, continuously scrolling content region. Header and footer never move. */
abstract class OrbitScrollScreen extends Screen {
    private record Entry(AbstractWidget widget,int y){}
    private final List<Entry> content=new ArrayList<>();
    protected int viewX,viewY,viewWidth,viewHeight;
    private boolean capture,dragging,focusTraversal;
    private double scroll,target,maxScroll,dragAnchor;
    private long lastFrame;
    private final OrbitPageTransition pageTransition=new OrbitPageTransition();
    protected OrbitScrollScreen(Component title){super(net.minecraft.client.Minecraft.getInstance(),OrbitFonts.ui(),title);}
    protected void contentRegion(int x,int y,int w,int h){content.clear();viewX=x;viewY=y;viewWidth=w;viewHeight=Math.max(1,h);}
    protected void content(Runnable factory){capture=true;try{factory.run();}finally{capture=false;}}
    @Override protected <T extends GuiEventListener & Renderable & NarratableEntry> T addRenderableWidget(T widget){
        if(!capture)return super.addRenderableWidget(widget);
        super.addWidget(widget);content.add(new Entry((AbstractWidget)widget,((AbstractWidget)widget).getY()));return widget;
    }
    protected void contentHeight(int height){maxScroll=Math.max(0,height-viewHeight);target=Math.clamp(target,0,maxScroll);scroll=Math.clamp(scroll,0,maxScroll);positionContent();}
    protected void resetScroll(){scroll=target=0;}
    protected void switchContent(Runnable change){change.run();pageTransition.start();}
    private static String focusKey(AbstractWidget widget){
        return widget instanceof OrbitSwitch toggle?"switch:"+toggle.label:
            widget instanceof OrbitSlider slider?"slider:"+slider.label():widget instanceof OrbitButton button?button.focusKey():widget.getClass().getName()+":"+widget.getMessage().getString();
    }
    @Override protected void rebuildWidgets(){
        String previous=getFocused() instanceof AbstractWidget widget?focusKey(widget):null;
        super.rebuildWidgets();
        if(previous!=null)for(var child:children())if(child instanceof AbstractWidget widget&&focusKey(widget).equals(previous)){setFocused(widget);break;}
    }
    protected int scrollOffset(){return (int)Math.round(scroll);}
    protected void animateScroll(){
        long now=System.nanoTime();double dt=lastFrame==0?0:(now-lastFrame)/1e9;lastFrame=now;
        scroll+=(target-scroll)*(1-Math.exp(-Math.min(dt,.1)*18));if(Math.abs(scroll-target)<.05)scroll=target;positionContent();
    }
    private void positionContent(){for(var e:content){e.widget.setY(e.y-scrollOffset());e.widget.visible=e.widget.getY()+e.widget.getHeight()>viewY&&e.widget.getY()<viewY+viewHeight;}}
    protected void renderContent(GuiGraphicsExtractor g,int mx,int my,float delta){
        g.enableScissor(viewX,viewY,viewX+viewWidth,viewY+viewHeight);
        boolean inside=inContent(mx,my);for(var e:content)e.widget.extractRenderState(g,inside?mx:-10000,inside?my:-10000,delta);
        g.disableScissor();
        pageTransition.finish(g,viewX,viewY,viewWidth,viewHeight);
        if(maxScroll>0){int h=thumbHeight(),y=viewY+(int)Math.round(scroll/maxScroll*(viewHeight-h));OrbitTheme.rounded(g,viewX+viewWidth-3,viewY,3,viewHeight,OrbitTheme.CARD);OrbitTheme.rounded(g,viewX+viewWidth-3,y,3,h,dragging?OrbitTheme.ACCENT:OrbitTheme.FOCUS);}
    }
    private int thumbHeight(){return Math.max(14,(int)Math.round(viewHeight*viewHeight/(viewHeight+maxScroll)));}
    protected boolean inContent(double x,double y){return x>=viewX&&x<viewX+viewWidth&&y>=viewY&&y<viewY+viewHeight;}
    boolean isContentWidget(AbstractWidget widget){return content.stream().anyMatch(e->e.widget==widget);}
    int[] clippedBounds(AbstractWidget widget){return new int[]{Math.max(widget.getX(),viewX),Math.max(widget.getY(),viewY),Math.min(widget.getX()+widget.getWidth(),viewX+viewWidth),Math.min(widget.getY()+widget.getHeight(),viewY+viewHeight)};}
    @Override public boolean mouseScrolled(double x,double y,double horizontal,double vertical){
        if(!inContent(x,y)||vertical==0||maxScroll==0)return super.mouseScrolled(x,y,horizontal,vertical);
        target=Math.clamp(target-vertical*42,0,maxScroll);return true;
    }
    @Override public boolean mouseClicked(MouseButtonEvent e,boolean twice){
        if(e.button()==0&&maxScroll>0&&inContent(e.x(),e.y())&&e.x()>=viewX+viewWidth-7){
            int thumbY=viewY+(int)Math.round(scroll/maxScroll*(viewHeight-thumbHeight()));
            if(e.y()<thumbY||e.y()>thumbY+thumbHeight()){scroll=target=Math.clamp((e.y()-viewY-thumbHeight()/2.0)*maxScroll/Math.max(1,viewHeight-thumbHeight()),0,maxScroll);positionContent();}
            dragging=true;dragAnchor=e.y();target=scroll;return true;
        }
        if(inContent(e.x(),e.y()))return super.mouseClicked(e,twice);
        var visibility=content.stream().map(entry->entry.widget.visible).toList();for(var entry:content)entry.widget.visible=false;
        try{return super.mouseClicked(e,twice);}finally{for(int i=0;i<content.size();i++)content.get(i).widget.visible=visibility.get(i);}
    }
    @Override public boolean mouseDragged(MouseButtonEvent e,double dx,double dy){
        if(!dragging)return super.mouseDragged(e,dx,dy);
        scroll=target=Math.clamp(scroll+(e.y()-dragAnchor)*maxScroll/Math.max(1,viewHeight-thumbHeight()),0,maxScroll);dragAnchor=e.y();positionContent();return true;
    }
    @Override public boolean mouseReleased(MouseButtonEvent e){if(dragging){dragging=false;return true;}return super.mouseReleased(e);}
    @Override public boolean keyPressed(KeyEvent e){
        if(e.key()==GLFW.GLFW_KEY_TAB){
            focusTraversal=true;
            for(var entry:content)entry.widget.visible=true;
            try{return super.keyPressed(e);}finally{focusTraversal=false;positionContent();}
        }
        if(!(getFocused() instanceof EditBox)&&maxScroll>0){
            switch(e.key()){
                case GLFW.GLFW_KEY_PAGE_DOWN -> {target=Math.min(maxScroll,target+viewHeight*.85);return true;}
                case GLFW.GLFW_KEY_PAGE_UP -> {target=Math.max(0,target-viewHeight*.85);return true;}
                case GLFW.GLFW_KEY_HOME -> {target=0;return true;}
                case GLFW.GLFW_KEY_END -> {target=maxScroll;return true;}
            }
        }
        return super.keyPressed(e);
    }
    @Override public void setFocused(GuiEventListener child){
        if(child instanceof AbstractWidget old&&!children().contains(child)){
            String key=focusKey(old);child=null;
            for(var candidate:children())if(candidate instanceof AbstractWidget widget&&focusKey(widget).equals(key)){child=widget;break;}
            if(child==null&&children().contains(getFocused()))child=getFocused();
        }
        super.setFocused(child);
        if(child instanceof AbstractWidget w&&isContentWidget(w)){
            if(w.getY()<viewY)target=Math.clamp(target+w.getY()-viewY,0,maxScroll);
            if(w.getY()+w.getHeight()>viewY+viewHeight)target=Math.clamp(target+w.getY()+w.getHeight()-viewY-viewHeight,0,maxScroll);
            if(focusTraversal){scroll=target;positionContent();}
        }
    }
    protected void verifyScrollLayouts(){
        double saved=scroll,savedTarget=target;
        for(double amount:new double[]{0,maxScroll*.5,maxScroll}){scroll=target=amount;positionContent();SmokeChecks.verifyWidgets(this);}
        scroll=saved;target=savedTarget;positionContent();
    }
    void verifyWheel(){
        if(maxScroll<=42)throw new IllegalStateException("Scroll test has no overflow");
        resetScroll();positionContent();mouseScrolled(viewX+10,viewY+10,0,-1);
        if(scroll!=0||target!=42)throw new IllegalStateException("Wheel jumps instead of animating");
        lastFrame=System.nanoTime()-16_000_000;animateScroll();
        if(scroll<=0||scroll>=42)throw new IllegalStateException("Scroll interpolation failed");
        mouseScrolled(viewX-10,viewY-10,0,-1);if(target!=42)throw new IllegalStateException("Wheel escaped its viewport");
        resetScroll();positionContent();lastFrame=0;
    }
}
