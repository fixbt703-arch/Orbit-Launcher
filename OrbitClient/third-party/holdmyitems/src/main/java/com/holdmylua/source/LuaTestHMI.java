package com.holdmylua.source;

import com.holdmylua.source.global.GlobalsStorage;
import com.holdmylua.source.lua_runtime.resource_controller.LuaAnimationResourceLoader;
import java.util.concurrent.atomic.AtomicInteger;
import net.fabricmc.api.ModInitializer;
import net.fabricmc.fabric.api.client.rendering.v1.hud.HudElementRegistry;
import net.fabricmc.fabric.api.resource.ResourceManagerHelper;
import net.fabricmc.fabric.api.resource.v1.ResourceLoader;
import net.fabricmc.fabric.api.resource.v1.pack.PackActivationType;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.Minecraft;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import net.minecraft.server.packs.PackType;
import org.joml.Matrix4f;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public class LuaTestHMI implements ModInitializer {
   public static final String MOD_ID = "holdmyitems";
   public static Matrix4f matricesMain = new Matrix4f();
   public static Matrix4f matricesOff = new Matrix4f();
   public static float tickProgress = 0.0F;
   public static final Logger LOGGER = LoggerFactory.getLogger("holdmyitems");
   public static Minecraft client = Minecraft.getInstance();
   public static float prevTime = 0.0F;
   public static float deltaTime = 0.0F;

   public void onInitialize() {
      net.minecraft.client.renderer.item.properties.conditional.ConditionalItemModelProperties.ID_MAPPER.put(Identifier.fromNamespaceAndPath("holdmyitems","enabled"), HmiEnabledProperty.CODEC);
      HudElementRegistry.addLast(Identifier.fromNamespaceAndPath("holdmyitems", "debug"), (context, tickCounter) -> {
         AtomicInteger y = new AtomicInteger(10);
         GlobalsStorage.debugTextRenderer.get().forEach(str -> {
            context.text(Minecraft.getInstance().font, str, 10, y.get(), -1, true);
            y.addAndGet(10);
         });
         GlobalsStorage.debugTextRenderer.clear();
      });
      ResourceManagerHelper.get(PackType.CLIENT_RESOURCES).registerReloadListener(new LuaAnimationResourceLoader());
      Identifier packIdentifier = Identifier.fromNamespaceAndPath("holdmyitems", "pack_test");
      FabricLoader.getInstance().getModContainer("holdmyitems").ifPresent(container -> {
         ResourceLoader.registerBuiltinPack(packIdentifier, container, Component.literal("Hold My Items • Original animations"), PackActivationType.ALWAYS_ENABLED);
         LOGGER.info("Registered embedded resource pack: {}", packIdentifier);
      });
      LOGGER.info("Hold My Items by sapling: original Lua animations, unofficial Minecraft 26.2 / Orbit integration");
   }
}
