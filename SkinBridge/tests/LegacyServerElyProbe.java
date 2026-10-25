import java.net.Proxy;
import java.nio.file.*;
import java.util.*;
import com.google.gson.*;
import com.google.common.collect.Multimap;

public class LegacyServerElyProbe {
    @SuppressWarnings({"unchecked","rawtypes"}) public static void main(String[] args)throws Exception{
        Class<?> auth=Class.forName("com.mojang.authlib.yggdrasil.YggdrasilAuthenticationService");
        Object a;
        try{a=auth.getConstructor(Proxy.class).newInstance(Proxy.NO_PROXY);}
        catch(NoSuchMethodException old){a=auth.getConstructor(Proxy.class,String.class).newInstance(Proxy.NO_PROXY,"fixture-only");}
        Object service=auth.getMethod("createMinecraftSessionService").invoke(a);
        Class<?> gp=Class.forName("com.mojang.authlib.GameProfile"),prop=Class.forName("com.mojang.authlib.properties.Property");
        UUID viewer=UUID.fromString("11111111-2222-4333-8444-555555555555");
        Object profile=gp.getConstructor(UUID.class,String.class).newInstance(viewer,"OrbitViewer");
        JsonObject p=new Gson().fromJson(Files.readString(Path.of(args[0])),JsonObject.class).getAsJsonArray("properties").get(0).getAsJsonObject();
        Object property=prop.getConstructor(String.class,String.class,String.class).newInstance("textures",p.get("value").getAsString(),p.get("signature").getAsString());
        ((Multimap)gp.getMethod("getProperties").invoke(profile)).put("textures",property);
        Object result=service.getClass().getMethod("getTextures",gp,boolean.class).invoke(service,profile,true);
        if(((Map)result).isEmpty())throw new AssertionError("Signed server Ely texture rejected");
        Object skin=((Map)result).values().iterator().next();
        if(!((String)skin.getClass().getMethod("getUrl").invoke(skin)).startsWith("https://ely.by/"))throw new AssertionError("Server Ely texture did not use HTTPS");
        if(!gp.getMethod("getId").invoke(profile).equals(viewer)||!gp.getMethod("getName").invoke(profile).equals("OrbitViewer"))throw new AssertionError("Server property changed identity");
        Object damaged=gp.getConstructor(UUID.class,String.class).newInstance(UUID.randomUUID(),"ErickSkrauch");
        String signature=p.get("signature").getAsString();
        String tampered=(signature.startsWith("A")?"B":"A")+signature.substring(1);
        ((Multimap)gp.getMethod("getProperties").invoke(damaged)).put("textures",prop.getConstructor(String.class,String.class,String.class).newInstance("textures",p.get("value").getAsString(),tampered));
        try {
            if(!((Map)service.getClass().getMethod("getTextures",gp,boolean.class).invoke(service,damaged,true)).isEmpty())throw new AssertionError("Tampered server signature was accepted or replaced");
        } catch(java.lang.reflect.InvocationTargetException expected) {
            String type=expected.getCause().getClass().getName();
            if(!type.equals("com.mojang.authlib.minecraft.InsecureTextureException")&&!type.equals("com.mojang.authlib.minecraft.InsecurePublicKeyException$InvalidException"))throw expected;
        }
        System.out.println("PASS signed legacy server Ely: HTTPS texture, viewer server-property priority, identity unchanged, tampered signature keeps vanilla rejection.");System.exit(0);
    }
}
