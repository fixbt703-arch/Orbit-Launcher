package org.orbitlauncher.client;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.client.Minecraft;
import net.minecraft.client.CameraType;
import net.minecraft.client.renderer.culling.Frustum;
import net.minecraft.core.BlockPos;
import net.minecraft.core.component.DataComponents;
import net.minecraft.gizmos.*;
import net.minecraft.tags.FluidTags;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.entity.projectile.ProjectileUtil;
import net.minecraft.world.item.*;
import net.minecraft.world.item.component.ChargedProjectiles;
import net.minecraft.world.item.enchantment.EnchantmentHelper;
import net.minecraft.world.level.ClipContext;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.phys.*;
import net.minecraft.world.phys.shapes.CollisionContext;
import static org.orbitlauncher.client.TrajectoryPhysics.Kind;

/** Read-only preview of the local player's next projectile. Never changes game input or sends packets. */
public final class Trajectories {
    static final int MAX_TICKS=200;
    record Launch(Kind kind,Vec3 start,Vec3 velocity){}
    record Path(List<Vec3> points,HitResult hit){}
    record Fluid(boolean water,Vec3 push,boolean known){}
    static int renderedForTest;

    static Launch launch(Player p,ClientConfig.State s,float partial){
        if(p.isSpectator()||!p.isAlive())return null;
        InteractionHand hand=p.isUsingItem()?p.getUsedItemHand():InteractionHand.MAIN_HAND;
        ItemStack stack=p.getItemInHand(hand);
        if(!p.isUsingItem()&&kind(stack,null)==null&&s.trajectoryOffhand){
            if(!stack.isEmpty()&&stack.getUseAnimation()!=net.minecraft.world.item.ItemUseAnimation.NONE)return null;
            if(stack.getItem() instanceof BlockItem&&Minecraft.getInstance().hitResult instanceof BlockHitResult block&&block.getType()!=HitResult.Type.MISS)return null;
            hand=InteractionHand.OFF_HAND;stack=p.getItemInHand(hand);
        }
        Kind kind=kind(stack,s);if(kind==null||hand==InteractionHand.OFF_HAND&&!s.trajectoryOffhand||p.getCooldowns().isOnCooldown(stack))return null;
        boolean using=p.isUsingItem()&&p.getUsedItemHand()==hand;
        float speed,elevation=0;
        boolean crossbow=stack.is(Items.CROSSBOW);
        if(stack.is(Items.BOW)){
            if(p.getProjectile(stack).isEmpty()&&!p.hasInfiniteMaterials()||!using&&s.trajectoryChargeOnly)return null;
            float power=using?BowItem.getPowerForTime(p.getTicksUsingItem()):1;
            if(power<.1)return null;speed=power*3;
        }else if(crossbow){
            var loaded=stack.getOrDefault(DataComponents.CHARGED_PROJECTILES,ChargedProjectiles.EMPTY);
            if(loaded.isEmpty()||loaded.contains(Items.FIREWORK_ROCKET))return null;
            speed=3.15f; // Central arrow only; enchantment spread is server-side.
        }else if(kind==Kind.TRIDENT){
            if(EnchantmentHelper.getTridentSpinAttackStrength(stack,p)>0||stack.nextDamageWillBreak()||using&&p.getTicksUsingItem()<10||!using&&s.trajectoryChargeOnly)return null;
            speed=2.5f;
        }else if(kind==Kind.POTION){speed=.5f;elevation=-20;}
        else if(kind==Kind.EXPERIENCE){speed=.7f;elevation=-20;}
        else speed=1.5f;
        Vec3 velocity=TrajectoryPhysics.direction(p.getXRot(),p.getYRot(),elevation).scale(speed);
        if(!crossbow)velocity=TrajectoryPhysics.inherit(velocity,p.getKnownMovement(),p.onGround());
        return new Launch(kind,p.getEyePosition(partial).add(0,-.10000000149011612,0),velocity);
    }
    static Kind kind(ItemStack stack,ClientConfig.State s){
        if(stack.is(Items.BOW)&&(s==null||s.trajectoryBow)||stack.is(Items.CROSSBOW)&&(s==null||s.trajectoryCrossbow))return Kind.ARROW;
        if(stack.is(Items.ENDER_PEARL)&&(s==null||s.trajectoryPearl))return Kind.PEARL;
        if(stack.is(Items.SNOWBALL)&&(s==null||s.trajectorySnowball))return Kind.SNOWBALL;
        if((stack.is(Items.EGG)||stack.is(Items.BLUE_EGG)||stack.is(Items.BROWN_EGG))&&(s==null||s.trajectoryEgg))return Kind.EGG;
        if((stack.is(Items.SPLASH_POTION)||stack.is(Items.LINGERING_POTION))&&(s==null||s.trajectoryPotion))return Kind.POTION;
        if(stack.is(Items.EXPERIENCE_BOTTLE)&&(s==null||s.trajectoryExperience))return Kind.EXPERIENCE;
        if(stack.is(Items.TRIDENT)&&(s==null||s.trajectoryTrident))return Kind.TRIDENT;
        return null;
    }
    static Path predict(Minecraft mc,Launch launch,int range){
        var points=new ArrayList<Vec3>();Vec3 pos=launch.start,velocity=launch.velocity;points.add(pos);
        if(!finite(pos)||!finite(velocity)||velocity.lengthSqr()>64)return new Path(List.of(),null);
        double travelled=0;boolean water=false,leftOwner=false; // Newly spawned entities have not sampled fluids yet.
        for(int i=0;i<MAX_TICKS;i++){
            Vec3 delta=TrajectoryPhysics.move(velocity,launch.kind,water);
            double length=delta.length();if(length<1e-6||!Double.isFinite(length))break;
            boolean limit=travelled+length>range;
            if(limit)delta=delta.scale(Math.max(0,range-travelled)/length);
            Vec3 next=pos.add(delta);
            if(!knownSegment(mc,pos,next))break;
            var block=mc.level.clipIncludingBorder(new ClipContext(pos,next,ClipContext.Block.COLLIDER,ClipContext.Fluid.NONE,CollisionContext.positionContext(pos.y)));
            Vec3 end=block.getType()==HitResult.Type.MISS?next:block.getLocation();
            if(!launch.kind.beforeMove&&!leftOwner)leftOwner=leftOwner(mc,pos,velocity.scale(water?launch.kind.waterDrag:1),launch.kind);
            boolean ownerCanBeHit=leftOwner;
            var entity=ProjectileUtil.getEntityHitResult(mc.level,null,pos,end,new AABB(pos,end).inflate(1),
                e->!e.isSpectator()&&e.canBeHitByProjectile()&&!e.isRemoved()&&(ownerCanBeHit||!mc.player.isPassengerOfSameVehicle(e))
                    &&(launch.kind.beforeMove||!(e instanceof Player player)||mc.player.canHarmPlayer(player)),Math.clamp((i-1)/20f,0,.3f));
            HitResult hit=entity!=null?entity:block.getType()!=HitResult.Type.MISS?block:null;
            points.add(hit==null?next:hit.getLocation());
            if(hit!=null)return new Path(List.copyOf(points),hit);
            if(limit)break;
            var fluid=fluid(mc,next,launch.kind);if(!fluid.known)break;
            velocity=TrajectoryPhysics.after(velocity,launch.kind,water,fluid.water);
            if(launch.kind.beforeMove&&!leftOwner)leftOwner=leftOwner(mc,next,velocity,launch.kind);
            velocity=velocity.add(fluid.push);water=fluid.water;pos=next;travelled+=length;
        }
        return new Path(List.copyOf(points),null);
    }
    private static boolean finite(Vec3 p){return Double.isFinite(p.x)&&Double.isFinite(p.y)&&Double.isFinite(p.z);}
    private static boolean leftOwner(Minecraft mc,Vec3 pos,Vec3 velocity,Kind kind){
        double half=kind.beforeMove?.125:.25;
        var swept=new AABB(pos.x-half,pos.y,pos.z-half,pos.x+half,pos.y+half*2,pos.z+half).expandTowards(velocity).inflate(1);
        return mc.player.getRootVehicle().getSelfAndPassengers().filter(e->e.isPickable()).noneMatch(e->swept.intersects(e.getBoundingBox()));
    }
    static Fluid fluid(Minecraft mc,Vec3 pos,Kind kind){
        double half=kind.beforeMove?.125:.25;
        var box=new AABB(pos.x-half,pos.y,pos.z-half,pos.x+half,pos.y+half*2,pos.z+half).deflate(.001);
        int minX=(int)Math.floor(box.minX),minY=(int)Math.floor(box.minY),minZ=(int)Math.floor(box.minZ);
        int maxX=(int)Math.ceil(box.maxX),maxY=(int)Math.ceil(box.maxY),maxZ=(int)Math.ceil(box.maxZ);
        for(int x:new int[]{minX-1,maxX+1})for(int z:new int[]{minZ-1,maxZ+1})
            if(!mc.level.hasChunkAt(new BlockPos(x,minY,z)))return new Fluid(false,Vec3.ZERO,false);
        double height=0;Vec3 flow=Vec3.ZERO;int count=0;
        for(int x=minX;x<maxX;x++)for(int y=minY;y<maxY;y++)for(int z=minZ;z<maxZ;z++){
            var bp=new BlockPos(x,y,z);var state=mc.level.getFluidState(bp);
            if(state.is(FluidTags.LAVA))return new Fluid(false,Vec3.ZERO,false);
            if(!state.is(FluidTags.WATER))continue;
            double surface=y+state.getHeight(mc.level,bp);if(surface<box.minY)continue;
            height=Math.max(height,surface-pos.y);
            Vec3 current=state.getFlow(mc.level,bp);if(height<.4)current=current.scale(height);
            flow=flow.add(current);count++;
        }
        return new Fluid(height>0,count>0&&flow.lengthSqr()>=9.999999747378752E-6?flow.normalize().scale(.014):Vec3.ZERO,true);
    }
    private static boolean knownSegment(Minecraft mc,Vec3 a,Vec3 b){
        int samples=Math.max(1,(int)Math.ceil(a.distanceTo(b)*2));
        for(int i=0;i<=samples;i++){
            var p=BlockPos.containing(a.lerp(b,i/(double)samples));
            if(!mc.level.hasChunkAt(p)||p.getY()<mc.level.getMinY()||p.getY()>=mc.level.getMaxY())return false;
            var state=mc.level.getBlockState(p);
            // End uncertain predictions at mechanics that can alter flight or change dimension.
            if(state.is(Blocks.BUBBLE_COLUMN)||state.is(Blocks.NETHER_PORTAL)||state.is(Blocks.END_PORTAL)||state.is(Blocks.END_GATEWAY))return false;
        }
        return true;
    }
    public static void extract(Frustum frustum,float partial){
        renderedForTest=0;var mc=Minecraft.getInstance();
        if(mc.level==null||mc.player==null||!OrbitClient.enabled(Module.TRAJECTORIES)||mc.gui.hud.isHidden())return;
        var s=OrbitClient.config.state(Module.TRAJECTORIES);
        if(s.trajectoryHideMenus&&mc.gui.screen()!=null||!s.trajectoryThirdPerson&&mc.options.getCameraType()!=CameraType.FIRST_PERSON)return;
        var launch=launch(mc.player,s,partial);if(launch==null)return;
        var path=predict(mc,launch,s.trajectoryRange);
        int rgb=s.trajectoryTheme?OrbitTheme.options().primary():s.trajectoryColor;
        for(int i=1;i<path.points.size();i++){
            Vec3 a=path.points.get(i-1),b=path.points.get(i);
            if(!frustum.isVisible(new AABB(a,b).inflate(.1)))continue;
            float fade=s.trajectoryFade?(float)Math.clamp(i/5d,.15,1):1;
            Gizmos.line(a,b,Hitboxes.argb(rgb,s.trajectoryOpacity*fade),s.trajectoryWidth);renderedForTest++;
        }
        if(s.trajectoryMarker&&path.hit!=null)marker(path.hit,launch.velocity,rgb,s);
    }
    private static void marker(HitResult hit,Vec3 velocity,int rgb,ClientConfig.State s){
        Vec3 normal=hit instanceof BlockHitResult block?Vec3.atLowerCornerOf(block.getDirection().getUnitVec3i()):velocity.normalize().scale(-1);
        Vec3 center=hit.getLocation().add(normal.scale(.012));
        Vec3 u=normal.cross(Math.abs(normal.y)>.9?new Vec3(1,0,0):new Vec3(0,1,0)).normalize();
        Vec3 v=normal.cross(u).normalize();double radius=s.trajectoryMarkerSize;
        int color=Hitboxes.argb(rgb,s.trajectoryOpacity);
        for(int i=0;i<24;i++){
            double a=i*Math.PI/12,b=(i+1)*Math.PI/12;
            Gizmos.line(center.add(u.scale(Math.cos(a)*radius)).add(v.scale(Math.sin(a)*radius)),center.add(u.scale(Math.cos(b)*radius)).add(v.scale(Math.sin(b)*radius)),color,s.trajectoryWidth);
        }
        Gizmos.line(center.add(u.scale(-radius*.35)),center.add(u.scale(radius*.35)),color,s.trajectoryWidth);
        Gizmos.line(center.add(v.scale(-radius*.35)),center.add(v.scale(radius*.35)),color,s.trajectoryWidth);
    }
    private Trajectories(){}
}
