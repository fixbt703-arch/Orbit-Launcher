package org.orbitlauncher.client;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.network.chat.Component;
import org.lwjgl.glfw.GLFW;
import static org.orbitlauncher.client.OrbitClient.tr;

final class HudEditorScreen extends OrbitUiScreen {
    private final Screen parent;
    private Module selected,dragging;
    private double dx,dy;
    private boolean commitNextFrame,dragMoved;
    private OrbitButton settings;
    HudEditorScreen(Screen parent) {super(net.minecraft.client.Minecraft.getInstance(),OrbitFonts.ui(),Component.literal("Orbit HUD"));this.parent=parent;}
    int selectedOrdinal(){return selected==null?-1:selected.ordinal();}
    void selectForTest(Module module){selected=module;}
    void beginMoveTest() {
        if(HudRenderer.bounds.isEmpty())throw new IllegalStateException("Editor did not render enabled HUD modules");
        rememberLayout();selected=HudRenderer.bounds.keySet().iterator().next();move(80,50,false);
    }
    void verifyMoveTest() {
        var b=HudRenderer.bounds.get(selected);
        if(b==null || b.x()!=80 || b.y()!=50)throw new IllegalStateException("Selected HUD did not follow its requested position");
        rememberLayout();OrbitClient.LOG.info("HUD editor movement and position commit passed");
    }
    @Override protected void init() {
        int x=width/2-131,y=height-30;
        addRenderableWidget(new OrbitButton(x,y,50,22,tr("Сетка","Snap"),OrbitClient.config.hudSnap,()->{OrbitClient.config.hudSnap=!OrbitClient.config.hudSnap;OrbitClient.save();rebuildWidgets();}));
        settings=new OrbitButton(x+56,y,78,22,tr("Настройки","Settings"),false,()->{if(selected!=null)minecraft.gui.setScreen(new ModuleScreen(this,selected));});
        settings.active=selected!=null;addRenderableWidget(settings);
        addRenderableWidget(new OrbitButton(x+140,y,56,22,tr("Авто","Auto"),false,()->{
            selected=null;dragging=null;commitNextFrame=false;
            for(var m:Module.values()) if(m.hasHud() && OrbitClient.enabled(m))OrbitClient.config.state(m).customPosition=false;
            OrbitClient.save();
        }));
        addRenderableWidget(new OrbitButton(x+202,y,60,22,tr("Готово","Done"),true,this::onClose));
    }
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta) {
        if(OrbitClient.config.hudSnap) {
            for(int gx=16;gx<width;gx+=16)for(int gy=16;gy<height-48;gy+=16)g.fill(gx,gy,gx+1,gy+1,OrbitTheme.translucent(OrbitTheme.FOCUS,72));
        }
        if(OrbitClient.config.hudSnap && dragging!=null) {
            g.fill(width/2,0,width/2+1,height-48,OrbitTheme.translucent(OrbitTheme.PRIMARY,72));
            g.fill(0,(height-48)/2,width,(height-48)/2+1,OrbitTheme.translucent(OrbitTheme.PRIMARY,72));
        }
        HudRenderer.render(g,true,mx,my);
        if(commitNextFrame){rememberLayout();OrbitClient.save();commitNextFrame=false;}
        if(selected!=null && !HudRenderer.bounds.containsKey(selected))selected=null;
        settings.active=selected!=null;
        if(HudRenderer.bounds.isEmpty())g.centeredText(font,tr("Включите HUD-модули в меню клиента","Enable HUD modules in the client menu"),width/2,height/2,OrbitTheme.TEXT);
        int dockWidth=Math.min(width-12,430);
        OrbitTheme.surface(g,(width-dockWidth)/2,height-47,dockWidth,44,OrbitTheme.translucent(OrbitTheme.BASE,248),OrbitTheme.BORDER);
        String hint=selected==null?tr("Перетащите элемент • Колесо: размер • ПКМ: настройки","Drag a widget • Scroll: scale • Right click: settings"):
            selected.title()+tr(" • Стрелки: точное перемещение"," • Arrow keys: precise movement");
        ButtonLabel.draw(g,font,hint,(width-dockWidth)/2+8,height-44,dockWidth-16,11,OrbitTheme.TEXT);
        super.extractRenderState(g,mx,my,delta);
    }
    private void rememberLayout() {
        for(var entry:HudRenderer.bounds.entrySet()) {
            var s=OrbitClient.config.state(entry.getKey());var b=entry.getValue();
            s.customPosition=true;s.x=b.x()/(double)width;s.y=b.y()/(double)height;
        }
    }
    @Override public boolean mouseClicked(MouseButtonEvent e,boolean doubleClick) {
        if(super.mouseClicked(e,doubleClick))return true;
        for(var entry:HudRenderer.bounds.entrySet()) {
            if(!entry.getValue().contains(e.x(),e.y()))continue;
            selected=entry.getKey();
            if(e.button()==1){minecraft.gui.setScreen(new ModuleScreen(this,selected));return true;}
            if(e.button()==0){dragging=selected;dragMoved=false;dx=e.x()-entry.getValue().x();dy=e.y()-entry.getValue().y();return true;}
        }
        selected=null;return false;
    }
    private boolean move(double x,double y,boolean snap) {
        if(selected==null)return false;
        var b=HudRenderer.bounds.get(selected);if(b==null)return false;
        if(snap){x=Math.round(x/4)*4;y=Math.round(y/4)*4;
            if(Math.abs(x+b.width()/2.0-width/2.0)<4)x=(width-b.width())/2.0;
            if(Math.abs(y+b.height()/2.0-(height-48)/2.0)<4)y=(height-48-b.height())/2.0;
        }
        double nextX=Math.clamp(x,4,Math.max(4,width-b.width()-4)),nextY=Math.clamp(y,4,Math.max(4,height-b.height()-48));
        if(nextX==b.x()&&nextY==b.y())return false;
        rememberLayout();var s=OrbitClient.config.state(selected);s.customPosition=true;
        s.x=nextX/width;s.y=nextY/height;return true;
    }
    @Override public boolean mouseDragged(MouseButtonEvent e,double deltaX,double deltaY) {
        if(dragging==null)return super.mouseDragged(e,deltaX,deltaY);
        dragMoved|=move(e.x()-dx,e.y()-dy,OrbitClient.config.hudSnap);return true;
    }
    @Override public boolean mouseReleased(MouseButtonEvent e) {
        if(dragging!=null){dragging=null;commitNextFrame|=dragMoved;return true;}return super.mouseReleased(e);
    }
    @Override public boolean mouseScrolled(double x,double y,double horizontal,double vertical) {
        for(var entry:HudRenderer.bounds.entrySet())if(entry.getValue().contains(x,y)){
            selected=entry.getKey();var s=OrbitClient.config.state(selected);float scale=(float)Math.clamp(s.scale+vertical*.05,.65,2);
            if(scale!=s.scale){rememberLayout();s.scale=scale;commitNextFrame=true;}return true;
        }
        return super.mouseScrolled(x,y,horizontal,vertical);
    }
    @Override public boolean keyPressed(KeyEvent e) {
        if(selected!=null && e.key()>=GLFW.GLFW_KEY_RIGHT && e.key()<=GLFW.GLFW_KEY_UP) {
            var b=HudRenderer.bounds.get(selected);
            if(b!=null)commitNextFrame|=move(b.x()+(e.key()==GLFW.GLFW_KEY_RIGHT?1:e.key()==GLFW.GLFW_KEY_LEFT?-1:0),
                b.y()+(e.key()==GLFW.GLFW_KEY_DOWN?1:e.key()==GLFW.GLFW_KEY_UP?-1:0),false);
            return true;
        }
        if(OrbitClient.menuKey.matches(e)){onClose();return true;}return super.keyPressed(e);
    }
    @Override public void onClose(){dragging=null;commitNextFrame=false;OrbitClient.save();minecraft.gui.setScreen(parent);}
    @Override public boolean isPauseScreen(){return false;}
}
