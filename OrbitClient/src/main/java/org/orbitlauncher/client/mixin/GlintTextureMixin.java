package org.orbitlauncher.client.mixin;
import org.orbitlauncher.client.GlintColorizer;
import net.minecraft.client.renderer.texture.*;
import net.minecraft.resources.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;
@Mixin(TextureManager.class)
public abstract class GlintTextureMixin {
    @Inject(method="getTexture",at=@At("RETURN"),cancellable=true)
    private void orbit$glint(Identifier id,CallbackInfoReturnable<AbstractTexture> cir){cir.setReturnValue(GlintColorizer.texture(id,cir.getReturnValue()));}
    @Inject(method="reload",at=@At("HEAD"))
    private void orbit$reload(CallbackInfoReturnable<java.util.concurrent.CompletableFuture<Void>> cir){GlintColorizer.invalidate();}
}
