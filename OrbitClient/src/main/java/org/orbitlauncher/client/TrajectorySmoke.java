package org.orbitlauncher.client;

import java.util.List;
import net.minecraft.client.Minecraft;
import net.minecraft.client.Screenshot;
import net.minecraft.client.renderer.culling.Frustum;
import net.minecraft.core.BlockPos;
import net.minecraft.core.component.DataComponents;
import net.minecraft.gizmos.*;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.entity.*;
import net.minecraft.world.entity.projectile.Projectile;
import net.minecraft.world.item.*;
import net.minecraft.world.item.component.ChargedProjectiles;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.phys.*;
import org.joml.Matrix4f;
import static org.orbitlauncher.client.TrajectoryPhysics.Kind;

/** Development-only native-physics and rendering checks in a fresh disposable world. */
final class TrajectorySmoke {
    private static int ticks;
    private static double earlyBowSpeed;
    private static final Frustum ALL=new Frustum(new Matrix4f(),new Matrix4f()){@Override public boolean isVisible(AABB b){return true;}};
    private static void require(boolean b,String message){if(!b)throw new IllegalStateException("Trajectories: "+message);}
    static void tick(Minecraft mc){
        ticks++;
        if(ticks==20){verify(mc);mc.getConnection().sendCommand("item replace entity @s weapon.mainhand with minecraft:ender_pearl");mc.gui.setScreen(null);mc.player.setXRot(-12);mc.player.setYRot(0);}
        if(ticks==42){mc.gui.hud.getChat().clearMessages(true);mc.gui.toastManager().clear();mc.player.setXRot(22);}
        if(ticks==45)Screenshot.grab(mc,false);
        if(ticks==46)mc.getConnection().sendCommand("item replace entity @s weapon.mainhand with minecraft:bow");
        if(ticks==52){require(mc.player.getMainHandItem().is(Items.BOW),"bow fixture missing");mc.options.keyUse.setDown(true);mc.gameMode.useItem(mc.player,InteractionHand.MAIN_HAND);}
        if(ticks==57){require(mc.player.isUsingItem(),"test input released bow");var launch=Trajectories.launch(mc.player,OrbitClient.config.state(Module.TRAJECTORIES),1);require(launch!=null,"charging bow preview missing at "+mc.player.getTicksUsingItem()+" ticks");earlyBowSpeed=launch.velocity().length();require(earlyBowSpeed<3,"short draw already has full power");}
        if(ticks==75){var launch=Trajectories.launch(mc.player,OrbitClient.config.state(Module.TRAJECTORIES),1);require(launch!=null&&launch.velocity().length()>earlyBowSpeed&&Math.abs(launch.velocity().length()-3)<.001,"bow preview did not follow real draw strength");mc.gui.hud.getChat().clearMessages(true);Screenshot.grab(mc,false);mc.options.keyUse.setDown(false);mc.gameMode.releaseUsingItem(mc.player);}
        if(ticks==80){var menu=new OrbitScreen();mc.gui.setScreen(menu);menu.expandForTest(Module.TRAJECTORIES);}
        if(ticks==95)Screenshot.grab(mc,false);
        if(ticks==100){var screen=new ModuleScreen(new OrbitScreen(),Module.TRAJECTORIES);screen.selectTabForTest(1);mc.gui.setScreen(screen);}
        if(ticks==115)Screenshot.grab(mc,false);
        if(ticks==120){var screen=new ModuleScreen(new OrbitScreen(),Module.TRAJECTORIES);screen.selectTabForTest(2);mc.gui.setScreen(screen);}
        if(ticks==135)Screenshot.grab(mc,false);
        if(ticks==140){OrbitClient.LOG.info("Trajectories integration checks completed: seven native projectile physics profiles, water/current ticks, real bow draw, collisions, settings, hand/charge filters, depth-tested gizmos and persistence");mc.stop();}
    }
    private static List<SimpleGizmoCollector.GizmoInstance> collect(Minecraft mc){
        var collector=new SimpleGizmoCollector();
        try(var ignored=Gizmos.withCollector(collector)){mc.levelExtractor.debugRenderer.emitGizmos(ALL,0,0,0,1);}
        return collector.getGizmos();
    }
    private static void verify(Minecraft mc){
        var s=OrbitClient.config.state(Module.TRAJECTORIES);s.enabled=true;s.trajectoryHideMenus=false;
        var p=mc.player;Vec3 start=p.position().add(0,28,0),initial=new Vec3(.15,.2,1.4);
        EntityType<?>[] types={EntityTypes.ARROW,EntityTypes.TRIDENT,EntityTypes.ENDER_PEARL,EntityTypes.SNOWBALL,EntityTypes.EGG,EntityTypes.SPLASH_POTION,EntityTypes.EXPERIENCE_BOTTLE};
        Kind[] kinds={Kind.ARROW,Kind.TRIDENT,Kind.PEARL,Kind.SNOWBALL,Kind.EGG,Kind.POTION,Kind.EXPERIENCE};
        for(int k=0;k<types.length;k++){
            var projectile=(Projectile)types[k].create(mc.level,EntitySpawnReason.COMMAND);require(projectile!=null,"native entity missing");
            projectile.setOwner(p);projectile.setPos(start);projectile.setDeltaMovement(initial);
            Vec3 pos=start,velocity=initial;
            for(int i=0;i<16;i++){
                pos=pos.add(TrajectoryPhysics.move(velocity,kinds[k],false));velocity=TrajectoryPhysics.after(velocity,kinds[k],false,false);
                projectile.tick();
                require(pos.distanceTo(projectile.position())<.00001,"native position mismatch: "+kinds[k]+" tick "+i+" predicted "+pos+" actual "+projectile.position());
                require(velocity.distanceTo(projectile.getDeltaMovement())<.00001,"native velocity mismatch: "+kinds[k]+" tick "+i);
            }
            var path=Trajectories.predict(mc,new Trajectories.Launch(kinds[k],start,initial),16);
            require(path.points().size()>2&&path.points().size()<=201,"path work bound failed");
            double length=0;for(int i=1;i<path.points().size();i++)length+=path.points().get(i-1).distanceTo(path.points().get(i));
            require(length<=16.00001,"range exceeded");
        }
        verifyWater(mc,types,kinds,start);
        var returning=Trajectories.predict(mc,new Trajectories.Launch(Kind.PEARL,p.getEyePosition(1).add(0,-.10000000149011612,0),new Vec3(0,1.5,0)),128);
        require(returning.hit() instanceof EntityHitResult self&&self.getEntity()==p,"returning pearl missed shooter");
        int slot=p.getInventory().getSelectedSlot();var held=p.getMainHandItem().copy();var off=p.getOffhandItem().copy();float pitch=p.getXRot(),yaw=p.getYRot();
        var wall=BlockPos.containing(start.add(0,0,4));var previous=mc.level.getBlockState(wall);
        try{
            mc.level.setBlock(wall,Blocks.STONE.defaultBlockState(),3);
            var hit=Trajectories.predict(mc,new Trajectories.Launch(Kind.PEARL,new Vec3(wall.getX()+.5,wall.getY()+.8,wall.getZ()-2),new Vec3(0,0,1.5)),128);
            require(hit.hit() instanceof BlockHitResult b&&b.getBlockPos().equals(wall),"first block impact missing");
            require(hit.points().getLast().equals(hit.hit().getLocation()),"line runs past impact");
            p.getInventory().setItem(slot,new ItemStack(Items.ENDER_PEARL));p.setXRot(-15);p.setYRot(0);
            require(Trajectories.launch(p,s,1)!=null,"pearl preview missing");
            var before=p.getMainHandItem().copy();Vec3 movement=p.getDeltaMovement();
            var gizmos=collect(mc);require(Trajectories.renderedForTest>0,"extraction hook failed");
            require(gizmos.stream().filter(g->g.gizmo() instanceof LineGizmo).anyMatch(g->!g.isAlwaysOnTop()),"depth-tested lines missing");
            require(ItemStack.matches(before,p.getMainHandItem())&&p.getDeltaMovement().equals(movement)&&p.getXRot()==-15&&p.getYRot()==0,"preview mutated player state");
            s.trajectoryPearl=false;require(Trajectories.launch(p,s,1)==null,"pearl toggle ignored");s.trajectoryPearl=true;
            s.enabled=false;collect(mc);require(Trajectories.renderedForTest==0,"disabled module still renders");s.enabled=true;
            s.trajectoryHideMenus=true;collect(mc);require(Trajectories.renderedForTest==0,"menu filter ignored");s.trajectoryHideMenus=false;
            p.getInventory().setItem(slot,ItemStack.EMPTY);p.setItemInHand(InteractionHand.OFF_HAND,new ItemStack(Items.ENDER_PEARL));
            require(Trajectories.launch(p,s,1)!=null,"offhand preview missing");s.trajectoryOffhand=false;require(Trajectories.launch(p,s,1)==null,"offhand filter ignored");s.trajectoryOffhand=true;
            p.getInventory().setItem(slot,new ItemStack(Items.SHIELD));require(Trajectories.launch(p,s,1)==null,"shield priority ignored");
            p.getInventory().setItem(slot,new ItemStack(Items.SNOWBALL));s.trajectorySnowball=false;require(Trajectories.launch(p,s,1)==null,"disabled main projectile fell through to offhand");s.trajectorySnowball=true;
            p.setItemInHand(InteractionHand.OFF_HAND,ItemStack.EMPTY);p.getInventory().setItem(slot,new ItemStack(Items.BOW));
            require(Trajectories.launch(p,s,1)==null,"uncharged bow displayed under charging-only filter");s.trajectoryChargeOnly=false;
            require(Trajectories.launch(p,s,1)!=null,"full-charge bow preview missing");s.trajectoryChargeOnly=true;
            var crossbow=new ItemStack(Items.CROSSBOW);p.getInventory().setItem(slot,crossbow);
            require(Trajectories.launch(p,s,1)==null,"empty crossbow displayed");
            crossbow.set(DataComponents.CHARGED_PROJECTILES,ChargedProjectiles.ofNonEmpty(List.of(new ItemStack(Items.ARROW))));
            require(Trajectories.launch(p,s,1)!=null,"charged crossbow missing");
            crossbow.set(DataComponents.CHARGED_PROJECTILES,ChargedProjectiles.ofNonEmpty(List.of(new ItemStack(Items.FIREWORK_ROCKET))));
            require(Trajectories.launch(p,s,1)==null,"firework incorrectly given an arrow path");
            for(var item:new Item[]{Items.ENDER_PEARL,Items.SNOWBALL,Items.EGG,Items.BLUE_EGG,Items.BROWN_EGG,Items.SPLASH_POTION,Items.LINGERING_POTION,Items.EXPERIENCE_BOTTLE}){
                p.getInventory().setItem(slot,new ItemStack(item));require(Trajectories.launch(p,s,1)!=null,"throwable not recognized: "+item);
            }
            var editor=new FeatureColorScreen(new OrbitScreen(),Module.TRAJECTORIES,false);editor.init(800,450);editor.verifyInput();
            var opacity=editor.children().stream().filter(c->c instanceof OrbitSlider).map(c->(OrbitSlider)c).findFirst().orElseThrow();
            opacity.onClick(new net.minecraft.client.input.MouseButtonEvent(opacity.getX()+10,opacity.getY()+24,new net.minecraft.client.input.MouseButtonInfo(0,0)),false);
            require(Math.abs(s.trajectoryOpacity-.1f)<.00001,"color picker allows invisible trajectory opacity");
            OrbitClient.save();
            require(ClientConfig.load().state(Module.TRAJECTORIES).trajectoryOpacity==s.trajectoryOpacity,"picker opacity changes after reload");
            s.trajectoryColor=0xAABBCC;s.trajectoryRange=96;s.trajectoryWidth=2.25f;OrbitClient.save();var saved=ClientConfig.load().state(Module.TRAJECTORIES);
            require(saved.trajectoryColor==0xAABBCC&&saved.trajectoryRange==96&&saved.trajectoryWidth==2.25f,"settings lost on save/load");
            s.trajectoryRange=999;s.trajectoryWidth=99;s.trajectoryOpacity=-2;s.trajectoryMarkerSize=9;OrbitClient.save();saved=ClientConfig.load().state(Module.TRAJECTORIES);
            require(saved.trajectoryRange==256&&saved.trajectoryWidth==4&&saved.trajectoryOpacity==.1f&&saved.trajectoryMarkerSize==.5f,"saved settings bounds not enforced");
            s.trajectoryOpacity=.85f;s.trajectoryMarkerSize=.18f;
            s.trajectoryTheme=true;s.trajectoryRange=128;s.trajectoryWidth=1.5f;s.trajectoryHideMenus=true;OrbitClient.save();
        }finally{mc.level.setBlock(wall,previous,3);p.getInventory().setItem(slot,held);p.setItemInHand(InteractionHand.OFF_HAND,off);p.setXRot(pitch);p.setYRot(yaw);}
    }
    private static void verifyWater(Minecraft mc,EntityType<?>[] types,Kind[] kinds,Vec3 airStart){
        var base=BlockPos.containing(airStart);var original=new java.util.LinkedHashMap<BlockPos,net.minecraft.world.level.block.state.BlockState>();
        try{
            for(int x=-2;x<=2;x++)for(int y=-4;y<=1;y++)for(int z=-2;z<=4;z++){
                var bp=base.offset(x,y,z);original.put(bp,mc.level.getBlockState(bp));mc.level.setBlock(bp,Blocks.WATER.defaultBlockState(),3);
            }
            for(boolean flowing:new boolean[]{false,true}){
                if(flowing)for(int y=-4;y<=1;y++)for(int z=-2;z<=4;z++)mc.level.setBlock(base.offset(1,y,z),Blocks.WATER.defaultBlockState().setValue(net.minecraft.world.level.block.LiquidBlock.LEVEL,4),3);
                for(int k=0;k<types.length;k++){
                    var projectile=(Projectile)types[k].create(mc.level,EntitySpawnReason.COMMAND);projectile.setOwner(mc.player);
                    Vec3 pos=new Vec3(base.getX()+.8,base.getY()+.5,base.getZ()+.5),velocity=new Vec3(.02,.1,.25);boolean water=false;
                    projectile.setPos(pos);projectile.setDeltaMovement(velocity);
                    for(int i=0;i<12;i++){
                        pos=pos.add(TrajectoryPhysics.move(velocity,kinds[k],water));var fluid=Trajectories.fluid(mc,pos,kinds[k]);
                        velocity=TrajectoryPhysics.after(velocity,kinds[k],water,fluid.water()).add(fluid.push());water=fluid.water();
                        projectile.tick();
                        require(pos.distanceTo(projectile.position())<.00001,"water position mismatch: "+kinds[k]+" flowing="+flowing+" tick="+i);
                        require(velocity.distanceTo(projectile.getDeltaMovement())<.00001,"water velocity mismatch: "+kinds[k]+" flowing="+flowing+" tick="+i+" expected "+velocity+" actual "+projectile.getDeltaMovement());
                        require(water==projectile.isInWater(),"water bounds mismatch");
                    }
                }
            }
        }finally{original.forEach((bp,state)->mc.level.setBlock(bp,state,3));}
        OrbitClient.LOG.info("Trajectories: 168 native water/current ticks matched across seven projectile types");
    }
    private TrajectorySmoke(){}
}
