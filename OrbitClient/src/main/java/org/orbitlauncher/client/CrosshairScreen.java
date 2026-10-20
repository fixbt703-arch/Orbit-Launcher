package org.orbitlauncher.client;

import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.network.chat.Component;
import org.lwjgl.glfw.GLFW;
import java.util.*;
import java.util.function.IntConsumer;
import static org.orbitlauncher.client.OrbitClient.tr;

final class CrosshairScreen extends OrbitScrollScreen {
    private final Screen parent;
    private int x,y,w,h,leftW,rightX,rightW,footer,gridX,gridY,cell,background;
    private int paint=-1,lastX=-1,lastY=-1;
    private String status="";
    CrosshairScreen(Screen parent){super(Component.literal(tr("Прицел","Crosshair")));this.parent=parent;}
    private ClientConfig.State state(){return OrbitClient.config.state(Module.CROSSHAIR);}
    @Override protected void init() {
        w=Math.min(width-20,680);h=Math.min(height-20,410);x=(width-w)/2;y=(height-h)/2;
        footer=y+h-28;leftW=(w-36)*3/5;rightX=x+24+leftW;rightW=w-leftW-36;
        var s=state();
        addRenderableWidget(new OrbitButton(x+w-140,y+8,50,20,tr("Цвет","Color"),false,()->{OrbitClient.save();minecraft.gui.setScreen(new CrosshairColorScreen(this));}));
        addRenderableWidget(new OrbitButton(x+w-86,y+8,74,20,tr("Параметры","Settings"),false,()->{OrbitClient.save();minecraft.gui.setScreen(new ModuleScreen(this,Module.CROSSHAIR));}));
        String[] tabs={tr("Простой","Simple"),tr("Готовые","Presets"),tr("Свой","Custom")};int tw=(leftW-8)/3;
        for(int i=0;i<3;i++){final int tab=i;addRenderableWidget(new OrbitButton(x+12+i*(tw+4),y+33,tw,20,tabs[i],s.crosshairMode==i,()->switchContent(()->{s.crosshairMode=tab;resetScroll();status="";OrbitClient.save();rebuildWidgets();})).asTab());}
        int bodyY=y+59,bodyH=footer-bodyY-15;
        contentRegion(x+12,bodyY,leftW,bodyH);contentHeight(0);
        gridX=x+12;gridY=bodyY;cell=Math.max(1,Math.min(leftW/15,bodyH/15));
        gridX+=(leftW-cell*15)/2;gridY+=(bodyH-cell*15)/2;
        if(s.crosshairMode==0) {
            var rows=new ArrayList<IntConsumer>();
            rows.add(yy->addRenderableWidget(new OrbitSlider(x+12,yy,leftW-8,tr("Размер","Size"),2,12,s.crosshairSize,v->s.crosshairSize=(int)Math.round(v),v->Long.toString(Math.round(v)))));
            rows.add(yy->addRenderableWidget(new OrbitSlider(x+12,yy,leftW-8,tr("Толщина","Thickness"),1,3,s.crosshairThickness,v->s.crosshairThickness=(int)Math.round(v),v->Long.toString(Math.round(v)))));
            rows.add(yy->addRenderableWidget(new OrbitSlider(x+12,yy,leftW-8,tr("Зазор","Gap"),0,8,s.crosshairGap,v->s.crosshairGap=(int)Math.round(v),v->Long.toString(Math.round(v)))));
            rows.add(yy->addRenderableWidget(new OrbitSwitch(x+12,yy,leftW-8,26,tr("Точка в центре","Center dot"),s.crosshairDot,()->{s.crosshairDot=!s.crosshairDot;OrbitClient.save();rebuildWidgets();})));
            rows.add(yy->addRenderableWidget(new OrbitSwitch(x+12,yy,leftW-8,26,tr("Обводка","Outline"),s.crosshairOutline,()->{s.crosshairOutline=!s.crosshairOutline;OrbitClient.save();rebuildWidgets();})));
            content(()->{for(int i=0;i<rows.size();i++)rows.get(i).accept(bodyY+i*36);});contentHeight(rows.size()*36-4);
        } else if(s.crosshairMode==1) {
            int columns=Math.max(1,(leftW-8)/40);
            content(()->{for(int i=0;i<14;i++)addRenderableWidget(new PresetButton(x+12+i%columns*40,bodyY+i/columns*40,i));});
            contentHeight(((14+columns-1)/columns)*40-4);
        }
        if(s.crosshairMode==2) {
            addRenderableWidget(new OrbitButton(x+12,footer,44,22,tr("Очистить","Clear"),false,this::clear));
            addRenderableWidget(new OrbitButton(x+60,footer,44,22,tr("Копия","Copy"),false,()->{minecraft.keyboardHandler.setClipboard(CrosshairPattern.encode(CrosshairPattern.pixels(s)));status=tr("Код скопирован","Code copied");}));
            addRenderableWidget(new OrbitButton(x+108,footer,44,22,tr("Вставить","Paste"),false,()->{
                try{s.crosshairPixels=CrosshairPattern.decode(minecraft.keyboardHandler.getClipboard().trim());OrbitClient.save();status=tr("Прицел загружен","Crosshair loaded");}
                catch(IllegalArgumentException e){status=tr("Нужен код ORBCH-15","Use an ORBCH-15 code");}
            }));
        } else {
            if(s.crosshairMode==1)addRenderableWidget(new OrbitButton(x+12,footer,90,22,tr("Редактировать","Edit pixels"),false,()->{s.crosshairPixels=CrosshairPattern.preset(s.crosshairPreset);s.crosshairMode=2;OrbitClient.save();rebuildWidgets();}));
        }
        addRenderableWidget(new OrbitButton(x+w-70,footer,58,22,tr("Готово","Done"),true,this::onClose));
        int bw=Math.max(12,(rightW-9)/4);
        for(int i=0;i<4;i++){final int id=i;var button=new OrbitColorButton(rightX+i*(bw+3),footer-23,bw,BACKGROUNDS[i],background==id,()->{background=id;rebuildWidgets();});button.setHeight(18);addRenderableWidget(button);}
    }
    private final class PresetButton extends OrbitButton {
        private final int id;
        PresetButton(int xx,int yy,int id){super(xx,yy,36,36,"",state().crosshairPreset==id,()->{state().crosshairPreset=id;OrbitClient.save();rebuildWidgets();});this.id=id;setMessage(Component.literal(tr("Прицел ","Crosshair ")+(id+1)));}
        @Override protected void extractWidgetRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){
            OrbitTheme.surface(g,getX(),getY(),width,height,state().crosshairPreset==id?OrbitTheme.SELECTED:isHoveredOrFocused()?OrbitTheme.HOVER:OrbitTheme.CARD,state().crosshairPreset==id?OrbitTheme.ACCENT:OrbitTheme.BORDER);
            CrosshairRenderer.pattern(g,CrosshairPattern.preset(id),getX()+10,getY()+10,1,0xFFF6F8FF);
        }
    }
    private static final int[] BACKGROUNDS={0xFF101927,0xFF25344A,0xFF674D3D,0xFFD7E4E7};
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta) {
        animateScroll();
        OrbitTheme.window(g,width,height,x,y,w,h);
        g.text(OrbitFonts.heading(),OrbitTheme.ellipsize(OrbitFonts.heading(),tr("Редактор прицела","Crosshair editor"),w-160),x+12,y+13,OrbitTheme.TEXT,false);
        g.text(font,tr("Предпросмотр","Preview"),rightX,y+39,OrbitTheme.MUTED,false);
        int py=y+59,ph=Math.max(20,footer-29-py);OrbitTheme.surface(g,rightX,py,rightW,ph,BACKGROUNDS[background],OrbitTheme.BORDER);
        g.enableScissor(rightX+6,py+6,rightX+rightW-6,py+ph-6);
        for(int yy=py+6;yy<py+ph-6;yy+=18)for(int xx=rightX+6;xx<rightX+rightW-6;xx+=18)g.fill(xx,yy,xx+1,yy+1,0x446D7D9E);
        int px=mx>=rightX&&mx<rightX+rightW&&my>=py&&my<py+ph?mx:rightX+rightW/2;
        int cy=mx>=rightX&&mx<rightX+rightW&&my>=py&&my<py+ph?my:py+ph/2;
        CrosshairRenderer.draw(g,px,cy,state(),false,0);g.disableScissor();
        if(state().crosshairMode==2) {
            String bits=CrosshairPattern.pixels(state());
            for(int row=0;row<15;row++)for(int col=0;col<15;col++) {
                int xx=gridX+col*cell,yy=gridY+row*cell;
                g.fill(xx,yy,xx+cell,yy+cell,bits.charAt(row*15+col)=='1'?0xFFF4F7FF:((row==7||col==7)?0xFF35445F:0xFF1C2B42));
                if(cell>=4){g.fill(xx,yy,xx+cell,yy+1,0xFF52617A);g.fill(xx,yy,xx+1,yy+cell,0xFF52617A);}
            }
            String hint=status.isEmpty()?tr("ЛКМ: рисовать • ПКМ: стереть","LMB: draw • RMB: erase"):status;
            g.text(font,font.plainSubstrByWidth(hint,leftW),x+12,footer-11,OrbitTheme.MUTED,false);
        }
        renderContent(g,mx,my,delta);
        super.extractRenderState(g,mx,my,delta);
    }
    private boolean paint(double mx,double my) {
        if(state().crosshairMode!=2 || mx<gridX||my<gridY||mx>=gridX+15*cell||my>=gridY+15*cell)return false;
        int col=(int)(mx-gridX)/cell,row=(int)(my-gridY)/cell;
        char[] bits=CrosshairPattern.pixels(state()).toCharArray();
        int fromX=lastX<0?col:lastX,fromY=lastY<0?row:lastY,steps=Math.max(Math.abs(col-fromX),Math.abs(row-fromY));
        for(int i=0;i<=steps;i++){int xx=steps==0?col:(int)Math.round(fromX+(col-fromX)*(double)i/steps),yy=steps==0?row:(int)Math.round(fromY+(row-fromY)*(double)i/steps);bits[yy*15+xx]=paint==0?'1':'0';}
        state().crosshairPixels=new String(bits);lastX=col;lastY=row;return true;
    }
    private void clear(){state().crosshairPixels="0".repeat(225);OrbitClient.save();}
    @Override public boolean mouseClicked(MouseButtonEvent e,boolean twice){
        if(e.button()==0||e.button()==1){paint=e.button();lastX=lastY=-1;if(paint(e.x(),e.y()))return true;paint=-1;}
        return super.mouseClicked(e,twice);
    }
    @Override public boolean mouseDragged(MouseButtonEvent e,double dx,double dy){if(paint>=0){if(!paint(e.x(),e.y()))lastX=lastY=-1;return true;}return super.mouseDragged(e,dx,dy);}
    @Override public boolean mouseReleased(MouseButtonEvent e){if(paint>=0){paint=-1;OrbitClient.save();return true;}return super.mouseReleased(e);}
    @Override public boolean keyPressed(KeyEvent e){if(e.key()==GLFW.GLFW_KEY_DELETE&&state().crosshairMode==2){clear();return true;}return super.keyPressed(e);}
    void verifyLayouts(){var s=state();int old=s.crosshairMode;for(int mode=0;mode<3;mode++){s.crosshairMode=mode;resetScroll();rebuildWidgets();verifyScrollLayouts();}s.crosshairMode=old;}
    @Override public void onClose(){OrbitClient.save();minecraft.gui.setScreen(parent);}
    @Override public boolean isPauseScreen(){return false;}
}
