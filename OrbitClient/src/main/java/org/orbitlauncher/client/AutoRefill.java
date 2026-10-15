package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.inventory.ContainerInput;
import net.minecraft.world.item.BlockItem;
import net.minecraft.world.item.ItemStack;
import net.minecraft.network.HashedStack;
import net.minecraft.network.protocol.game.ServerboundContainerClickPacket;
import it.unimi.dsi.fastutil.ints.Int2ObjectMaps;

/** Server-synchronised hotbar refill after the final block in the selected stack is placed. */
public final class AutoRefill {
    private static int pendingSlot=-1;
    private static int pendingTicks;
    private static ItemStack placedStack=ItemStack.EMPTY;
    private static Player pendingPlayer;
    private static boolean depleted,swapping,targetConfirmed,sourceConfirmed;
    private static int sourceSlot=-1;
    private static ItemStack replacement=ItemStack.EMPTY;
    private AutoRefill() { }

    public static void recordUse(Player player,InteractionHand hand) {
        Minecraft mc=Minecraft.getInstance();
        if(player!=mc.player || hand!=InteractionHand.MAIN_HAND || !OrbitClient.enabled(Module.AUTO_REFILL)
            || pendingSlot>=0 || mc.gui.screen()!=null || player.containerMenu!=player.inventoryMenu
            || !player.inventoryMenu.getCarried().isEmpty() || mc.gameMode==null || mc.gameMode.isSpectator())return;
        ItemStack selected=player.getInventory().getSelectedItem();
        if(selected.getCount()!=1 || !(selected.getItem() instanceof BlockItem))return;
        pendingSlot=player.getInventory().getSelectedSlot();
        pendingPlayer=player;
        placedStack=selected.copyWithCount(1);
        pendingTicks=80;
    }

    public static void tick(Minecraft mc) {
        if(!OrbitClient.enabled(Module.AUTO_REFILL) || mc.player==null || mc.gameMode==null || mc.player!=pendingPlayer) {reset();return;}
        if(pendingTicks<=0)return;
        if(mc.gui.screen()!=null || mc.gameMode.isSpectator() || mc.player.isDeadOrDying()
            || mc.player.containerMenu!=mc.player.inventoryMenu || !mc.player.inventoryMenu.getCarried().isEmpty()) {reset();return;}
        var inventory=mc.player.getInventory();
        if(inventory.getSelectedSlot()!=pendingSlot) {reset();return;}
        if(--pendingTicks<=0){reset();return;}
        if(swapping || !depleted || !inventory.getSelectedItem().isEmpty())return;

        int source=findReplacement(mc.player,placedStack,pendingSlot,true,false);
        if(source>=0) {
            var menuSlot=mc.player.inventoryMenu.findSlot(inventory,source);
            if(menuSlot.isPresent() && mc.getConnection()!=null){
                sourceSlot=source;replacement=inventory.getItem(source).copy();swapping=true;pendingTicks=80;
                // Do not call handleContainerInput: its local prediction can race a delayed
                // placement correction and temporarily erase both visible stacks. Empty
                // changedSlots asks vanilla to return the authoritative result of the swap.
                var menu=mc.player.inventoryMenu;
                mc.getConnection().send(new ServerboundContainerClickPacket(menu.containerId,menu.getStateId(),
                    (short)menuSlot.getAsInt(),(byte)pendingSlot,ContainerInput.SWAP,Int2ObjectMaps.emptyMap(),
                    HashedStack.create(menu.getCarried(),mc.getConnection().decoratedHashOpsGenenerator())));
            }else reset();
        }else reset();
    }

    /** Called after vanilla applies an inventory update on the client thread. */
    public static void serverSlot(int slot,ItemStack contents){
        if(pendingSlot<0 || pendingPlayer!=Minecraft.getInstance().player)return;
        if(!swapping){
            if(slot!=pendingSlot)return;
            depleted=contents.isEmpty();
            if(!contents.isEmpty()&&!ItemStack.isSameItemSameComponents(contents,placedStack))reset();
            return;
        }
        if(slot==pendingSlot){
            if(!ItemStack.matches(contents,replacement)){reset();return;}
            targetConfirmed=true;
        }else if(slot==sourceSlot){
            if(!contents.isEmpty()){reset();return;}
            sourceConfirmed=true;
        }
        if(targetConfirmed&&sourceConfirmed)reset();
    }
    public static void serverMenuSlot(int containerId,int menuSlot,ItemStack contents){
        if(pendingSlot<0 || pendingPlayer!=Minecraft.getInstance().player
            || containerId!=pendingPlayer.inventoryMenu.containerId)return;
        var inventory=pendingPlayer.getInventory();
        for(int slot=0;slot<36;slot++){
            var mapped=pendingPlayer.inventoryMenu.findSlot(inventory,slot);
            if(mapped.isPresent()&&mapped.getAsInt()==menuSlot){serverSlot(slot,contents);return;}
        }
    }

    static int findReplacement(Player player,ItemStack previous,int selectedSlot,boolean preferSame,boolean allowOther) {
        int same=-1,other=-1,sameCount=-1,otherCount=-1;
        for(int slot=0;slot<36;slot++) {
            if(slot==selectedSlot)continue;
            ItemStack candidate=player.getInventory().getItem(slot);
            if(candidate.isEmpty() || !(candidate.getItem() instanceof BlockItem))continue;
            if(ItemStack.isSameItemSameComponents(previous,candidate)) {
                if(candidate.getCount()>sameCount) {same=slot;sameCount=candidate.getCount();}
            } else if(candidate.getCount()>otherCount) {other=slot;otherCount=candidate.getCount();}
        }
        if(preferSame && same>=0)return same;
        if(allowOther)return other>=0?other:same;
        return preferSame?same:-1;
    }

    public static void reset() {
        pendingSlot=sourceSlot=-1;pendingTicks=0;placedStack=replacement=ItemStack.EMPTY;
        pendingPlayer=null;depleted=swapping=targetConfirmed=sourceConfirmed=false;
    }
}
