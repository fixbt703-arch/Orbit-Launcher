package org.orbitlauncher.client;

import com.google.gson.JsonParser;
import java.lang.reflect.Proxy;
import java.nio.file.Files;
import net.fabricmc.loader.impl.FabricLoaderImpl;
import net.fabricmc.loader.impl.game.GameProvider;

public final class ClientConfigMigrationTest {
    public static void main(String[] args) throws Exception {
        var directory=Files.createTempDirectory("orbit-config-migration-");
        FabricLoaderImpl.INSTANCE.setGameProvider((GameProvider)Proxy.newProxyInstance(GameProvider.class.getClassLoader(),new Class<?>[]{GameProvider.class},
            (proxy,method,arguments)->{if(method.getName().equals("getLaunchDirectory"))return directory;throw new UnsupportedOperationException(method.getName());}));
        try {
            var config=ClientConfig.parse(JsonParser.parseString("""
                {"modules":{"FUTURE_MODULE":{},"HP_INDICATOR":{},"FPS":{"enabled":false,"customPosition":true,"x":0.43,"scale":1.35},
                 "COOL_ANIM":{"enabled":true,"hmiScale":1.2}},
                 "interfaceOptions":{"theme":7,"themeSetVersion":1,"animationSpeed":3,"hud":{"padding":12,"opacity":0.6}}}
                """));
            if(config.modules.size()!=2||config.state(Module.FPS).enabled||!config.state(Module.FPS).customPosition
                ||config.state(Module.FPS).x!=.43||Math.abs(config.state(Module.FPS).scale-1.35)>.001)
                throw new AssertionError("Unknown module discarded surviving FPS settings");
            if(!config.state(Module.HOLD_MY_ITEMS).enabled||Math.abs(config.state(Module.HOLD_MY_ITEMS).hmiScale-1.2)>.001)
                throw new AssertionError("Unknown module discarded COOL_ANIM migration");
            if(config.interfaceOptions.theme!=7||config.interfaceOptions.animationSpeed!=3||config.interfaceOptions.hud.padding!=12
                ||Math.abs(config.interfaceOptions.hud.opacity-.6)>.001)
                throw new AssertionError("Unknown module discarded Interface settings");
            var modern=ClientConfig.parse(JsonParser.parseString("""
                {"modules":{"FUTURE_MODULE":{},"COOL_ANIM":{"hmiScale":1.2},"HOLD_MY_ITEMS":{"hmiScale":1.1}}}
                """));
            if(modern.modules.size()!=1||Math.abs(modern.state(Module.HOLD_MY_ITEMS).hmiScale-1.1)>.001)
                throw new AssertionError("Legacy settings replaced existing HOLD_MY_ITEMS settings");
            System.out.println("PASS unknown module filtering preserves FPS, Interface and COOL_ANIM migration precedence");
        } finally {
            Files.deleteIfExists(directory.resolve("config"));Files.deleteIfExists(directory);
        }
    }
}
