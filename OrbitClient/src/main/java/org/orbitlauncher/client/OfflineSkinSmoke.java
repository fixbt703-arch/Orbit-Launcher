package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.CameraType;
import net.minecraft.client.Screenshot;
import net.minecraft.core.ClientAsset;

/** Opt-in check in a disposable world with a public skin sample and fictional offline session. */
final class OfflineSkinSmoke {
    private static int ticks,ready;
    static void tick(Minecraft mc){
        ticks++;
        if(ticks==5){mc.gui.setScreen(null);mc.options.setCameraType(CameraType.THIRD_PERSON_FRONT);mc.gui.hud.getChat().clearMessages(true);mc.gui.toastManager().clear();}
        var skin=mc.player.getSkin();
        if(ready==0&&skin.secure()&&skin.body() instanceof ClientAsset.DownloadedTexture body&&body.url().startsWith("https://ely.by/")){
            ready=ticks;OrbitClient.LOG.info("Offline own Ely skin reached actual PlayerSkin and HTTPS texture renderer");
        }
        if(ready>0&&ticks==ready+15)Screenshot.grab(mc,false);
        if(ready>0&&ticks==ready+20){OrbitClient.LOG.info("Offline skin integration checks completed");mc.stop();}
        if(ticks>200)throw new AssertionError("Offline viewer's signed Ely skin did not reach Minecraft renderer");
    }
    private OfflineSkinSmoke(){}
}
