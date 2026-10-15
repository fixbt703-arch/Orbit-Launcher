package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.CameraType;
import net.minecraft.client.renderer.culling.Frustum;
import net.minecraft.gizmos.*;
import net.minecraft.world.entity.*;
import net.minecraft.world.entity.monster.Enemy;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.phys.*;

/** Cosmetic bounds only: never alters collision, entity size or attack reach. */
public final class Hitboxes {
    public static int renderedForTest;
    public static boolean accepts(Entity entity,ClientConfig.State s){
        if(entity instanceof Player)return s.hitboxPlayers;
        if(entity instanceof Enemy)return s.hitboxHostile;
        if(entity instanceof LivingEntity)return s.hitboxPassive;
        return s.hitboxOther;
    }
    public static void extract(Frustum frustum,double cx,double cy,double cz,float partial){
        renderedForTest=0;var mc=Minecraft.getInstance();
        if(mc.level==null||mc.player==null||!OrbitClient.enabled(Module.HITBOX))return;
        var s=OrbitClient.config.state(Module.HITBOX);
        boolean firstPerson=mc.options.getCameraType()==CameraType.FIRST_PERSON;
        if(s.hitboxHideMenus&&mc.gui.screen()!=null||s.hitboxCrouchOnly&&!mc.player.isCrouching()
            ||s.hitboxCameraMode==1&&!firstPerson||s.hitboxCameraMode==2&&firstPerson)return;
        if(s.hitboxWeaponOnly){var item=mc.player.getMainHandItem();if(!item.is(net.minecraft.tags.ItemTags.MELEE_WEAPON_ENCHANTABLE))return;}
        Entity target=attackTarget(mc);
        for(Entity e:mc.level.entitiesForRendering()){
            if(!accepts(e,s)||e.isRemoved()||e.isInvisible()&&!s.hitboxInvisible)continue;
            double distance=e.distanceTo(mc.player);
            if(distance>s.hitboxDistance||distance<s.hitboxMinDistance)continue;
            if(s.hitboxHideAllies&&e!=mc.player&&mc.player.isAlliedTo(e))continue;
            if(e==mc.getCameraEntity()&&(!s.hitboxSelf||firstPerson))continue;
            boolean selected=e==target;if(s.hitboxOnlyTarget&&!selected)continue;
            AABB box=e.getBoundingBox().move(e.getPosition(partial).subtract(e.position()));if(!frustum.isVisible(box))continue;
            boolean highlight=selected&&s.hitboxHighlight;
            float fade=fade(s,distance),lineWidth=highlight&&s.hitboxTargetWidthOverride?s.hitboxTargetWidth:s.hitboxWidth;
            int rgb=highlight?s.hitboxTargetColor:baseColor(e,mc.player,s),color=argb(rgb,(highlight?s.hitboxTargetOpacity:s.hitboxOpacity)*fade);
            boolean targetFill=highlight&&s.hitboxTargetFillOverride;
            if(s.hitboxFill||targetFill){int fillRgb=targetFill?s.hitboxTargetFillColor:s.hitboxOwnFillColor?s.hitboxFillColor:rgb;
                property(Gizmos.cuboid(box,GizmoStyle.fill(argb(fillRgb,(targetFill?s.hitboxTargetFillOpacity:s.hitboxFillOpacity)*fade))),s);}
            int style=highlight&&s.hitboxTargetStyleOverride?s.hitboxTargetStyle:s.hitboxStyle;
            if(s.hitboxOutline){
                if(style==0&&s.hitboxTop&&s.hitboxBottom&&s.hitboxVertical)property(Gizmos.cuboid(box,GizmoStyle.stroke(color,lineWidth)),s);
                else edges(box,color,lineWidth,style,s);
            }
            if(s.hitboxEyes&&e instanceof LivingEntity){double eye=box.minY+e.getEyeHeight();property(Gizmos.cuboid(new AABB(box.minX,eye-.005,box.minZ,box.maxX,eye+.005,box.maxZ),GizmoStyle.stroke(color,lineWidth)),s);}
            if(s.hitboxLook){Vec3 eye=e.getPosition(partial).add(0,e.getEyeHeight(),0),end=eye.add(e.getViewVector(partial).scale(s.hitboxLookLength));property(s.hitboxLookArrow?Gizmos.arrow(eye,end,color,lineWidth):Gizmos.line(eye,end,color,lineWidth),s);}
            renderedForTest++;
        }
    }
    static int baseColor(Entity e,Player viewer,ClientConfig.State s){
        if(s.hitboxHurtTint&&e instanceof LivingEntity living&&living.hurtTime>0)return s.hitboxHurtColor;
        if(s.hitboxFriendlyTint&&viewer.isAlliedTo(e))return s.hitboxFriendlyColor;
        if(s.hitboxColorMode==1&&e instanceof LivingEntity living){float health=Math.clamp(living.getHealth()/Math.max(1,living.getMaxHealth()),0,1);return java.awt.Color.HSBtoRGB(health/3f,.8f,1)&0xFFFFFF;}
        return s.hitboxColor;
    }
    static float fade(ClientConfig.State s,double distance){
        if(!s.hitboxFade)return 1;
        double start=s.hitboxDistance*s.hitboxFadeStart;
        return (float)Math.clamp((s.hitboxDistance-distance)/(s.hitboxDistance-start),0,1);
    }
    private static Entity attackTarget(Minecraft mc){
        // Use the game's unobstructed pick, independently of the hitbox visibility range.
        // Item-specific attack ranges can be shorter than the interaction raycast.
        if(mc.player.isSpectator()||!(mc.hitResult instanceof EntityHitResult hit))return null;
        return !hit.getEntity().isRemoved()&&mc.player.getAttackRangeWith(mc.player.getMainHandItem()).isInRange(mc.player,hit.getLocation())?hit.getEntity():null;
    }
    private static void property(GizmoProperties p,ClientConfig.State s){if(s.hitboxThroughWalls)p.setAlwaysOnTop();}
    private static void edges(AABB b,int color,float width,int style,ClientConfig.State s){
        Vec3[] v={new Vec3(b.minX,b.minY,b.minZ),new Vec3(b.maxX,b.minY,b.minZ),new Vec3(b.maxX,b.minY,b.maxZ),new Vec3(b.minX,b.minY,b.maxZ),new Vec3(b.minX,b.maxY,b.minZ),new Vec3(b.maxX,b.maxY,b.minZ),new Vec3(b.maxX,b.maxY,b.maxZ),new Vec3(b.minX,b.maxY,b.maxZ)};
        int[][] edges={{0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7}};
        for(int n=0;n<edges.length;n++){
            if(n<4&&!s.hitboxBottom||n>=4&&n<8&&!s.hitboxTop||n>=8&&!s.hitboxVertical)continue;
            Vec3 a=v[edges[n][0]],z=v[edges[n][1]];
            if(style==0)property(Gizmos.line(a,z,color,width),s);
            else if(style==1){property(Gizmos.line(a,a.lerp(z,s.hitboxCornerLength),color,width),s);property(Gizmos.line(a.lerp(z,1-s.hitboxCornerLength),z,color,width),s);}
            else for(int i=0;i<s.hitboxDashCount;i+=2)property(Gizmos.line(a.lerp(z,i/(double)s.hitboxDashCount),a.lerp(z,(i+1)/(double)s.hitboxDashCount),color,width),s);
        }
    }
    static int argb(int rgb,float opacity){return Math.round(Math.clamp(opacity,0,1)*255)<<24|rgb&0xFFFFFF;}
    private Hitboxes(){}
}
