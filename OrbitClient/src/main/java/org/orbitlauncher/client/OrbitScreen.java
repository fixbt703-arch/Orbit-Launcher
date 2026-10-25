package org.orbitlauncher.client;

import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.input.CharacterEvent;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.MouseButtonInfo;
import net.minecraft.network.chat.Component;
import org.lwjgl.glfw.GLFW;
import java.util.*;
import static org.orbitlauncher.client.OrbitClient.tr;
import static org.orbitlauncher.client.OrbitTheme.CYAN;
import static org.orbitlauncher.client.OrbitTheme.MUTED;

/** A movable workspace: each category owns its scrolling, expansion and input bounds. */
public final class OrbitScreen extends OrbitScrollScreen {
    private static final int HEADER=22,ROW=20,INK=0xFFE4EDF5;
    private final List<Panel> panels=new ArrayList<>();
    private final EnumMap<Module,ModuleScreen> settings=new EnumMap<>(Module.class);
    private final EnumMap<Module,OrbitMotion> expansion=new EnumMap<>(Module.class);
    private final List<AbstractWidget> globals=new ArrayList<>();
    private EditBox search;
    private OrbitButton clearSearch,recover;
    private String query="";
    private Panel dragging,focusedPanel,scrollingPanel;
    private ModuleScreen activeInline;
    private double dragX,dragY,pressX,pressY,scrollAnchor;
    private boolean moved;
    private OrbitReveal reveal=new OrbitReveal();
    private long frame,closing;
    private ClickGuiViewport viewport=ClickGuiViewport.fit(960,540,1);
    private static OrbitScreen drawingViewport;
    private static final boolean SCALE_SMOKE=Boolean.getBoolean("orbit.client.scale-smoke");
    private boolean scaleScissorSeen,scaleScissorValid=true;

    public void configureViewport(int width,int height){viewport=ClickGuiViewport.fit(width,height,minecraft.getWindow().getGuiScale());}
    public int viewportWidth(){return viewport.width();}
    public int viewportHeight(){return viewport.height();}
    public int viewportMouse(int coordinate){return (int)Math.floor(viewport.local(coordinate));}
    public double screenCoordinate(double coordinate){return viewport.screen(coordinate);}
    public void beginViewport(GuiGraphicsExtractor g){g.pose().pushMatrix();g.pose().scale((float)viewport.scale(),(float)viewport.scale());drawingViewport=this;}
    public void endViewport(GuiGraphicsExtractor g){drawingViewport=null;g.pose().popMatrix();}
    public static int renderWidth(int original){return drawingViewport==null?original:drawingViewport.width;}
    public static int renderHeight(int original){return drawingViewport==null?original:drawingViewport.height;}
    public static int scissorCoordinate(int original){return drawingViewport==null?original:(int)Math.floor(drawingViewport.viewport.screen(original));}
    private MouseButtonEvent localPointer(MouseButtonEvent event){return new MouseButtonEvent(viewport.local(event.x()),viewport.local(event.y()),event.buttonInfo());}

    private final class Panel {
        final ClickGuiGroup group;
        final List<ModuleRow> rows=new ArrayList<>();
        final List<ExpandButton> expanders=new ArrayList<>();
        OrbitButton header;
        Module expanded;
        int x,y,w,limit,bodyHeight,contentHeight,expandedAnchor=-1;
        double scroll,target;
        boolean collapsed;
        OrbitMotion open=new OrbitMotion(true);
        Panel(ClickGuiGroup group){this.group=group;}
        int bottom(){return y+HEADER+bodyHeight;}
        boolean contains(double mx,double my){return mx>=x&&mx<x+w&&my>=y&&my<bottom();}
        int capacity(){return Math.max(12,(expanded==null?Math.min(limit,height-40-y):height-40-y)-HEADER);}
    }

    public OrbitScreen(){super(Component.literal("Orbit Client"));}
    @Override public void added(){super.added();reveal=new OrbitReveal();closing=0;}
    @Override protected void init(){
        globals.clear();
        int sw=Math.min(178,width-120),sx=(width-sw)/2;
        search=new EditBox(font,sx+23,30,sw-45,10,Component.literal(tr("Поиск модулей","Search modules"))){
            @Override public void extractWidgetRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){
                super.extractWidgetRenderState(g,mx,my,delta);
                if(getValue().isEmpty()&&!isFocused())g.text(OrbitFonts.ui(),tr("Поиск…","Search…"),getX(),getY(),MUTED,false);
            }
        };
        search.setBordered(false);search.setTextShadow(false);search.setTextColor(INK);search.setMaxLength(64);search.setValue(query);
        search.setResponder(value->{query=value;for(var p:panels)p.scroll=p.target=0;rebuildRows();});
        global(search);
        clearSearch=global(new OrbitButton(sx+sw-23,25,18,18,tr("Очистить поиск","Clear search"),false,()->{search.setValue("");setFocused(search);}).iconOnly("close").plain());
        global(new OrbitButton(width-31,14,20,20,tr("Закрыть","Close"),false,this::onClose).iconOnly("close").compact());
        global(new OrbitButton((width-128)/2,height-30,128,23,tr("Редактор HUD","HUD Editor"),false,()->{remember();minecraft.gui.setScreen(new HudEditorScreen(this));}).withIcon("hud").compact());
        global(new OrbitButton(width-33,height-30,22,23,tr("Сбросить расположение","Reset layout"),false,this::resetLayout).iconOnly("all").compact());
        recover=global(new OrbitButton((width-158)/2,Math.max(76,height/2),158,24,tr("Показать все модули","Show all modules"),false,()->{search.setValue("");setFocused(search);}).compact());
        if(panels.isEmpty())for(var group:ClickGuiGroup.values())panels.add(new Panel(group));
        for(var p:panels){
            var f=ClickGuiLayout.frame(width,height,p.group.ordinal());p.w=f.width();p.limit=f.maxHeight();
            var saved=OrbitClient.config.clientPanels.get(p.group.name());
            p.x=saved==null?f.x():ClickGuiLayout.x(saved.x,width,p.w);
            p.y=saved==null?f.y():ClickGuiLayout.y(saved.y,height);
            p.expandedAnchor=-1;
            p.collapsed=saved!=null&&saved.collapsed;p.open=new OrbitMotion(!p.collapsed);
            makeRoom(p);
            p.header=new Header(p);addWidget(p.header);
        }
        for(var entry:settings.entrySet())entry.getValue().initInline(width,height,0,0,Math.max(90,panels.getFirst().w-8));
        rebuildRows();
    }
    private <T extends AbstractWidget>T global(T widget){globals.add(widget);return addRenderableWidget(widget);}
    private void rebuildRows(){
        if(search==null)return;
        Module focused=getFocused() instanceof ModuleRow r?r.module:null;
        Module expandedFocus=getFocused() instanceof ExpandButton b?b.module:null;
        int count=0;
        for(var p:panels){
            for(var row:p.rows)removeWidget(row);for(var button:p.expanders)removeWidget(button);
            p.rows.clear();p.expanders.clear();
            for(var module:p.group.modules)if(matches(module)){
                var row=new ModuleRow(p,module);p.rows.add(row);addWidget(row);
                var expand=new ExpandButton(p,module);p.expanders.add(expand);addWidget(expand);count++;
                if(module==focused)setFocused(row);if(module==expandedFocus)setFocused(expand);
            }
        }
        clearSearch.visible=!query.isEmpty();recover.visible=count==0;layout(0,true);
    }
    private boolean matches(Module module){return (module.en+" "+module.ru+" "+module.description()).toLowerCase(Locale.ROOT).contains(query.strip().toLowerCase(Locale.ROOT));}
    private ModuleScreen inline(Module module,int w){return settings.computeIfAbsent(module,m->{var s=new ModuleScreen(this,m);s.initInline(width,height,0,0,w-8);return s;});}
    private void makeRoom(Panel p){
        if(p.expanded==null)return;
        int wanted=p.expanded==Module.INTERFACE?Math.min(inline(p.expanded,p.w).inlineHeight()+ROW,Math.max(100,height-114)):100;
        if(height-40-p.y-HEADER<wanted){if(p.expandedAnchor<0)p.expandedAnchor=p.y;p.y=Math.max(52,height-40-HEADER-wanted);}
    }
    private void expand(Panel p,Module module){
        deactivateInline();
        p.expanded=p.expanded==module?null:module;
        if(p.expanded!=null){inline(module,p.w);makeRoom(p);front(p);}
        else if(p.expandedAnchor>=0){p.y=p.expandedAnchor;p.expandedAnchor=-1;}
        layout(0,false);
    }
    private void deactivateInline(){if(activeInline!=null){activeInline.cancelBinding();activeInline.setFocused(null);activeInline=null;}}
    @Override protected void setInitialFocus(){if(activeInline!=null&&activeInline.getFocused()!=null){setFocused(null);return;}super.setInitialFocus();}
    private void layout(double dt,boolean settle){
        for(var s:settings.values())s.clipInline(0,0);
        for(var p:panels){
            p.header.setPosition(p.x,p.y);p.header.setWidth(p.w);
            int total=0;var heights=new EnumMap<Module,Integer>(Module.class);
            for(var row:p.rows){
                var module=row.module;var animation=expansion.computeIfAbsent(module,m->new OrbitMotion());
                if(settle)animation.snap(p.expanded==module);
                float a=animation.toward(p.expanded==module);
                int h=settings.containsKey(module)?Math.round(settings.get(module).inlineHeight()*a):0;
                heights.put(module,h);total+=ROW+h;
            }
            p.contentHeight=total;
            // Search opens collapsed groups temporarily without changing saved layout.
            boolean open=!p.collapsed||!query.isBlank();if(settle)p.open.snap(open);float amount=p.open.toward(open);
            p.bodyHeight=Math.round(Math.min(total,p.capacity())*amount);
            double max=Math.max(0,total-p.capacity());p.target=Math.clamp(p.target,0,max);
            p.scroll=settle?Math.clamp(p.scroll,0,max):p.scroll+(p.target-p.scroll)*OrbitTheme.options().blend(dt,18);
            if(Math.abs(p.target-p.scroll)<.05)p.scroll=p.target;
            int cursor=p.y+HEADER-(int)Math.round(p.scroll);
            for(int i=0;i<p.rows.size();i++){
                var row=p.rows.get(i);var button=p.expanders.get(i);
                row.setPosition(p.x,cursor);row.setWidth(p.w-22);button.setPosition(p.x+p.w-22,cursor);
                boolean visible=cursor+ROW>p.y+HEADER&&cursor<p.bottom()&&p.bodyHeight>0;row.visible=button.visible=visible;
                cursor+=ROW;int h=heights.get(row.module);
                if(h>0){var s=settings.get(row.module);s.moveInline(p.x+4,cursor);s.clipInline(Math.max(cursor,p.y+HEADER),Math.min(cursor+h,p.bottom()));cursor+=h;}
            }
        }
    }
    private final class Header extends OrbitButton {
        final Panel p;
        Header(Panel p){super(p.x,p.y,p.w,HEADER,p.group.title,false,()->{});this.p=p;setTooltip(OrbitTheme.tooltip(OrbitFonts.text(tr("Перетащите заголовок • Нажмите, чтобы свернуть • Стрелки: перемещение","Drag header • Click to collapse • Arrow keys: move"))));setTooltipDelay(java.time.Duration.ofMillis(600));}
        @Override public void onClick(MouseButtonEvent e,boolean twice){p.collapsed=!p.collapsed;remember();}
        @Override public boolean keyPressed(KeyEvent e){if(e.key()==GLFW.GLFW_KEY_ENTER||e.key()==GLFW.GLFW_KEY_SPACE){onClick(null,false);return true;}return super.keyPressed(e);}
        @Override protected void extractWidgetRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){
            OrbitTheme.rounded(g,getX(),getY(),width,HEADER,dragging==p?OrbitTheme.SELECTED:OrbitTheme.translucent(OrbitTheme.CONTROL,245));
            OrbitTheme.accentLine(g,getX(),getY()+HEADER-1,width);
            g.text(OrbitFonts.strong(),p.group.title.toUpperCase(Locale.ROOT),getX()+9,getY()+7,INK,false);
            String count=Integer.toString(p.rows.size());g.text(font,count,getX()+width-32,getY()+7,MUTED,false);
            g.text(font,p.collapsed&&query.isBlank()?"+":"−",getX()+width-14,getY()+6,CYAN,false);
            if(isFocused())OrbitTheme.outline(g,getX(),getY(),width,HEADER,CYAN);
        }
    }
    private final class ModuleRow extends OrbitButton {
        final Module module;
        final OrbitMotion hover=new OrbitMotion(),enabled;
        ModuleRow(Panel p,Module module){super(0,0,p.w-22,ROW,module.title(),false,()->{if(module==Module.INTERFACE)expand(p,module);else{OrbitClient.config.state(module).enabled=!OrbitClient.enabled(module);OrbitClient.save();}});this.module=module;enabled=new OrbitMotion(module==Module.INTERFACE||OrbitClient.enabled(module));setTooltip(OrbitTheme.tooltip(OrbitFonts.text(module.description())));setTooltipDelay(java.time.Duration.ofMillis(500));}
        @Override protected void extractWidgetRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){
            float h=hover.toward(isHoveredOrFocused()),on=enabled.toward(module==Module.INTERFACE||OrbitClient.enabled(module));
            g.fill(getX(),getY(),getX()+width+22,getY()+height,OrbitMotion.mix(OrbitTheme.translucent(OrbitTheme.CARD,0),OrbitTheme.translucent(OrbitTheme.HOVER,192),h));
            if(on>.01)g.fill(getX(),getY()+5,getX()+2,getY()+height-5,(Math.round(on*255)<<24)|(CYAN&0xFFFFFF));
            g.text(OrbitFonts.strong(),OrbitTheme.ellipsize(OrbitFonts.strong(),module.title().toUpperCase(Locale.ROOT),width-14),getX()+9,getY()+7,OrbitMotion.mix(MUTED,CYAN,on),false);
            if(isFocused())g.outline(getX()+2,getY()+1,width-2,height-2,CYAN);
        }
    }
    private final class ExpandButton extends OrbitButton {
        final Module module;final Panel p;
        ExpandButton(Panel p,Module module){super(0,0,22,ROW,tr("Настроить: ","Configure: ")+module.title(),false,()->expand(p,module));this.module=module;this.p=p;setTooltip(OrbitTheme.tooltip(getMessage()));}
        @Override protected void extractWidgetRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){
            if(isHoveredOrFocused())g.fill(getX(),getY(),getX()+width,getY()+height,OrbitTheme.translucent(OrbitTheme.HOVER,176));
            g.text(font,p.expanded==module?"−":"+",getX()+7,getY()+6,p.expanded==module?CYAN:MUTED,false);
            if(isFocused())g.outline(getX()+1,getY()+1,width-2,height-2,CYAN);
        }
    }
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){
        long now=System.nanoTime();double dt=frame==0?0:(now-frame)/1e9;frame=now;layout(dt,false);
        float amount=closing==0?reveal.progress():1-OrbitReveal.ease(OrbitTheme.elapsed(closing,160));
        g.pose().pushMatrix();g.pose().translate(0,6*(1-amount));
        if(SCALE_SMOKE&&(g.guiWidth()!=width||g.guiHeight()!=height))throw new AssertionError("Tooltip viewport differs from menu dimensions");
        g.centeredText(OrbitFonts.brand(),"ORBIT CLIENT",width/2,12,CYAN);
        int sw=Math.min(178,width-120),sx=(width-sw)/2;
        OrbitTheme.surface(g,sx,23,sw,22,OrbitTheme.translucent(OrbitTheme.CARD,247),search.isFocused()?CYAN:OrbitTheme.BORDER);
        ModuleIcons.ui(g,"search",sx+7,28,11,MUTED);
        Panel pointed=panelAt(mx,my);
        for(var p:panels){
            if(p.bodyHeight>0)OrbitTheme.rounded(g,p.x,p.y,p.w,HEADER+p.bodyHeight,OrbitTheme.translucent(OrbitTheme.CARD,247));
            p.header.extractRenderState(g,pointed==p?mx:-9999,pointed==p?my:-9999,delta);
            if(p.bodyHeight==0)continue;
            g.enableScissor(p.x,p.y+HEADER,p.x+p.w,p.bottom());
            for(int i=0;i<p.rows.size();i++){
                var row=p.rows.get(i);if(row.visible){row.extractRenderState(g,pointed==p?mx:-9999,pointed==p?my:-9999,delta);p.expanders.get(i).extractRenderState(g,pointed==p?mx:-9999,pointed==p?my:-9999,delta);}
                if(SCALE_SMOKE&&row.visible&&row.getY()>=p.y+HEADER&&row.getY()+ROW<=p.bottom()){
                    scaleScissorSeen=true;scaleScissorValid&=g.containsPointInScissor(row.getX()+row.getWidth()/2,row.getY()+ROW/2);
                }
                var s=settings.get(row.module);if(s!=null)s.renderInline(g,pointed==p?mx:-9999,pointed==p?my:-9999,delta);
            }
            g.disableScissor();
            if(p.contentHeight>p.capacity()){
                int thumb=Math.max(8,(int)((double)p.bodyHeight*p.capacity()/p.contentHeight));
                int y=p.y+HEADER+(int)(p.scroll/Math.max(1,p.contentHeight-p.capacity())*(p.bodyHeight-thumb));
                g.fill(p.x+p.w-2,y,p.x+p.w-1,y+thumb,OrbitTheme.translucent(CYAN,176));
            }
        }
        if(recover.visible)g.centeredText(font,tr("Ничего не найдено","No modules found"),width/2,recover.getY()-17,INK);
        if(width>=700)g.text(font,tr("ЛКМ · вкл/выкл   ПКМ · настройки","LMB · toggle   RMB · settings"),12,height-21,MUTED,false);
        super.extractRenderState(g,mx,my,delta);g.pose().popMatrix();
    }
    private Panel panelAt(double x,double y){for(int i=panels.size()-1;i>=0;i--)if(panels.get(i).contains(x,y))return panels.get(i);return null;}
    private void front(Panel p){panels.remove(p);panels.add(p);focusedPanel=p;}
    @Override public boolean mouseClicked(MouseButtonEvent e,boolean twice){
        return clickViewport(localPointer(e),twice);
    }
    private boolean clickViewport(MouseButtonEvent e,boolean twice){
        if(closing!=0)return true;
        for(var widget:globals)if(widget.visible&&widget.isMouseOver(e.x(),e.y())){deactivateInline();setFocused(widget);if(widget.mouseClicked(e,twice)){setDragging(e.button()==0);return true;}}
        var p=panelAt(e.x(),e.y());if(p==null){setFocused(null);deactivateInline();return false;}
        front(p);
        if(e.button()==0&&e.y()>=p.y+HEADER&&e.x()>=p.x+p.w-6&&p.contentHeight>p.capacity()){
            deactivateInline();
            int thumb=Math.max(8,(int)((double)p.bodyHeight*p.capacity()/p.contentHeight));
            p.scroll=p.target=Math.clamp((e.y()-p.y-HEADER-thumb/2d)/Math.max(1,p.bodyHeight-thumb)*(p.contentHeight-p.capacity()),0,p.contentHeight-p.capacity());
            scrollingPanel=p;scrollAnchor=e.y();layout(0,false);return true;
        }
        if(e.y()<p.y+HEADER){
            deactivateInline();
            setFocused(p.header);
            if(e.button()==1){p.collapsed=!p.collapsed;remember();return true;}
            if(e.button()==0){dragging=p;dragX=e.x()-p.x;dragY=e.y()-p.y;pressX=e.x();pressY=e.y();moved=false;return true;}
        }
        for(int i=0;i<p.rows.size();i++){
            var row=p.rows.get(i);var expand=p.expanders.get(i);
            if(row.visible&&row.isMouseOver(e.x(),e.y())&&e.button()==1){expand(p,row.module);setFocused(expand);return true;}
            for(var widget:new AbstractWidget[]{row,expand})if(widget.visible&&widget.isMouseOver(e.x(),e.y())){deactivateInline();if(widget.mouseClicked(e,twice)){setFocused(widget);setDragging(true);return true;}}
            var s=settings.get(row.module);
            if(s!=null&&p.expanded==row.module&&s.children().stream().anyMatch(c->c instanceof AbstractWidget w&&w.visible&&w.isMouseOver(e.x(),e.y()))){
                if(activeInline!=s)deactivateInline();
                boolean capturing=s.isBinding();String captureFocus=s.getFocused() instanceof AbstractWidget widget?focusKey(widget):null;
                if(s.mouseClicked(e,twice)){
                    if(capturing&&s.isBinding()&&(!(s.getFocused() instanceof AbstractWidget widget)||!Objects.equals(captureFocus,focusKey(widget))))s.cancelBinding();
                    setFocused(null);activeInline=s;return true;
                }
            }
        }
        deactivateInline();
        return true;
    }
    @Override public boolean mouseDragged(MouseButtonEvent e,double dx,double dy){
        return dragViewport(localPointer(e),viewport.local(dx),viewport.local(dy));
    }
    private boolean dragViewport(MouseButtonEvent e,double dx,double dy){
        if(scrollingPanel!=null){var p=scrollingPanel;int thumb=Math.max(8,(int)((double)p.bodyHeight*p.capacity()/p.contentHeight));p.scroll=p.target=Math.clamp(p.scroll+(e.y()-scrollAnchor)*(p.contentHeight-p.capacity())/Math.max(1,p.bodyHeight-thumb),0,p.contentHeight-p.capacity());scrollAnchor=e.y();layout(0,false);return true;}
        if(dragging!=null){
            moved|=Math.abs(e.x()-pressX)+Math.abs(e.y()-pressY)>3;
            if(moved){dragging.expandedAnchor=-1;dragging.x=Math.clamp((int)Math.round(e.x()-dragX),6,width-dragging.w-6);dragging.y=Math.clamp((int)Math.round(e.y()-dragY),52,dragging.expanded==null?height-80:Math.max(52,height-94));layout(0,false);}return true;
        }
        return activeInline!=null?activeInline.mouseDragged(e,dx,dy):super.mouseDragged(e,dx,dy);
    }
    @Override public boolean mouseReleased(MouseButtonEvent e){
        return releaseViewport(localPointer(e));
    }
    private boolean releaseViewport(MouseButtonEvent e){
        if(scrollingPanel!=null){scrollingPanel=null;return true;}
        if(dragging!=null){if(!moved)dragging.collapsed=!dragging.collapsed;dragging=null;remember();return true;}
        if(activeInline!=null){boolean result=activeInline.mouseReleased(e);OrbitClient.save();return result;}
        return super.mouseReleased(e);
    }
    @Override public boolean mouseScrolled(double x,double y,double horizontal,double vertical){
        return scrollViewport(viewport.local(x),viewport.local(y),horizontal,vertical);
    }
    @Override public void mouseMoved(double x,double y){super.mouseMoved(viewport.local(x),viewport.local(y));}
    private boolean scrollViewport(double x,double y,double horizontal,double vertical){
        var p=panelAt(x,y);if(p==null||y<p.y+HEADER||vertical==0)return false;
        p.target=Math.clamp(p.target-vertical*32,0,Math.max(0,p.contentHeight-p.capacity()));return true;
    }
    private record Focus(AbstractWidget widget,Panel panel,ModuleScreen host){}
    private boolean traverse(boolean reverse){
        var order=new ArrayList<Focus>();
        for(var widget:globals)if(widget.visible&&widget.active)order.add(new Focus(widget,null,null));
        for(var p:panels.stream().sorted(Comparator.comparingInt(p->p.group.ordinal())).toList()){order.add(new Focus(p.header,p,null));if(p.collapsed&&query.isBlank())continue;
            for(int i=0;i<p.rows.size();i++){
                order.add(new Focus(p.rows.get(i),p,null));order.add(new Focus(p.expanders.get(i),p,null));
                var s=settings.get(p.rows.get(i).module);if(s!=null&&p.expanded==p.rows.get(i).module)for(var c:s.children())if(c instanceof AbstractWidget w&&w.active)order.add(new Focus(w,p,s));
            }
        }
        var current=activeInline!=null?activeInline.getFocused():getFocused();int at=-1;
        for(int i=0;i<order.size();i++)if(order.get(i).widget==current){at=i;break;}
        var next=order.get(Math.floorMod(at+(reverse?-1:1),order.size()));
        if(activeInline!=next.host)deactivateInline();else if(activeInline!=null)activeInline.setFocused(null);activeInline=next.host;
        if(next.host!=null){setFocused(null);next.widget.visible=true;next.host.setFocused(next.widget);}else setFocused(next.widget);
        if(next.panel!=null){var p=next.panel;front(p);int top=p.y+HEADER,bottom=p.y+HEADER+p.capacity();
            if(next.widget!=p.header){if(next.widget.getY()<top)p.target+=next.widget.getY()-top;else if(next.widget.getY()+next.widget.getHeight()>bottom)p.target+=next.widget.getY()+next.widget.getHeight()-bottom;p.scroll=p.target;layout(0,true);}
        }
        return true;
    }
    @Override public boolean keyPressed(KeyEvent e){
        if(closing!=0)return true;
        if(activeInline!=null&&activeInline.isBinding())return activeInline.keyPressed(e);
        if(e.key()==GLFW.GLFW_KEY_TAB)return traverse(e.hasShiftDown());
        if(e.key()==GLFW.GLFW_KEY_F&&e.hasControlDown()){deactivateInline();setFocused(search);search.setHighlightPos(0);return true;}
        if(e.key()==GLFW.GLFW_KEY_ESCAPE||OrbitClient.menuKey.matches(e)){if(!query.isEmpty()&&search.isFocused()){search.setValue("");return true;}onClose();return true;}
        if(getFocused() instanceof Header h){
            int dx=e.key()==GLFW.GLFW_KEY_LEFT?-4:e.key()==GLFW.GLFW_KEY_RIGHT?4:0,dy=e.key()==GLFW.GLFW_KEY_UP?-4:e.key()==GLFW.GLFW_KEY_DOWN?4:0;
            if(dx!=0||dy!=0){var p=h.p;p.expandedAnchor=-1;p.x=Math.clamp(p.x+dx,6,width-p.w-6);p.y=Math.clamp(p.y+dy,52,p.expanded==null?height-80:Math.max(52,height-94));front(p);remember();layout(0,false);return true;}
        }
        if(focusedPanel!=null&&!(getFocused() instanceof EditBox)&&!(activeInline!=null&&activeInline.getFocused() instanceof EditBox)){
            var p=focusedPanel;
            switch(e.key()){
                case GLFW.GLFW_KEY_PAGE_DOWN -> {p.target=Math.min(Math.max(0,p.contentHeight-p.capacity()),p.target+p.capacity()*.8);return true;}
                case GLFW.GLFW_KEY_PAGE_UP -> {p.target=Math.max(0,p.target-p.capacity()*.8);return true;}
                case GLFW.GLFW_KEY_HOME -> {p.target=0;return true;}
                case GLFW.GLFW_KEY_END -> {p.target=Math.max(0,p.contentHeight-p.capacity());return true;}
            }
        }
        if(activeInline!=null)return activeInline.keyPressed(e);
        return super.keyPressed(e);
    }
    @Override public boolean charTyped(CharacterEvent e){return activeInline!=null?activeInline.charTyped(e):super.charTyped(e);}
    private void remember(){
        for(var p:panels)OrbitClient.config.clientPanels.put(p.group.name(),new ClientConfig.PanelPosition((p.x-6)/(double)Math.max(1,width-p.w-12),((p.expandedAnchor>=0?p.expandedAnchor:p.y)-52)/(double)Math.max(1,height-132),p.collapsed));
        OrbitClient.save();
    }
    private void resetLayout(){OrbitClient.config.clientPanels.clear();for(var p:panels){var f=ClickGuiLayout.frame(width,height,p.group.ordinal());p.x=f.x();p.y=f.y();p.collapsed=false;p.expandedAnchor=-1;p.scroll=p.target=0;makeRoom(p);}OrbitClient.save();layout(0,true);}
    @Override public void onClose(){if(closing==0){deactivateInline();remember();closing=System.nanoTime();}}
    @Override public void tick(){super.tick();if(closing!=0&&OrbitTheme.elapsed(closing,160)>=1)minecraft.gui.setScreen(null);}
    @Override public boolean isPauseScreen(){return false;}

    void expandForTest(Module module){for(var p:List.copyOf(panels))if(Arrays.asList(p.group.modules).contains(module)){if(p.expanded!=module)expand(p,module);layout(0,true);return;}}
    ModuleScreen inlineForTest(Module module){return inline(module,panels.getFirst().w);}
    ModuleScreen activeInlineForTest(){return activeInline;}
    boolean inlineFocusFullyVisibleForTest(Module module){
        var host=settings.get(module);if(host==null||activeInline!=host||!(host.getFocused() instanceof AbstractWidget w)||!w.visible)return false;
        for(var p:panels)if(p.expanded==module)return w.getY()>=p.y+HEADER&&w.getY()+w.getHeight()<=p.bottom()&&panelAt(w.getX()+w.getWidth()/2d,w.getY()+w.getHeight()/2d)==p;
        return false;
    }
    void settleForTest(){layout(0,true);}
    void verifyViewportForTest(){
        var errors=new ArrayList<String>();
        if(!scaleScissorSeen||!scaleScissorValid)errors.add("scaled scissor rejects visible rows");
        int oldWidth=width,oldHeight=height;
        resize(minecraft.getWindow().getGuiScaledWidth(),minecraft.getWindow().getGuiScaledHeight());
        if(width!=oldWidth||height!=oldHeight)errors.add("resize bypasses menu viewport");
        if(renderWidth(123)!=123||renderHeight(456)!=456)errors.add("viewport leaks into native HUD");
        if(!errors.isEmpty())throw new AssertionError(String.join("; ",errors));
    }
    void verifyViewportInputs(){
        var state=OrbitClient.config.state(Module.FPS);boolean enabled=state.enabled;float scale=state.scale;
        try{
            for(var p:panels){p.expanded=null;p.scroll=p.target=0;}layout(0,true);
            var p=panels.stream().filter(panel->panel.group==ClickGuiGroup.HUD).findFirst().orElseThrow();
            var row=p.rows.stream().filter(r->r.module==Module.FPS).findFirst().orElseThrow();
            mouseClicked(pointer(row.getX()+5,row.getY()+10,0),false);mouseReleased(pointer(row.getX()+5,row.getY()+10,0));
            if(state.enabled==enabled)throw new AssertionError("Scaled click missed the module row");
            mouseClicked(pointer(row.getX()+5,row.getY()+10,1),false);layout(0,true);
            var host=inline(Module.FPS,p.w);host.selectTabForTest(0);host.rebuildWidgets();layout(0,true);
            var slider=host.children().stream().filter(c->c instanceof OrbitSlider s&&s.label().equals(tr("Масштаб HUD","HUD scale"))).map(c->(OrbitSlider)c).findFirst().orElseThrow();
            for(int i=0;i<200&&(activeInline!=host||host.getFocused()!=slider);i++)traverse(false);
            if(!inlineFocusFullyVisibleForTest(Module.FPS))throw new AssertionError("Scaled slider is not reachable");
            mouseClicked(pointer(slider.getX()+10,slider.getY()+24,0),false);
            mouseDragged(pointer(slider.getX()+slider.getWidth()-10,slider.getY()+24,0),viewport.screen(slider.getWidth()-20),0);
            mouseReleased(pointer(slider.getX()+slider.getWidth()-10,slider.getY()+24,0));
            if(Math.abs(state.scale-2)>.001)throw new AssertionError("Scaled slider drag misses the track end");
            p.scroll=p.target=0;layout(0,true);
            double other=panels.stream().filter(panel->panel!=p).mapToDouble(panel->panel.target).sum();
            if(!mouseScrolled(viewport.screen(p.x+10),viewport.screen(p.y+HEADER+10),0,-1)||p.target<=0||p.scroll!=0)
                throw new AssertionError("Scaled wheel misses the panel or jumps instead of smoothing");
            if(other!=panels.stream().filter(panel->panel!=p).mapToDouble(panel->panel.target).sum())throw new AssertionError("Wheel moved another category");
        }finally{state.enabled=enabled;state.scale=scale;for(var p:panels){p.expanded=null;p.scroll=p.target=0;}activeInline=null;setFocused(null);OrbitClient.save();layout(0,true);}
    }
    void verifyLayouts(){
        layout(0,true);for(var p:panels){if(p.x<0||p.x+p.w>width||p.y<0||p.bottom()>height-36)throw new AssertionError("Unreachable group: "+p.group);for(var m:p.group.modules){var s=inline(m,p.w);s.verifyInlineLayout();}}
    }
    void verifyFiltering(){String old=query;search.setValue("FPS");if(panels.stream().mapToInt(p->p.rows.size()).sum()!=1)throw new AssertionError("Search mismatch");search.setValue("no-such-orbit-module");if(!recover.visible)throw new AssertionError("Missing empty recovery");search.setValue(old);}
    void verifyInteractions(){
        resetLayout();var p=panels.getFirst();int oldX=p.x,oldY=p.y;
        mouseClicked(pointer(p.x+20,p.y+10,0),false);mouseDragged(pointer(-500,-500,0),-500,-500);mouseReleased(pointer(-500,-500,0));
        if(p.x!=6||p.y!=52||p.collapsed)throw new AssertionError("Header drag/clamp failed");
        var saved=ClientConfig.load().clientPanels.get(p.group.name());if(saved==null||saved.x!=0||saved.y!=0)throw new AssertionError("Panel position not persisted");
        mouseClicked(pointer(p.x+20,p.y+10,1),false);if(!p.collapsed)throw new AssertionError("Collapse failed");
        p.collapsed=false;p.x=oldX;p.y=oldY;remember();for(var panel:panels)panel.scroll=panel.target=0;
        OrbitClient.LOG.info("Click GUI: drag/clamp/save, header collapse, independent panel geometry and inline settings passed");
    }
    private MouseButtonEvent pointer(double x,double y,int button){return new MouseButtonEvent(viewport.screen(x),viewport.screen(y),new MouseButtonInfo(button,0));}
}
