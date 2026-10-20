import java.net.Proxy;
import java.nio.charset.StandardCharsets;
import java.util.UUID;
import com.mojang.authlib.GameProfile;
import com.mojang.authlib.SignatureState;
import com.mojang.authlib.properties.Property;
import com.mojang.authlib.properties.PropertyMap;
import com.google.common.collect.ArrayListMultimap;

/** Real authlib 9 / Minecraft 26.2 probe, using a public Ely.by sample; no login token. */
public class ModernProbe {
    public static void main(String[] args) {
        try { verify(); System.exit(0); } catch (Throwable failure) { failure.printStackTrace(); System.exit(1); }
    }
    private static void verify() throws Exception {
        Object authentication;
        try { authentication = Class.forName("com.mojang.authlib.yggdrasil.YggdrasilAuthenticationService").getConstructor(Proxy.class).newInstance(Proxy.NO_PROXY); }
        catch (ClassNotFoundException modern) { authentication = Class.forName("com.mojang.authlib.services.MinecraftServicesDiscoveryService").getMethod("create", Proxy.class, boolean.class).invoke(null, Proxy.NO_PROXY, false); }
        var service = authentication.getClass().getMethod("createMinecraftSessionService").invoke(authentication);
        var packedMethod = service.getClass().getMethod("getPackedTextures", GameProfile.class);
        var unpackMethod = service.getClass().getMethod("unpackTextures", Property.class);
        // Simulate an Ely-aware server supplying a real signed profile property, before any fallback lookup.
        var connection = new java.net.URI("https://skinsystem.ely.by/profile/ErickSkrauch?unsigned=false&version=2").toURL().openConnection();
        connection.setConnectTimeout(4000); connection.setReadTimeout(4000);
        com.google.gson.JsonObject serverProfile;
        try (var input = connection.getInputStream()) { serverProfile = com.google.gson.JsonParser.parseReader(new java.io.InputStreamReader(input, StandardCharsets.UTF_8)).getAsJsonObject(); }
        com.google.gson.JsonObject signed = serverProfile.getAsJsonArray("properties").get(0).getAsJsonObject();
        var serverProperty = new Property("textures", signed.get("value").getAsString(), signed.get("signature").getAsString());
        var serverTextures = (com.mojang.authlib.minecraft.MinecraftProfileTextures)unpackMethod.invoke(service, serverProperty);
        if (serverTextures.skin() == null || serverTextures.signatureState() != SignatureState.SIGNED) throw new AssertionError("A server-supplied Ely signature was rejected");
        String tampered = serverProperty.signature().startsWith("A") ? "B" + serverProperty.signature().substring(1) : "A" + serverProperty.signature().substring(1);
        if (Class.forName("org.orbitlauncher.skinbridge.Bridge").getMethod("unpack", Object.class, Class.class).invoke(null, new Property("textures", serverProperty.value(), tampered), com.mojang.authlib.minecraft.MinecraftProfileTextures.class) != null) throw new AssertionError("A tampered Ely signature was trusted");
        UUID id = UUID.nameUUIDFromBytes("OfflinePlayer:ErickSkrauch".getBytes(StandardCharsets.UTF_8));
        var profile = new GameProfile(id, "ErickSkrauch");
        var viewer = new GameProfile(UUID.fromString("11111111-2222-4333-8444-555555555555"), "OrbitViewer");
        if (packedMethod.invoke(service, viewer) != null) throw new AssertionError("Viewer's Microsoft skin was replaced");
        if (packedMethod.invoke(service, new GameProfile(UUID.nameUUIDFromBytes("OfflinePlayer:OrbitViewer".getBytes(StandardCharsets.UTF_8)), "OrbitViewer")) != null) throw new AssertionError("Viewer with a server-assigned UUID was replaced");
        Thread.currentThread().setName("Render thread"); long start = System.nanoTime();
        var packed = packedMethod.invoke(service, profile);
        if ((System.nanoTime() - start) / 1000000 > 100) throw new AssertionError("Render thread blocked on skin IO");
        if (packed == null || packed != packedMethod.invoke(service, profile)) throw new AssertionError("Missing/stale deferred property");
        Thread.currentThread().setName("Skin probe worker");
        var textures = (com.mojang.authlib.minecraft.MinecraftProfileTextures)unpackMethod.invoke(service, packed);
        if (textures.skin() == null || !textures.skin().getUrl().startsWith("https://ely.by/")) throw new AssertionError("Ely skin was not resolved");
        if (textures.signatureState() != SignatureState.SIGNED) throw new AssertionError("Ely signature wasn't validated");
        if (!profile.id().equals(id) || !viewer.name().equals("OrbitViewer")) throw new AssertionError("Identity changed");
        var supplied = new Property("textures", "server-supplied-property");
        var map = ArrayListMultimap.<String,Property>create(); map.put("textures", supplied);
        if (packedMethod.invoke(service, new GameProfile(id, "ErickSkrauch", new PropertyMap(map))) != supplied) throw new AssertionError("Server's skin property was replaced");
        if (Class.forName("org.orbitlauncher.skinbridge.Bridge").getMethod("unpack", Object.class, Class.class).invoke(null, new Property("textures", "orbit-ely:untrusted-server-input"), com.mojang.authlib.minecraft.MinecraftProfileTextures.class) != null) throw new AssertionError("Unissued marker was trusted");
        System.out.println("PASS licensed skin bridge: signed Ely skin, unchanged viewer/server skins and UUIDs, stable cache key, nonblocking render-thread lookup.");
    }
}
