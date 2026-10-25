import java.net.*;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.lang.reflect.Method;
import com.google.gson.*;
import com.google.common.collect.Multimap;

public class LegacyProbe {
    static void check(boolean condition,String message){if(!condition)throw new AssertionError(message);}
    static Object profile(Class<?> type,UUID id,String name)throws Exception{return type.getConstructor(UUID.class,String.class).newInstance(id,name);}
    @SuppressWarnings({"unchecked","rawtypes"}) static void verify(String[] args)throws Exception{
        Class<?> auth=Class.forName("com.mojang.authlib.yggdrasil.YggdrasilAuthenticationService");
        Object authentication;
        try{authentication=auth.getConstructor(Proxy.class).newInstance(Proxy.NO_PROXY);}
        catch(NoSuchMethodException old){authentication=auth.getConstructor(Proxy.class,String.class).newInstance(Proxy.NO_PROXY,"fixture-only");}
        Object service=auth.getMethod("createMinecraftSessionService").invoke(authentication);
        Class<?> gp=Class.forName("com.mojang.authlib.GameProfile"),prop=Class.forName("com.mojang.authlib.properties.Property");
        Method textures=service.getClass().getMethod("getTextures",gp,boolean.class);
        UUID id=UUID.nameUUIDFromBytes("OfflinePlayer:ErickSkrauch".getBytes(StandardCharsets.UTF_8));
        Object target=profile(gp,id,"ErickSkrauch");
        Map result=(Map)textures.invoke(service,target,true);
        check(!result.isEmpty(),"Legacy missing-profile Ely fallback failed");
        Object skin=result.values().iterator().next();String url=(String)skin.getClass().getMethod("getUrl").invoke(skin);
        check(url.startsWith("https://ely.by/"),"Legacy texture is not verified HTTPS Ely fallback");
        check(gp.getMethod("getId").invoke(target).equals(id)&&gp.getMethod("getName").invoke(target).equals("ErickSkrauch"),"Identity changed");
        Object own=profile(gp,UUID.fromString("11111111-2222-4333-8444-555555555555"),"ErickSkrauch");
        boolean include=Boolean.getBoolean("orbit.skinbridge.includeViewer");
        check(((Map)textures.invoke(service,own,true)).isEmpty()!=include,"Viewer inclusion mode wrong");
        Object ownName=profile(gp,UUID.randomUUID(),"OrbitViewer");
        if(!include)check(((Map)textures.invoke(service,ownName,true)).isEmpty(),"Server-assigned viewer UUID bypassed exclusion");
        Object mojang=profile(gp,id,"ErickSkrauch");
        JsonObject fixture=new Gson().fromJson(Files.readString(Path.of(args[0])),JsonObject.class);
        JsonObject p=fixture.getAsJsonArray("properties").get(0).getAsJsonObject();
        Object signed=prop.getConstructor(String.class,String.class,String.class).newInstance("textures",p.get("value").getAsString(),p.get("signature").getAsString());
        ((Multimap)gp.getMethod("getProperties").invoke(mojang)).put("textures",signed);
        Map kept=(Map)textures.invoke(service,mojang,true);
        check(!kept.isEmpty(),"Existing Mojang signed texture rejected");
        String keptUrl=(String)kept.values().iterator().next().getClass().getMethod("getUrl").invoke(kept.values().iterator().next());
        check(keptUrl.contains("textures.minecraft.net"),"Mojang signed property replaced by Ely");
        System.out.println("PASS legacy actual Authlib: signed HTTPS Ely fallback, UUID/name preserved, viewer mode="+include+", server/Mojang signed texture preserved.");
    }
    public static void main(String[] args){try{verify(args);System.exit(0);}catch(Throwable t){t.printStackTrace();System.exit(1);}}
}
