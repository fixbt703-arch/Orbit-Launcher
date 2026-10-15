package org.orbitlauncher.client;

import java.util.*;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.texture.*;
import net.minecraft.resources.Identifier;
import com.mojang.blaze3d.platform.NativeImage;

/** Recolors only the two glint textures; the underlying item/armor and shader stay intact. */
public final class GlintColorizer {
    private record Cached(int color,int opacity,AbstractTexture source,DynamicTexture texture){}
    private static final Map<Identifier,Cached> CACHE=new java.util.concurrent.ConcurrentHashMap<>();
    private static final Set<Identifier> FAILED=java.util.concurrent.ConcurrentHashMap.newKeySet();
    public static void invalidate(){CACHE.clear();FAILED.clear();EnchantmentGlow.invalidate();}
    public static AbstractTexture texture(Identifier id,AbstractTexture original){
        if(OrbitClient.config==null||!OrbitClient.enabled(Module.GLINT)||!id.getNamespace().equals("minecraft"))return original;
        boolean armor=id.getPath().equals("textures/misc/enchanted_glint_armor.png"),item=id.getPath().equals("textures/misc/enchanted_glint_item.png");
        var s=OrbitClient.config.state(Module.GLINT);
        if(!(armor&&s.glintArmor||item&&s.glintItems)||FAILED.contains(id))return original;
        int opacity=Math.round(s.glintOpacity*255);Cached cached=CACHE.get(id);
        if(cached!=null&&cached.color==s.glintColor&&cached.opacity==opacity&&cached.source==original)return cached.texture;
        try(var stream=Minecraft.getInstance().getResourceManager().getResourceOrThrow(id).open();var pixels=NativeImage.read(stream)){
            NativeImage tinted=pixels.mappedCopy(pixel->tint(pixel,s.glintColor,s.glintOpacity));
            DynamicTexture texture=new DynamicTexture(()->"Orbit glint "+id,tinted){{sampler=original.getSampler();}};
            Minecraft.getInstance().getTextureManager().register(Identifier.fromNamespaceAndPath("orbit_client",armor?"glint/armor":"glint/item"),texture);
            CACHE.put(id,new Cached(s.glintColor,opacity,original,texture));return texture;
        }catch(Exception e){FAILED.add(id);OrbitClient.LOG.warn("Cannot recolor glint texture {}",id,e);return original;}
    }
    static int tint(int pixel,int rgb,float opacity){
        float intensity=Math.max(pixel>>16&255,Math.max(pixel>>8&255,pixel&255))/255f*(float)Math.sqrt(opacity);
        return pixel&0xFF000000|Math.round((rgb>>16&255)*intensity)<<16|Math.round((rgb>>8&255)*intensity)<<8|Math.round((rgb&255)*intensity);
    }
    private GlintColorizer(){}
}
