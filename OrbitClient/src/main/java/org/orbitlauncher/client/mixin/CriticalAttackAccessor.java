package org.orbitlauncher.client.mixin;

import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.player.Player;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Invoker;

@Mixin(Player.class)
public interface CriticalAttackAccessor {
    @Invoker("canCriticalAttack") boolean orbit$canCriticalAttack(Entity target);
}
