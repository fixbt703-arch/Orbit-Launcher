package org.orbitlauncher.client;

import java.util.List;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.culling.Frustum;
import net.minecraft.gizmos.*;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.item.ItemStack;

/** Opt-in development checks of the extracted world geometry and saved settings. */
final class HitboxSettingsSmoke {
    private static List<SimpleGizmoCollector.GizmoInstance> collect(Frustum f){
        var collector=new SimpleGizmoCollector();
        try(var ignored=Gizmos.withCollector(collector)){Hitboxes.extract(f,0,0,0,1);}
        return collector.getGizmos();
    }
    private static void require(boolean value,String message){if(!value)throw new IllegalStateException("Hitbox settings: "+message);}
    static void verify(Minecraft mc,Entity target,Frustum f){
        var original=OrbitClient.config.state(Module.HITBOX);var oldTarget=mc.crosshairPickEntity;
        var s=ClientConfig.defaults(Module.HITBOX);s.enabled=true;s.hitboxOnlyTarget=true;s.hitboxDistance=128;
        OrbitClient.config.modules.put(Module.HITBOX,s);mc.crosshairPickEntity=target;
        try{
            s.hitboxTargetWidthOverride=true;s.hitboxTargetWidth=3;s.hitboxTargetStyleOverride=true;s.hitboxTargetStyle=1;s.hitboxCornerLength=.1f;
            var corners=collect(f);require(corners.size()==24,"target corners were not applied");
            var corner=(LineGizmo)corners.getFirst().gizmo();
            require(corner.width()==3&&Math.abs(corner.start().distanceTo(corner.end())-(target.getBoundingBox().maxX-target.getBoundingBox().minX)*.1)<.001,"corner length or target width is wrong");
            s.hitboxTargetStyle=2;s.hitboxDashCount=8;require(collect(f).size()==48,"dash density is wrong");
            s.hitboxTargetStyle=0;s.hitboxTop=s.hitboxBottom=false;
            var vertical=collect(f);require(vertical.size()==4&&vertical.stream().allMatch(g->g.gizmo() instanceof LineGizmo l&&l.start().x==l.end().x&&l.start().z==l.end().z),"edge selection is wrong");
            s.hitboxOutline=false;require(collect(f).isEmpty(),"outline did not disable");
            s.hitboxFill=true;s.hitboxOwnFillColor=true;s.hitboxFillColor=0x123456;s.hitboxFillOpacity=.25f;
            var fill=(CuboidGizmo)collect(f).getFirst().gizmo();require(fill.style().fill()==0x40123456,"independent fill RGB/opacity is wrong");
            s.hitboxFill=false;s.hitboxTargetFillOverride=true;s.hitboxTargetFillColor=0x654321;s.hitboxTargetFillOpacity=.2f;
            fill=(CuboidGizmo)collect(f).getFirst().gizmo();require(fill.style().fill()==0x33654321,"target fill did not override disabled base fill");
            s.hitboxHighlight=false;require(collect(f).isEmpty(),"disabled highlighting still uses target fill");
            s.hitboxHighlight=true;s.hitboxTargetFillOverride=false;s.hitboxEyes=s.hitboxLook=s.hitboxLookArrow=true;s.hitboxLookLength=3.25f;
            var look=collect(f);require(look.stream().anyMatch(g->g.gizmo() instanceof ArrowGizmo a&&Math.abs(a.start().distanceTo(a.end())-3.25)<.001),"look arrow length is wrong");
            s.hitboxEyes=s.hitboxLook=false;s.hitboxOutline=s.hitboxTop=s.hitboxBottom=true;
            s.hitboxMinDistance=(int)Math.ceil(target.distanceTo(mc.player))+1;require(collect(f).isEmpty(),"minimum distance did not filter");s.hitboxMinDistance=0;
            s.hitboxFade=true;s.hitboxFadeStart=.1f;s.hitboxDistance=20;
            var faded=(CuboidGizmo)collect(f).getFirst().gizmo();
            require(faded.style().stroke()>>>24==Math.round(s.hitboxTargetOpacity*Hitboxes.fade(s,target.distanceTo(mc.player))*255),"fade did not affect rendered alpha");
            require(Hitboxes.fade(s,20)==0&&Hitboxes.fade(s,1)==1&&Math.abs(Hitboxes.fade(s,11)-.5)<.001,"fade range is wrong");
            s.hitboxFade=false;s.hitboxHideMenus=true;require(collect(f).isEmpty(),"menu visibility filter failed");s.hitboxHideMenus=false;
            s.hitboxCameraMode=mc.options.getCameraType()==net.minecraft.client.CameraType.FIRST_PERSON?2:1;require(collect(f).isEmpty(),"camera view filter failed");s.hitboxCameraMode=0;
            if(!mc.player.isCrouching()){s.hitboxCrouchOnly=true;require(collect(f).isEmpty(),"sneaking condition failed");s.hitboxCrouchOnly=false;}
            var inventory=mc.player.getInventory();int slot=inventory.getSelectedSlot();var held=inventory.getSelectedItem().copy();
            try{s.hitboxWeaponOnly=true;inventory.setItem(slot,ItemStack.EMPTY);require(collect(f).isEmpty(),"weapon condition accepted an empty hand");inventory.setItem(slot,held);require(!collect(f).isEmpty(),"weapon condition rejected the test sword");}finally{inventory.setItem(slot,held);s.hitboxWeaponOnly=false;}
            if(target instanceof LivingEntity living){float health=living.getHealth();int hurt=living.hurtTime;
                try{s.hitboxColorMode=1;living.setHealth(living.getMaxHealth());int healthy=Hitboxes.baseColor(living,mc.player,s);living.setHealth(1);int low=Hitboxes.baseColor(living,mc.player,s);require(healthy!=low&&(low>>16&255)>(low>>8&255),"health color did not change");s.hitboxHurtTint=true;s.hitboxHurtColor=0xABCDEF;living.hurtTime=5;require(Hitboxes.baseColor(living,mc.player,s)==0xABCDEF,"damage color did not override health");}
                finally{living.setHealth(health);living.hurtTime=hurt;s.hitboxHurtTint=false;s.hitboxColorMode=0;}
            }
            var score=mc.level.getScoreboard();var oldPlayerTeam=mc.player.getTeam();var oldEntityTeam=target.getTeam();var team=score.addPlayerTeam("orbit_hitbox_smoke");
            try{score.addPlayerToTeam(mc.player.getScoreboardName(),team);score.addPlayerToTeam(target.getScoreboardName(),team);s.hitboxFriendlyTint=true;s.hitboxFriendlyColor=0x765432;
                require(Hitboxes.baseColor(target,mc.player,s)==0x765432,"allied color failed");s.hitboxHideAllies=true;require(collect(f).isEmpty(),"allied filter failed");}
            finally{score.removePlayerTeam(team);if(oldPlayerTeam!=null)score.addPlayerToTeam(mc.player.getScoreboardName(),oldPlayerTeam);if(oldEntityTeam!=null)score.addPlayerToTeam(target.getScoreboardName(),oldEntityTeam);s.hitboxHideAllies=false;}
            OrbitClient.save();var saved=ClientConfig.load().state(Module.HITBOX);
            require(saved.hitboxTargetWidth==3&&saved.hitboxDashCount==8&&saved.hitboxLookLength==3.25f&&saved.hitboxTargetFillColor==0x654321&&saved.hitboxFriendlyColor==0x765432,"new settings did not survive save/load");
            OrbitClient.LOG.info("Hitbox settings: target overrides, corner/dash geometry, edge masks, fill, arrows, fade, visibility, health, damage, allies and persistence passed");
        }finally{OrbitClient.config.modules.put(Module.HITBOX,original);mc.crosshairPickEntity=oldTarget;OrbitClient.save();}
    }
    private HitboxSettingsSmoke(){}
}
