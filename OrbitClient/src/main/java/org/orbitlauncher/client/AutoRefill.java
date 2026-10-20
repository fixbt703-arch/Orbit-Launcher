package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.inventory.ContainerInput;
import net.minecraft.world.item.BlockItem;
import net.minecraft.world.item.ItemStack;
import org.orbitlauncher.client.mixin.ClientLevelPredictionAccessor;
import org.orbitlauncher.client.mixin.RefillHandAccessor;

/** Uses vanilla's predicted SWAP immediately after the final successful placement. */
public final class AutoRefill {
    private static int pendingSlot=-1,previousSequence=-1;
    private static ItemStack placedStack=ItemStack.EMPTY;
    private static Player pendingPlayer;
    private AutoRefill() { }

    public static void recordUse(Player player,InteractionHand hand) {
        Minecraft mc=Minecraft.getInstance();
        if(player!=mc.player||hand!=InteractionHand.MAIN_HAND||!OrbitClient.enabled(Module.AUTO_REFILL)
            ||pendingSlot>=0||!available(mc,player)||mc.level==null)return;
        ItemStack selected=player.getInventory().getSelectedItem();
        if(selected.getCount()!=1||!(selected.getItem() instanceof BlockItem))return;
        pendingSlot=player.getInventory().getSelectedSlot();pendingPlayer=player;
        placedStack=selected.copyWithCount(1);
        previousSequence=((ClientLevelPredictionAccessor)mc.level).orbit$predictions().currentSequence();
    }
    private static boolean available(Minecraft mc,Player player){
        return mc.gui.screen()==null&&mc.gameMode!=null&&!mc.gameMode.isSpectator()&&mc.getConnection()!=null
            &&!player.isDeadOrDying()&&!player.getAbilities().instabuild
            &&player.containerMenu==player.inventoryMenu&&player.inventoryMenu.getCarried().isEmpty();
    }
    public static void finishUse(Player player,InteractionHand hand,InteractionResult result){
        if(pendingSlot<0||player!=pendingPlayer||hand!=InteractionHand.MAIN_HAND)return;
        Minecraft mc=Minecraft.getInstance();int target=pendingSlot,sequence=previousSequence;ItemStack previous=placedStack;
        // Consume the request before invoking vanilla: packet corrections never replay a swap.
        reset();
        if(player!=mc.player||!OrbitClient.enabled(Module.AUTO_REFILL)||!available(mc,player)||mc.level==null
            ||result==null||!result.consumesAction()||player.getInventory().getSelectedSlot()!=target
            ||!player.getInventory().getItem(target).isEmpty()
            ||((ClientLevelPredictionAccessor)mc.level).orbit$predictions().currentSequence()<=sequence)return;
        int source=findReplacement(player,previous,target,true,false);if(source<0)return;
        var mapped=player.inventoryMenu.findSlot(player.getInventory(),source);if(mapped.isEmpty())return;
        // useItemOn has already queued the placement. The ordered connection sends this
        // ordinary inventory click after it; vanilla predicts and reconciles both slots.
        // Waiting for an ACK here leaves the hotbar and first-person hand empty for an RTT.
        mc.gameMode.handleContainerInput(player.inventoryMenu.containerId,mapped.getAsInt(),target,ContainerInput.SWAP,player);
        var replacement=player.getMainHandItem();
        // The renderer may still hold the consumed (now empty) stack until its next tick.
        // Continue with the actual replacement stack, preserving the existing equip height.
        if(ItemStack.isSameItemSameComponents(previous,replacement))
            ((RefillHandAccessor)mc.gameRenderer.itemInHandRenderer).orbit$heldStack(replacement);
    }
    public static void tick(Minecraft mc){reset();}

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

    public static void reset(){pendingSlot=previousSequence=-1;pendingPlayer=null;placedStack=ItemStack.EMPTY;}
}
