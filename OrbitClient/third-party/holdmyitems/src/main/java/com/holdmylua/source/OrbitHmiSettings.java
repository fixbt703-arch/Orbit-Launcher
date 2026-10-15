package com.holdmylua.source;

import java.util.function.BooleanSupplier;
import net.minecraft.client.Minecraft;
import net.minecraft.world.InteractionHand;

/** Small integration API. Original Lua animations remain in the upstream pack. */
public final class OrbitHmiSettings {
    public static BooleanSupplier enabled=()->true,inspectPressed=()->false,windowActive=()->Minecraft.getInstance().isWindowActive();
    public static boolean mainHand=true,offHand=true,showHands=true,blocks3d=true,particles=true,cameraMotion=true,vanillaCombat=false;
    public static float offsetX,offsetY,offsetZ,scale=1,motionIntensity=1;
    public static int renderedHands,renderedItems,vanillaHands;
    public static java.util.function.BiConsumer<net.minecraft.client.renderer.feature.ItemFeatureRenderer.Submit,java.util.function.Function<net.minecraft.client.renderer.rendertype.RenderType,com.mojang.blaze3d.vertex.VertexConsumer>> outlineRenderer=(submit,buffers)->{};
    private static boolean previouslyAnimating;
    public static boolean inspecting(){
        var mc=Minecraft.getInstance();var player=mc.player;
        return active() && player!=null && mc.gui.screen()==null && windowActive.getAsBoolean()
            && !player.swinging && !player.isUsingItem() && !player.isScoping()
            && !mc.options.keyAttack.isDown() && !mc.options.keyUse.isDown() && inspectPressed.getAsBoolean();
    }
    public static boolean animationsActive(){return active() && (!vanillaCombat || inspecting());}
    public static void prepareFrame(){
        boolean animating=animationsActive();
        if(previouslyAnimating!=animating){
            com.holdmylua.source.global.GlobalsStorage.registry.clear();
            com.holdmylua.source.global.GlobalsStorage.modelPartAnimator.clear();
            com.holdmylua.source.global.DispatcherStorage.clear();
            com.holdmylua.source.global.item_model.ItemModelStorage.clear();
            if(Minecraft.getInstance().gameRenderer.mainCamera() instanceof com.holdmylua.source.access.CameraAccessor camera){
                camera.hMI5_0$setRotationValues(0,0,0);camera.hMI5_0$setPosValues(0,0,0);
            }
            previouslyAnimating=animating;
        }
    }
    public static boolean active(){return enabled.getAsBoolean();}
    public static boolean customHand(InteractionHand hand){
        var player=Minecraft.getInstance().player;
        return animationsActive()&&(hand==InteractionHand.MAIN_HAND?mainHand:offHand);
    }
    private OrbitHmiSettings(){}
}
