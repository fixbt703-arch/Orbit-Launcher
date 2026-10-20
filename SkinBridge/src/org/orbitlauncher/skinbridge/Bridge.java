package org.orbitlauncher.skinbridge;

import java.io.*;
import java.net.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.security.*;
import java.security.spec.X509EncodedKeySpec;
import java.util.*;
import java.util.concurrent.*;

/** Bootstrap-visible adapter. Only public texture endpoints are queried; no credentials are read. */
public final class Bridge {
    private static final Map<UUID,String> NAMES = Collections.synchronizedMap(new LinkedHashMap<UUID,String>(128, .75f, true) {
        protected boolean removeEldestEntry(Map.Entry<UUID,String> entry) { return size() > 4096; }
    });
    private static final Map<String,Future<Entry>> CACHE = new ConcurrentHashMap<>();
    private static final Map<String,Skin> ISSUED = new ConcurrentHashMap<>();
    private static final Map<Object,Request> DEFERRED = Collections.synchronizedMap(new WeakHashMap<Object,Request>());
    private static final Map<Object,Object> PACKED = Collections.synchronizedMap(new WeakHashMap<Object,Object>());
    private static final ExecutorService IO = new ThreadPoolExecutor(2, 2, 30, TimeUnit.SECONDS, new ArrayBlockingQueue<Runnable>(128), task -> {
        Thread thread = new Thread(task, "Orbit skin download"); thread.setDaemon(true); return thread;
    }, new ThreadPoolExecutor.AbortPolicy());
    private static final String VIEWER = System.getProperty("orbit.skinbridge.viewer", "").replace("-", "");
    private static final String VIEWER_NAME = System.getProperty("orbit.skinbridge.viewerName", "");
    private static final String ELY_KEY = "MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEAxgFJRb0e9fRyVG5+JlCgh0hccRIcgO5yxEVkMJajAI12Ev/Pc7lpTt6OtKTEcUNfjYgBnEhIKbdLD0Z+B5BxSg9DQmozgzZcesScpASQb4Kt6P8itowdbgbUm4v+6x1QUKJjjmhHq93m9OIEbxQLCq+SrEMZpDrXRgd9DhNPjZv/95ximP8otvh7+bmEl8jwINgfJx0PAeJFYlceQcshiYh+LHtaIwzbTTqkDibDm7QiEc+/qGab3mABtVTpqw/refwFoR0M8+xkWF+1/D8kH0WFa+rBhdjLyLG+2hdOpKXoH/fMH0tQMPHU78J17JVKWwIWCwEWXp8HiWSbIt3acmBYtyW3tqarFFMMECx2wmJP6FVOvYVThZxq9qc9/f3yeTGz3g7zU1YljHSVRP16iEbEnHQBKxmrj2cdZgosJej4YppV7f3iZ8o8PF6UY51LSqvaCteXuWeYSJJESGAsUoV7ihJfWL8DymHamywB2Cahx7EiDGS3/iBcQUmpk4TTg2FrZPuKGItn1QfIRieOknnj9CPKiWdfOtJBr3i1FXLEfExgcJhQ00Y6B08QVvgiCzUF3t+VAG3Ef2YINYyGAXcW0TIgMalwwgGzdhQRhItODXptWigy0DNTUAgKQT9PS8N09yPBGxIq64T9A3/zFqC/k2bMLWUSVtIlilIItn0CAwEAAQ==";

    public static void remember(UUID id, String name) {
        if (id != null && name != null && name.matches("[A-Za-z0-9_]{3,16}")) NAMES.put(id, name);
    }
    private static boolean viewer(UUID id) { return id != null && id.toString().replace("-", "").equalsIgnoreCase(VIEWER); }
    private static Object call(Object object, String modern, String old) throws Exception {
        try { return object.getClass().getMethod(modern).invoke(object); }
        catch (NoSuchMethodException ignored) { return object.getClass().getMethod(old).invoke(object); }
    }
    private static Skin skin(UUID id, String name, boolean wait) {
        if (id == null || viewer(id) || name == null || name.equalsIgnoreCase(VIEWER_NAME) || !name.matches("[A-Za-z0-9_]{3,16}")) return null;
        try {
            String key = name.toLowerCase(Locale.ROOT);
            if (CACHE.size() > 1024) CACHE.clear();
            Future<Entry> future = CACHE.get(key);
            if (future != null && future.isDone() && future.get().expires < System.currentTimeMillis()) { CACHE.remove(key, future); future = null; }
            if (future == null) {
                FutureTask<Entry> task = new FutureTask<>(() -> {
                    Skin value = null; try { value = download(name); } catch (Exception ignored) { }
                    return new Entry(value, System.currentTimeMillis() + (value == null ? 60000 : 300000));
                });
                future = CACHE.putIfAbsent(key, task);
                if (future == null) {
                    future = task;
                    try { IO.execute(task); } catch (RejectedExecutionException full) { CACHE.remove(key, task); return null; }
                }
            }
            String thread = Thread.currentThread().getName().toLowerCase(Locale.ROOT);
            if (!future.isDone() && (!wait || thread.contains("render") || thread.equals("client thread"))) return null;
            return future.get(6, TimeUnit.SECONDS).skin;
        } catch (Exception ignored) { return null; }
    }

    public static Object packed(Object original, Object profile, Class<?> propertyType) {
        if (original != null || profile == null) return original;
        try {
            UUID id = (UUID)call(profile, "id", "getId"); String name = (String)call(profile, "name", "getName");
            if (id == null || viewer(id) || name == null || name.equalsIgnoreCase(VIEWER_NAME) || !name.matches("[A-Za-z0-9_]{3,16}")) return original;
            skin(id, name, false); // Schedule IO; getPackedTextures also runs on Minecraft's render thread.
            synchronized (PACKED) {
                Object pending = PACKED.get(profile);
                if (pending == null) {
                    pending = propertyType.getConstructor(String.class, String.class).newInstance("textures", "orbit-ely:" + UUID.randomUUID());
                    DEFERRED.put(pending, new Request(id, name)); PACKED.put(profile, pending);
                }
                return pending; // SkinManager unpacks it asynchronously and waits for the actual texture.
            }
        } catch (Exception | LinkageError ignored) { return original; }
    }

    public static Object profile(Object original, Object identifier, Class<?> resultType) {
        if (original != null || !(identifier instanceof UUID)) return original;
        try {
            UUID id = (UUID)identifier; String name = NAMES.get(id); Skin skin = skin(id, name, true);
            if (skin == null) return original;
            ClassLoader loader = resultType.getClassLoader();
            Class<?> gameProfile = Class.forName("com.mojang.authlib.GameProfile", true, loader);
            Class<?> propertyType = Class.forName("com.mojang.authlib.properties.Property", true, loader);
            Object property = propertyType.getConstructor(String.class, String.class, String.class).newInstance("textures", skin.value, skin.signature);
            Class<?> multimapType = Class.forName("com.google.common.collect.Multimap", true, loader);
            Object multimap = Class.forName("com.google.common.collect.ArrayListMultimap", true, loader).getMethod("create").invoke(null);
            multimapType.getMethod("put", Object.class, Object.class).invoke(multimap, "textures", property);
            Class<?> propertiesType = Class.forName("com.mojang.authlib.properties.PropertyMap", true, loader);
            Object properties;
            try { properties = propertiesType.getConstructor(multimapType).newInstance(multimap); }
            catch (NoSuchMethodException old) {
                properties = propertiesType.getConstructor().newInstance();
                multimapType.getMethod("put", Object.class, Object.class).invoke(properties, "textures", property);
            }
            // Preserve the UUID supplied by the server, not the Ely.by account's UUID.
            Object replacement = gameProfile.getConstructor(UUID.class, String.class, propertiesType).newInstance(id, name, properties);
            try { return resultType.getConstructor(gameProfile).newInstance(replacement); }
            catch (NoSuchMethodException older) { return resultType.getConstructor(gameProfile, Set.class).newInstance(replacement, Collections.emptySet()); }
        } catch (Exception | LinkageError ignored) { return original; }
    }

    @SuppressWarnings({"unchecked", "rawtypes"})
    public static Object unpack(Object property, Class<?> texturesType) {
        if (property == null) return null;
        try {
            Request request = DEFERRED.get(property);
            Skin skin;
            if (request != null) {
                skin = skin(request.id, request.name, true);
                if (skin == null) return texturesType.getField("EMPTY").get(null);
            } else {
                String value = (String)call(property, "value", "getValue"); skin = ISSUED.get(value);
                String signature = (String)call(property, "signature", "getSignature");
                if (skin == null) skin = signedSkin(value, signature, null);
                if (skin == null || !skin.signature.equals(signature)) return null;
            }
            ClassLoader loader = texturesType.getClassLoader();
            Class<?> textureType = Class.forName("com.mojang.authlib.minecraft.MinecraftProfileTexture", true, loader);
            Class<?> stateType = Class.forName("com.mojang.authlib.SignatureState", true, loader);
            Object body = textureType.getConstructor(String.class, Map.class).newInstance(skin.url, skin.slim ? Collections.singletonMap("model", "slim") : Collections.emptyMap());
            return texturesType.getConstructor(textureType, textureType, textureType, stateType).newInstance(body, null, null, Enum.valueOf((Class)stateType, "SIGNED"));
        } catch (Exception | LinkageError ignored) { return null; }
    }

    @SuppressWarnings({"unchecked", "rawtypes"})
    public static Object legacy(Object original, Object profile) {
        if (!(original instanceof Map) || !((Map)original).isEmpty() || profile == null) return original;
        try {
            Skin skin = skin((UUID)call(profile, "id", "getId"), (String)call(profile, "name", "getName"), true);
            if (skin == null) return original;
            ClassLoader loader = profile.getClass().getClassLoader();
            Class<?> textureType = Class.forName("com.mojang.authlib.minecraft.MinecraftProfileTexture", true, loader);
            Class<?> type = Class.forName("com.mojang.authlib.minecraft.MinecraftProfileTexture$Type", true, loader);
            Map result = new HashMap();
            result.put(Enum.valueOf((Class)type, "SKIN"), textureType.getConstructor(String.class, Map.class).newInstance(skin.url, skin.slim ? Collections.singletonMap("model", "slim") : Collections.emptyMap()));
            return result;
        } catch (Exception | LinkageError ignored) { return original; }
    }

    private static Skin download(String name) throws Exception {
        URL address = new URL("https://skinsystem.ely.by/profile/" + name + "?unsigned=false&version=2");
        HttpURLConnection connection = (HttpURLConnection)address.openConnection();
        connection.setConnectTimeout(2000); connection.setReadTimeout(3000); connection.setInstanceFollowRedirects(false);
        connection.setRequestProperty("User-Agent", "OrbitLauncher-SkinBridge/1.0");
        try {
            if (connection.getResponseCode() != 200) return null;
            ByteArrayOutputStream buffer = new ByteArrayOutputStream();
            try (InputStream input = connection.getInputStream()) {
                byte[] block = new byte[4096]; int count;
                long deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(5);
                while ((count = input.read(block)) != -1) { if (buffer.size() + count > 196608 || System.nanoTime() > deadline) return null; buffer.write(block, 0, count); }
            }
            Map<String,Object> response = Json.object(Json.read(new String(buffer.toByteArray(), StandardCharsets.UTF_8)));
            if (!name.equalsIgnoreCase(Json.string(response.get("name")))) return null;
            Object properties = response.get("properties"); if (!(properties instanceof List)) return null;
            for (Object item : (List<?>)properties) {
                Map<String,Object> property = Json.object(item); if (!"textures".equals(property.get("name"))) continue;
                String value = Json.string(property.get("value")), signature = Json.string(property.get("signature"));
                return signedSkin(value, signature, name);
            }
            return null;
        } finally { connection.disconnect(); }
    }
    private static Skin signedSkin(String value, String signature, String expectedName) throws Exception {
        if (value == null || signature == null || value.length() > 196608 || signature.length() > 2048) return null;
        Signature verifier = Signature.getInstance("SHA1withRSA");
        verifier.initVerify(KeyFactory.getInstance("RSA").generatePublic(new X509EncodedKeySpec(Base64.getDecoder().decode(ELY_KEY))));
        verifier.update(value.getBytes(StandardCharsets.UTF_8));
        if (!verifier.verify(Base64.getDecoder().decode(signature))) return null;
        Map<String,Object> payload = Json.object(Json.read(new String(Base64.getDecoder().decode(value), StandardCharsets.UTF_8)));
        String name = Json.string(payload.get("profileName"));
        if (!name.matches("[A-Za-z0-9_]{3,16}") || (expectedName != null && !name.equalsIgnoreCase(expectedName))) return null;
        Map<String,Object> texture = Json.object(Json.object(payload.get("textures")).get("SKIN"));
        URI url = new URI(Json.string(texture.get("url"))); String host = url.getHost();
        if (host == null || !(host.equals("ely.by") || host.endsWith(".ely.by") || host.equals("textures.minecraft.net")) ||
            url.getUserInfo() != null || (url.getPort() != -1 && url.getPort() != 80 && url.getPort() != 443) ||
            !(url.getScheme().equals("http") || url.getScheme().equals("https"))) return null;
        String secure = new URI("https", null, host, -1, url.getPath(), url.getQuery(), null).toASCIIString();
        boolean slim = texture.get("metadata") instanceof Map && "slim".equals(Json.object(texture.get("metadata")).get("model"));
        Skin skin = new Skin(value, signature, secure, slim);
        if (ISSUED.size() > 1024) ISSUED.clear(); ISSUED.put(value, skin); return skin;
    }
    private static final class Entry { final Skin skin; final long expires; Entry(Skin skin, long expires) { this.skin = skin; this.expires = expires; } }
    private static final class Request { final UUID id; final String name; Request(UUID id, String name) { this.id = id; this.name = name; } }
    private static final class Skin { final String value, signature, url; final boolean slim; Skin(String value, String signature, String url, boolean slim) { this.value = value; this.signature = signature; this.url = url; this.slim = slim; } }
}
