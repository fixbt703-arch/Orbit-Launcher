package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.culling.Frustum;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.resources.Identifier;
import net.minecraft.gizmos.*;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.phys.AABB;
import org.joml.Matrix4f;

/** Development-only checks against the actual texture manager, skin meshes and world renderer. */
final class NewFeatureSmoke {
    static void verify(Minecraft mc){verifyGlint(mc);verifySkin(mc);verifyHitboxes(mc);}
    private static void verifyGlint(Minecraft mc){
        var s=OrbitClient.config.state(Module.GLINT);boolean enabled=s.enabled,item=s.glintItems,armor=s.glintArmor;int color=s.glintColor;float opacity=s.glintOpacity;
        try{
            for(String kind:new String[]{"item","armor"}){
                var id=Identifier.withDefaultNamespace("textures/misc/enchanted_glint_"+kind+".png");s.enabled=false;var original=mc.getTextureManager().getTexture(id);
                s.enabled=s.glintItems=s.glintArmor=true;s.glintColor=0x00FF00;s.glintOpacity=1;
                var changed=mc.getTextureManager().getTexture(id);
                if(!(changed instanceof DynamicTexture texture)||changed==original)throw new IllegalStateException("Glint texture hook did not apply: "+kind);
                var pixels=texture.getPixels();boolean lit=false;
                for(int y=0;y<pixels.getHeight();y++)for(int x=0;x<pixels.getWidth();x++){int p=pixels.getPixel(x,y);if((p&0xFF00FF)!=0)throw new IllegalStateException("Glint has wrong RGB");lit|=(p&0xFF00)>0;}
                if(!lit)throw new IllegalStateException("Glint lost image intensity");
                s.glintOpacity=0;var zero=((DynamicTexture)mc.getTextureManager().getTexture(id)).getPixels();
                for(int y=0;y<zero.getHeight();y++)for(int x=0;x<zero.getWidth();x++)if((zero.getPixel(x,y)&0xFFFFFF)!=0)throw new IllegalStateException("Zero opacity glint is visible");
                s.glintItems=s.glintArmor=false;if(mc.getTextureManager().getTexture(id)!=original)throw new IllegalStateException("Glint per-type toggle failed");
            }
            s.glintItems=s.glintArmor=true;s.glintOpacity=.5f;GlintColorizer.invalidate();
            if(!(mc.getTextureManager().getTexture(Identifier.withDefaultNamespace("textures/misc/enchanted_glint_item.png")) instanceof DynamicTexture))throw new IllegalStateException("Glint cache invalidation failed");
        }finally{s.enabled=enabled;s.glintItems=item;s.glintArmor=armor;s.glintColor=color;s.glintOpacity=opacity;}
        OrbitClient.LOG.info("Glint: item/armor textures, RGB, zero opacity, toggles and cache invalidation passed");
    }
    private static void verifySkin(Minecraft mc){
        var s=OrbitClient.config.state(Module.SKIN_3D);boolean enabled=s.enabled,head=s.skinHead;
        try{
            s.enabled=true;s.skinHead=true;SkinLayers.sync();var c=dev.tr7zw.skinlayers.versionless.ModBase.config;
            if(!c.enableHat||!c.enableJacket||c.renderDistanceLOD!=s.skinDistance)throw new IllegalStateException("3D skin settings did not apply");
            var data=(dev.tr7zw.skinlayers.accessor.PlayerSettings)mc.player;
            if(!dev.tr7zw.skinlayers.SkinUtil.setup3dLayers(mc.player,data,data.hasThinArms())||data.getHeadMesh()==null||data.getTorsoMesh()==null)throw new IllegalStateException("3D skin meshes were not generated");
            s.skinHead=false;SkinLayers.sync();if(c.enableHat)throw new IllegalStateException("3D head toggle failed");
            s.enabled=false;SkinLayers.sync();if(c.enableHat||c.enableJacket||c.enableLeftSleeve||c.enableRightPants)throw new IllegalStateException("3D skin did not disable");
        }finally{s.enabled=enabled;s.skinHead=head;SkinLayers.sync();}
        OrbitClient.LOG.info("3D skin: live voxel mesh generation, part settings and disable passed");
    }
    private static void verifyHitboxes(Minecraft mc){
        var old=OrbitClient.config.state(Module.HITBOX);var s=ClientConfig.defaults(Module.HITBOX);OrbitClient.config.modules.put(Module.HITBOX,s);Entity originalTarget=mc.crosshairPickEntity;
        var originalHit=mc.hitResult;Entity target=null;net.minecraft.world.phys.Vec3 position=null;float yaw=mc.player.getYRot(),pitch=mc.player.getXRot();
        try{
            for(var e:mc.level.entitiesForRendering())if(e instanceof LivingEntity&&e!=mc.player){target=e;break;}
            if(target==null)throw new IllegalStateException("Test target did not spawn");mc.crosshairPickEntity=target;
            position=target.position();mc.player.setYRot(0);mc.player.setXRot(0);
            target.setPos(mc.player.getX(),mc.player.getEyeY()-.45,mc.player.getZ()+2);
            mc.hitResult=mc.player.raycastHitResult(1,mc.player);
            s.enabled=true;s.hitboxOnlyTarget=true;s.hitboxOther=true;s.hitboxDistance=128;s.hitboxThroughWalls=true;s.hitboxTargetColor=0x12AB34;s.hitboxTargetOpacity=.5f;
            var f=new Frustum(new Matrix4f(),new Matrix4f()){@Override public boolean isVisible(AABB b){return true;}};
            for(int style=0;style<3;style++){
                s.hitboxStyle=style;var collector=new SimpleGizmoCollector();
                try(var ignored=Gizmos.withCollector(collector)){mc.levelExtractor.debugRenderer.emitGizmos(f,0,0,0,1);}
                if(Hitboxes.renderedForTest!=1||collector.getGizmos().isEmpty())throw new IllegalStateException("Hitbox render hook/style failed: "+style);
                if(style==0&&collector.getGizmos().stream().noneMatch(g->g.gizmo() instanceof CuboidGizmo c&&c.style().stroke()==0x8012AB34&&g.isAlwaysOnTop()))throw new IllegalStateException("Hitbox target color/opacity failed");
            }
            s.hitboxPassive=false;if(Hitboxes.accepts(target,s))throw new IllegalStateException("Hitbox entity filter failed");
            s.hitboxPassive=true;s.hitboxStyle=0;s.hitboxThroughWalls=false;
            var block=net.minecraft.core.BlockPos.containing(mc.player.getX(),mc.player.getEyeY(),mc.player.getZ()+1);var oldBlock=mc.level.getBlockState(block);
            try{
                for(int distance:new int[]{6,32}){
                    target.setPos(mc.player.getX(),mc.player.getEyeY()-.45,mc.player.getZ()+distance);
                    mc.hitResult=mc.player.raycastHitResult(1,mc.player);
                    s.hitboxOnlyTarget=false;var far=new SimpleGizmoCollector();try(var ignored=Gizmos.withCollector(far)){Hitboxes.extract(f,0,0,0,1);}
                    if(far.getGizmos().stream().noneMatch(g->g.gizmo() instanceof CuboidGizmo c&&c.style().stroke()==Hitboxes.argb(s.hitboxColor,s.hitboxOpacity))
                        ||far.getGizmos().stream().anyMatch(g->g.gizmo() instanceof CuboidGizmo c&&c.style().stroke()==0x8012AB34))throw new IllegalStateException("Distant hitbox did not retain its base color: "+distance);
                    s.hitboxOnlyTarget=true;try(var ignored=Gizmos.withCollector(new SimpleGizmoCollector())){Hitboxes.extract(f,0,0,0,1);}
                    if(Hitboxes.renderedForTest!=0)throw new IllegalStateException("Distant entity was selected for attack: "+distance);
                }
                target.setPos(mc.player.getX(),mc.player.getEyeY()-.45,mc.player.getZ()+2);
                mc.level.setBlock(block,net.minecraft.world.level.block.Blocks.STONE.defaultBlockState(),3);
                mc.hitResult=mc.player.raycastHitResult(1,mc.player);
                try(var ignored=Gizmos.withCollector(new SimpleGizmoCollector())){Hitboxes.extract(f,0,0,0,1);}
                if(Hitboxes.renderedForTest!=0)throw new IllegalStateException("Target highlight ignored obstacle");
                s.hitboxThroughWalls=true;try(var ignored=Gizmos.withCollector(new SimpleGizmoCollector())){Hitboxes.extract(f,0,0,0,1);}
                if(Hitboxes.renderedForTest!=0)throw new IllegalStateException("Through-obstacle setting incorrectly enabled target highlight");
            }finally{mc.level.setBlock(block,oldBlock,3);mc.hitResult=mc.player.raycastHitResult(1,mc.player);}
            HitboxSettingsSmoke.verify(mc,target,f);
            s.enabled=false;var collector=new SimpleGizmoCollector();try(var ignored=Gizmos.withCollector(collector)){Hitboxes.extract(f,0,0,0,1);}if(!collector.getGizmos().isEmpty())throw new IllegalStateException("Disabled hitboxes still rendered");
        }finally{if(target!=null&&position!=null)target.setPos(position);mc.player.setYRot(yaw);mc.player.setXRot(pitch);OrbitClient.config.modules.put(Module.HITBOX,old);mc.crosshairPickEntity=originalTarget;mc.hitResult=originalHit;}
        OrbitClient.LOG.info("Hitbox: actual attack raycast, nearby target RGB/alpha, base color at 6/32 blocks, obstacle rejection, styles, settings and disable passed");
    }
    private NewFeatureSmoke(){}
}
