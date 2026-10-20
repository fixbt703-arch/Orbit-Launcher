import java.net.*;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;
import javax.imageio.ImageIO;
import com.mojang.authlib.GameProfile;
import com.mojang.authlib.yggdrasil.YggdrasilAuthenticationService;
import net.minecraft.client.renderer.texture.SkinTextureDownloader;
import net.minecraft.client.resources.SkinManager;
import net.minecraft.core.ClientAsset;
import net.minecraft.resources.Identifier;
import net.minecraft.server.Services;

/** Actual Minecraft 26.2 SkinManager without a game window/GPU or account credentials. */
public class SkinManagerProbe {
    public static void main(String[] args) {
        int result = 1;
        try {
            var authentication = new YggdrasilAuthenticationService(Proxy.NO_PROXY);
            var services = new Services(authentication.createMinecraftSessionService(), authentication.getServicesKeySet(), authentication.createProfileRepository(), null, null);
            var downloads = new AtomicInteger();
            var downloader = new SkinTextureDownloader(Proxy.NO_PROXY, null, Runnable::run) {
                public CompletableFuture<ClientAsset.Texture> downloadAndRegisterSkin(Identifier id, Path path, String url, boolean legacy) {
                    try {
                        if (!url.startsWith("https://ely.by/")) throw new AssertionError("Unexpected source: " + url);
                        var connection = new URI(url).toURL().openConnection(); connection.setConnectTimeout(4000); connection.setReadTimeout(4000);
                        try (var input = connection.getInputStream()) {
                            var image = ImageIO.read(input);
                            if (image == null || image.getWidth() != 64 || (image.getHeight() != 32 && image.getHeight() != 64)) throw new AssertionError("Invalid PNG");
                        }
                        downloads.incrementAndGet();
                        return CompletableFuture.completedFuture(new ClientAsset.DownloadedTexture(id, url));
                    } catch (Exception ex) { return CompletableFuture.failedFuture(ex); }
                }
            };
            var manager = new SkinManager(Path.of(args[0]), services, downloader, Runnable::run);
            var profile = new GameProfile(UUID.nameUUIDFromBytes("OfflinePlayer:ErickSkrauch".getBytes(StandardCharsets.UTF_8)), "ErickSkrauch");
            Thread.currentThread().setName("Render thread");
            long start = System.nanoTime(); var future = manager.get(profile);
            if ((System.nanoTime() - start) / 1000000 > 200) throw new AssertionError("SkinManager blocked render thread");
            Thread.currentThread().setName("SkinManager test worker");
            var skin = future.get(20, TimeUnit.SECONDS).orElseThrow();
            if (!skin.secure() || !(skin.body() instanceof ClientAsset.DownloadedTexture)) throw new AssertionError("Skin did not reach the native pipeline");
            if (downloads.get() != 1) throw new AssertionError("Unexpected download count");
            var viewer = new GameProfile(UUID.fromString("11111111-2222-4333-8444-555555555555"), "OrbitViewer");
            manager.get(viewer).get(5, TimeUnit.SECONDS);
            manager.get(new GameProfile(UUID.nameUUIDFromBytes("OfflinePlayer:OrbitViewer".getBytes(StandardCharsets.UTF_8)), "OrbitViewer")).get(5, TimeUnit.SECONDS);
            if (downloads.get() != 1) throw new AssertionError("Viewer's skin was replaced");
            System.out.println("PASS real Minecraft 26.2 SkinManager: async Ely resolution, valid PNG download, secure PlayerSkin, viewer exclusion, no loader or GPU required.");
            result = 0;
        } catch (Throwable failure) { failure.printStackTrace(); }
        System.exit(result); // Minecraft's global background pool is deliberately isolated to this process.
    }
}
