package org.orbitlauncher.client;

import com.holdmylua.source.OrbitHmiSettings;
import com.holdmylua.source.global.GlobalsStorage;
import com.holdmylua.source.scripting.custom_api.KeyBindManager;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.Minecraft;
import net.minecraft.client.Screenshot;
import net.minecraft.world.item.*;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.entity.HumanoidArm;
import org.lwjgl.glfw.GLFW;

/** Development-only test in the isolated run directory. */
final class HmiSmoke {
    private static int ticks,before;
    private static final Item[] ITEMS={Items.DIAMOND_SWORD,Items.DIAMOND_AXE,Items.DIAMOND_PICKAXE,Items.BOW,Items.CROSSBOW,Items.TRIDENT,Items.MACE,Items.STONE,Items.LANTERN,Items.WATER_BUCKET,Items.APPLE,Items.SHIELD,Items.FILLED_MAP};
    private static void require(boolean ok,String message){if(!ok)throw new IllegalStateException("HMI: "+message);}
    static void tick(Minecraft mc){
        ticks++;var s=OrbitClient.config.state(Module.HOLD_MY_ITEMS);
        if(ticks==1){
            s.hmiVanillaCombat=false;mc.options.pauseOnLostFocus=false;OrbitHmiSettings.windowActive=()->true;
            require(FabricLoader.getInstance().isModLoaded("holdmyitems"),"nested component not loaded");
            require(OrbitClient.inspectKey.saveString().equals("key.keyboard.r"),"inspection default is not R");
            require(mc.options.keyDrop.saveString().equals("key.keyboard.q")&&mc.options.keySwapOffhand.saveString().equals("key.keyboard.f"),"Q/F mappings changed");
            var migrated=ClientConfig.parse(com.google.gson.JsonParser.parseString("{\"modules\":{\"COOL_ANIM\":{\"enabled\":true},\"FPS\":{\"enabled\":false}}}"));
            require(migrated.state(Module.HOLD_MY_ITEMS).enabled&&!migrated.state(Module.FPS).enabled,"configuration migration lost module state");
            var existing=ClientConfig.parse(com.google.gson.JsonParser.parseString("{\"modules\":{\"COOL_ANIM\":{\"enabled\":true},\"HOLD_MY_ITEMS\":{\"enabled\":false}}}"));
            require(!existing.state(Module.HOLD_MY_ITEMS).enabled,"migration overwrote new settings");
            String lang=mc.options.languageCode;
            for(String test:new String[]{"ru_ru","en_us"})for(int[] size:new int[][]{{320,180},{427,240},{640,360},{960,540}}){mc.options.languageCode=test;var ui=new ModuleScreen(new OrbitScreen(),Module.HOLD_MY_ITEMS);ui.init(size[0],size[1]);ui.verifyLayouts();}
            mc.options.languageCode=lang;s.enabled=true;s.hmiScale=.9f;s.hmiOffsetX=.05f;s.hmiMotion=.8f;OrbitClient.save();
            var saved=ClientConfig.load().state(Module.HOLD_MY_ITEMS);require(saved.enabled&&saved.hmiScale==.9f&&saved.hmiOffsetX==.05f&&saved.hmiMotion==.8f,"settings persistence failed");
            s.hmiScale=1;s.hmiOffsetX=0;s.hmiMotion=1;HoldMyItems.sync(mc);
            var menu=new ModuleScreen(new OrbitScreen(),Module.HOLD_MY_ITEMS);mc.gui.setScreen(menu);
        }
        if(ticks==12)capture(mc,"hmi-settings");
        if(ticks==16){mc.gui.setScreen(null);mc.player.getInventory().setSelectedSlot(0);mc.player.getInventory().setItem(0,new ItemStack(Items.DIAMOND_SWORD));}
        if(ticks==45){
            require(OrbitHmiSettings.renderedItems>0&&OrbitHmiSettings.renderedHands>0,"original renderer not reached");
            require(!GlobalsStorage.registry.isEmpty(),"original Lua scripts did not execute");
            var keys=new KeyBindManager();OrbitClient.inspectKey.setDown(true);
            require(keys.isInspectPressed(),"R not forwarded to original scripts");
            OrbitHmiSettings.windowActive=()->false;require(!keys.isInspectPressed(),"inactive window accepted input");OrbitHmiSettings.windowActive=()->true;
            mc.options.keyAttack.setDown(true);require(!keys.isInspectPressed(),"attack accepted inspection");mc.options.keyAttack.setDown(false);
            mc.gui.setScreen(new OrbitScreen());require(!keys.isInspectPressed(),"menu accepted inspection");mc.gui.setScreen(null);
            s.hmiInspect=false;require(!keys.isInspectPressed(),"inspection toggle ignored");s.hmiInspect=true;
            s.enabled=false;HoldMyItems.sync(mc);require(!keys.isInspectPressed()&&!OrbitHmiSettings.customHand(InteractionHand.MAIN_HAND),"disabled module intercepted input/render");
            require(GlobalsStorage.registry.isEmpty(),"disabled module kept stale Lua state");before=OrbitHmiSettings.vanillaHands;
        }
        if(ticks==55){
            var property=new com.holdmylua.source.HmiEnabledProperty();
            require(!property.get(new ItemStack(Items.WATER_BUCKET),mc.level,mc.player,0,ItemDisplayContext.FIRST_PERSON_RIGHT_HAND),"disabled module kept custom item models");
            require(OrbitHmiSettings.vanillaHands>before,"disabled module did not use native renderer");s.enabled=true;HoldMyItems.sync(mc);}
        if(ticks==80){
            var property=new com.holdmylua.source.HmiEnabledProperty();
            require(property.get(new ItemStack(Items.WATER_BUCKET),mc.level,mc.player,0,ItemDisplayContext.THIRD_PERSON_RIGHT_HAND),"active custom item model unavailable");
            s.hmiBlocks3d=false;HoldMyItems.sync(mc);require(!property.get(new ItemStack(Items.WATER_BUCKET),mc.level,mc.player,0,ItemDisplayContext.THIRD_PERSON_RIGHT_HAND),"3D model setting ignored");s.hmiBlocks3d=true;HoldMyItems.sync(mc);
            capture(mc,"hmi-sword-inspect");
        }
        if(ticks==85){OrbitClient.inspectKey.setDown(false);s.hmiShowHands=false;HoldMyItems.sync(mc);before=OrbitHmiSettings.renderedHands;}
        if(ticks==95){require(OrbitHmiSettings.renderedHands==before,"hide hands ignored");s.hmiShowHands=true;s.hmiOffHand=false;HoldMyItems.sync(mc);require(!OrbitHmiSettings.customHand(InteractionHand.OFF_HAND),"off-hand toggle ignored");s.hmiOffHand=true;}
        if(ticks>=100 && ticks<100+ITEMS.length*35){
            int index=(ticks-100)/35,frame=(ticks-100)%35;
            if(frame==0){mc.player.getInventory().setItem(0,new ItemStack(ITEMS[index]));OrbitClient.inspectKey.setDown(index<7);}
            if(frame==12 && index<3)mc.player.swing(InteractionHand.MAIN_HAND);
            if(frame==28)capture(mc,"hmi-item-"+index);
        }
        int end=100+ITEMS.length*35;
        if(ticks==end){OrbitClient.inspectKey.setDown(false);mc.player.setMainArm(HumanoidArm.LEFT);mc.player.getInventory().setItem(0,new ItemStack(Items.DIAMOND_SWORD));mc.player.setItemInHand(InteractionHand.OFF_HAND,new ItemStack(Items.SHIELD));}
        if(ticks==end+30)capture(mc,"hmi-left-offhand");
        if(ticks==end+40){s.hmiVanillaCombat=true;HoldMyItems.sync(mc);mc.player.swing(InteractionHand.MAIN_HAND);require(!OrbitHmiSettings.customHand(InteractionHand.MAIN_HAND),"vanilla combat setting ignored");}
        if(ticks==end+55){
            OrbitClient.LOG.info("HMI integration passed: migration, saved settings, R/Q/F, input gating, disabled native rendering, hand toggles, original Lua, 13 items, left/off hand and UI layouts");
            mc.player.setMainArm(HumanoidArm.RIGHT);OrbitClient.inspectKey.setDown(false);mc.stop();
        }
    }
    private static void capture(Minecraft mc,String name){Screenshot.grab(mc.gameDirectory,name+".png",mc.gameRenderer.mainRenderTarget(),1,c->{});}
    private HmiSmoke(){}
}
