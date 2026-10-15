package com.holdmylua.source;

import com.mojang.serialization.MapCodec;
import net.minecraft.client.Minecraft;
import net.minecraft.client.multiplayer.ClientLevel;
import net.minecraft.client.renderer.item.properties.conditional.ConditionalItemModelProperty;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.HumanoidArm;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.ItemDisplayContext;
import net.minecraft.world.InteractionHand;

/** Keeps original models completely inactive when Orbit switches this module off. */
public record HmiEnabledProperty() implements ConditionalItemModelProperty {
    public static final MapCodec<HmiEnabledProperty> CODEC=MapCodec.unit(new HmiEnabledProperty());
    @Override public MapCodec<? extends ConditionalItemModelProperty> type(){return CODEC;}
    @Override public boolean get(ItemStack stack,ClientLevel level,LivingEntity entity,int seed,ItemDisplayContext context){
        if(!OrbitHmiSettings.active() || !OrbitHmiSettings.blocks3d)return false;
        var mc=Minecraft.getInstance();
        if(entity==mc.player && entity!=null){
            boolean left=context==ItemDisplayContext.FIRST_PERSON_LEFT_HAND || context==ItemDisplayContext.THIRD_PERSON_LEFT_HAND;
            boolean right=context==ItemDisplayContext.FIRST_PERSON_RIGHT_HAND || context==ItemDisplayContext.THIRD_PERSON_RIGHT_HAND;
            if(left||right){
                boolean main=right==(entity.getMainArm()==HumanoidArm.RIGHT);
                return OrbitHmiSettings.customHand(main?InteractionHand.MAIN_HAND:InteractionHand.OFF_HAND);
            }
        }
        return true;
    }
}
