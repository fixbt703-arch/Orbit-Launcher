package org.orbitlauncher.client;

import com.mojang.blaze3d.platform.InputConstants;
import net.minecraft.client.Minecraft;
import net.minecraft.client.KeyMapping;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.input.KeyEvent;
import org.lwjgl.glfw.GLFW;
import static org.orbitlauncher.client.OrbitClient.tr;

/** Runs only in the isolated UI smoke world. Exercises the actual controls and disk config. */
final class InterfaceChecks {
    private static InterfaceOptions showcaseOptions;
    static void showcase(Minecraft mc,int tick){
        if(tick==82){showcaseOptions=OrbitClient.config.interfaceOptions;OrbitClient.config.interfaceOptions=new InterfaceOptions();var menu=new OrbitScreen();mc.gui.setScreen(menu);menu.expandForTest(Module.INTERFACE);}
        if(tick==88){var ui=OrbitClient.config.interfaceOptions;ui.theme=1;ui.gradient=1;ui.background=1;ui.cornerRadius=0;var menu=new OrbitScreen();mc.gui.setScreen(menu);menu.expandForTest(Module.INTERFACE);}
        if(tick==99){var ui=OrbitClient.config.interfaceOptions;ui.theme=4;ui.gradient=2;ui.background=2;ui.cornerRadius=6;ui.animations=false;var menu=new OrbitScreen();mc.gui.setScreen(menu);menu.expandForTest(Module.INTERFACE);}
        if(tick==86||tick==97||tick==104)net.minecraft.client.Screenshot.grab(mc,false);
        if(tick==106){OrbitClient.config.interfaceOptions=showcaseOptions;showcaseOptions=null;OrbitClient.save();mc.gui.setScreen(new OrbitScreen());}
    }
    static void verify(Minecraft mc){
        var config=OrbitClient.config;var original=config.interfaceOptions;var screen=mc.gui.screen();
        String binding=OrbitClient.menuKey.saveString();boolean fps=config.state(Module.FPS).enabled;
        try{
            for(String language:new String[]{"ru_ru","en_us"}){
                String previous=mc.options.languageCode;mc.options.languageCode=language;
                try{
                    config.interfaceOptions=new InterfaceOptions();var menu=new OrbitScreen();mc.gui.setScreen(menu);
                    menu.children().stream().filter(c->c instanceof OrbitButton b&&b.getMessage().getString().equals("Interface")).map(c->(OrbitButton)c).findFirst().orElseThrow().onClick(null,false);
                    var settings=menu.inlineForTest(Module.INTERFACE);menu.settleForTest();settings.verifyInlineLayout();
                    if(settings.children().size()!=15)throw new AssertionError("Interface must expose twelve settings, two tabs and reset");
                    if(menu.height>=500&&settings.children().stream().anyMatch(c->c instanceof AbstractWidget widget&&!widget.visible))throw new AssertionError("Interface menu unnecessarily clips controls in a normal window");
                    var ui=config.interfaceOptions;
                    toggle(settings,tr("Плавная смена","Smooth changes"));
                    for(int i=0;i<InterfaceOptions.THEMES.length;i++){
                        action(settings,tr("Тема: ","Theme: "));OrbitTheme.update();
                        if((ui.theme!=InterfaceOptions.RAINBOW&&(OrbitTheme.PRIMARY!=ui.primary()||OrbitTheme.CARD!=ui.card(System.nanoTime()/1e9)))||ClientConfig.load().interfaceOptions.theme!=ui.theme)throw new AssertionError("Theme is not live or persisted");
                    }
                    for(int i=0;i<3;i++){action(settings,tr("Градиент: ","Gradient: "));if(ClientConfig.load().interfaceOptions.gradient!=ui.gradient)throw new AssertionError("Gradient not persisted");}
                    for(int i=0;i<3;i++){action(settings,tr("Фон меню: ","Menu background: "));if(ClientConfig.load().interfaceOptions.background!=ui.background)throw new AssertionError("Background not persisted");}
                    for(String label:new String[]{tr("Анимации GUI","GUI animations"),tr("Свечение","Glow"),tr("Подсказки","Tooltips"),tr("Сообщения биндов","Bind messages")})toggle(settings,label);
                    if(new OrbitMotion().toward(true)!=1||new OrbitReveal().progress()!=1||OrbitClient.tooltipsAllowed())throw new AssertionError("Motion/tooltips switches have no effect");
                    for(var editor:new OrbitUiScreen[]{new FeatureColorScreen(menu,Module.GLINT,false),new CrosshairColorScreen(menu),new HudEditorScreen(menu)}){
                        mc.gui.setScreen(editor);if(OrbitClient.tooltipsAllowed())throw new AssertionError("Tooltip suppression excludes a client editor");
                    }
                    mc.gui.setScreen(menu);
                    if(ClientConfig.load().interfaceOptions.tooltips||ClientConfig.load().interfaceOptions.animations)throw new AssertionError("Toggle settings not persisted");
                    var sliders=settings.children().stream().filter(OrbitSlider.class::isInstance).map(OrbitSlider.class::cast).toList();
                    for(var slider:sliders){slider.onClick(new net.minecraft.client.input.MouseButtonEvent(slider.getX()+10,slider.getY()+24,new net.minecraft.client.input.MouseButtonInfo(0,0)),false);}
                    OrbitClient.save();var loaded=ClientConfig.load().interfaceOptions;
                    if(loaded.cornerRadius!=0||loaded.animationSpeed!=.5||loaded.blurRadius!=0)throw new AssertionError("Interface sliders do not persist their endpoints");
                    action(settings,tr("Бинд: ","Bind: "));settings.keyPressed(new KeyEvent(GLFW.GLFW_KEY_TAB,0,0));
                    if(!settings.isBinding()||OrbitClient.menuKey.saveString().equals("key.keyboard.tab"))throw new AssertionError("Navigation key accepted as menu bind");
                    settings.keyPressed(new KeyEvent(GLFW.GLFW_KEY_G,0,0));
                    if(!OrbitClient.menuKey.saveString().equals("key.keyboard.g"))throw new AssertionError("Interface bind does not control menu key");
                    action(settings,tr("Бинд: ","Bind: "));settings.keyPressed(new KeyEvent(GLFW.GLFW_KEY_DELETE,0,0));
                    if(!OrbitClient.menuKey.saveString().equals("key.keyboard.right.shift"))throw new AssertionError("Clearing menu bind must restore a reachable default");
                    config.state(Module.FPS).enabled=false;OrbitClient.toggleFromKey(Module.FPS);
                    if(!config.state(Module.FPS).enabled)throw new AssertionError("Binding toggle stopped working");
                    action(settings,tr("Сбросить настройки","Reset settings"));
                    var defaults=config.interfaceOptions;
                    if(defaults.theme!=0||!defaults.tooltips||!defaults.animations||defaults.cornerRadius!=12||defaults.animationSpeed!=2)throw new AssertionError("Interface reset did not restore reference defaults");
                    settings.selectTabForTest(1);settings.rebuildWidgets();menu.settleForTest();
                    for(String label:new String[]{tr("Фон панели","Panel background"),tr("Рамка карточки","Card border"),tr("Акцентная полоса","Accent stripe"),tr("Заголовок","Title"),tr("Акцентный заголовок","Accent title")})toggle(settings,label);
                    var hud=defaults.hud;var savedHud=ClientConfig.load().interfaceOptions.hud;
                    if(hud.background||hud.hudBorder||hud.accentBar||hud.showTitle||hud.accentTitle||savedHud.showTitle||savedHud.background)throw new AssertionError("Shared HUD toggles do not persist");
                    action(settings,tr("Стиль: ","Style: "));action(settings,tr("Ширина: ","Width: "));action(settings,tr("Выравнивание: ","Alignment: "));OrbitClient.save();
                    savedHud=ClientConfig.load().interfaceOptions.hud;
                    if(savedHud.hudStyle!=1||savedHud.panelWidth!=90||savedHud.alignment!=1)throw new AssertionError("Shared HUD cycles do not persist");
                    for(var child:settings.children())if(child instanceof OrbitSlider slider)slider.onClick(new net.minecraft.client.input.MouseButtonEvent(slider.getX()+10,slider.getY()+24,new net.minecraft.client.input.MouseButtonInfo(0,0)),false);
                    OrbitClient.save();savedHud=ClientConfig.load().interfaceOptions.hud;
                    if(savedHud.opacity!=0||savedHud.textOpacity!=.2f||savedHud.padding!=4||savedHud.rowSpacing!=0)throw new AssertionError("Shared HUD sliders do not persist");
                    defaults.theme=3;action(settings,tr("Сбросить настройки","Reset settings"));
                    if(defaults.theme!=3||defaults.hud.padding!=8||!defaults.hud.background)throw new AssertionError("HUD reset changes menu theme or misses appearance");
                    for(var module:Module.values())if(module.hasHud()){
                        var own=new ModuleScreen(menu,module);own.init(427,240);
                        if(own.children().stream().anyMatch(c->c instanceof OrbitButton b&&b.getMessage().getString().equals(tr("Оформление","Appearance"))))throw new AssertionError("Individual HUD appearance tab remains: "+module);
                    }
                }finally{mc.options.languageCode=previous;}
            }
            config.interfaceOptions=new InterfaceOptions();
            var preferences=config.interfaceOptions;
            mc.gui.setScreen(null);int vanillaBlur=mc.options.getMenuBackgroundBlurriness();
            var backgroundMenu=new OrbitScreen();mc.gui.setScreen(backgroundMenu);
            preferences.blurRadius=10;
            if(mc.options.getMenuBackgroundBlurriness()!=10)throw new AssertionError("Orbit blur strength is not used by native rendering");
            preferences.background=1;
            if(mc.options.getMenuBackgroundBlurriness()!=0)throw new AssertionError("Dim mode still invokes native blur");
            var dimPanel=new ModuleScreen(backgroundMenu,Module.INTERFACE);mc.gui.setScreen(dimPanel);dimPanel.init(427,240);
            var dimSlider=dimPanel.children().stream().filter(c->c instanceof OrbitSlider slider&&slider.label().equals(tr("Сила затемнения","Dim strength"))).map(OrbitSlider.class::cast).findFirst().orElseThrow();
            dimSlider.onClick(new net.minecraft.client.input.MouseButtonEvent(dimSlider.getX()+dimSlider.getWidth()-10,dimSlider.getY()+24,new net.minecraft.client.input.MouseButtonInfo(0,0)),false);OrbitClient.save();
            if(ClientConfig.load().interfaceOptions.dimStrength!=1||preferences.blurRadius!=10)throw new AssertionError("Background strengths do not persist independently");
            preferences.background=2;dimPanel.rebuildWidgets();
            if(dimPanel.children().stream().anyMatch(c->c instanceof OrbitSlider slider&&(slider.label().equals(tr("Сила затемнения","Dim strength"))||slider.label().equals(tr("Сила размытия","Blur strength")))))throw new AssertionError("None mode exposes a meaningless strength slider");
            mc.gui.setScreen(null);
            if(mc.options.getMenuBackgroundBlurriness()!=vanillaBlur)throw new AssertionError("Orbit changed Minecraft's background setting");
            for(int old=0;old<5;old++){
                var legacy=ClientConfig.parse(com.google.gson.JsonParser.parseString("{\"interfaceOptions\":{\"theme\":"+old+"},\"modules\":{\"FPS\":{\"scale\":1.25,\"customPosition\":true,\"x\":0.37}}}"));
                if(legacy.interfaceOptions.theme!=new int[]{0,4,2,3,1}[old]||legacy.state(Module.FPS).scale!=1.25f||!legacy.state(Module.FPS).customPosition||legacy.state(Module.FPS).x!=.37)throw new AssertionError("Theme migration changed unrelated HUD settings");
            }
            OrbitClient.LOG.info("Interface checks: RU/EN controls, eight complete live palettes, gradient/background modes, all editor tooltip scopes, toggles, slider endpoints, reserved/menu binding, reset and disk persistence passed");
        }finally{
            config.interfaceOptions=original;config.state(Module.FPS).enabled=fps;
            OrbitClient.menuKey.setKey(InputConstants.getKey(binding));KeyMapping.resetMapping();mc.options.save();OrbitClient.save();OrbitTheme.update();mc.gui.setScreen(screen);
        }
    }
    private static void action(ModuleScreen screen,String prefix){screen.children().stream().filter(c->c instanceof OrbitButton b&&!(b instanceof OrbitSwitch)&&b.getMessage().getString().startsWith(prefix)).map(c->(OrbitButton)c).findFirst().orElseThrow().onClick(null,false);}
    private static void toggle(ModuleScreen screen,String label){screen.children().stream().filter(c->c instanceof OrbitSwitch s&&s.label.equals(label)).map(c->(OrbitSwitch)c).findFirst().orElseThrow().onClick(null,false);}
}
