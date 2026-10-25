package org.orbitlauncher.client;

import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.keymapping.v1.KeyMappingHelper;
import net.fabricmc.fabric.api.client.rendering.v1.hud.HudElementRegistry;
import net.fabricmc.fabric.api.client.rendering.v1.hud.VanillaHudElements;
import net.minecraft.client.Minecraft;
import net.minecraft.client.KeyMapping;
import net.minecraft.resources.Identifier;
import net.minecraft.network.chat.Component;
import org.lwjgl.glfw.GLFW;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import java.util.ArrayDeque;

public final class OrbitClient implements ClientModInitializer {
    public static final Logger LOG=LoggerFactory.getLogger("OrbitClient");
    public static ClientConfig config;
    public static boolean flatText(net.minecraft.client.gui.Font font){return HudRenderer.drawing||OrbitFonts.owns(font)||Minecraft.getInstance().gui.screen() instanceof OrbitUiScreen;}
    public static KeyMapping menuKey, zoomKey,inspectKey;
    public static final java.util.EnumMap<Module,KeyMapping> moduleKeys=new java.util.EnumMap<>(Module.class);
    private static final ArrayDeque<Long> LEFT=new ArrayDeque<>(), RIGHT=new ArrayDeque<>();
    public static long sessionStart;
    private static boolean inWorld;

    @Override public void onInitializeClient() {
        config=ClientConfig.load();
        var category=KeyMapping.Category.register(Identifier.fromNamespaceAndPath("orbit_client","controls"));
        menuKey=KeyMappingHelper.registerKeyMapping(new KeyMapping("key.orbit_client.menu",GLFW.GLFW_KEY_RIGHT_SHIFT,category));
        zoomKey=KeyMappingHelper.registerKeyMapping(new KeyMapping("key.orbit_client.zoom",GLFW.GLFW_KEY_C,category));
        inspectKey=KeyMappingHelper.registerKeyMapping(new KeyMapping("key.orbit_client.inspect",GLFW.GLFW_KEY_R,category));
        for(var module:Module.values()) if(module!=Module.INTERFACE)moduleKeys.put(module,KeyMappingHelper.registerKeyMapping(new KeyMapping(
            "key.orbit_client.module."+module.name().toLowerCase(java.util.Locale.ROOT),GLFW.GLFW_KEY_UNKNOWN,category)));
        HoldMyItems.initialize();
        ClientTickEvents.END_CLIENT_TICK.register(client -> {
            SkinLayers.sync();
            ClientFeatures.tick(client);
            ExtendedChunks.tick(client);
            AutoRefill.tick(client);
            HoldMyItems.sync(client);
            PingMonitor.tick(client);
            if (client.player!=null && !inWorld) { sessionStart=System.nanoTime(); inWorld=true; }
            if(client.player==null) { inWorld=false; LEFT.clear(); RIGHT.clear(); }
            if(Boolean.getBoolean("orbit.client.smoke-test")) SmokeChecks.tick(client);
            while(menuKey.consumeClick()) {
                if(client.player!=null && client.gui.screen()==null) client.gui.setScreen(new OrbitScreen());
            }
            for(var entry:moduleKeys.entrySet()) while(entry.getValue().consumeClick()) {
                if(client.player!=null && client.gui.screen()==null) {
                    toggleFromKey(entry.getKey());
                }
            }
            prune(LEFT); prune(RIGHT);
        });
        net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientLifecycleEvents.CLIENT_STOPPING.register(client->{ClientFeatures.restoreSprint(client);ExtendedChunks.restore(client);AutoRefill.reset();GlowOutline.close();});
        HudElementRegistry.attachElementBefore(VanillaHudElements.CHAT,
            Identifier.fromNamespaceAndPath("orbit_client","hud"),(g,delta)-> HudRenderer.render(g,false));
        LOG.info("Orbit Client 0.24.1 initialized for Minecraft 26.2");
    }
    public static String tr(String ru,String en) {
        return Minecraft.getInstance().options.languageCode.startsWith("ru") ? ru : en;
    }
    public static boolean enabled(Module m) { return config!=null && config.state(m).enabled; }
    public static int menuBlurRadius(int vanilla){return config!=null&&Minecraft.getInstance().gui.screen() instanceof OrbitUiScreen?OrbitTheme.options().effectiveBlurRadius():vanilla;}
    public static boolean tooltipsAllowed(){return OrbitTheme.options().tooltips||!(Minecraft.getInstance().gui.screen() instanceof OrbitUiScreen);}
    static void toggleFromKey(Module module){
        var state=config.state(module);state.enabled=!state.enabled;save();
        var player=Minecraft.getInstance().player;
        if(OrbitTheme.options().bindMessages&&player!=null)Minecraft.getInstance().gui.hud.setOverlayMessage(Component.literal("Orbit · "+module.title()+": "+(state.enabled?tr("включён","enabled"):tr("выключен","disabled"))),false);
    }
    public static void click(int button) {
        Minecraft mc=Minecraft.getInstance();
        if(mc.player==null || mc.gui.screen()!=null || !mc.isWindowActive()) return;
        if(button==0) LEFT.addLast(System.nanoTime());
        if(button==1) RIGHT.addLast(System.nanoTime());
    }
    private static void prune(ArrayDeque<Long> q) {
        long cutoff=System.nanoTime()-1_000_000_000L;
        while(!q.isEmpty() && q.peekFirst()<cutoff) q.removeFirst();
    }
    public static int cps(boolean right) { var q=right?RIGHT:LEFT; prune(q); return q.size(); }
    public static boolean zooming() {
        var mc=Minecraft.getInstance();
        return enabled(Module.ZOOM) && zoomKey!=null && zoomKey.isDown() && mc.player!=null && mc.gui.screen()==null;
    }
    public static void save() {
        HoldMyItems.sync(Minecraft.getInstance());
        if(!config.save() && Minecraft.getInstance().player!=null)
            Minecraft.getInstance().player.sendSystemMessage(Component.literal(tr("Orbit: не удалось сохранить настройки","Orbit: unable to save settings")));
    }
}
