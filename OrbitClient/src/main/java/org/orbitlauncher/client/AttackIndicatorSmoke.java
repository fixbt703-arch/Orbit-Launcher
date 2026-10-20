package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.AttackIndicatorStatus;
import net.minecraft.client.Screenshot;
import net.minecraft.resources.Identifier;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.animal.pig.Pig;
import net.minecraft.commands.arguments.EntityAnchorArgument;
import org.orbitlauncher.client.mixin.MinecraftAttackAccessor;

/** Real aim, real left-click path, real jumping and server damage in a fresh development world. */
final class AttackIndicatorSmoke {
    static final boolean ACTIVE=Boolean.getBoolean("orbit.client.attack-smoke");
    private static int ticks,nativeFull,customFull,progressFrames,lastDraw=-1,highlightTick=-1,criticalHit=-1;
    private static float nativeWidth,customWidth,minProgress=99,maxProgress,before,normalDamage;
    private static boolean criticalSeen;
    private static LivingEntity target;
    static void rendered(Identifier sprite,float width,float height,boolean custom,boolean highlighted){
        if(!ACTIVE)return;lastDraw=ticks;
        if(sprite.equals(CriticalIndicator.FULL)){
            if(custom){customFull++;customWidth=width;}else{nativeFull++;nativeWidth=width;}
            if(highlighted)criticalSeen=true;
        }
        if(custom&&sprite.equals(CriticalIndicator.PROGRESS)&&width>0){progressFrames++;minProgress=Math.min(minProgress,width);maxProgress=Math.max(maxProgress,width);}
    }
    static void tick(Minecraft mc){
        ticks++;mc.options.pauseOnLostFocus=false;
        var s=OrbitClient.config.state(Module.CROSSHAIR);
        if(ticks==5){
            mc.gui.setScreen(null);s.enabled=false;s.criticalIndicator=true;s.criticalTargetOnly=false;s.criticalHighlight=true;s.criticalScale=75;s.criticalOffset=9;
            mc.options.attackIndicator().set(AttackIndicatorStatus.CROSSHAIR);
            String[] commands={"gamemode survival","effect clear @s","tp @s 0 4 0 0 0","kill @e[type=minecraft:pig]",
                "item replace entity @s hotbar.0 with minecraft:diamond_sword",
                "summon minecraft:pig 0 4 1.8 {NoAI:1b,NoGravity:1b,Silent:1b,Tags:[\"orbit_attack_probe\"]}",
                "attribute @e[type=minecraft:pig,tag=orbit_attack_probe,limit=1] minecraft:max_health base set 100",
                "attribute @e[type=minecraft:pig,tag=orbit_attack_probe,limit=1] minecraft:knockback_resistance base set 1",
                "effect give @e[type=minecraft:pig,tag=orbit_attack_probe] minecraft:instant_health 1 10 true"};
            for(String command:commands)mc.getConnection().sendCommand(command);
            mc.player.getInventory().setSelectedSlot(0);
        }
        if(ticks>=25){
            if(target==null)for(var entity:mc.level.entitiesForRendering())if(entity instanceof Pig pig&&pig.isAlive()){target=pig;break;}
            if(target!=null)mc.player.lookAt(EntityAnchorArgument.Anchor.EYES,target.getBoundingBox().getCenter());
        }
        if(ticks==45){require(target!=null&&mc.crosshairPickEntity==target,"Native raycast did not acquire the real target");require(nativeFull>0&&nativeWidth==16,"Original native full sword was not rendered at 16 px");Screenshot.grab(mc,false);}
        if(ticks==50)s.enabled=true;
        if(ticks==60){require(customFull>0&&customWidth==12,"Compact indicator was not rendered at 12 px");require(!criticalSeen,"Grounded full charge incorrectly marked critical");Screenshot.grab(mc,false);}
        if(ticks==65){before=health(mc);require(before>40,"Server target health setup failed");((MinecraftAttackAccessor)mc).orbit$startAttack();require(mc.player.getAttackStrengthScale(0)<.2f,"Real attack did not reset the actual vanilla clock");}
        if(ticks==73){require(progressFrames>0&&minProgress<12,"Real cooldown progress sprite was not rendered");Screenshot.grab(mc,false);}
        if(ticks==90){normalDamage=before-health(mc);require(normalDamage>0,"Server did not receive the real hit");require(mc.player.getAttackStrengthScale(0)==1&&lastDraw>=88,"Full charge did not recover");mc.options.attackIndicator().set(AttackIndicatorStatus.OFF);}
        if(ticks==100){require(lastDraw>=98,"Orbit setting stopped working when vanilla option was off");mc.getConnection().sendCommand("item replace entity @s hotbar.0 with minecraft:stone");}
        if(ticks==115){require(lastDraw<112,"Indicator rendered for a block");mc.getConnection().sendCommand("item replace entity @s hotbar.0 with minecraft:bow");}
        if(ticks==130){require(lastDraw<127,"Indicator rendered for a bow");mc.getConnection().sendCommand("item replace entity @s hotbar.0 with minecraft:diamond_spear");}
        if(ticks==145){require(lastDraw<142,"Indicator rendered for a spear");mc.getConnection().sendCommand("item replace entity @s hotbar.0 with minecraft:diamond_sword");}
        if(ticks==170){require(lastDraw>=168,"Indicator did not return after weapon switch");before=health(mc);mc.player.setSprinting(false);mc.player.jumpFromGround();}
        if(ticks>170&&criticalHit<0&&criticalSeen&&CriticalIndicator.critical()){
            if(highlightTick<0){highlightTick=ticks;Screenshot.grab(mc,false);}
            else if(ticks>highlightTick){((MinecraftAttackAccessor)mc).orbit$startAttack();criticalHit=ticks;}
        }
        if(ticks==215){require(criticalHit>0,"No actual falling critical opportunity was rendered");float damage=before-health(mc);require(Math.abs(damage-normalDamage*1.5f)<.05f,"Server critical damage mismatch: normal="+normalDamage+", critical="+damage);OrbitClient.LOG.info("Attack indicator real combat passed: original 16px, compact 12px, actual attack/reset/recovery, weapon changes, native option off, airborne highlight and server critical damage {} vs normal {}",damage,normalDamage);}
        if(ticks==220){var panel=new ModuleScreen(new OrbitScreen(),Module.CROSSHAIR);panel.selectTabForTest(3);mc.gui.setScreen(panel);}
        if(ticks==245)Screenshot.grab(mc,false);
        if(ticks==255){OrbitClient.LOG.info("Attack indicator smoke completed: {} progress frames; widths {}..{}",progressFrames,minProgress,maxProgress);mc.stop();}
    }
    private static float health(Minecraft mc){return mc.getSingleplayerServer().submit(()->((LivingEntity)mc.getSingleplayerServer().overworld().getEntity(target.getUUID())).getHealth()).join();}
    private static void require(boolean value,String message){if(!value)throw new AssertionError(message);}
    private AttackIndicatorSmoke(){}
}
