package org.orbitlauncher.client.mixin;

import net.minecraft.client.multiplayer.MultiPlayerGameMode;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.phys.BlockHitResult;
import org.orbitlauncher.client.AutoRefill;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(MultiPlayerGameMode.class)
abstract class RefillUseMixin {
    @Inject(method="useItemOn",at=@At("HEAD"))
    private void orbit$beforeUse(LocalPlayer player,InteractionHand hand,BlockHitResult hit,CallbackInfoReturnable<InteractionResult> ci){
        AutoRefill.recordUse(player,hand);
    }
    @Inject(method="useItemOn",at=@At("RETURN"))
    private void orbit$afterUse(LocalPlayer player,InteractionHand hand,BlockHitResult hit,CallbackInfoReturnable<InteractionResult> ci){
        AutoRefill.finishUse(player,hand,ci.getReturnValue());
    }
}
