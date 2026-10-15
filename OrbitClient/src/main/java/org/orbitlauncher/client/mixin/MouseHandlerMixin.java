package org.orbitlauncher.client.mixin;
import net.minecraft.client.MouseHandler;
import net.minecraft.client.input.MouseButtonInfo;
import org.orbitlauncher.client.OrbitClient;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.lwjgl.glfw.GLFW;

@Mixin(MouseHandler.class)
public class MouseHandlerMixin {
    @Inject(method="onButton",at=@At("HEAD"))
    private void orbit$click(long window,MouseButtonInfo button,int action,CallbackInfo ci) {
        if(action==GLFW.GLFW_PRESS) {OrbitClient.click(button.button());}
    }
}
