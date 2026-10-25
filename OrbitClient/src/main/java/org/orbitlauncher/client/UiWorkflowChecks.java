package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.KeyMapping;
import com.mojang.blaze3d.platform.InputConstants;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.MouseButtonInfo;
import org.lwjgl.glfw.GLFW;
import static org.orbitlauncher.client.OrbitClient.tr;

/** Integration checks executed only by the explicitly enabled development smoke harness. */
final class UiWorkflowChecks {
    private static final KeyEvent TAB=new KeyEvent(GLFW.GLFW_KEY_TAB,0,0);
    private static final KeyEvent SPACE=new KeyEvent(GLFW.GLFW_KEY_SPACE,0,0);
    private static final KeyEvent ENTER=new KeyEvent(GLFW.GLFW_KEY_ENTER,0,0);
    static void verify(Minecraft mc){
        var previous=mc.gui.screen();String language=mc.options.languageCode;boolean enabled=OrbitClient.config.state(Module.FPS).enabled,accent=OrbitTheme.options().hud.accentBar;int panelWidth=OrbitTheme.options().hud.panelWidth;
        var positions=new java.util.HashMap<>(OrbitClient.config.clientPanels);
        try{
            for(String testLanguage:new String[]{"ru_ru","en_us"}){
                mc.options.languageCode=testLanguage;verifyMenu(mc,enabled,accent);verifyCompactInline(mc);
            }
            mc.options.languageCode=language;
            var menu=new OrbitScreen();mc.gui.setScreen(menu);
            var panel=new ModuleScreen(menu,Module.INTERFACE);panel.selectTabForTest(1);mc.gui.setScreen(panel);panel.init(427,240);
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
            var accentToggle=panel.children().stream().filter(c->c instanceof OrbitSwitch s&&s.label.equals(tr("Акцентная полоса","Accent stripe"))).map(c->(OrbitSwitch)c).findFirst().orElseThrow();
            require(accentToggle.labelFont()==OrbitFonts.ui(),"Settings use inconsistent label weight");
            for(String title:new String[]{tr("Масштаб HUD","HUD scale"),tr("Изменить положение","Edit position"),tr("Отмечать возможность критического удара","Highlight critical opportunity")}){
                var label=ButtonLabel.settingLayout(OrbitFonts.ui(),title,180,26);
                require(label.scale()==1&&label.lines().size()<=2,"Setting labels stretch or shrink instead of wrapping");
            }
            panel.setFocused(accentToggle);panel.keyPressed(SPACE);
            require(OrbitTheme.options().hud.accentBar!=accent,"Settings toggle did not change the value");
            require(panel.getFocused() instanceof OrbitSwitch s&&s.label.equals(accentToggle.label),"Rebuilding settings loses keyboard focus");
            panel.keyPressed(SPACE);require(OrbitTheme.options().hud.accentBar==accent,"Repeated keyboard toggle did not restore the value");
            var width=panel.children().stream().filter(c->c instanceof OrbitButton b&&b.getMessage().getString().startsWith(tr("Ширина: ","Width: "))).map(c->(OrbitButton)c).findFirst().orElseThrow();
            panel.setFocused(width);panel.keyPressed(ENTER);
            require(OrbitTheme.options().hud.panelWidth!=panelWidth,"Value-cycle action did not change the value");
            require(panel.getFocused() instanceof OrbitButton b&&b.focusKey().equals(width.focusKey()),"A changing button label loses focus");
            var bindingPanel=new ModuleScreen(menu,Module.FPS);mc.gui.setScreen(bindingPanel);bindingPanel.init(427,240);
            String binding=OrbitClient.moduleKeys.get(Module.FPS).saveString();
            var key=bindingPanel.children().stream().filter(c->c instanceof OrbitButton b&&b.getMessage().getString().startsWith(tr("Клавиша: ","Key: "))).map(c->(OrbitButton)c).findFirst().orElseThrow();
            bindingPanel.setFocused(key);bindingPanel.keyPressed(ENTER);require(bindingPanel.isBinding(),"Key-binding action did not start capture");
            bindingPanel.keyPressed(new KeyEvent(GLFW.GLFW_KEY_ESCAPE,0,0));
            require(!bindingPanel.isBinding()&&OrbitClient.moduleKeys.get(Module.FPS).saveString().equals(binding),"Cancelling key capture changed binding");
            OrbitClient.LOG.info("UI workflow: bilingual module names, search/recovery, toggle persistence, inline settings/tabs/editor return, panel interaction, standalone keyboard traversal and binding passed");
        }finally{
            OrbitClient.config.state(Module.FPS).enabled=enabled;OrbitTheme.options().hud.accentBar=accent;OrbitTheme.options().hud.panelWidth=panelWidth;OrbitClient.config.clientPanels.clear();OrbitClient.config.clientPanels.putAll(positions);mc.options.languageCode=language;OrbitClient.save();mc.gui.setScreen(previous);
        }
    }
    private static void verifyMenu(Minecraft mc,boolean enabled,boolean accent){
        var menu=new OrbitScreen();mc.gui.setScreen(menu);menu.verifyInteractions();
        for(var module:Module.values()){
            require(module.title().equals(module.en),"Module name changes with UI language: "+module);
            action(menu,module.en);
        }
        var field=search(menu);menu.keyPressed(new KeyEvent(GLFW.GLFW_KEY_F,0,GLFW.GLFW_MOD_CONTROL));
        require(menu.getFocused()==field,"Ctrl+F does not focus module search");
        require(!action(menu,tr("Очистить поиск","Clear search")).visible,"Empty search exposes a meaningless clear action");
        field.setValue("FPS");var toggle=action(menu,Module.FPS.title());
        menu.setFocused(toggle);menu.keyPressed(SPACE);
        require(OrbitClient.config.state(Module.FPS).enabled!=enabled&&ClientConfig.load().state(Module.FPS).enabled!=enabled,"Space did not toggle and persist module state");
        require(menu.getFocused()==toggle,"Module toggle loses keyboard focus");
        menu.keyPressed(SPACE);
        require(OrbitClient.config.state(Module.FPS).enabled==enabled&&ClientConfig.load().state(Module.FPS).enabled==enabled,"Repeated Space did not restore and persist the module state");
        field.setValue("no-such-orbit-module");var reset=action(menu,tr("Показать все модули","Show all modules"));
        require(reset.visible,"Empty search has no recovery action");menu.setFocused(reset);menu.keyPressed(ENTER);
        require(search(menu).getValue().isEmpty()&&!action(menu,tr("Очистить поиск","Clear search")).visible,"Empty-result recovery did not clear the query");
        search(menu).setValue("FPS");var expand=action(menu,tr("Настроить: ","Configure: ")+Module.FPS.title());
        menu.setFocused(expand);menu.keyPressed(ENTER);menu.settleForTest();
        require(mc.gui.screen()==menu,"Expanding settings replaced the Click GUI");
        var fps=menu.inlineForTest(Module.FPS);
        require(action(fps,tr("Основное","General")).visible,"Expanded settings do not expose inline tabs");
        require(fps.children().stream().noneMatch(c->c instanceof OrbitButton b&&b.getMessage().getString().equals(tr("Оформление","Appearance"))),"HUD still exposes independent design");
        search(menu).setValue("");menu.expandForTest(Module.INTERFACE);var shared=menu.inlineForTest(Module.INTERFACE);
        focusInline(menu,shared,action(shared,"HUD"));menu.keyPressed(ENTER);menu.settleForTest();
        var stripe=shared.children().stream().filter(c->c instanceof OrbitSwitch s&&s.label.equals(tr("Акцентная полоса","Accent stripe"))).map(c->(OrbitSwitch)c).findFirst().orElseThrow();
        focusInline(menu,shared,stripe);menu.keyPressed(SPACE);
        require(OrbitTheme.options().hud.accentBar!=accent,"Global HUD setting did not change");
        require(shared.getFocused() instanceof OrbitSwitch s&&s.label.equals(stripe.label),"Shared HUD focus lost");
        menu.keyPressed(SPACE);require(OrbitTheme.options().hud.accentBar==accent,"Shared HUD toggle did not restore");
        search(menu).setValue("");menu.expandForTest(Module.GLINT);var glint=menu.inlineForTest(Module.GLINT);
        focusInline(menu,glint,action(glint,tr("Оформление","Appearance")));menu.keyPressed(ENTER);menu.settleForTest();
        require(shared.children().stream().anyMatch(c->c instanceof OrbitSwitch s&&s.label.equals(stripe.label)),"Another module changed Interface selected tab");
        focusInline(menu,glint,action(glint,tr("Цвет сияния","Glint color")));menu.keyPressed(ENTER);
        require(mc.gui.screen() instanceof FeatureColorScreen,"Inline color action did not open the color editor");mc.gui.screen().onClose();
        require(mc.gui.screen()==menu&&search(menu).getValue().isEmpty(),"Color editor did not return to its owning Click GUI");
        require(menu.activeInlineForTest()==menu.inlineForTest(Module.GLINT)&&focusedAction(menu.inlineForTest(Module.GLINT),tr("Цвет сияния","Glint color")),"Color editor return lost its originating inline action");
        fps=menu.inlineForTest(Module.FPS);focusInline(menu,fps,action(fps,tr("Основное","General")));menu.keyPressed(ENTER);menu.settleForTest();
        require(menu.inlineForTest(Module.GLINT).children().stream().anyMatch(c->c instanceof OrbitButton b&&b.getMessage().getString().equals(tr("Цвет сияния","Glint color"))),"FPS tab changed Glint's selected tab");
        verifyInlineBinding(mc,menu,fps);
        focusInline(menu,fps,action(fps,tr("Изменить положение","Edit position")));menu.keyPressed(ENTER);
        require(mc.gui.screen() instanceof HudEditorScreen,"Inline position action did not open HUD editor");mc.gui.screen().onClose();
        require(mc.gui.screen()==menu,"HUD editor did not return to its owning Click GUI");
        require(menu.activeInlineForTest()==menu.inlineForTest(Module.FPS)&&focusedAction(menu.inlineForTest(Module.FPS),tr("Изменить положение","Edit position")),"HUD editor return lost its originating inline action");
        var hud=action(menu,tr("Редактор HUD","HUD Editor"));
        for(int i=0;i<500&&menu.getFocused()!=hud;i++)menu.keyPressed(TAB);
        require(menu.getFocused()==hud&&menu.activeInlineForTest()==null,"Tab cannot leave inline settings for the HUD editor action");menu.keyPressed(ENTER);
        require(mc.gui.screen() instanceof HudEditorScreen,"Global HUD editor button did not open editor");mc.gui.screen().onClose();require(mc.gui.screen()==menu,"Global HUD editor lost Click GUI parent");
        search(menu).setValue("Hitbox");var hitbox=action(menu,Module.HITBOX.title());
        require(menu.mouseClicked(new MouseButtonEvent(menu.screenCoordinate(hitbox.getX()+5),menu.screenCoordinate(hitbox.getY()+5),new MouseButtonInfo(GLFW.GLFW_MOUSE_BUTTON_RIGHT,0)),false),"Right-click settings action was not handled");
        menu.settleForTest();require(mc.gui.screen()==menu&&action(menu.inlineForTest(Module.HITBOX),tr("Основное","General")).visible,"Right-click did not expand inline Hitbox settings");
    }
    private static boolean focusedAction(ModuleScreen host,String text){return host.getFocused() instanceof OrbitButton b&&b.getMessage().getString().equals(text);}
    private static void verifyInlineBinding(Minecraft mc,OrbitScreen menu,ModuleScreen host){
        var mapping=OrbitClient.moduleKeys.get(Module.FPS);String original=mapping.saveString();
        try{
            for(var capture:new KeyEvent[]{TAB,new KeyEvent(GLFW.GLFW_KEY_F,0,GLFW.GLFW_MOD_CONTROL)}){
                var key=host.children().stream().filter(c->c instanceof OrbitButton b&&b.getMessage().getString().startsWith(tr("Клавиша: ","Key: "))).map(c->(OrbitButton)c).findFirst().orElseThrow();
                focusInline(menu,host,key);menu.keyPressed(ENTER);require(host.isBinding(),"Inline key binding did not start capture");
                menu.keyPressed(capture);
                require(!host.isBinding()&&mapping.saveString().equals(capture.key()==GLFW.GLFW_KEY_TAB?"key.keyboard.tab":"key.keyboard.f"),"Click GUI intercepted a captured Tab or Ctrl+F");
                require(menu.activeInlineForTest()==host&&!search(menu).isFocused(),"Key capture transferred focus to Click GUI navigation");
            }
        }finally{mapping.setKey(InputConstants.getKey(original));KeyMapping.resetMapping();mc.options.save();}
    }
    private static void verifyCompactInline(Minecraft mc){
        var menu=new OrbitScreen();mc.gui.setScreen(menu);menu.init(320,180);
        action(menu,tr("Сбросить расположение","Reset layout")).onClick(null,false);menu.expandForTest(Module.FPS);
        var host=menu.inlineForTest(Module.FPS);
        for(String tab:new String[]{tr("Основное","General"),tr("Параметры","Options")}){
            focusInline(menu,host,action(host,tab));menu.keyPressed(ENTER);menu.settleForTest();
            var expected=host.children().stream().filter(c->c instanceof AbstractWidget w&&w.active).map(c->(AbstractWidget)c).toList();
            var reached=new java.util.HashSet<AbstractWidget>();
            for(int i=0;i<menu.children().size()+host.children().size()+10;i++){
                menu.keyPressed(TAB);
                if(host.getFocused() instanceof AbstractWidget widget){
                    require(menu.inlineFocusFullyVisibleForTest(Module.FPS),"Compact Click GUI clips a focused setting: "+widget.getMessage().getString());reached.add(widget);
                }
            }
            require(reached.containsAll(expected),"Compact Click GUI Tab cannot reach every setting in "+tab);
        }
    }
    private static void focusInline(OrbitScreen menu,ModuleScreen host,AbstractWidget target){
        for(int i=0;i<500;i++){
            menu.keyPressed(TAB);
            if(host.getFocused()==target){require(target.visible,"Tab focused a clipped inline control");return;}
        }
        throw new AssertionError("Tab cannot reach inline setting: "+target.getMessage().getString());
    }
    private static EditBox search(OrbitScreen menu){return menu.children().stream().filter(EditBox.class::isInstance).map(EditBox.class::cast).findFirst().orElseThrow();}
    private static OrbitButton action(net.minecraft.client.gui.screens.Screen screen,String text){return screen.children().stream().filter(c->c instanceof OrbitButton b&&b.getMessage().getString().equals(text)).map(c->(OrbitButton)c).findFirst().orElseThrow(()->new AssertionError("Missing action: "+text));}
    private static void require(boolean condition,String message){if(!condition)throw new AssertionError(message);}
    private UiWorkflowChecks(){}
}
