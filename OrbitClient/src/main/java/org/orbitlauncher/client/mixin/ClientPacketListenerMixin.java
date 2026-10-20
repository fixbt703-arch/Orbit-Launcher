package org.orbitlauncher.client.mixin;

import net.minecraft.client.multiplayer.ClientChunkCache;
import net.minecraft.client.multiplayer.ClientPacketListener;
import net.minecraft.network.protocol.game.ClientboundForgetLevelChunkPacket;
import org.orbitlauncher.client.ExtendedChunks;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ClientPacketListener.class)
abstract class ClientPacketListenerMixin {
    @Inject(method="handleForgetLevelChunk",at=@At(value="INVOKE",target="Lnet/minecraft/client/multiplayer/ClientChunkCache;drop(Lnet/minecraft/world/level/ChunkPos;)V"),cancellable=true)
    private void orbit$retainReceivedChunk(ClientboundForgetLevelChunkPacket packet,CallbackInfo ci) {
        if(ExtendedChunks.shouldRetain(packet.pos()))ci.cancel();
    }
}
