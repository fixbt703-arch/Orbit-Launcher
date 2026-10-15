package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.world.level.ChunkPos;

/**
 * Extends the client-side chunk window. On multiplayer this can only retain and render chunk
 * data that the server has already sent. Real block/entity ticking remains authoritative
 * on the server.
 */
public final class ExtendedChunks {
    private static Integer previousRenderDistance;
    private static Object previousLevel;
    private static int appliedRender=-1;
    private static boolean appliedRetain;
    private ExtendedChunks() { }

    public static boolean active() {
        return OrbitClient.config!=null && OrbitClient.enabled(Module.CHUNKS);
    }
    public static int renderDistance() {
        return OrbitClient.config==null?24:Math.clamp(OrbitClient.config.state(Module.CHUNKS).chunkRenderDistance,8,32);
    }
    public static int effectiveRenderDistance(int vanilla) {
        return active()?Math.max(vanilla,renderDistance()):vanilla;
    }
    public static int storageRadius(int serverRadius) {
        var state=OrbitClient.config==null?null:OrbitClient.config.state(Module.CHUNKS);
        return active() && state.retainChunks?Math.max(serverRadius,renderDistance()):serverRadius;
    }
    public static boolean shouldRetain(ChunkPos pos) {
        Minecraft mc=Minecraft.getInstance();
        if(!active() || !OrbitClient.config.state(Module.CHUNKS).retainChunks || mc.player==null || mc.level==null || mc.getCurrentServer()==null)return false;
        ChunkPos player=mc.player.chunkPosition();int distance=renderDistance()+1;
        return Math.abs(pos.x()-player.x())<=distance && Math.abs(pos.z()-player.z())<=distance;
    }
    public static void tick(Minecraft mc) {
        if(!active() || mc.level==null || mc.player==null) {restore(mc);return;}
        var state=OrbitClient.config.state(Module.CHUNKS);
        if(previousRenderDistance==null) previousRenderDistance=mc.options.renderDistance().get();
        int render=Math.clamp(state.chunkRenderDistance,8,32);
        boolean changed=previousLevel!=mc.level || appliedRender!=render || appliedRetain!=state.retainChunks;
        if(mc.options.renderDistance().get()!=render)mc.options.renderDistance().set(render);
        if(changed) {
            previousLevel=mc.level;appliedRender=render;appliedRetain=state.retainChunks;
            refreshWorld(mc);
        }
    }
    public static void restore(Minecraft mc) {
        boolean changed=previousRenderDistance!=null || previousLevel!=null;
        if(previousRenderDistance!=null && mc.options.renderDistance().get()!=previousRenderDistance)mc.options.renderDistance().set(previousRenderDistance);
        previousRenderDistance=null;previousLevel=null;appliedRender=-1;appliedRetain=false;
        if(changed && mc.level!=null)refreshWorld(mc);
    }
    private static void refreshWorld(Minecraft mc) {
        if(mc.level==null)return;
        if(mc.level.getChunkSource() instanceof ExtendedChunkCacheAccess access)access.orbit$refreshRadius();
        mc.levelRenderer.invalidateCompiledGeometry(mc.level,mc.options,mc.gameRenderer.mainCamera(),mc.getBlockColors());
    }
}
