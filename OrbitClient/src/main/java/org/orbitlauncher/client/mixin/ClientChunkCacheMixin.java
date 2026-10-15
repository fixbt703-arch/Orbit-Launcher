package org.orbitlauncher.client.mixin;

import net.minecraft.client.multiplayer.ClientChunkCache;
import org.orbitlauncher.client.ExtendedChunkCacheAccess;
import org.orbitlauncher.client.ExtendedChunks;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyVariable;

@Mixin(ClientChunkCache.class)
abstract class ClientChunkCacheMixin implements ExtendedChunkCacheAccess {
    @Unique private int orbit$serverRadius=-1;

    @ModifyVariable(method="updateViewRadius",at=@At("HEAD"),argsOnly=true)
    private int orbit$extendStorageRadius(int radius) {
        orbit$serverRadius=radius;
        return ExtendedChunks.storageRadius(radius);
    }
    @Override public void orbit$refreshRadius() {
        if(orbit$serverRadius>=0)((ClientChunkCache)(Object)this).updateViewRadius(orbit$serverRadius);
    }
}
