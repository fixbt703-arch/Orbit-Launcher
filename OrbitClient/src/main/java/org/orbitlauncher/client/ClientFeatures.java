package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.phys.EntityHitResult;

public final class ClientFeatures {
    private static Boolean previousToggleSprint;
    public static double lastReach;
    public static long lastAttack;
    private ClientFeatures() { }

    public static void tick(Minecraft mc) {
        if (mc.player != null && OrbitClient.enabled(Module.TOGGLE_SPRINT)) {
            if (previousToggleSprint == null) previousToggleSprint=mc.options.toggleSprint().get();
            if (!mc.options.toggleSprint().get()) mc.options.toggleSprint().set(true);
        } else restoreSprint(mc);
        if (mc.player == null) { lastAttack=0; lastReach=0; }
    }
    public static void restoreSprint(Minecraft mc) {
        if (previousToggleSprint != null) {
            mc.options.toggleSprint().set(previousToggleSprint);
            previousToggleSprint=null;
            mc.options.save();
        }
    }
    public static void recordAttack(Entity target) {
        var mc=Minecraft.getInstance();
        if(mc.player==null || !OrbitClient.enabled(Module.REACH)) return;
        if(mc.hitResult instanceof EntityHitResult hit && hit.getEntity()==target) {
            lastReach=mc.player.getEyePosition().distanceTo(hit.getLocation());
            lastAttack=System.nanoTime();
        }
    }
    public static String scope() {
        var mc=Minecraft.getInstance();
        if(mc.level==null) return "";
        String world=mc.getCurrentServer()!=null ? mc.getCurrentServer().ip.toLowerCase(java.util.Locale.ROOT)
            : mc.getSingleplayerServer()!=null ? "local:"+mc.getSingleplayerServer().getWorldPath(net.minecraft.world.level.storage.LevelResource.ROOT).toAbsolutePath().normalize() : "local";
        return world+"|"+mc.level.dimension().identifier();
    }
    public static void saveWaypoint(String name) {
        var p=Minecraft.getInstance().player;
        if(p==null) return;
        var point=new ClientConfig.Waypoint();
        point.name=name==null || name.isBlank()?"Orbit":name.strip().substring(0,Math.min(32,name.strip().length()));
        point.scope=scope();point.x=p.getX();point.y=p.getY();point.z=p.getZ();
        OrbitClient.config.waypoint=point;
        OrbitClient.save();
    }
}
