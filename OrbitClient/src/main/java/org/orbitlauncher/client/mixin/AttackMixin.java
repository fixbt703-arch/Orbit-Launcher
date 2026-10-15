package org.orbitlauncher.client.mixin;

import net.minecraft.client.multiplayer.MultiPlayerGameMode;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.player.Player;
import org.orbitlauncher.client.ClientFeatures;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(MultiPlayerGameMode.class)
public class AttackMixin {
    @Inject(method="attack",at=@At("HEAD"))
    private void orbit$recordAttack(Player player,Entity target,CallbackInfo ci) {
        ClientFeatures.recordAttack(target);
    }
}
