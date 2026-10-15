package org.orbitlauncher.client;

import dev.tr7zw.skinlayers.versionless.ModBase;

/** Runtime controls for the official Skin Layers 3D component downloaded by the launcher. */
final class SkinLayers {
    private static int previous;
    static void sync(){
        var c=ModBase.config;if(c==null)return;
        var s=OrbitClient.config.state(Module.SKIN_3D);boolean on=s.enabled;
        c.enableHat=on&&s.skinHead;c.enableJacket=on&&s.skinBody;
        c.enableLeftSleeve=c.enableRightSleeve=on&&s.skinArms;
        c.enableLeftPants=c.enableRightPants=on&&s.skinLegs;
        c.baseVoxelSize=s.skinThickness;c.bodyVoxelWidthSize=s.skinThickness;c.headVoxelSize=s.skinThickness;
        c.renderDistanceLOD=s.skinDistance;c.firstPersonPixelScaling=s.skinFirstPerson?1.1f:0;
        c.enableSkulls=false;c.enableSkullsItems=false;
        int fingerprint=java.util.Objects.hash(on,s.skinHead,s.skinBody,s.skinArms,s.skinLegs,s.skinThickness,s.skinDistance,s.skinFirstPerson);
        if(previous!=fingerprint){previous=fingerprint;var mc=net.minecraft.client.Minecraft.getInstance();if(mc.level!=null&&dev.tr7zw.skinlayers.SkinLayersModBase.instance!=null)for(var player:mc.level.players())dev.tr7zw.skinlayers.SkinLayersModBase.instance.refreshLayers(player);}
    }
    private SkinLayers(){}
}
