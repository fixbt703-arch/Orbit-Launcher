package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.core.component.DataComponents;
import net.minecraft.resources.Identifier;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.item.ItemStack;
import org.orbitlauncher.client.mixin.CriticalAttackAccessor;

/** The live indicator is extracted by vanilla Hud, including its cooldown and range checks. */
public final class CriticalIndicator {
    private static final Identifier CROSSHAIR=Identifier.withDefaultNamespace("hud/crosshair");
    static final Identifier BACKGROUND=Identifier.withDefaultNamespace("hud/crosshair_attack_indicator_background");
    static final Identifier PROGRESS=Identifier.withDefaultNamespace("hud/crosshair_attack_indicator_progress");
    static final Identifier FULL=Identifier.withDefaultNamespace("hud/crosshair_attack_indicator_full");
    public static boolean customized(){return OrbitClient.config!=null&&HudRenderer.customCrosshairVisible();}
    public static boolean enabled(){
        var mc=Minecraft.getInstance();var s=OrbitClient.config.state(Module.CROSSHAIR);
        return s.criticalIndicator&&mc.player!=null&&supports(mc.player.getMainHandItem())
            &&(!s.criticalTargetOnly||mc.crosshairPickEntity instanceof LivingEntity);
    }
    static boolean supports(ItemStack stack){return !stack.isEmpty()&&stack.has(DataComponents.WEAPON)&&!stack.has(DataComponents.PIERCING_WEAPON);}
    static boolean critical(){
        var mc=Minecraft.getInstance();var target=mc.crosshairPickEntity;
        return mc.player!=null&&!mc.player.isUsingItem()&&target instanceof LivingEntity&&target.isAlive()
            &&mc.player.getAttackStrengthScale(.5f)>.9f&&((CriticalAttackAccessor)mc.player).orbit$canCriticalAttack(target);
    }
    public static void sprite(GuiGraphicsExtractor g,Identifier sprite,int x,int y,int width,int height,Runnable vanilla){
        boolean custom=customized();
        if(custom&&sprite.equals(CROSSHAIR))return;
        boolean indicator=sprite.equals(BACKGROUND)||sprite.equals(PROGRESS)||sprite.equals(FULL);
        if(!custom||!indicator){vanilla.run();if(indicator&&AttackIndicatorSmoke.ACTIVE)AttackIndicatorSmoke.rendered(sprite,width,height,false,false);return;}
        var s=OrbitClient.config.state(Module.CROSSHAIR);float scale=s.criticalScale/100f;
        boolean highlight=s.criticalHighlight&&sprite.equals(FULL)&&critical();
        g.pose().pushMatrix();
        try{
            int cx=g.guiWidth()/2,cy=g.guiHeight()/2;
            g.pose().translate(cx,cy+s.criticalOffset);g.pose().scale(scale,scale);g.pose().translate(-cx,-cy-9);
            if(highlight)g.blitSprite(RenderPipelines.GUI_TEXTURED,sprite,x,y,width,height,0xFF83E6B4);
            else vanilla.run();
            if(AttackIndicatorSmoke.ACTIVE)AttackIndicatorSmoke.rendered(sprite,width*scale,height*scale,true,highlight);
        }finally{g.pose().popMatrix();}
    }
    static void preview(GuiGraphicsExtractor g,int x,int y,ClientConfig.State s){
        if(!s.criticalIndicator)return;
        float scale=s.criticalScale/100f;
        g.pose().pushMatrix();
        try{
            g.pose().translate(x,y+s.criticalOffset);g.pose().scale(scale,scale);
            g.blitSprite(RenderPipelines.GUI_TEXTURED,BACKGROUND,-8,0,16,4);
            g.blitSprite(RenderPipelines.GUI_TEXTURED,PROGRESS,16,4,0,0,-8,0,10,4);
        }finally{g.pose().popMatrix();}
    }
    private CriticalIndicator(){}
}
