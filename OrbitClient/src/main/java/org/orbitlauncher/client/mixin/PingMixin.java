package org.orbitlauncher.client.mixin;
import net.minecraft.client.multiplayer.ClientPacketListener;
import net.minecraft.network.protocol.ping.ClientboundPongResponsePacket;
import org.orbitlauncher.client.PingMonitor;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ClientPacketListener.class)
public class PingMixin {
    @Inject(method="handlePongResponse",at=@At("HEAD"),cancellable=true)
    private void orbit$pong(ClientboundPongResponsePacket packet,CallbackInfo ci) {
        if(PingMonitor.receive((ClientPacketListener)(Object)this,packet.time()))ci.cancel();
    }
}
