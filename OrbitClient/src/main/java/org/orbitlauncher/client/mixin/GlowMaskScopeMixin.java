package org.orbitlauncher.client.mixin;

import com.llamalad7.mixinextras.injector.wrapmethod.WrapMethod;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import net.minecraft.client.renderer.SubmitNodeStorage;
import net.minecraft.client.renderer.feature.FeatureRenderDispatcher;
import org.orbitlauncher.client.GlowOutline;
import org.spongepowered.asm.mixin.Mixin;

@Mixin(FeatureRenderDispatcher.class)
public abstract class GlowMaskScopeMixin {
    @WrapMethod(method="renderAllFeatures")
    private void orbit$glowScope(SubmitNodeStorage storage,Operation<Void> original){
        GlowOutline.beginScope();
        try{original.call(storage);}finally{GlowOutline.endScope();}
    }

    @Mixin(FeatureRenderDispatcher.PreparedFrame.class)
    public static abstract class WorldPhases {
        @WrapMethod(method={"executeSolid","executeTranslucent","executeTranslucentAfterTerrain","executeAlwaysOnTop"})
        private void orbit$glowPhaseScope(Operation<Void> original){
            GlowOutline.beginScope();
            try{original.call();}finally{GlowOutline.endScope();}
        }
    }
}
