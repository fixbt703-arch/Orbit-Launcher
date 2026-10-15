package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.multiplayer.ClientPacketListener;
import net.minecraft.network.protocol.ping.ServerboundPingRequestPacket;

/** Low-frequency round trips over the current play connection, not a second server connection. */
public final class PingMonitor {
    private static ClientPacketListener connection;
    private static long nextProbe, sentAt, sampleAt, nonce, sequence;
    private static int latency=-1;
    private PingMonitor() {}
    public static synchronized void tick(Minecraft mc) {
        var current=mc.player==null?null:mc.getConnection();
        if(current!=connection) {connection=current;nextProbe=sentAt=sampleAt=nonce=0;latency=-1;}
        if(current==null || (mc.isLocalServer() && !Boolean.getBoolean("orbit.client.world-smoke")) || !OrbitClient.enabled(Module.PING))return;
        long now=System.nanoTime();
        if(now<nextProbe)return;
        // Negative cookies cannot collide with vanilla's millisecond timestamps.
        nonce=Long.MIN_VALUE+(++sequence);sentAt=now;nextProbe=now+5_000_000_000L;
        current.send(new ServerboundPingRequestPacket(nonce));
    }
    public static synchronized boolean receive(ClientPacketListener source,long cookie) {
        if(source==null || source!=connection || cookie<=Long.MIN_VALUE || cookie>Long.MIN_VALUE+sequence)return false;
        if(cookie==nonce && sentAt!=0) {
            long now=System.nanoTime();latency=(int)Math.clamp(Math.round((now-sentAt)/1_000_000d),1,60000);
            sampleAt=now;sentAt=0;
        }
        return true; // Do not feed our samples into Minecraft's F3 ping monitor.
    }
    public static synchronized String label(boolean units) {
        var mc=Minecraft.getInstance();
        if(mc.isLocalServer())return OrbitClient.tr("Локально","Local");
        int value=sampleAt!=0 && System.nanoTime()-sampleAt<15_000_000_000L?latency:-1;
        if(value<0 && mc.getConnection()!=null && mc.player!=null) {
            var info=mc.getConnection().getPlayerInfo(mc.player.getUUID());
            if(info!=null && info.getLatency()>0)value=info.getLatency();
        }
        return value<0?"—":value+(units?" ms":"");
    }
    static synchronized void verifySampleForTest(){if(sampleAt==0 || latency<1)throw new IllegalStateException("Play ping roundtrip was not received");}
}
