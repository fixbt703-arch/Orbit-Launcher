import net.fabricmc.api.ClientModInitializer;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.client.gui.screens.ConnectScreen;
import net.minecraft.client.multiplayer.ServerData;
import net.minecraft.client.multiplayer.resolver.ServerAddress;
import net.minecraft.server.MinecraftServer;
import net.minecraft.world.Difficulty;
import net.minecraft.world.level.*;
import net.minecraft.world.level.levelgen.WorldOptions;
import net.minecraft.world.level.levelgen.presets.WorldPresets;
import java.net.ServerSocket;
import java.nio.file.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** Development-only Fabric fixture, never included in the released launcher or adapter. */
public final class ConnectWorldSmoke implements ClientModInitializer {
    private int stage;
    private final AtomicBoolean queued = new AtomicBoolean();
    private final Path root = Path.of(System.getProperty("user.dir"));
    public void onInitializeClient() {
        Thread thread = new Thread(() -> {
            while (true) {
                try {
                    Minecraft mc = Minecraft.getInstance();
                    if (mc != null && queued.compareAndSet(false, true)) mc.execute(() -> {
                        try { tick(mc); }
                        catch (Throwable failure) { try { Files.writeString(root.resolve("connect-failed.txt"), failure.toString()); } catch (Exception ignored) { } mc.stop(); }
                        finally { queued.set(false); }
                    });
                    Thread.sleep(250);
                } catch (Throwable ignored) { }
            }
        }, "Orbit Connect isolated live test"); thread.setDaemon(true); thread.start();
    }
    private void tick(Minecraft mc) throws Exception {
        if (Files.exists(root.resolve("connect-stop"))) { mc.stop(); return; }
        boolean host = "host".equals(System.getProperty("orbit.connect.smoke.mode"));
        if (host && stage == 2 && !Files.exists(root.resolve("connect-chat.ok"))) {
            Object state = mc.gui.hud.getChat().storeState();
            for (var field : state.getClass().getDeclaredFields()) {
                field.setAccessible(true); Object values = field.get(state);
                if (values instanceof java.util.List<?> messages) for (Object entry : messages)
                    if (entry instanceof net.minecraft.client.multiplayer.chat.GuiMessage message && message.content().getString().contains("Orbit Connect")
                        && message.content().getStyle().getClickEvent() instanceof net.minecraft.network.chat.ClickEvent.CopyToClipboard) {
                        Files.writeString(root.resolve("connect-chat.ok"), "Real clickable invitation component in Minecraft chat");
                        net.minecraft.client.Screenshot.grab(mc, false);
                    }
            }
        }
        if (stage == 0 && mc.gui != null && mc.gui.screen() instanceof TitleScreen) {
            if (host) {
                stage = 1;
                mc.createWorldOpenFlows().createFreshLevel("Orbit-Connect-isolated-test",
                    new LevelSettings("Orbit Connect test", GameType.CREATIVE, new LevelSettings.DifficultySettings(Difficulty.PEACEFUL, false, false), true, WorldDataConfiguration.DEFAULT),
                    new WorldOptions(42, false, false), WorldPresets::createTestWorldDimensions, mc.gui.screen());
            } else if (Files.exists(root.resolve("connect-invitation"))) {
                String code = Files.readString(root.resolve("connect-invitation")).trim(); Files.delete(root.resolve("connect-invitation"));
                stage = 1;
                ConnectScreen.startConnecting(mc.gui.screen(), mc, ServerAddress.parseString(code), new ServerData("Orbit Connect fixture", code, ServerData.Type.OTHER), false, null);
            }
        }
        if (!host && stage == 1 && mc.gui.screen() instanceof net.minecraft.client.gui.screens.DisconnectedScreen)
            throw new AssertionError("Guest disconnected during ordinary direct-connect flow; see isolated logs");
        if (stage == 1 && mc.player != null && mc.getConnection() != null && mc.getConnection().hasClientLoaded()) {
            if (host) {
                int port; try (ServerSocket socket = new ServerSocket(0)) { port = socket.getLocalPort(); }
                if (!mc.getSingleplayerServer().publishServer(MinecraftServer.MultiplayerScope.LAN, GameType.CREATIVE, true, port)) throw new AssertionError("LAN publish failed");
                Files.writeString(root.resolve("connect-lan.ok"), Integer.toString(port));
                System.out.println("PASS Orbit Connect live: real integrated world opened to LAN");
            } else {
                if (mc.hasSingleplayerServer()) throw new AssertionError("Guest loaded a local world");
                Files.writeString(root.resolve("connect-joined.ok"), "Guest entered real host world");
                System.out.println("PASS Orbit Connect live: guest entered remote world via invitation parser and asynchronous proxy");
            }
            stage = 2;
        }
    }
}
