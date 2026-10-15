package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.input.KeyEvent;
import org.lwjgl.glfw.GLFW;
import static org.orbitlauncher.client.OrbitClient.tr;

/** Integration checks executed only by the explicitly enabled development smoke harness. */
final class UiWorkflowChecks {
    private static final KeyEvent TAB=new KeyEvent(GLFW.GLFW_KEY_TAB,0,0);
    private static final KeyEvent SPACE=new KeyEvent(GLFW.GLFW_KEY_SPACE,0,0);
    private static final KeyEvent ENTER=new KeyEvent(GLFW.GLFW_KEY_ENTER,0,0);
    static void verify(Minecraft mc){
        var previous=mc.gui.screen();String language=mc.options.languageCode;boolean enabled=OrbitClient.config.state(Module.FPS).enabled,shadow=OrbitClient.config.state(Module.FPS).shadow;int panelWidth=OrbitClient.config.state(Module.FPS).panelWidth;
        try{
            var menu=new OrbitScreen();mc.gui.setScreen(menu);
            var field=search(menu);
            menu.keyPressed(new KeyEvent(GLFW.GLFW_KEY_F,0,GLFW.GLFW_MOD_CONTROL));
            require(menu.getFocused()==field,"Ctrl+F does not focus module search");
            require(!action(menu,tr("Очистить поиск","Clear search")).visible,"Empty search exposes a meaningless clear action");
            field.setValue("FPS");
            var toggle=menu.children().stream().filter(c->c instanceof OrbitSwitch s&&s.label.equals(Module.FPS.title())).map(c->(OrbitSwitch)c).findFirst().orElseThrow();
            menu.setFocused(toggle);menu.keyPressed(SPACE);
            require(OrbitClient.config.state(Module.FPS).enabled!=enabled,"Space did not toggle the selected module");
            require(menu.getFocused() instanceof OrbitSwitch s&&s.label.equals(Module.FPS.title()),"Module toggle loses keyboard focus");
            menu.keyPressed(SPACE);
            require(OrbitClient.config.state(Module.FPS).enabled==enabled,"A second Space did not toggle the same module back");
            require(ClientConfig.load().state(Module.FPS).enabled==enabled,"Module state was not persisted");
            search(menu).setValue("no-such-orbit-module");
            var reset=action(menu,tr("Показать все модули","Show all modules"));
            menu.setFocused(reset);menu.keyPressed(ENTER);
            require(search(menu).getValue().isEmpty(),"Empty-result recovery did not clear the query");
            require(!action(menu,tr("Очистить поиск","Clear search")).visible,"Reset leaves an empty clear action visible");
            var pvp=action(menu,"PVP");menu.setFocused(pvp);menu.keyPressed(ENTER);search(menu).setValue("Hitbox");
            var settings=action(menu,tr("Настроить: ","Configure: ")+Module.HITBOX.title());menu.setFocused(settings);menu.keyPressed(ENTER);
            require(mc.gui.screen() instanceof ModuleScreen,"Enter did not open the selected module settings");
            mc.gui.screen().onClose();
            require(mc.gui.screen()==menu&&search(menu).getValue().equals("Hitbox"),"Returning from settings lost search context");
            require(menu.getFocused() instanceof OrbitButton b&&b.getMessage().getString().equals(settings.getMessage().getString()),"Returning from settings lost the initiating action's focus");
            search(menu).setValue("FPS");
            require(menu.children().stream().noneMatch(c->c instanceof OrbitSwitch),"Returning from settings lost the PVP filter");
            var panel=new ModuleScreen(menu,Module.FPS);panel.selectTabForTest(1);mc.gui.setScreen(panel);panel.init(427,240);
            var reachable=new java.util.HashSet<AbstractWidget>();
            var expected=panel.children().stream().filter(c->c instanceof AbstractWidget w&&panel.isContentWidget(w)).map(c->(AbstractWidget)c).toList();
            for(int i=0;i<panel.children().size()+5;i++){
                panel.keyPressed(TAB);
                if(panel.getFocused() instanceof AbstractWidget w){
                    reachable.add(w);
                    if(panel.isContentWidget(w))require(w.visible&&w.getY()>=panel.viewY&&w.getY()+w.getHeight()<=panel.viewY+panel.viewHeight,"Keyboard focus is clipped by the scroll viewport");
                }
            }
            require(reachable.containsAll(expected),"Tab cannot reach all scrolled settings");
            reachable.clear();
            for(int i=0;i<panel.children().size()+5;i++){
                panel.keyPressed(new KeyEvent(GLFW.GLFW_KEY_TAB,0,GLFW.GLFW_MOD_SHIFT));
                if(panel.getFocused() instanceof AbstractWidget w)reachable.add(w);
            }
            require(reachable.containsAll(expected),"Shift+Tab cannot reach all scrolled settings");
            var shadowToggle=panel.children().stream().filter(c->c instanceof OrbitSwitch s&&s.label.equals(tr("Тень текста","Text shadow"))).map(c->(OrbitSwitch)c).findFirst().orElseThrow();
            panel.setFocused(shadowToggle);panel.keyPressed(SPACE);
            require(OrbitClient.config.state(Module.FPS).shadow!=shadow,"Settings toggle did not change the value");
            require(panel.getFocused() instanceof OrbitSwitch s&&s.label.equals(shadowToggle.label),"Rebuilding settings loses keyboard focus");
            panel.keyPressed(SPACE);require(OrbitClient.config.state(Module.FPS).shadow==shadow,"Repeated keyboard toggle did not restore the value");
            var width=panel.children().stream().filter(c->c instanceof OrbitButton b&&b.getMessage().getString().startsWith(tr("Ширина: ","Width: "))).map(c->(OrbitButton)c).findFirst().orElseThrow();
            panel.setFocused(width);panel.keyPressed(ENTER);
            require(OrbitClient.config.state(Module.FPS).panelWidth!=panelWidth,"Value-cycle action did not change the value");
            require(panel.getFocused() instanceof OrbitButton b&&b.focusKey().equals(width.focusKey()),"A changing button label loses focus");
            OrbitClient.LOG.info("UI workflow: search, empty recovery, module persistence, return context, keyboard traversal and focus restoration passed");
        }finally{
            OrbitClient.config.state(Module.FPS).enabled=enabled;OrbitClient.config.state(Module.FPS).shadow=shadow;OrbitClient.config.state(Module.FPS).panelWidth=panelWidth;mc.options.languageCode=language;OrbitClient.save();mc.gui.setScreen(previous);
        }
    }
    private static EditBox search(OrbitScreen menu){return menu.children().stream().filter(EditBox.class::isInstance).map(EditBox.class::cast).findFirst().orElseThrow();}
    private static OrbitButton action(net.minecraft.client.gui.screens.Screen screen,String text){return screen.children().stream().filter(c->c instanceof OrbitButton b&&b.getMessage().getString().equals(text)).map(c->(OrbitButton)c).findFirst().orElseThrow(()->new AssertionError("Missing action: "+text));}
    private static void require(boolean condition,String message){if(!condition)throw new AssertionError(message);}
    private UiWorkflowChecks(){}
}
