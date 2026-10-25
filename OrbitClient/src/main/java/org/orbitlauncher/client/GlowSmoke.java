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
    private static int tick,custom,glow,nativeHands,armor;
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
            for(String slot:new String[]{"head","chest","legs","feet"})mc.getConnection().sendCommand("item modify entity @s armor."+slot+" {function:\"minecraft:set_components\",components:{\"minecraft:enchantment_glint_override\":true}}");
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
        if(tick==56)GlowMaskVerification.requested=true;
        if(tick==58){
            require(GlowOutline.compositesForTest>0,"exterior perimeter post process never executed");
            require(OrbitHmiSettings.renderedItems==custom,"HMI leaked into native idle/attack/recovery frames");require(OrbitHmiSettings.vanillaHands>nativeHands,"native renderer not called");
            require(EnchantmentGlow.renderedForTest>0&&EnchantmentGlow.segmentsForTest>0,"native enchanted sword outline not emitted");capture(mc,"glow-native-sword");
            s.glowFirstPerson=false;glow=EnchantmentGlow.renderedForTest;
        }
        if(tick==70){GlowMaskVerification.verify();require(EnchantmentGlow.renderedForTest==glow,"first-person toggle ignored");s.glowFirstPerson=true;s.glowOutlineOpacity=0;}
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
        if(tick==155){mc.getConnection().sendCommand("gamemode survival");require(OrbitHmiSettings.renderedItems==custom,"original bow pose leaked into native use");mc.player.stopUsingItem();mc.player.getInventory().setItem(0,sword(true));h.hmiVanillaCombat=false;HoldMyItems.sync(mc);glow=EnchantmentGlow.renderedForTest;}
        if(tick==175){require(EnchantmentGlow.renderedForTest>glow,"full HMI mode lost outline");capture(mc,"glow-hmi-idle");h.enabled=false;HoldMyItems.sync(mc);s.glowInventory=true;mc.gui.setScreen(new InventoryScreen(mc.player));glow=EnchantmentGlow.renderedForTest;}
        if(tick==195){require(EnchantmentGlow.renderedForTest>glow,"inventory outline not emitted");capture(mc,"glow-inventory");require(mc.player.getItemBySlot(net.minecraft.world.entity.EquipmentSlot.HEAD).hasFoil(),"armor fixture not enchanted");require(ArmorGlow.renderedForTest>0,"inventory armor hull not emitted");mc.gui.setScreen(null);mc.options.setCameraType(CameraType.THIRD_PERSON_BACK);glow=EnchantmentGlow.renderedForTest;armor=ArmorGlow.renderedForTest;}
        if(tick==220){require(EnchantmentGlow.renderedForTest>glow,"third-person outline not emitted");require(ArmorGlow.renderedForTest>armor,"third-person armor hull not emitted");capture(mc,"glow-third-person");mc.options.setCameraType(CameraType.FIRST_PERSON);s.glowThirdPerson=false;s.glowOutline=false;glow=EnchantmentGlow.renderedForTest;}
        if(tick==235){require(EnchantmentGlow.renderedForTest==glow,"outline off toggle ignored");s.glowOutline=true;s.glowThirdPerson=true;var ui=new ModuleScreen(new OrbitScreen(),Module.GLINT);ui.selectTabForTest(1);mc.gui.setScreen(ui);}
        if(tick==245){
            capture(mc,"glow-settings");s.glintColor=0xB9B8FF;
            for(int expected:new int[]{0x59D7C6,0xFFAD86,0x91DDF8,0xB9B8FF}){
                var ui=(ModuleScreen)mc.gui.screen();
                ui.children().stream().filter(c->c instanceof OrbitButton b&&b.getMessage().getString().startsWith(OrbitClient.tr("Палитра: ","Palette: "))).map(c->(OrbitButton)c).findFirst().orElseThrow().onClick(null,false);
                var loaded=ClientConfig.load().state(Module.GLINT);
                require(s.glintColor==expected&&s.glowOutlineColor==expected&&s.glowMatchGlint&&!s.glowRainbow&&loaded.glintColor==expected&&loaded.glowOutlineColor==expected,"palette did not apply and persist both glow colors");
            }
        }
        if(tick==250){var picker=new FeatureColorScreen(new OrbitScreen(),Module.GLINT,FeatureColorScreen.GLOW_OUTLINE);mc.gui.setScreen(picker);picker.verifyInput();}
        if(tick==260){
            OrbitClient.LOG.info("Enchantment Glow passed: native/HMI/inspection/inventory/third-person pipelines, silhouette geometry, RGB persistence, zero alpha, toggles, non-enchanted filtering and all module settings layouts");
            OrbitClient.LOG.info("HMI regression passed: 145 idle/attack/recovery/inspect/use frames with no mixing and cleared Lua state");
            mc.gui.setScreen(null);s.glintColor=0xB9B8FF;s.glowMatchGlint=true;s.glowWidth=1.25f;s.glowOutlineOpacity=.7f;s.glowSoftness=.8f;
            mc.getConnection().sendCommand("time set midnight");
        }
        if(tick==280)capture(mc,"glow-amethyst-night");
        if(tick==282)s.glintColor=0x59D7C6;
        if(tick==295)capture(mc,"glow-aurora-night");
        if(tick==300){
            mc.getConnection().sendCommand("time set day");s.glintColor=0xF5DF69;s.glowWidth=1.25f;s.glowPulse=true;s.glowOutlineOpacity=.9f;
            var pickaxe=new ItemStack(Items.GOLDEN_PICKAXE);pickaxe.set(DataComponents.ENCHANTMENT_GLINT_OVERRIDE,true);mc.player.getInventory().setItem(0,pickaxe);
        }
        if(tick==315){GlowMaskVerification.minimumCoverage=10000;GlowMaskVerification.requested=true;}
        if(tick==320){GlowMaskVerification.verify();require(GlowMaskVerification.passed>=2,"pickaxe pixel perimeter was not verified");capture(mc,"glow-reference-pickaxe");}
        if(tick==325){mc.options.setCameraType(CameraType.THIRD_PERSON_FRONT);armor=ArmorGlow.renderedForTest;}
        if(tick==335){GlowMaskVerification.minimumCoverage=5000;GlowMaskVerification.requested=true;}
        if(tick==345){GlowMaskVerification.verify();require(GlowMaskVerification.passed>=3,"armor pixel perimeter was not verified");require(ArmorGlow.renderedForTest>armor,"front-facing armor silhouette not emitted");capture(mc,"glow-reference-armor");s.glintArmor=false;armor=ArmorGlow.renderedForTest;}
        if(tick==355){require(ArmorGlow.renderedForTest==armor,"armor toggle ignored");s.glintArmor=true;s.glowThirdPerson=false;}
        if(tick==365){require(ArmorGlow.renderedForTest==armor,"third-person armor toggle ignored");s.glowInventory=true;mc.gui.setScreen(new InventoryScreen(mc.player));}
        if(tick==385){require(ArmorGlow.renderedForTest>armor,"inventory toggle incorrectly coupled to third person");s.glowInventory=false;armor=ArmorGlow.renderedForTest;}
        if(tick==395){
            require(ArmorGlow.renderedForTest==armor,"inventory armor toggle ignored");mc.gui.setScreen(null);s.glowThirdPerson=true;
            var center=mc.player.position().add(0,1,0).lerp(mc.gameRenderer.mainCamera().position(),.5);
            mc.getConnection().sendCommand(String.format(java.util.Locale.ROOT,"summon minecraft:block_display %.4f %.4f %.4f {billboard:\"center\",width:4f,height:4f,block_state:{Name:\"minecraft:stone\"},transformation:{translation:[-2f,-2f,0f],left_rotation:[0f,0f,0f,1f],scale:[4f,4f,0.2f],right_rotation:[0f,0f,0f,1f]}}",center.x,center.y,center.z));
        }
        if(tick==415){GlowMaskVerification.expectOccluded=true;GlowMaskVerification.requested=true;}
        if(tick==435){GlowMaskVerification.verifyOcclusion();capture(mc,"glow-occluded");}
        if(tick==440){OrbitClient.LOG.info("Glow reference passed: exterior pixel perimeters for sword/pickaxe/armor, unchanged model interiors, late occluder, preserved glint/highlights/pulse and independent armor contexts");mc.stop();}
    }
    private static void capture(Minecraft mc,String name){Screenshot.grab(mc.gameDirectory,name+".png",mc.gameRenderer.mainRenderTarget(),1,message->{});}
    private GlowSmoke(){}
}
