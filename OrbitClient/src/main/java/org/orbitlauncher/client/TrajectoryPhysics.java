package org.orbitlauncher.client;

import net.minecraft.util.Mth;
import net.minecraft.world.phys.Vec3;

/** Nominal Minecraft 26.2 flight; spread is deliberately not sampled or manipulated. */
final class TrajectoryPhysics {
    enum Kind {
        ARROW(.05,.6f,false), TRIDENT(.05,.99f,false),
        PEARL(.03,.8f,true), SNOWBALL(.03,.8f,true), EGG(.03,.8f,true),
        POTION(.05,.8f,true), EXPERIENCE(.07,.8f,true);
        final double gravity,waterDrag;final boolean beforeMove;
        Kind(double gravity,double drag,boolean beforeMove){this.gravity=gravity;waterDrag=drag;this.beforeMove=beforeMove;}
    }
    static Vec3 move(Vec3 velocity,Kind kind,boolean water){
        return kind.beforeMove?velocity.add(0,-kind.gravity,0).scale(water?kind.waterDrag:.99f):velocity;
    }
    static Vec3 after(Vec3 velocity,Kind kind,boolean waterBefore,boolean waterAfter){
        if(kind.beforeMove)return move(velocity,kind,waterBefore);
        return velocity.scale(waterBefore?kind.waterDrag:.99f).add(0,-kind.gravity,0);
    }
    static Vec3 direction(float pitch,float yaw,float elevation){
        float p=pitch*Mth.DEG_TO_RAD,y=yaw*Mth.DEG_TO_RAD;
        return new Vec3(-Mth.sin(y)*Mth.cos(p),-Mth.sin((pitch+elevation)*Mth.DEG_TO_RAD),Mth.cos(y)*Mth.cos(p)).normalize();
    }
    static Vec3 inherit(Vec3 velocity,Vec3 movement,boolean grounded){return velocity.add(movement.x,grounded?0:movement.y,movement.z);}
    private TrajectoryPhysics(){}
}
