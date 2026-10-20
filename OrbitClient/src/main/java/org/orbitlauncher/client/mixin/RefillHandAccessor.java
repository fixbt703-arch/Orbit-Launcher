package org.orbitlauncher.client.mixin;

import net.minecraft.client.renderer.ItemInHandRenderer;
import net.minecraft.world.item.ItemStack;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

@Mixin(ItemInHandRenderer.class)
public interface RefillHandAccessor {
    @Accessor("mainHandItem") void orbit$heldStack(ItemStack stack);
    @Accessor("mainHandItem") ItemStack orbit$heldStack();
}
