package org.orbitlauncher.client.mixin;

import com.llamalad7.mixinextras.injector.wrapmethod.WrapMethod;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.mojang.blaze3d.IndexType;
import com.mojang.blaze3d.buffers.GpuBuffer;
import net.minecraft.client.renderer.rendertype.PreparedRenderType;
import org.orbitlauncher.client.GlowOutline;
import org.orbitlauncher.client.GlowRenderTypes;
import org.spongepowered.asm.mixin.Mixin;

@Mixin(PreparedRenderType.class)
public abstract class GlowMaskTargetMixin {
    @WrapMethod(method="drawFromBuffer(Lcom/mojang/blaze3d/buffers/GpuBuffer;Lcom/mojang/blaze3d/buffers/GpuBuffer;Lcom/mojang/blaze3d/IndexType;III)V")
    private void orbit$drawMask(GpuBuffer vertices,GpuBuffer indices,IndexType indexType,int baseVertex,int firstIndex,int indexCount,Operation<Void> original){
        if(GlowRenderTypes.isMaskPipeline(((PreparedRenderType)(Object)this).pipeline())){
            GlowOutline.drawMask(()->original.call(vertices,indices,indexType,baseVertex,firstIndex,indexCount));
        }else original.call(vertices,indices,indexType,baseVertex,firstIndex,indexCount);
    }
}
