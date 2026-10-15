package org.orbitlauncher.client;

import com.mojang.blaze3d.textures.GpuTextureView;
import java.util.Collections;
import java.util.Set;
import java.util.WeakHashMap;

/** Only Orbit's dedicated font atlases use filtered sampling; vanilla glyphs stay pixel sharp. */
public final class SmoothUiText {
    private static final Set<GpuTextureView> ATLASES=Collections.synchronizedSet(Collections.newSetFromMap(new WeakHashMap<>()));
    public static void register(GpuTextureView texture){ATLASES.add(texture);}
    public static boolean contains(GpuTextureView texture){return ATLASES.contains(texture);}
    private SmoothUiText(){}
}
