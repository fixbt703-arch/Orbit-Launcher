package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.renderer.state.gui.GuiRenderState;
import net.minecraft.client.renderer.state.gui.GuiTextRenderState;
import net.minecraft.world.item.Items;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.effect.MobEffects;
import net.minecraft.world.effect.MobEffectInstance;
import org.orbitlauncher.client.mixin.CriticalAttackAccessor;

/** Development-only checks against the loaded game's real item components and combat predicate. */
final class CriticalSmoke {
    static void verify(Minecraft mc){
        for(var item:new net.minecraft.world.item.Item[]{Items.DIAMOND_SWORD,Items.DIAMOND_AXE,Items.TRIDENT,Items.MACE})
            require(CriticalIndicator.supports(new ItemStack(item)),"Eligible melee weapon hidden: "+item);
        for(var item:new net.minecraft.world.item.Item[]{Items.DIAMOND_SPEAR,Items.BOW,Items.CROSSBOW,Items.STONE})
            require(!CriticalIndicator.supports(new ItemStack(item)),"Unsupported item shown: "+item);
        require(!CriticalIndicator.supports(ItemStack.EMPTY),"Empty hand shown");
        var player=mc.player;var predicate=(CriticalAttackAccessor)player;
        boolean ground=player.onGround(),sprint=player.isSprinting();double fall=player.fallDistance;
        var old=player.getEffect(MobEffects.BLINDNESS);
        try{
            player.setSprinting(false);player.setOnGround(true);player.fallDistance=0;
            require(!predicate.orbit$canCriticalAttack(player),"Grounded attack marked critical");
            player.setOnGround(false);player.fallDistance=1;
            require(predicate.orbit$canCriticalAttack(player),"Falling attack not ready");
            player.setSprinting(true);require(!predicate.orbit$canCriticalAttack(player),"Sprinting marked critical");
            player.setSprinting(false);player.addEffect(new MobEffectInstance(MobEffects.BLINDNESS,200));
            require(!predicate.orbit$canCriticalAttack(player),"Blindness marked critical");
        }finally{
            player.removeEffect(MobEffects.BLINDNESS);if(old!=null)player.addEffect(old);
            player.setOnGround(ground);player.setSprinting(sprint);player.fallDistance=fall;
        }
        var previous=mc.gui.screen();
        try{
            mc.gui.setScreen(null);
            require(!textShadow(mc,true),"Orbit font retains shadow");
            require(textShadow(mc,false),"Vanilla font shadow changed outside Orbit");
            mc.gui.setScreen(new ModuleScreen(new OrbitScreen(),Module.CROSSHAIR));
            require(!textShadow(mc,false),"Native editor/tooltip text retains shadow in Orbit");
            UiWorkflowChecks.verify(mc);
        }finally{mc.gui.setScreen(previous);}
        OrbitClient.LOG.info("Critical indicator: sword/axe/trident/mace accepted; spear/bow/crossbow/block/empty excluded; real vanilla falling/ground/sprint/blindness predicate and scoped shadow extraction passed");
    }
    private static boolean textShadow(Minecraft mc,boolean ownFont){
        try{
            var state=new GuiRenderState();var graphics=new GuiGraphicsExtractor(mc,state,640,360);
            graphics.text(ownFont?OrbitFonts.ui():mc.font,"Text probe",12,12,0xFFFFFFFF,true);
            var field=GuiTextRenderState.class.getDeclaredField("dropShadow");field.setAccessible(true);
            var values=new java.util.ArrayList<Boolean>();state.forEachText(text->{try{values.add(field.getBoolean(text));}catch(IllegalAccessException e){throw new AssertionError(e);}});
            require(values.size()==1,"Text extraction missing");return values.getFirst();
        }catch(ReflectiveOperationException e){throw new AssertionError(e);}
    }
    private static void require(boolean value,String message){if(!value)throw new AssertionError(message);}
    private CriticalSmoke(){}
}
