import java.net.*;
import java.nio.file.*;
import java.util.*;

/** Reproduces Knot's refusal to resolve an agent class through game-class linkage. */
public class IsolatedLoaderProbe {
    public static void main(String[] args) throws Exception {
        URL[] urls = Arrays.stream(System.getProperty("java.class.path").split(java.io.File.pathSeparator))
            .map(path -> { try { return Paths.get(path).toUri().toURL(); } catch (Exception e) { throw new RuntimeException(e); } })
            .toArray(URL[]::new);
        try (URLClassLoader isolated = new URLClassLoader(urls, ClassLoader.getPlatformClassLoader()) {
            protected Class<?> loadClass(String name, boolean resolve) throws ClassNotFoundException {
                if (name.startsWith("org.orbitlauncher.skinbridge.")) throw new ClassNotFoundException("Agent is not exposed to the game: " + name);
                return super.loadClass(name, resolve);
            }
        }) {
            Class<?> profile = Class.forName("com.mojang.authlib.GameProfile", true, isolated);
            Object target = profile.getConstructor(UUID.class, String.class).newInstance(UUID.randomUUID(), "ErickSkrauch");
            Object auth;
            try {auth=Class.forName("com.mojang.authlib.yggdrasil.YggdrasilAuthenticationService", true, isolated).getConstructor(Proxy.class).newInstance(Proxy.NO_PROXY);}
            catch(ClassNotFoundException modern){auth=Class.forName("com.mojang.authlib.services.MinecraftServicesDiscoveryService",true,isolated).getMethod("create",Proxy.class,boolean.class).invoke(null,Proxy.NO_PROXY,false);}
            Object service = auth.getClass().getMethod("createMinecraftSessionService").invoke(auth);
            Object property = service.getClass().getMethod("getPackedTextures", profile).invoke(service, target);
            if (property == null) throw new AssertionError("Texture fallback did not run across isolated loader");
            Thread.currentThread().setName("Skin probe worker");
            Class<?> propertyType = Class.forName("com.mojang.authlib.properties.Property", true, isolated);
            Object textures = service.getClass().getMethod("unpackTextures", propertyType).invoke(service, property);
            if (textures.getClass().getMethod("skin").invoke(textures) == null)
                throw new AssertionError("Signed texture did not resolve across isolated loader");
            Object own = profile.getConstructor(UUID.class, String.class).newInstance(UUID.randomUUID(), "OrbitViewer");
            if (service.getClass().getMethod("getPackedTextures", profile).invoke(service, own) != null)
                throw new AssertionError("Viewer exclusion lost across isolated loader");
            System.out.println("PASS isolated game loader: profile initialization, deferred textures, viewer exclusion; no direct agent-class linkage.");
        }
    }
}
