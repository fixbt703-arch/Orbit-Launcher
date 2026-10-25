package org.orbitlauncher.client.mixin;

import net.minecraft.client.model.geom.ModelPart;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;
import java.util.List;
import java.util.Map;

@Mixin(ModelPart.class)
public interface ArmorGlowPartAccessor {
    @Accessor("cubes") List<ModelPart.Cube> orbit$armorCubes();
    @Accessor("children") Map<String,ModelPart> orbit$armorChildren();
}
