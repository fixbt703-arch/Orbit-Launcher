package org.orbitlauncher.client;

import com.holdmylua.source.OrbitHmiSettings;
import com.holdmylua.source.access.CameraAccessor;
import com.holdmylua.source.global.GlobalsStorage;
import net.minecraft.client.Minecraft;
import net.minecraft.world.item.ItemStack;

/** Controls the bundled original HMI component through Orbit's saved settings. */
final class HoldMyItems {
    private static boolean wasEnabled;
    private static Object previousLevel;
    static void initialize(){
        OrbitHmiSettings.enabled=()->OrbitClient.enabled(Module.HOLD_MY_ITEMS);
        OrbitHmiSettings.inspectPressed=()->OrbitClient.config.state(Module.HOLD_MY_ITEMS).hmiInspect && OrbitClient.inspectKey.isDown();
        OrbitHmiSettings.outlineRenderer=(submit,buffers)->EnchantmentGlow.render(submit,buffers,true);
        sync(Minecraft.getInstance());
    }
    static void sync(Minecraft mc){
        var s=OrbitClient.config.state(Module.HOLD_MY_ITEMS);
        OrbitHmiSettings.mainHand=s.hmiMainHand;OrbitHmiSettings.offHand=s.hmiOffHand;
        OrbitHmiSettings.showHands=s.hmiShowHands;OrbitHmiSettings.blocks3d=s.hmiBlocks3d;
        OrbitHmiSettings.particles=s.hmiParticles;OrbitHmiSettings.cameraMotion=s.hmiCameraMotion;
        OrbitHmiSettings.vanillaCombat=s.hmiVanillaCombat;
        OrbitHmiSettings.offsetX=s.hmiOffsetX;OrbitHmiSettings.offsetY=s.hmiOffsetY;OrbitHmiSettings.offsetZ=s.hmiOffsetZ;
        OrbitHmiSettings.scale=s.hmiScale;OrbitHmiSettings.motionIntensity=s.hmiMotion;
        if(wasEnabled!=s.enabled || previousLevel!=mc.level){
            GlobalsStorage.registry.clear();GlobalsStorage.particles.clear();
            GlobalsStorage.mainHandItem=ItemStack.EMPTY;GlobalsStorage.offHandItem=ItemStack.EMPTY;
            clearCamera(mc);wasEnabled=s.enabled;previousLevel=mc.level;
        }
        if(!OrbitHmiSettings.animationsActive() || !s.hmiCameraMotion)clearCamera(mc);
        while(OrbitClient.inspectKey.consumeClick()){} // Native held state drives the original inspection.
    }
    private static void clearCamera(Minecraft mc){
        if(mc.gameRenderer!=null && mc.gameRenderer.mainCamera() instanceof CameraAccessor camera){
            camera.hMI5_0$setRotationValues(0,0,0);camera.hMI5_0$setPosValues(0,0,0);
        }
    }
    private HoldMyItems(){}
}

