package org.orbitlauncher.client.mixin;

import net.minecraft.client.multiplayer.ClientChunkCache;
import net.minecraft.client.multiplayer.ClientPacketListener;
import net.minecraft.network.protocol.game.ClientboundForgetLevelChunkPacket;
import org.orbitlauncher.client.ExtendedChunks;
import org.orbitlauncher.client.AutoRefill;
import net.minecraft.network.protocol.game.ClientboundSetPlayerInventoryPacket;
import net.minecraft.network.protocol.game.ClientboundContainerSetSlotPacket;
import net.minecraft.network.protocol.game.ClientboundContainerSetContentPacket;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ClientPacketListener.class)
abstract class ClientPacketListenerMixin {
    @Inject(method="handleSetPlayerInventory",at=@At("TAIL"))
    private void orbit$confirmedInventory(ClientboundSetPlayerInventoryPacket packet,CallbackInfo ci){
        AutoRefill.serverSlot(packet.slot(),packet.contents());
    }
    @Inject(method="handleContainerSetSlot",at=@At("TAIL"))
    private void orbit$confirmedSlot(ClientboundContainerSetSlotPacket packet,CallbackInfo ci){
        AutoRefill.serverMenuSlot(packet.getContainerId(),packet.getSlot(),packet.getItem());
    }
    @Inject(method="handleContainerContent",at=@At("TAIL"))
    private void orbit$confirmedContent(ClientboundContainerSetContentPacket packet,CallbackInfo ci){
        for(int slot=0;slot<packet.items().size();slot++)AutoRefill.serverMenuSlot(packet.containerId(),slot,packet.items().get(slot));
    }
    @Inject(method="handleForgetLevelChunk",at=@At(value="INVOKE",target="Lnet/minecraft/client/multiplayer/ClientChunkCache;drop(Lnet/minecraft/world/level/ChunkPos;)V"),cancellable=true)
    private void orbit$retainReceivedChunk(ClientboundForgetLevelChunkPacket packet,CallbackInfo ci) {
        if(ExtendedChunks.shouldRetain(packet.pos()))ci.cancel();
    }
}
