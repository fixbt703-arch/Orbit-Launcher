package org.orbitlauncher.client.mixin;
import net.minecraft.client.renderer.debug.DebugRenderer;
import net.minecraft.client.renderer.culling.Frustum;
import org.orbitlauncher.client.Hitboxes;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(DebugRenderer.class)
public abstract class HitboxMixin {
    @Inject(method="emitGizmos",at=@At("TAIL"))
    private void orbit$hitboxes(Frustum frustum,double x,double y,double z,float partial,CallbackInfo ci){Hitboxes.extract(frustum,x,y,z,partial);org.orbitlauncher.client.Trajectories.extract(frustum,partial);}
}
