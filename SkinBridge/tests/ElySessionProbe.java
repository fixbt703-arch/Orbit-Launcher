import java.net.Proxy;
import java.util.UUID;

/** Resolves one public Ely.by profile through the official auth agent, without signing in. */
public class ElySessionProbe {
    public static void main(String[] args){try{verify(args[0]);System.exit(0);}catch(Throwable t){t.printStackTrace();System.exit(1);}}
    private static void verify(String uuid) throws Exception {
        Object authentication;
        try{authentication=Class.forName("com.mojang.authlib.yggdrasil.YggdrasilAuthenticationService").getConstructor(Proxy.class).newInstance(Proxy.NO_PROXY);}
        catch(ClassNotFoundException modern){authentication=Class.forName("com.mojang.authlib.services.MinecraftServicesDiscoveryService").getMethod("create",Proxy.class,boolean.class).invoke(null,Proxy.NO_PROXY,false);}
        Object service=authentication.getClass().getMethod("createMinecraftSessionService").invoke(authentication);
        UUID id=UUID.fromString(uuid.replaceFirst("(\\w{8})(\\w{4})(\\w{4})(\\w{4})(\\w{12})","$1-$2-$3-$4-$5"));
        Object result=service.getClass().getMethod("fetchProfile",UUID.class,boolean.class).invoke(service,id,true);
        if(result==null)throw new AssertionError("Official Ely profile did not resolve");
        Object profile=result.getClass().getMethod("profile").invoke(result);
        Object property=service.getClass().getMethod("getPackedTextures",profile.getClass()).invoke(service,profile);
        if(property==null)throw new AssertionError("Official Ely profile has no skin property");
        Object textures=service.getClass().getMethod("unpackTextures",property.getClass()).invoke(service,property);
        Object skin=textures.getClass().getMethod("skin").invoke(textures);
        if(skin==null)throw new AssertionError("Official Ely skin rejected by Authlib");
        String url=(String)skin.getClass().getMethod("getUrl").invoke(skin);
        System.out.println("PASS Ely account profile: official agent resolved signed skin; URL scheme="+new java.net.URI(url).getScheme());
        if(Boolean.getBoolean("orbit.test.requireHttps")&&!url.startsWith("https://"))throw new AssertionError("Ely account skin still uses HTTP");
    }
}
