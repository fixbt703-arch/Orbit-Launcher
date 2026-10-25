import java.net.Proxy;
import java.nio.charset.StandardCharsets;
import java.util.UUID;

/** Real Authlib with a public sample identity; never authenticates or reads account credentials. */
public class OfflineViewerProbe {
    public static void main(String[] args) {
        try { verify(); System.exit(0); } catch(Throwable failure){failure.printStackTrace();System.exit(1);}
    }
    private static void verify() throws Exception {
        Object authentication;
        try {authentication=Class.forName("com.mojang.authlib.yggdrasil.YggdrasilAuthenticationService").getConstructor(Proxy.class).newInstance(Proxy.NO_PROXY);}
        catch(ClassNotFoundException modern){authentication=Class.forName("com.mojang.authlib.services.MinecraftServicesDiscoveryService").getMethod("create",Proxy.class,boolean.class).invoke(null,Proxy.NO_PROXY,false);}
        Object service=authentication.getClass().getMethod("createMinecraftSessionService").invoke(authentication);
        Class<?> profile=Class.forName("com.mojang.authlib.GameProfile"),property=Class.forName("com.mojang.authlib.properties.Property");
        UUID id=UUID.nameUUIDFromBytes("OfflinePlayer:ErickSkrauch".getBytes(StandardCharsets.UTF_8));
        Object viewer=profile.getConstructor(UUID.class,String.class).newInstance(id,"ErickSkrauch");
        Thread.currentThread().setName("Render thread");long start=System.nanoTime();
        Object packed=service.getClass().getMethod("getPackedTextures",profile).invoke(service,viewer);
        if((System.nanoTime()-start)/1000000>200)throw new AssertionError("Viewer lookup blocked render thread");
        if(packed==null)throw new AssertionError("Offline viewer's Ely skin was excluded");
        Thread.currentThread().setName("Skin test worker");
        Object textures=service.getClass().getMethod("unpackTextures",property).invoke(service,packed);
        Object skin=textures.getClass().getMethod("skin").invoke(textures);
        if(skin==null)throw new AssertionError("Offline viewer skin did not resolve");
        String url=(String)skin.getClass().getMethod("getUrl").invoke(skin);
        if(!url.startsWith("https://ely.by/"))throw new AssertionError("Unsecured viewer skin URL: "+url);
        if(!textures.getClass().getMethod("signatureState").invoke(textures).toString().equals("SIGNED"))throw new AssertionError("Unverified Ely signature");
        System.out.println("PASS offline viewer: own signed Ely skin, HTTPS URL, nonblocking lookup and unchanged identity on Authlib "+profile.getPackage().getImplementationVersion());
    }
}
