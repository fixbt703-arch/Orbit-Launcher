package org.orbitlauncher.client;

import java.util.EnumMap;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.input.CharacterEvent;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.MouseButtonInfo;
import org.lwjgl.glfw.GLFW;
import static org.orbitlauncher.client.OrbitClient.tr;

/** Pointer regressions exercised only by the isolated development smoke harness. */
final class HudUiRegressionChecks {
    static void verify(Minecraft mc){
        var previous=mc.gui.screen();var config=OrbitClient.config;
        int mode=config.state(Module.CROSSHAIR).crosshairMode;boolean snap=config.hudSnap;
        var positions=new EnumMap<Module,double[]>(Module.class);
        for(var m:Module.values()){var s=config.state(m);positions.put(m,new double[]{s.enabled?1:0,s.customPosition?1:0,s.x,s.y,s.scale});}
        var bounds=new EnumMap<>(HudRenderer.bounds);
        try{
            var menu=new OrbitScreen();mc.gui.setScreen(menu);
            var search=menu.children().stream().filter(EditBox.class::isInstance).map(EditBox.class::cast).findFirst().orElseThrow();
            search.setValue("FPS");click(menu,button(menu,tr("Очистить поиск","Clear search")));
            require(menu.getFocused()==search&&menu.charTyped(new CharacterEvent('F'))&&search.getValue().equals("F"),"Clear search does not hand typing back to search");
            search.setValue("no-such-orbit-module");click(menu,button(menu,tr("Показать все модули","Show all modules")));
            require(menu.getFocused()==search&&menu.charTyped(new CharacterEvent('P'))&&search.getValue().equals("P"),"Search recovery loses typing focus");
            search.setValue("");menu.expandForTest(Module.FPS);var host=menu.inlineForTest(Module.FPS);
            var bind=host.children().stream().filter(c->c instanceof OrbitButton b&&b.getMessage().getString().startsWith(tr("Клавиша: ","Key: "))).map(c->(OrbitButton)c).findFirst().orElseThrow();
            for(int i=0;i<500&&(menu.activeInlineForTest()!=host||host.getFocused()!=bind);i++)menu.keyPressed(new KeyEvent(GLFW.GLFW_KEY_TAB,0,0));
            require(menu.inlineFocusFullyVisibleForTest(Module.FPS),"Binding action is not reachable");click(menu,bind);
            require(host.isBinding(),"Pointer did not start key capture");click(menu,search);
            require(!host.isBinding()&&menu.activeInlineForTest()==null&&host.getFocused()==null,"Leaving inline settings retains capture or focus");

            config.state(Module.CROSSHAIR).crosshairMode=0;
            var crosshair=new CrosshairScreen(menu);mc.gui.setScreen(crosshair);crosshair.init(427,240);
            click(crosshair,button(crosshair,tr("Готовые","Presets")));
            require(config.state(Module.CROSSHAIR).crosshairMode==1,"Pointer did not select presets");currentFocus(crosshair);
            click(crosshair,button(crosshair,tr("Простой","Simple")));currentFocus(crosshair);
            var glint=new ModuleScreen(menu,Module.GLINT);mc.gui.setScreen(glint);glint.init(427,240);
            click(glint,button(glint,tr("Оформление","Appearance")));currentFocus(glint);
            click(glint,button(glint,tr("Основное","General")));currentFocus(glint);

            var colors=new CrosshairColorScreen(menu);mc.gui.setScreen(colors);colors.init(427,240);
            for(int i=0;i<3;i++){
                var cycle=colors.children().stream().filter(c->c instanceof OrbitButton b&&b.focusKey().equals("crosshair-color-mode")).map(c->(OrbitButton)c).findFirst().orElseThrow();
                click(colors,cycle);currentFocus(colors);
                require(colors.getFocused() instanceof OrbitButton b&&b.focusKey().equals("crosshair-color-mode"),"Changing color-mode label loses pointer focus");
            }
            colors.keyPressed(new KeyEvent(GLFW.GLFW_KEY_ENTER,0,0));currentFocus(colors);
            require(colors.getFocused() instanceof OrbitButton b&&b.focusKey().equals("crosshair-color-mode"),"Color-mode keyboard activation loses focus");
            click(colors,button(colors,tr("При наведении","On target")));currentFocus(colors);
            colors.keyPressed(new KeyEvent(GLFW.GLFW_KEY_ENTER,0,0));
            require(colors.getFocused() instanceof OrbitButton b&&b.getMessage().getString().equals(tr("При наведении","On target")),"Color keyboard activation jumps to another role");

            for(var m:Module.values())if(m.hasHud())config.state(m).customPosition=false;
            config.state(Module.FPS).enabled=true;HudRenderer.bounds.clear();HudRenderer.bounds.put(Module.FPS,new HudRenderer.Bounds(20,20,90,45));
            var hud=new HudEditorScreen(menu);mc.gui.setScreen(hud);hud.init(427,240);
            var select=new MouseButtonEvent(25,25,new MouseButtonInfo(GLFW.GLFW_MOUSE_BUTTON_LEFT,0));hud.mouseClicked(select,false);hud.mouseReleased(select);hud.onClose();
            require(!config.state(Module.FPS).customPosition,"Opening and closing the HUD editor freezes automatic layout");
            config.state(Module.FPS).customPosition=true;hud=new HudEditorScreen(menu);mc.gui.setScreen(hud);hud.init(427,240);
            click(hud,button(hud,tr("Сетка","Snap")));currentFocus(hud);
            click(hud,button(hud,tr("Авто","Auto")));click(hud,button(hud,tr("Готово","Done")));
            require(config.modules.entrySet().stream().noneMatch(e->e.getKey().hasHud()&&e.getValue().enabled&&e.getValue().customPosition),"Closing HUD editor undoes Auto");
            OrbitClient.LOG.info("HUD/UI regressions: real pointer search recovery, capture cancellation, growing/shrinking tabs, native rebuild focus and automatic layout passed");
        }finally{
            config.state(Module.CROSSHAIR).crosshairMode=mode;config.hudSnap=snap;
            for(var entry:positions.entrySet()){var s=config.state(entry.getKey());var p=entry.getValue();s.enabled=p[0]!=0;s.customPosition=p[1]!=0;s.x=p[2];s.y=p[3];s.scale=(float)p[4];}
            HudRenderer.bounds.clear();HudRenderer.bounds.putAll(bounds);OrbitClient.save();mc.gui.setScreen(previous);
        }
    }
    private static OrbitButton button(OrbitUiScreen screen,String label){return screen.children().stream().filter(c->c instanceof OrbitButton b&&b.getMessage().getString().equals(label)).map(c->(OrbitButton)c).findFirst().orElseThrow();}
    private static void click(OrbitUiScreen screen,AbstractWidget widget){
        double x=widget.getX()+widget.getWidth()/2d,y=widget.getY()+widget.getHeight()/2d;
        if(screen instanceof OrbitScreen menu){x=menu.screenCoordinate(x);y=menu.screenCoordinate(y);}
        var event=new MouseButtonEvent(x,y,new MouseButtonInfo(GLFW.GLFW_MOUSE_BUTTON_LEFT,0));
        require(screen.mouseClicked(event,false),"Pointer action was not handled: "+widget.getMessage().getString());screen.mouseReleased(event);
    }
    private static void currentFocus(OrbitUiScreen screen){require(screen.getFocused()!=null&&screen.children().contains(screen.getFocused()),"Rebuild retains a removed widget as focus");}
    private static void require(boolean value,String message){if(!value)throw new AssertionError(message);}
}
