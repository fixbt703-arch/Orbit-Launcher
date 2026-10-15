package org.orbitlauncher.client;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.narration.NarrationElementOutput;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.network.chat.Component;
import org.lwjgl.glfw.GLFW;

class OrbitButton extends AbstractWidget {
    private final Runnable action;
    private final boolean primary;
    private String icon;
    private String focusId;
    private boolean tab,iconOnly,leftAligned;
    private final OrbitMotion hover=new OrbitMotion();
    private OrbitMotion selection;
    private static final java.util.Map<net.minecraft.client.gui.screens.Screen,java.util.Map<String,OrbitMotion>> TABS=new java.util.WeakHashMap<>();
    OrbitButton(int x,int y,int w,int h,String text,boolean primary,Runnable action) {
        super(x,y,w,h,OrbitFonts.text(text)); this.action=action; this.primary=primary;
    }
    OrbitButton withIcon(String name){icon=name;return this;}
    OrbitButton focusId(String id){focusId=id;return this;}
    String focusKey(){return focusId!=null?focusId:getClass().getName()+":"+getMessage().getString();}
    OrbitButton asTab(){tab=true;return this;}
    OrbitButton alignLeft(){leftAligned=true;return this;}
    OrbitButton iconOnly(String name){icon=name;iconOnly=true;setTooltip(OrbitTheme.tooltip(getMessage()));setTooltipDelay(java.time.Duration.ofMillis(450));return this;}
    int textInset(){return icon!=null && !iconOnly?18:0;}
    net.minecraft.client.gui.Font labelFont(){return primary||tab?OrbitFonts.strong():OrbitFonts.ui();}
    @Override protected void extractWidgetRenderState(GuiGraphicsExtractor g,int mx,int my,float delta) {
        float amount=hover.toward(active&&isHoveredOrFocused());
        float selected=primary?1:0;
        if(tab){
            if(selection==null){
                var screen=Minecraft.getInstance().gui.screen();
                selection=screen==null?new OrbitMotion(primary):TABS.computeIfAbsent(screen,s->new java.util.HashMap<>()).computeIfAbsent(focusKey(),s->new OrbitMotion(primary));
            }
            selected=selection.toward(primary);
        }
        int color=!active?OrbitTheme.BASE:primary?(tab?OrbitTheme.SELECTED:OrbitMotion.mix(OrbitTheme.PRIMARY,OrbitTheme.PRIMARY_HOVER,amount)):OrbitMotion.mix(tab?0x00171E2E:OrbitTheme.CARD,OrbitTheme.HOVER,amount);
        if(tab&&active)color=OrbitMotion.mix(OrbitMotion.mix(0x00171E2E,OrbitTheme.HOVER,amount),OrbitTheme.SELECTED,selected);
        int border=!active?OrbitTheme.BORDER:OrbitMotion.mix(OrbitTheme.BORDER,OrbitTheme.FOCUS,amount);
        if(tab || iconOnly){OrbitTheme.rounded(g,getX(),getY(),width,height,color);if(tab&&selected>.001)g.fill(getX()+9,getY()+height-1,getX()+width-9,getY()+height,((int)(255*selected)<<24)|(OrbitTheme.PRIMARY&0xFFFFFF));if(isFocused())OrbitTheme.outline(g,getX(),getY(),width,height,OrbitTheme.ACCENT);}
        else OrbitTheme.surface(g,getX(),getY(),width,height,color,border);
        var font=labelFont();
        int textColor=!active?OrbitTheme.DISABLED:primary?(tab?OrbitTheme.ACCENT:OrbitTheme.ON_PRIMARY):iconOnly?OrbitMotion.mix(OrbitTheme.MUTED,OrbitTheme.TEXT,amount):OrbitTheme.TEXT;
        if(tab&&active)textColor=OrbitMotion.mix(OrbitTheme.TEXT,OrbitTheme.ACCENT,selected);
        String symbol=switch(getMessage().getString()){case "×" -> "close";case "<" -> "left";case ">" -> "right";default -> null;};
        if(iconOnly || symbol!=null)ModuleIcons.ui(g,iconOnly?icon:symbol,getX()+(width-14)/2,getY()+(height-14)/2,14,textColor);
        else {
            if(icon!=null){
                int available=Math.max(1,width-20-textInset());
                var label=ButtonLabel.layout(font,getMessage().getString(),available,height-6);
                float textWidth=label.lines().stream().mapToInt(font::width).max().orElse(0)*label.scale();
                int groupWidth=13+6+(int)Math.ceil(textWidth),groupX=getX()+(width-groupWidth)/2;
                ModuleIcons.ui(g,icon,groupX,getY()+(height-13)/2,13,textColor);
                ButtonLabel.left(g,font,getMessage().getString(),groupX+19,getY()+3,Math.max(1,groupWidth-19),height-6,textColor);
            }else if(leftAligned)ButtonLabel.left(g,font,getMessage().getString(),getX()+10,getY()+3,width-20,height-6,textColor);
            else ButtonLabel.draw(g,font,getMessage().getString(),getX()+5,getY()+3,width-10,height-6,textColor);
        }
    }
    @Override public void onClick(MouseButtonEvent event,boolean doubleClick) { action.run(); }
    @Override public boolean keyPressed(KeyEvent event) {
        if(active && (event.key()==GLFW.GLFW_KEY_ENTER || event.key()==GLFW.GLFW_KEY_SPACE)) { action.run(); return true; }
        return super.keyPressed(event);
    }
    @Override protected void updateWidgetNarration(NarrationElementOutput output) { defaultButtonNarrationText(output); }
}
