package org.orbitlauncher.client.mixin;

import java.io.*;
import java.nio.file.*;
import net.minecraft.resources.Identifier;
import net.minecraft.server.packs.resources.ResourceManager;
import net.minecraft.client.gui.font.providers.TrueTypeGlyphProviderDefinition;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

/** Uses the player's installed UI font. No Windows font file is redistributed with the mod. */
@Mixin(TrueTypeGlyphProviderDefinition.class)
public abstract class UiFontResourceMixin {
    @Redirect(method="load",at=@At(value="INVOKE",target="Lnet/minecraft/server/packs/resources/ResourceManager;open(Lnet/minecraft/resources/Identifier;)Ljava/io/InputStream;"))
    private InputStream orbit$uiFont(ResourceManager resources,Identifier id)throws IOException {
        if(id.getNamespace().equals("orbit_client")) {
            String file=switch(id.getPath()){
                case "font/system-ui.ttf" -> "segoeui.ttf";
                case "font/system-ui-semibold.ttf" -> "seguisb.ttf";
                case "font/system-brand.ttf" -> "bahnschrift.ttf";
                default -> null;
            };
            if(file!=null){
                Path font=Path.of("C:/Windows/Fonts",file);
                if(Files.isRegularFile(font))return Files.newInputStream(font);
            }
        }
        return resources.open(id);
    }
}
