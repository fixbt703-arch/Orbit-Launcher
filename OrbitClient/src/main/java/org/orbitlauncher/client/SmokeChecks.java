package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.Screenshot;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.components.AbstractWidget;
import java.util.*;

/** Only executed in a development run with -PorbitSmoke. Never changes a user's worlds. */
final class SmokeChecks {
    private static int ticks;
    private static int checks;
    private static String initialLanguage;
    private static java.util.concurrent.CompletableFuture<Void> reload;
    static void tick(Minecraft mc) {
        if(Boolean.getBoolean("orbit.client.world-smoke") && !WorldSmoke.prepare(mc))return;
        if(Boolean.getBoolean("orbit.client.offline-skin-smoke")){OfflineSkinSmoke.tick(mc);return;}
        if(Boolean.getBoolean("orbit.client.trajectory-smoke")){TrajectorySmoke.tick(mc);return;}
        if(Boolean.getBoolean("orbit.client.theme-smoke")){ThemeShowcaseChecks.tick(mc);return;}
        if(AttackIndicatorSmoke.ACTIVE){AttackIndicatorSmoke.tick(mc);return;}
        if(Boolean.getBoolean("orbit.client.refill-smoke")){RefillSmoke.tick(mc);return;}
        if(Boolean.getBoolean("orbit.client.glow-smoke")){GlowSmoke.tick(mc);return;}
        if(Boolean.getBoolean("orbit.client.hmi-smoke")){HmiSmoke.tick(mc);return;}
        if(mc.gui.screen()==null && mc.player==null) return;
        if(reload!=null){
            if(!reload.isDone())return;
            reload.join();reload=null;verifyTypography();OrbitClient.LOG.info("Orbit typography survives resource reload");
        }
        ticks++;
        if(Boolean.getBoolean("orbit.client.visual-smoke"))InterfaceChecks.showcase(mc,ticks);
        if(Boolean.getBoolean("orbit.client.visual-smoke"))SharedHudChecks.tick(mc,ticks);
        if(ticks==20) {initialLanguage=mc.options.languageCode;if(Boolean.getBoolean("orbit.client.label-smoke"))mc.options.languageCode="ru_ru";mc.gui.setScreen(new OrbitScreen());}
        if(ticks==25&&Boolean.getBoolean("orbit.client.scale-smoke"))Screenshot.grab(mc,false);
        if(ticks==30&&Boolean.getBoolean("orbit.client.scale-smoke")){
            if(!(mc.gui.screen() instanceof OrbitScreen menu)||menu.width<600||menu.height<360)
                throw new AssertionError("Orbit menu inherits oversized Minecraft GUI scale: "+mc.getWindow().getGuiScale());
            ((OrbitScreen)mc.gui.screen()).verifyViewportForTest();
        }
        if(ticks==35&&Boolean.getBoolean("orbit.client.scale-smoke"))verifyGuiScales(mc);
        if(ticks==80) {
            InterfaceChecks.verify(mc);
            UiWorkflowChecks.verify(mc);
            HudUiRegressionChecks.verify(mc);
            var legacyCrosshair=ClientConfig.parse(com.google.gson.JsonParser.parseString("{\"modules\":{\"CROSSHAIR\":{\"criticalWidth\":48,\"criticalOffset\":18}}}"));
            if(legacyCrosshair.state(Module.CROSSHAIR).criticalScale!=75||legacyCrosshair.state(Module.CROSSHAIR).criticalOffset!=9)throw new AssertionError("Old critical-bar layout did not migrate to native compact defaults");
            var customOffset=ClientConfig.parse(com.google.gson.JsonParser.parseString("{\"modules\":{\"CROSSHAIR\":{\"criticalWidth\":24,\"criticalOffset\":10}}}"));
            if(customOffset.state(Module.CROSSHAIR).criticalOffset!=10)throw new AssertionError("Explicit critical offset lost during migration");

            verifyTypography();
            verifyLibraryMigration();
            var migrated=ClientConfig.parse(com.google.gson.JsonParser.parseString("{\"modules\":{\"HP_INDICATOR\":{\"enabled\":true},\"FPS\":{\"enabled\":false,\"customPosition\":true,\"x\":0.43},\"HOLD_MY_ITEMS\":{\"hmiScale\":1.2}}}"));
            if(migrated.state(Module.FPS).enabled||!migrated.state(Module.FPS).customPosition||migrated.state(Module.FPS).x!=.43||Math.abs(migrated.state(Module.HOLD_MY_ITEMS).hmiScale-1.2)>.001)
                throw new AssertionError("Retired module migration lost existing settings");
            OrbitSlider.verifyPointerMapping();
            var scrollTest=new ModuleScreen(new OrbitScreen(),Module.INTERFACE);scrollTest.selectTabForTest(1);scrollTest.init(427,240);scrollTest.verifyWheel();
            String language=mc.options.languageCode;
            for(String testLanguage:new String[]{"ru_ru","en_us"}) {
            mc.options.languageCode=testLanguage;
            for(int[] size:new int[][]{{320,180},{427,240},{550,270},{580,300},{640,360},{960,540}}) {
                var menu=new OrbitScreen();menu.init(size[0],size[1]);menu.verifyLayouts();menu.verifyFiltering();
                var pixelScreen=new CrosshairScreen(menu);pixelScreen.init(size[0],size[1]);pixelScreen.verifyLayouts();
                var colorScreen=new CrosshairColorScreen(menu);colorScreen.init(size[0],size[1]);colorScreen.verifyLayouts();
                for(var module:new Module[]{Module.GLINT,Module.HITBOX,Module.TRAJECTORIES})for(int role=0;role<(module==Module.HITBOX?6:1);role++){var screen=new FeatureColorScreen(menu,module,role);screen.init(size[0],size[1]);verifyWidgets(screen);}
                for(var module:Module.values()) {var screen=new ModuleScreen(menu,module);screen.init(size[0],size[1]);screen.verifyLayouts();}
                var cross=OrbitClient.config.state(Module.CROSSHAIR);
                int shape=cross.crosshairShape;boolean dot=cross.crosshairDot,dynamic=cross.crosshairDynamic;
                for(int form=0;form<5;form++){cross.crosshairShape=form;cross.crosshairDot=true;cross.crosshairDynamic=true;
                    var screen=new ModuleScreen(menu,Module.CROSSHAIR);screen.init(size[0],size[1]);screen.verifyLayouts();}
                cross.crosshairShape=shape;cross.crosshairDot=dot;cross.crosshairDynamic=dynamic;
                var editor=new HudEditorScreen(menu);editor.init(size[0],size[1]);verifyWidgets(editor);
            }
            }
            mc.options.languageCode=language;
            OrbitClient.LOG.info("Orbit UI geometry: {} screen/scroll positions passed",checks);
            mc.gui.setScreen(new OrbitScreen());
        }
        if(ticks==108&&Boolean.getBoolean("orbit.client.visual-smoke"))FocusShapeChecks.tick(mc,ticks);
        if(ticks==110) Screenshot.grab(mc,false);
        if(ticks==114&&mc.gui.screen() instanceof OrbitScreen menu)menu.expandForTest(Module.INTERFACE);
        if(ticks==114&&Boolean.getBoolean("orbit.client.visual-smoke"))FocusShapeChecks.tick(mc,ticks);
        if(ticks==115||ticks==116||ticks==118)Screenshot.grab(mc,false);
        if(ticks==130) {
            if(Boolean.getBoolean("orbit.client.label-smoke")){var shared=new ModuleScreen(new OrbitScreen(),Module.INTERFACE);shared.selectTabForTest(1);mc.gui.setScreen(shared);}
            else {
            var screen=new ModuleScreen(new OrbitScreen(),Module.INTERFACE);screen.selectTabForTest(Boolean.getBoolean("orbit.client.visual-smoke")?0:1);mc.gui.setScreen(screen);
            }
        }
        if(ticks==150&&mc.gui.screen() instanceof ModuleScreen screen)screen.mouseScrolled(screen.viewX+10,screen.viewY+10,0,-4);
        if(ticks==165&&mc.gui.screen() instanceof ModuleScreen screen)screen.children().stream().filter(c->c instanceof OrbitSwitch s&&s.label.equals(OrbitClient.tr("Акцентная полоса","Accent stripe"))).map(c->(OrbitSwitch)c).findFirst().ifPresent(screen::setFocused);
        if(ticks==170) Screenshot.grab(mc,false);
        if(ticks==185 && Boolean.getBoolean("orbit.client.label-smoke")){mc.options.languageCode=initialLanguage;OrbitClient.LOG.info("Orbit label smoke checks completed");mc.stop();return;}
        if(ticks==190) {OrbitClient.config.state(Module.ARMOR).enabled=true;OrbitClient.config.state(Module.EFFECTS).enabled=true;mc.gui.setScreen(new HudEditorScreen(new OrbitScreen()));}
        if(ticks==205 && mc.gui.screen() instanceof HudEditorScreen editor)editor.beginMoveTest();
        if(ticks==220 && mc.gui.screen() instanceof HudEditorScreen editor)editor.verifyMoveTest();
        if(ticks==225 && mc.gui.screen() instanceof HudEditorScreen editor)editor.selectForTest(Module.ARMOR);
        if(ticks==230) Screenshot.grab(mc,false);
        if(ticks==260) mc.gui.setScreen(new IconPreview());
        if(ticks==300) Screenshot.grab(mc,false);
        if(ticks==315){OrbitClient.config.state(Module.CROSSHAIR).crosshairMode=1;mc.gui.setScreen(new CrosshairScreen(new OrbitScreen()));}
        if(ticks==345)Screenshot.grab(mc,false);
        if(ticks==355){var s=OrbitClient.config.state(Module.CROSSHAIR);s.crosshairMode=2;s.crosshairColorMode=2;mc.gui.setScreen(new CrosshairScreen(new OrbitScreen()));}
        if(ticks==385)Screenshot.grab(mc,false);
        if(ticks==395 && Boolean.getBoolean("orbit.client.world-smoke"))WorldSmoke.verify(mc);
        if(ticks==400 && Boolean.getBoolean("orbit.client.world-smoke"))WorldSmoke.prepareRefill(mc);
        if(ticks==415 && Boolean.getBoolean("orbit.client.world-smoke"))WorldSmoke.consumeRefill(mc);
        if(ticks==420 && Boolean.getBoolean("orbit.client.world-smoke"))Screenshot.grab(mc,false);
        if(ticks==430 && Boolean.getBoolean("orbit.client.world-smoke")){WorldSmoke.finishRefill(mc);mc.gui.setScreen(new OrbitScreen());}
        if(ticks==438 && Boolean.getBoolean("orbit.client.world-smoke")&&Boolean.getBoolean("orbit.client.visual-smoke"))expandedMenu(mc,Module.GLINT);
        if(ticks==450 && Boolean.getBoolean("orbit.client.world-smoke"))Screenshot.grab(mc,false);
        if(ticks==455 && Boolean.getBoolean("orbit.client.world-smoke")){var screen=new CrosshairColorScreen(new OrbitScreen());mc.gui.setScreen(screen);screen.verifyInput();for(var module:new Module[]{Module.GLINT,Module.HITBOX,Module.TRAJECTORIES})for(boolean target:new boolean[]{false,true}){var feature=new FeatureColorScreen(new OrbitScreen(),module,target);feature.init(640,360);feature.verifyInput();}WorldSmoke.verifyNewFeatures(mc);expandedMenu(mc,Module.HITBOX);}
        if(ticks==480 && Boolean.getBoolean("orbit.client.world-smoke"))Screenshot.grab(mc,false);
        if(Boolean.getBoolean("orbit.client.world-smoke")){
            if(ticks==490)reload=mc.reloadResourcePacks();
            if(ticks==500){var menu=new OrbitScreen();mc.gui.setScreen(menu);menu.init(320,180);}
            if(ticks==520)Screenshot.grab(mc,false);
            if(ticks==525)mc.gui.setScreen(new OrbitScreen());
            if(ticks==545)Screenshot.grab(mc,false);
            if(ticks==550&&mc.gui.screen() instanceof OrbitScreen menu)menu.children().stream().filter(net.minecraft.client.gui.components.EditBox.class::isInstance).map(net.minecraft.client.gui.components.EditBox.class::cast).findFirst().orElseThrow().setValue("no-such-orbit-module");
            if(ticks==570)Screenshot.grab(mc,false);
            if(ticks==575&&mc.gui.screen() instanceof OrbitScreen menu)menu.children().stream().filter(c->c instanceof OrbitButton b&&b.getMessage().getString().equals(OrbitClient.tr("Показать все модули","Show all modules"))).map(c->(OrbitButton)c).findFirst().orElseThrow().onClick(null,false);
            if(ticks==590)Screenshot.grab(mc,false);
        }
        if(Boolean.getBoolean("orbit.client.visual-smoke")){
            if(ticks==395)expandedMenu(mc,Module.GLINT);
            if(ticks==398&&Boolean.getBoolean("orbit.client.world-smoke"))Screenshot.grab(mc,false);
            if(ticks==420&&!Boolean.getBoolean("orbit.client.world-smoke"))Screenshot.grab(mc,false);
            if(ticks==430)mc.gui.setScreen(new OrbitScreen());
            if(ticks==431||ticks==433||ticks==438)Screenshot.grab(mc,false);
            if(ticks==440&&!Boolean.getBoolean("orbit.client.world-smoke"))expandedMenu(mc,Module.HITBOX);
            if(ticks==455&&!Boolean.getBoolean("orbit.client.world-smoke"))Screenshot.grab(mc,false);
        }
        if(ticks==(Boolean.getBoolean("orbit.client.world-smoke")?600:Boolean.getBoolean("orbit.client.visual-smoke")?460:400)) {OrbitClient.LOG.info("Orbit Client smoke checks completed");mc.stop();}
    }
    private static void expandedMenu(Minecraft mc,Module module){
        var menu=new OrbitScreen();mc.gui.setScreen(menu);menu.expandForTest(module);
        menu.inlineForTest(module).children().stream().filter(c->c instanceof OrbitButton b&&b.getMessage().getString().equals(OrbitClient.tr("Оформление","Appearance"))).map(c->(OrbitButton)c).findFirst().orElseThrow().onClick(null,false);
        menu.settleForTest();
    }
    private static void verifyGuiScales(Minecraft mc){
        var menu=(OrbitScreen)mc.gui.screen();int nativeScale=mc.options.guiScale().get();
        try{
            var legacy=ClientConfig.parse(com.google.gson.JsonParser.parseString("{\"clientPanels\":{\"HUD\":{\"x\":0.8,\"y\":0.8}},\"modules\":{\"FPS\":{\"enabled\":false,\"customPosition\":true,\"x\":0.43}}}"));
            legacy.save();var migrated=ClientConfig.load();
            if(migrated.clientPanelViewportVersion!=1||!migrated.clientPanels.isEmpty()||migrated.state(Module.FPS).enabled||migrated.state(Module.FPS).x!=.43||!migrated.state(Module.FPS).customPosition)
                throw new AssertionError("Viewport migration lost module or HUD settings");
            OrbitClient.config.save();
            for(int scale:new int[]{1,2,3,4}){
                mc.options.guiScale().set(scale);mc.resizeGui();
                if(menu.width<600||menu.height<360||menu.width!=menu.viewportWidth()||menu.height!=menu.viewportHeight())throw new AssertionError("Native resize bypassed viewport at GUI "+scale);
                menu.verifyViewportInputs();
                if(mc.getWindow().getGuiScale()!=scale||mc.options.guiScale().get()!=scale)throw new AssertionError("Menu changed the game's HUD scale");
            }
            OrbitClient.LOG.info("GUI scale regression: native scales 1-4, resize, clicks, slider drag, smooth wheel, clipping, scoped tooltip metrics and legacy layout migration passed");
        }finally{mc.options.guiScale().set(nativeScale);mc.resizeGui();OrbitClient.config.save();mc.gui.setScreen(new OrbitScreen());}
    }
    private static void verifyLibraryMigration() {
        var modules=new com.google.gson.JsonObject();
        for(String retired:new String[]{"ITEM","CLOCK","SPRINT","ARROWS","RESOURCES","DIRECTION"})modules.add(retired,new com.google.gson.JsonObject());
        modules.add("FUTURE_MODULE",new com.google.gson.JsonObject());
        var legacy=new com.google.gson.JsonObject();legacy.addProperty("hmiScale",1.2);modules.add("COOL_ANIM",legacy);
        var fps=new com.google.gson.JsonObject();fps.addProperty("enabled",false);fps.addProperty("scale",1.35);modules.add("FPS",fps);
        var tree=new com.google.gson.JsonObject();tree.add("modules",modules);
        var ui=new com.google.gson.JsonObject();ui.addProperty("theme",7);ui.addProperty("themeSetVersion",1);tree.add("interfaceOptions",ui);
        var migrated=ClientConfig.parse(tree);
        if(migrated.state(Module.FPS).enabled||Math.abs(migrated.state(Module.FPS).scale-1.35)>.001||migrated.modules.size()!=2
            ||Math.abs(migrated.state(Module.HOLD_MY_ITEMS).hmiScale-1.2)>.001||migrated.interfaceOptions.theme!=7)
            throw new AssertionError("Removing unknown modules reset surviving configuration or legacy migration");
        float previous=0;
        for(int frame=0;frame<=100;frame++){float progress=OrbitReveal.ease(frame/100d);if(progress<previous||progress>1)throw new AssertionError("Opening animation overshoots");previous=progress;}
        if(OrbitReveal.ease(0)!=0||OrbitReveal.ease(1)!=1||OrbitReveal.ease(-1)!=0||OrbitReveal.ease(2)!=1)throw new AssertionError("Opening animation endpoints");
        OrbitClient.LOG.info("Module library migration and bounded opening animation passed");
    }
    private static void verifyTypography() {
        var samplers=com.mojang.blaze3d.systems.RenderSystem.getSamplerCache();
        for(var font:java.util.List.of(OrbitFonts.ui(),OrbitFonts.strong(),OrbitFonts.heading(),OrbitFonts.brand(),Minecraft.getInstance().font)) {
            boolean orbit=font!=Minecraft.getInstance().font;
            var provider=((org.orbitlauncher.client.mixin.FontProviderAccessor)(Object)font).orbit$provider();
            var glyph=provider.glyphs(net.minecraft.network.chat.FontDescription.DEFAULT).getGlyph('Ж')
                .createGlyph(0,0,-1,0,net.minecraft.network.chat.Style.EMPTY,0,0);
            var render=new net.minecraft.client.renderer.state.gui.GlyphRenderState(new org.joml.Matrix3x2f(),glyph,null);
            var expected=samplers.getClampToEdge(orbit?com.mojang.blaze3d.textures.FilterMode.LINEAR:com.mojang.blaze3d.textures.FilterMode.NEAREST);
            if(render.textureSetup().sampler0()!=expected)throw new AssertionError(orbit?"Orbit UI font is sampled with sharp nearest filtering":"Vanilla text filtering was modified");
        }
        OrbitClient.LOG.info("Typography: four smooth Orbit type roles and unchanged vanilla filtering passed");
    }
    static void verifyWidgets(Screen screen) {
        List<AbstractWidget> widgets=new ArrayList<>();
        for(var child:screen.children()) if(child instanceof AbstractWidget widget && widget.visible) widgets.add(widget);
        for(int i=0;i<widgets.size();i++) {
            var a=widgets.get(i);
            if(a instanceof OrbitSwitch toggle) {
                int trackX=OrbitSwitch.trackX(toggle.getX(),toggle.getWidth());
                if(toggle.getWidth()>50 && trackX+26!=toggle.getX()+toggle.getWidth()-10)
                    throw new IllegalStateException("Switch is not right aligned: "+toggle.getMessage().getString());
                if(trackX<toggle.getX() || trackX+26>toggle.getX()+toggle.getWidth())
                    throw new IllegalStateException("Switch track is clipped: "+toggle.getMessage().getString());
            }
            if(a instanceof OrbitButton || a instanceof OrbitSlider) {
                String text=a instanceof OrbitSwitch toggle?(a.getWidth()>50?toggle.label:""):a.getMessage().getString();var font=a instanceof OrbitButton button?button.labelFont():screen.getFont();
                int available=Math.max(1,a.getWidth()-(a instanceof OrbitSlider?16:10)),height=a instanceof OrbitSlider?13:a.getHeight()-6;
                if(a instanceof OrbitSwitch)available=Math.max(1,a.getWidth()-48);
                else if(a instanceof OrbitButton button)available=Math.max(1,available-button.textInset());
                var label=ButtonLabel.layout(font,text,available,height);
                if(!String.join(" ",label.lines()).equals(text))throw new IllegalStateException("Label lost characters: "+text);
                for(String line:label.lines())if(font.width(line)*label.scale()>available+.01)throw new IllegalStateException("Label overflows: "+text);
                if((label.lines().size()*10-1)*label.scale()>height+.01)throw new IllegalStateException("Label too tall: "+text);
            }
            int[] ab=screen instanceof OrbitScrollScreen s&&s.isContentWidget(a)?s.clippedBounds(a):new int[]{a.getX(),a.getY(),a.getX()+a.getWidth(),a.getY()+a.getHeight()};
            if(ab[0]<0 || ab[1]<0 || ab[2]>screen.width || ab[3]>screen.height)
                throw new IllegalStateException("Clipped widget: "+a.getMessage().getString());
            for(int j=i+1;j<widgets.size();j++) {
                var b=widgets.get(j);
                int[] bb=screen instanceof OrbitScrollScreen s&&s.isContentWidget(b)?s.clippedBounds(b):new int[]{b.getX(),b.getY(),b.getX()+b.getWidth(),b.getY()+b.getHeight()};
                if(ab[0]<bb[2] && ab[2]>bb[0] && ab[1]<bb[3] && ab[3]>bb[1])
                    throw new IllegalStateException("Overlapping widgets: "+a.getMessage().getString()+" / "+b.getMessage().getString());
            }
        }
        checks++;
    }
    private static final class IconPreview extends Screen {
        IconPreview(){super(net.minecraft.network.chat.Component.literal("Orbit icon test"));}
        @Override public void extractRenderState(net.minecraft.client.gui.GuiGraphicsExtractor g,int mx,int my,float delta){
            g.fill(0,0,width,height,0xFF101927);int columns=Math.max(1,width/65);
            for(var m:Module.values()){int x=(m.ordinal()%columns)*65+10,y=(m.ordinal()/columns)*50+10;
                ModuleIcons.draw(g,m,x+16,y,26,0xFFE5E8F0);g.text(font,font.plainSubstrByWidth(m.title(),62),x,y+31,0xFFB0BCD0,false);}
        }
    }
}
