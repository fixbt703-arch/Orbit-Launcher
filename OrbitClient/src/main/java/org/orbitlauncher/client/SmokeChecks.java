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
        if(ticks==20) {initialLanguage=mc.options.languageCode;if(Boolean.getBoolean("orbit.client.label-smoke"))mc.options.languageCode="ru_ru";mc.gui.setScreen(new OrbitScreen());}
        if(ticks==80) {
            UiWorkflowChecks.verify(mc);
            var legacyCrosshair=ClientConfig.parse(com.google.gson.JsonParser.parseString("{\"modules\":{\"CROSSHAIR\":{\"criticalWidth\":48,\"criticalOffset\":18}}}"));
            if(legacyCrosshair.state(Module.CROSSHAIR).criticalScale!=75||legacyCrosshair.state(Module.CROSSHAIR).criticalOffset!=9)throw new AssertionError("Old critical-bar layout did not migrate to native compact defaults");
            var customOffset=ClientConfig.parse(com.google.gson.JsonParser.parseString("{\"modules\":{\"CROSSHAIR\":{\"criticalWidth\":24,\"criticalOffset\":10}}}"));
            if(customOffset.state(Module.CROSSHAIR).criticalOffset!=10)throw new AssertionError("Explicit critical offset lost during migration");

            verifyTypography();
            var migrated=ClientConfig.parse(com.google.gson.JsonParser.parseString("{\"modules\":{\"HP_INDICATOR\":{\"enabled\":true},\"FPS\":{\"enabled\":false,\"customPosition\":true,\"x\":0.43},\"HOLD_MY_ITEMS\":{\"hmiScale\":1.2}}}"));
            if(migrated.state(Module.FPS).enabled||!migrated.state(Module.FPS).customPosition||migrated.state(Module.FPS).x!=.43||Math.abs(migrated.state(Module.HOLD_MY_ITEMS).hmiScale-1.2)>.001)
                throw new AssertionError("Retired module migration lost existing settings");
            OrbitSlider.verifyPointerMapping();
            var scrollTest=new ModuleScreen(new OrbitScreen(),Module.FPS);scrollTest.selectTabForTest(1);scrollTest.init(427,240);scrollTest.verifyWheel();
            String language=mc.options.languageCode;
            for(String testLanguage:new String[]{"ru_ru","en_us"}) {
            mc.options.languageCode=testLanguage;
            for(int[] size:new int[][]{{320,180},{427,240},{550,270},{580,300},{640,360},{960,540}}) {
                var menu=new OrbitScreen();menu.init(size[0],size[1]);menu.verifyLayouts();menu.verifyFiltering();
                var pixelScreen=new CrosshairScreen(menu);pixelScreen.init(size[0],size[1]);pixelScreen.verifyLayouts();
                var colorScreen=new CrosshairColorScreen(menu);colorScreen.init(size[0],size[1]);colorScreen.verifyLayouts();
                var hudColors=new HudColorScreen(menu,Module.FPS);hudColors.init(size[0],size[1]);hudColors.verifyLayouts();
                for(var module:new Module[]{Module.GLINT,Module.HITBOX})for(int role=0;role<(module==Module.HITBOX?6:1);role++){var screen=new FeatureColorScreen(menu,module,role);screen.init(size[0],size[1]);verifyWidgets(screen);}
                var performance=new OrbitScreen.PerformanceScreen(menu);performance.init(size[0],size[1]);performance.verifyLayouts();
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
        if(ticks==110) Screenshot.grab(mc,false);
        if(ticks==114&&mc.gui.screen() instanceof OrbitScreen menu)menu.children().stream().filter(c->c instanceof OrbitButton b&&b.getMessage().getString().equals("PVP")).map(c->(OrbitButton)c).findFirst().orElseThrow().onClick(null,false);
        if(ticks==115||ticks==116||ticks==118)Screenshot.grab(mc,false);
        if(ticks==130) {
            if(Boolean.getBoolean("orbit.client.label-smoke")){mc.gui.setScreen(new HudColorScreen(new OrbitScreen(),Module.FPS));}
            else {
            var screen=new ModuleScreen(new OrbitScreen(),Module.FPS);screen.selectTabForTest(1);mc.gui.setScreen(screen);
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
        if(ticks==430 && Boolean.getBoolean("orbit.client.world-smoke")){WorldSmoke.finishRefill(mc);var menu=new OrbitScreen();mc.gui.setScreen(menu);menu.mouseScrolled(menu.viewX+10,menu.viewY+10,0,-100);}
        if(ticks==450 && Boolean.getBoolean("orbit.client.world-smoke"))Screenshot.grab(mc,false);
        if(ticks==455 && Boolean.getBoolean("orbit.client.world-smoke")){var screen=new CrosshairColorScreen(new OrbitScreen());mc.gui.setScreen(screen);screen.verifyInput();for(var module:new Module[]{Module.GLINT,Module.HITBOX})for(boolean target:new boolean[]{false,true}){var feature=new FeatureColorScreen(new OrbitScreen(),module,target);feature.init(640,360);feature.verifyInput();}WorldSmoke.verifyNewFeatures(mc);var hitbox=new ModuleScreen(new OrbitScreen(),Module.HITBOX);hitbox.selectTabForTest(1);mc.gui.setScreen(hitbox);}
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
        if(ticks==(Boolean.getBoolean("orbit.client.world-smoke")?600:400)) {OrbitClient.LOG.info("Orbit Client smoke checks completed");mc.stop();}
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
