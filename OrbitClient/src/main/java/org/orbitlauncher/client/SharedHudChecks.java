package org.orbitlauncher.client;

import java.util.EnumMap;
import net.minecraft.client.Minecraft;

/** Frame-to-frame rendering checks confined to the isolated UI smoke world. */
final class SharedHudChecks {
    private static final EnumMap<Module,ClientConfig.State> original=new EnumMap<>(Module.class);
    private static EnumMap<Module,HudRenderer.Bounds> baseline;
    private static InterfaceOptions options;
    static void tick(Minecraft mc,int tick){
        if(tick==192){
            baseline=new EnumMap<>(HudRenderer.bounds);options=OrbitTheme.options();
            var json=new com.google.gson.Gson();
            for(var module:Module.values())if(module.hasHud()){
                var state=OrbitClient.config.state(module);original.put(module,state);
                var legacy=json.fromJson(json.toJson(state),ClientConfig.State.class);
                legacy.panelWidth=240;legacy.padding=16;legacy.rowSpacing=8;legacy.showTitle=!state.showTitle;
                legacy.background=false;legacy.opacity=0;legacy.textOpacity=.2f;legacy.customColor=0xFF0000;
                OrbitClient.config.modules.put(module,legacy);
            }
        }
        if(tick==194){
            if(!baseline.equals(HudRenderer.bounds))throw new AssertionError("Legacy per-widget design still changes rendered HUD geometry");
            var json=new com.google.gson.Gson();var shared=json.fromJson(json.toJson(options),InterfaceOptions.class);
            shared.theme=3;shared.animations=false;shared.cornerRadius=0;
            shared.hud.panelWidth=180;shared.hud.padding=12;shared.hud.showTitle=false;shared.hud.opacity=.4f;
            OrbitClient.config.interfaceOptions=shared;
        }
        if(tick==200){
            var fps=HudRenderer.bounds.get(Module.FPS);
            if(fps==null||fps.equals(baseline.get(Module.FPS)))throw new AssertionError("Global HUD controls do not change rendered widgets");
            OrbitClient.LOG.info("Shared HUD render: old per-widget design ignored; global theme, width, padding, title, opacity and corners applied");
        }
        if(tick==201)net.minecraft.client.Screenshot.grab(mc,false);
        if(tick==202){OrbitClient.config.modules.putAll(original);original.clear();OrbitClient.config.interfaceOptions=options;OrbitClient.save();}
    }
}
