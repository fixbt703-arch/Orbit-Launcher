package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.world.item.Items;

/** Opt-in real client/server checks in a disposable development world. */
final class RefillSmoke {
    private static int ticks,frames;
    private static boolean checking;
    static void tick(Minecraft mc){
        ticks++;int phase=ticks%80;
        if(ticks==10)CriticalSmoke.verify(mc);
        if(ticks<=240){
            if(phase==20)WorldSmoke.prepareRefill(mc);
            if(phase==40){WorldSmoke.consumeRefill(mc);checking=true;}
            if(phase==0){checking=false;WorldSmoke.finishRefill(mc);}
        }
        if(ticks==245)net.minecraft.client.Screenshot.grab(mc,false);
        if(ticks==250){var panel=new ModuleScreen(new OrbitScreen(),Module.CROSSHAIR);panel.selectTabForTest(3);mc.gui.setScreen(panel);}
        if(ticks==275)net.minecraft.client.Screenshot.grab(mc,false);
        if(ticks==285)mc.gui.setScreen(new CrosshairScreen(new OrbitScreen()));
        if(ticks==310)net.minecraft.client.Screenshot.grab(mc,false);
        if(ticks==320){OrbitClient.LOG.info("Stack Refill integration checks completed: 3 real server transactions, 6 blocks placed, {} frames without an empty hand, matching inventories, no duplicate stacks",frames);mc.stop();}
    }
    static void frame(Minecraft mc){
        if(!checking)return;frames++;
        if(!mc.player.getMainHandItem().is(Items.STONE)||mc.player.getMainHandItem().getCount()!=31)throw new IllegalStateException("Refill inventory blink after server correction");
        if(!((org.orbitlauncher.client.mixin.RefillHandAccessor)mc.gameRenderer.itemInHandRenderer).orbit$heldStack().is(Items.STONE))throw new IllegalStateException("Refill first-person renderer retained an empty stack");
    }
}
