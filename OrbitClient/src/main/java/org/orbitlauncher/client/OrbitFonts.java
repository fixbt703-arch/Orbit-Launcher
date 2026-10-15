package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GlyphSource;
import net.minecraft.client.gui.font.glyphs.EffectGlyph;
import net.minecraft.network.chat.FontDescription;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.MutableComponent;
import net.minecraft.resources.Identifier;
import org.orbitlauncher.client.mixin.FontProviderAccessor;

/** A separate font instance keeps all vanilla screens, chat and resource packs untouched. */
final class OrbitFonts {
    private static final FontDescription UI=new FontDescription.Resource(Identifier.fromNamespaceAndPath("orbit_client","ui"));
    private static final FontDescription STRONG=new FontDescription.Resource(Identifier.fromNamespaceAndPath("orbit_client","ui_strong"));
    private static final FontDescription HEADING=new FontDescription.Resource(Identifier.fromNamespaceAndPath("orbit_client","ui_heading"));
    private static final FontDescription BRAND=new FontDescription.Resource(Identifier.fromNamespaceAndPath("orbit_client","ui_brand"));
    private static Font source;
    private static final java.util.Map<FontDescription,Font> cached=new java.util.HashMap<>();
    static MutableComponent text(String value){return Component.literal(value).withStyle(style->style.withFont(UI));}
    static Font ui(){return font(UI);}
    static Font strong(){return font(STRONG);}
    static Font heading(){return font(HEADING);}
    static Font brand(){return font(BRAND);}
    private static Font font(FontDescription face) {
        var original=Minecraft.getInstance().font;
        if(source!=original) {
            source=original;
            cached.clear();
        }
        return cached.computeIfAbsent(face,key->{
            var provider=((FontProviderAccessor)(Object)original).orbit$provider();
            return new Font(new Font.Provider() {
                public GlyphSource glyphs(FontDescription description) {
                    return provider.glyphs(FontDescription.DEFAULT.equals(description)?key:description);
                }
                public EffectGlyph effect(){return provider.effect();}
            });
        });
    }
    private OrbitFonts() {}
}
