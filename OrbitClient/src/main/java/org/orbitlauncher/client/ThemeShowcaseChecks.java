package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;

/** Runs only with the explicit theme-smoke flag in a new test world. */
final class ThemeShowcaseChecks {
    private static int ticks;
    private static InterfaceOptions original;
    static void tick(Minecraft mc){
        ticks++;
        if(ticks==20){
            original=OrbitClient.config.interfaceOptions;
            var ui=new InterfaceOptions();ui.animations=false;ui.smoothChanges=false;
            OrbitClient.config.interfaceOptions=ui;
            mc.gui.hud.getChat().clearMessages(true);mc.gui.toastManager().clear();
        }
        if(ticks>=20&&ticks<116&&(ticks-20)%12==0){
            var ui=OrbitTheme.options();ui.theme=(ticks-20)/12;
            show(mc);OrbitClient.LOG.info("Theme showcase: {}",InterfaceOptions.THEMES[ui.theme]);
        }
        if(ticks>=26&&ticks<116&&(ticks-26)%12==0)capture(mc);
        if(ticks==118){var ui=OrbitTheme.options();ui.theme=0;ui.background=0;ui.blurRadius=0;show(mc);OrbitClient.LOG.info("Background showcase: blur 0");}
        if(ticks==124)capture(mc);
        if(ticks==130){OrbitTheme.options().blurRadius=10;show(mc);OrbitClient.LOG.info("Background showcase: blur 10");}
        if(ticks==136)capture(mc);
        if(ticks==142){var ui=OrbitTheme.options();ui.background=1;ui.dimStrength=0;show(mc);OrbitClient.LOG.info("Background showcase: dim 0");}
        if(ticks==148)capture(mc);
        if(ticks==154){OrbitTheme.options().dimStrength=1;show(mc);OrbitClient.LOG.info("Background showcase: dim 100");}
        if(ticks==160)capture(mc);
        if(ticks==168){OrbitClient.config.interfaceOptions=original;OrbitClient.save();OrbitClient.LOG.info("Orbit Client smoke checks completed: eight theme surfaces and native blur/dim strengths");mc.stop();}
    }
    private static void show(Minecraft mc){var menu=new OrbitScreen();mc.gui.setScreen(menu);menu.expandForTest(Module.INTERFACE);menu.settleForTest();}
    private static void capture(Minecraft mc){
        int expected=OrbitTheme.options().effectiveBlurRadius();
        if(mc.gameRenderer.gameRenderState().optionsRenderState.menuBackgroundBlurriness!=expected)throw new AssertionError("Native blur uniform does not match Interface strength");
        if(OrbitTheme.CARD!=OrbitTheme.options().card(System.nanoTime()/1e9))throw new AssertionError("Theme surface did not reach renderer");
        net.minecraft.client.Screenshot.grab(mc,false);
    }
}
