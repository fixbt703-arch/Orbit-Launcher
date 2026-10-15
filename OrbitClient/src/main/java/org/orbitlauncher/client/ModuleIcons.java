package org.orbitlauncher.client;

import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.resources.Identifier;
import java.util.EnumMap;
import java.util.HashMap;
import java.util.Locale;
import java.util.Map;

/** Antialiased artwork with a consistent round stroke and optical size. */
public final class ModuleIcons {
    private static final EnumMap<Module,Identifier> MODULES=new EnumMap<>(Module.class);
    private static final Map<String,Identifier> UI=new HashMap<>();
    private static final java.util.Set<String> SHARED=java.util.Set.of("settings","search","close","left","right","all","on","check","chunks","zoom","clock");
    static {for(var module:Module.values())MODULES.put(module,id(module.name().toLowerCase(Locale.ROOT)));}
    private static Identifier id(String name){return Identifier.fromNamespaceAndPath("orbit_client","textures/gui/sprites/"+(SHARED.contains(name)?"launcher_icons/":"ui/icons/")+name+".png");}
    public static void draw(GuiGraphicsExtractor g,Module module,int x,int y,int size,int color) {
        g.blit(RenderPipelines.GUI_TEXTURED,MODULES.get(module),x,y,0,0,size,size,96,96,96,96,color);
    }
    static void ui(GuiGraphicsExtractor g,String name,int x,int y,int size,int color) {
        int source=name.equals("logo")?1024:96;
        g.blit(RenderPipelines.GUI_TEXTURED,UI.computeIfAbsent(name,ModuleIcons::id),x,y,0,0,size,size,source,source,source,source,color);
    }
    private ModuleIcons() { }
}
