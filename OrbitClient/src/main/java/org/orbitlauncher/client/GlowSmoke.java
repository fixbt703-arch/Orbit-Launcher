package org.orbitlauncher.client;

import com.holdmylua.source.OrbitHmiSettings;
import com.holdmylua.source.global.GlobalsStorage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.Screenshot;
import net.minecraft.client.CameraType;
import net.minecraft.client.gui.screens.inventory.InventoryScreen;
import net.minecraft.world.item.*;
import net.minecraft.core.component.DataComponents;
import net.minecraft.world.InteractionHand;

/** Actual item-feature pipeline and animation regression checks in an isolated world. */
final class GlowSmoke {
    private static int tick,custom,glow,nativeHands;
    private static void require(boolean ok,String why){if(!ok)throw new IllegalStateException("Glow/HMI: "+why);}
    private static ItemStack sword(boolean foil){var stack=new ItemStack(Items.DIAMOND_SWORD);stack.set(DataComponents.ENCHANTMENT_GLINT_OVERRIDE,foil);return stack;}
    static void tick(Minecraft mc){
        tick++;var h=OrbitClient.config.state(Module.HOLD_MY_ITEMS);var s=OrbitClient.config.state(Module.GLINT);
        if(tick==1){
            mc.options.pauseOnLostFocus=false;OrbitHmiSettings.windowActive=()->true;
            h.enabled=true;h.hmiVanillaCombat=true;h.hmiMainHand=h.hmiOffHand=true;h.hmiShowHands=true;h.hmiScale=1;h.hmiOffsetX=h.hmiOffsetY=h.hmiOffsetZ=0;
            OrbitClient.inspectKey.setDown(false);mc.options.keyAttack.setDown(false);mc.options.keyUse.setDown(false);
            s.enabled=true;s.glowOutline=s.glowFirstPerson=s.glowThirdPerson=true;s.glowInventory=false;s.glowOutlineOpacity=.85f;s.glowSoftness=.8f;s.glowWidth=2;s.glowMatchGlint=false;s.glowOutlineColor=0x48D6C5;
            mc.player.getInventory().setSelectedSlot(0);mc.player.getInventory().setItem(0,sword(true));mc.player.setItemInHand(InteractionHand.OFF_HAND,ItemStack.EMPTY);HoldMyItems.sync(mc);mc.gui.setScreen(null);
            String lang=mc.options.languageCode;
            for(String code:new String[]{"ru_ru","en_us"})for(int[] size:new int[][]{{320,180},{427,240},{640,360},{960,540}}){
                mc.options.languageCode=code;
                for(var module:Module.values()){var ui=new ModuleScreen(new OrbitScreen(),module);ui.init(size[0],size[1]);ui.verifyLayouts();}
                var picker=new FeatureColorScreen(new OrbitScreen(),Module.GLINT,FeatureColorScreen.GLOW_OUTLINE);picker.init(size[0],size[1]);SmokeChecks.verifyWidgets(picker);
            }
            mc.options.languageCode=lang;OrbitClient.save();var loaded=ClientConfig.load().state(Module.GLINT);require(loaded.glowOutlineColor==s.glowOutlineColor&&loaded.glowWidth==s.glowWidth&&loaded.glowOutlineOpacity==s.glowOutlineOpacity,"glow settings persistence");
            require(!OrbitHmiSettings.animationsActive(),"native mode accepts original idle animation");custom=OrbitHmiSettings.renderedItems;nativeHands=OrbitHmiSettings.vanillaHands;
        }
        if(tick>=10&&tick<55){if(tick%10==0){mc.player.swing(InteractionHand.MAIN_HAND);mc.options.keyAttack.setDown(true);}if(tick%10==3)mc.options.keyAttack.setDown(false);}
        if(tick==58){
            require(OrbitHmiSettings.renderedItems==custom,"HMI leaked into native idle/attack/recovery frames");require(OrbitHmiSettings.vanillaHands>nativeHands,"native renderer not called");
            require(EnchantmentGlow.renderedForTest>0&&EnchantmentGlow.segmentsForTest>0,"native enchanted sword outline not emitted");capture(mc,"glow-native-sword");
            s.glowFirstPerson=false;glow=EnchantmentGlow.renderedForTest;
        }
        if(tick==70){require(EnchantmentGlow.renderedForTest==glow,"first-person toggle ignored");s.glowFirstPerson=true;s.glowOutlineOpacity=0;}
        if(tick==80){require(EnchantmentGlow.renderedForTest==glow,"zero outline opacity ignored");mc.player.getInventory().setItem(0,sword(false));}
        if(tick==90){s.glowOutlineOpacity=.85f;glow=EnchantmentGlow.renderedForTest;}
        if(tick==95){require(EnchantmentGlow.renderedForTest==glow,"non-enchanted sword outlined");mc.player.getInventory().setItem(0,sword(true));OrbitClient.inspectKey.setDown(true);}
        if(tick==115){
            require(OrbitHmiSettings.renderedItems>custom,"manual inspection did not use original renderer");
            require(EnchantmentGlow.renderedForTest>glow,"original HMI outline integration not reached");
            require(GlobalsStorage.registry.get("inspectionSpin") instanceof Number n&&n.doubleValue()>0,"original inspection script did not advance");capture(mc,"glow-hmi-inspect");
            mc.options.keyAttack.setDown(true);mc.player.swing(InteractionHand.MAIN_HAND);custom=OrbitHmiSettings.renderedItems;
        }
        if(tick==125){require(OrbitHmiSettings.renderedItems==custom,"native attack mixed with inspection");OrbitClient.inspectKey.setDown(false);mc.options.keyAttack.setDown(false);}
        if(tick==145){require(OrbitHmiSettings.renderedItems==custom,"original animation resumed after native attack");require(GlobalsStorage.registry.isEmpty(),"original Lua motion retained after returning to native");
            mc.player.getInventory().setItem(0,new ItemStack(Items.BOW));mc.player.startUsingItem(InteractionHand.MAIN_HAND);
        }
        if(tick==155){require(OrbitHmiSettings.renderedItems==custom,"original bow pose leaked into native use");mc.player.stopUsingItem();mc.player.getInventory().setItem(0,sword(true));h.hmiVanillaCombat=false;HoldMyItems.sync(mc);glow=EnchantmentGlow.renderedForTest;}
        if(tick==175){require(EnchantmentGlow.renderedForTest>glow,"full HMI mode lost outline");capture(mc,"glow-hmi-idle");h.enabled=false;HoldMyItems.sync(mc);s.glowInventory=true;mc.gui.setScreen(new InventoryScreen(mc.player));glow=EnchantmentGlow.renderedForTest;}
        if(tick==195){require(EnchantmentGlow.renderedForTest>glow,"inventory outline not emitted");capture(mc,"glow-inventory");mc.gui.setScreen(null);mc.options.setCameraType(CameraType.THIRD_PERSON_BACK);glow=EnchantmentGlow.renderedForTest;}
        if(tick==220){require(EnchantmentGlow.renderedForTest>glow,"third-person outline not emitted");capture(mc,"glow-third-person");mc.options.setCameraType(CameraType.FIRST_PERSON);s.glowThirdPerson=false;s.glowOutline=false;glow=EnchantmentGlow.renderedForTest;}
        if(tick==235){require(EnchantmentGlow.renderedForTest==glow,"outline off toggle ignored");s.glowOutline=true;s.glowThirdPerson=true;var ui=new ModuleScreen(new OrbitScreen(),Module.GLINT);ui.selectTabForTest(1);mc.gui.setScreen(ui);}
        if(tick==245)capture(mc,"glow-settings");
        if(tick==250){var picker=new FeatureColorScreen(new OrbitScreen(),Module.GLINT,FeatureColorScreen.GLOW_OUTLINE);mc.gui.setScreen(picker);picker.verifyInput();}
        if(tick==260){
            OrbitClient.LOG.info("Enchantment Glow passed: native/HMI/inspection/inventory/third-person pipelines, silhouette geometry, RGB persistence, zero alpha, toggles, non-enchanted filtering and all module settings layouts");
            OrbitClient.LOG.info("HMI regression passed: 145 idle/attack/recovery/inspect/use frames with no mixing and cleared Lua state");mc.stop();
        }
    }
    private static void capture(Minecraft mc,String name){Screenshot.grab(mc.gameDirectory,name+".png",mc.gameRenderer.mainRenderTarget(),1,message->{});}
    private GlowSmoke(){}
}
