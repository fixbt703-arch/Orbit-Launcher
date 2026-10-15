package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.world.Difficulty;
import net.minecraft.world.level.*;
import net.minecraft.world.level.levelgen.WorldOptions;
import net.minecraft.world.level.levelgen.presets.WorldPresets;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;

/** Explicit development-only integration run in a fresh world under OrbitClient/run. */
final class WorldSmoke {
    private static long started;
    private static boolean ready;
    private static net.minecraft.core.BlockPos refillBase;
    static boolean prepare(Minecraft mc) {
        if(ready)return true;
        if(started==0) {
            started=System.nanoTime();
            mc.createWorldOpenFlows().createFreshLevel("Orbit-smoke-"+System.currentTimeMillis(),
                new LevelSettings("Orbit integration test",GameType.CREATIVE,new LevelSettings.DifficultySettings(Difficulty.PEACEFUL,false,false),true,WorldDataConfiguration.DEFAULT),
                new WorldOptions(42,false,false),WorldPresets::createTestWorldDimensions,mc.gui.screen());
            return false;
        }
        if(System.nanoTime()-started>180_000_000_000L)throw new IllegalStateException("Test world did not load");
        if(mc.player==null || mc.getConnection()==null || !mc.getConnection().hasClientLoaded())return false;
        var conn=mc.getConnection();
        conn.sendCommand("item replace entity @s armor.head with minecraft:diamond_helmet[minecraft:damage=52]");
        conn.sendCommand("item replace entity @s armor.chest with minecraft:diamond_chestplate[minecraft:damage=107]");
        conn.sendCommand("item replace entity @s armor.legs with minecraft:diamond_leggings");
        conn.sendCommand("item replace entity @s armor.feet with minecraft:diamond_boots");
        conn.sendCommand("effect give @s minecraft:speed 120 0 true");
        conn.sendCommand("effect give @s minecraft:strength infinite 1 true");
        conn.sendCommand("summon minecraft:pig ~ ~ ~3 {NoAI:1b,Invulnerable:1b}");
        conn.sendCommand("item replace entity @s weapon.mainhand with minecraft:diamond_sword[minecraft:enchantment_glint_override=true]");
        OrbitClient.config.state(Module.HITBOX).enabled=true;
        OrbitClient.config.state(Module.SKIN_3D).enabled=true;
        OrbitClient.config.state(Module.GLINT).enabled=true;
        OrbitClient.config.state(Module.GLINT).glintColor=0x48D6C5;
        OrbitClient.config.state(Module.CROSSHAIR).enabled=true;
        OrbitClient.config.state(Module.PING).enabled=true;
        OrbitClient.config.state(Module.ARMOR).enabled=true;
        OrbitClient.config.state(Module.EFFECTS).enabled=true;
        var chunks=OrbitClient.config.state(Module.CHUNKS);
        chunks.enabled=true;chunks.chunkRenderDistance=16;chunks.retainChunks=true;
        mc.gui.setScreen(new OrbitScreen());ready=true;return true;
    }
    static void verify(Minecraft mc) {
        if(mc.player==null || mc.player.getActiveEffects().size()<2 || mc.player.getItemBySlot(net.minecraft.world.entity.EquipmentSlot.HEAD).isEmpty())throw new IllegalStateException("Test armor/effects not applied");
        PingMonitor.verifySampleForTest();
        if(!(mc.level.getChunkSource() instanceof ExtendedChunkCacheAccess) || mc.options.getEffectiveRenderDistance()<16)
            throw new IllegalStateException("Extended chunks were not applied to the live world");
        verifyAutoRefill(mc);
        OrbitClient.LOG.info("World smoke: armor, effects, ping, extended chunks and auto refill selection passed");
    }
    static void verifyNewFeatures(Minecraft mc){NewFeatureSmoke.verify(mc);}
    static void prepareRefill(Minecraft mc){
        mc.gui.setScreen(null);OrbitClient.config.state(Module.AUTO_REFILL).enabled=true;mc.player.getInventory().setSelectedSlot(0);
        refillBase=mc.player.blockPosition().offset(1,-1,0);
        mc.getConnection().sendCommand("gamemode survival");
        mc.getConnection().sendCommand("fill "+(refillBase.getX()-3)+" "+refillBase.getY()+" "+(refillBase.getZ()-3)+" "+(refillBase.getX()+3)+" "+refillBase.getY()+" "+(refillBase.getZ()+3)+" minecraft:stone");
        mc.getConnection().sendCommand("setblock "+refillBase.getX()+" "+refillBase.getY()+" "+refillBase.getZ()+" minecraft:stone");
        mc.getConnection().sendCommand("setblock "+refillBase.getX()+" "+(refillBase.getY()+1)+" "+refillBase.getZ()+" minecraft:air");
        mc.getConnection().sendCommand("item replace entity @s hotbar.0 with minecraft:stone 1");
        mc.getConnection().sendCommand("item replace entity @s inventory.0 with minecraft:stone 32");
        mc.getConnection().sendCommand("item replace entity @s inventory.1 with minecraft:oak_planks 64");
    }
    static void consumeRefill(Minecraft mc){
        if(!mc.player.getInventory().getSelectedItem().is(Items.STONE))throw new IllegalStateException("Refill test stack was not installed");
        AutoRefill.recordUse(mc.player,net.minecraft.world.InteractionHand.MAIN_HAND);
        mc.player.getInventory().setItem(0,ItemStack.EMPTY);
        for(int tick=0;tick<12;tick++){
            AutoRefill.tick(mc);
            if(!mc.player.getInventory().getItem(0).isEmpty()||mc.player.getInventory().getItem(9).getCount()!=32
                ||mc.player.getInventory().getItem(10).getCount()!=64)throw new IllegalStateException("Refill changed inventory before server confirmed depletion");
        }
        OrbitClient.LOG.info("Stack Refill: delayed placement acknowledgement leaves all inventory stacks untouched");
        var original=new ItemStack(Items.STONE);
        AutoRefill.serverSlot(0,original);mc.player.getInventory().setItem(0,original.copy());AutoRefill.tick(mc);
        if(mc.player.getInventory().getItem(9).getCount()!=32)throw new IllegalStateException("Rejected placement consumed a reserve stack");
        AutoRefill.reset();
        AutoRefill.recordUse(mc.player,net.minecraft.world.InteractionHand.MAIN_HAND);mc.player.getInventory().setItem(0,ItemStack.EMPTY);
        for(int tick=0;tick<85;tick++)AutoRefill.tick(mc);
        if(mc.player.getInventory().getItem(9).getCount()!=32)throw new IllegalStateException("Refill timeout changed inventory");
        mc.player.getInventory().setItem(0,original.copy());
        AutoRefill.recordUse(mc.player,net.minecraft.world.InteractionHand.MAIN_HAND);mc.player.getInventory().setItem(0,ItemStack.EMPTY);
        mc.gui.setScreen(new net.minecraft.client.gui.screens.inventory.InventoryScreen(mc.player));AutoRefill.tick(mc);mc.gui.setScreen(null);
        if(mc.player.getInventory().getItem(9).getCount()!=32)throw new IllegalStateException("Opening inventory did not cancel refill");
        mc.player.getInventory().setItem(0,original.copy());
        OrbitClient.LOG.info("Stack Refill: rejected placement, missing acknowledgement timeout and opening inventory preserve reserve items");
        var hit=new net.minecraft.world.phys.BlockHitResult(new net.minecraft.world.phys.Vec3(refillBase.getX()+.5,refillBase.getY()+1,refillBase.getZ()+.5),net.minecraft.core.Direction.UP,refillBase,false);
        mc.gameMode.useItemOn(mc.player,net.minecraft.world.InteractionHand.MAIN_HAND,hit);
        if(!mc.player.getInventory().getItem(0).isEmpty()||mc.player.getInventory().getItem(9).getCount()!=32)throw new IllegalStateException("Final block placement was not predicted without changing reserve items");
    }
    static void finishRefill(Minecraft mc){
        var selected=mc.player.getInventory().getItem(0);
        if(!selected.is(Items.STONE)||selected.getCount()!=32||!mc.player.getInventory().getItem(9).isEmpty()||!mc.player.getInventory().getItem(10).is(Items.OAK_PLANKS))throw new IllegalStateException("Refill did not synchronize the exact stack: selected="+selected+", source="+mc.player.getInventory().getItem(9)+", other="+mc.player.getInventory().getItem(10));
        boolean server=mc.getSingleplayerServer().submit(()->{var player=mc.getSingleplayerServer().getPlayerList().getPlayers().getFirst();var stack=player.getInventory().getItem(0);return stack.is(Items.STONE)&&stack.getCount()==32&&player.getInventory().getItem(9).isEmpty();}).join();
        boolean placed=mc.getSingleplayerServer().submit(()->mc.getSingleplayerServer().overworld().getBlockState(refillBase.above()).is(net.minecraft.world.level.block.Blocks.STONE)).join();
        if(!server||!placed)throw new IllegalStateException("Refill or real block placement was only applied on the client");
        OrbitClient.LOG.info("Stack Refill: exhausted stack swapped, exact block retained and server inventory synchronized");
        mc.getConnection().sendCommand("item replace entity @s hotbar.0 with minecraft:diamond_sword[minecraft:enchantment_glint_override=true]");
    }
    private static void verifyAutoRefill(Minecraft mc) {
        var inventory=mc.player.getInventory();
        int[] slots={0,1,9,10};ItemStack[] saved=new ItemStack[slots.length];
        for(int i=0;i<slots.length;i++)saved[i]=inventory.getItem(slots[i]).copy();
        try {
            ItemStack previous=new ItemStack(Items.STONE);
            inventory.setItem(0,ItemStack.EMPTY);
            inventory.setItem(1,new ItemStack(Items.OAK_PLANKS,32));
            inventory.setItem(9,new ItemStack(Items.STONE,16));
            inventory.setItem(10,new ItemStack(Items.STONE,64));
            if(AutoRefill.findReplacement(mc.player,previous,0,true,true)!=10
                || AutoRefill.findReplacement(mc.player,previous,0,false,true)!=1
                || AutoRefill.findReplacement(mc.player,previous,0,false,false)!=-1
                || mc.player.inventoryMenu.findSlot(inventory,10).isEmpty())
                throw new IllegalStateException("Auto refill chose an invalid inventory stack");
        } finally {for(int i=0;i<slots.length;i++)inventory.setItem(slots[i],saved[i]);}
    }
}
