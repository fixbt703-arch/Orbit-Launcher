package org.orbitlauncher.client;

import net.minecraft.world.phys.Vec3;

public final class TrajectoryPhysicsTest {
    private static void near(Vec3 actual,Vec3 expected){if(actual.distanceTo(expected)>1e-7)throw new AssertionError(actual+" != "+expected);}
    public static void main(String[] args){
        var arrow=TrajectoryPhysics.Kind.ARROW;var pearl=TrajectoryPhysics.Kind.PEARL;
        var v=new Vec3(0,0,3);
        near(TrajectoryPhysics.move(v,arrow,false),v);
        near(TrajectoryPhysics.after(v,arrow,false,false),new Vec3(0,-.05,3*.99f));
        near(TrajectoryPhysics.move(v,pearl,false),new Vec3(0,-.03*.99f,3*.99f));
        near(TrajectoryPhysics.move(v,pearl,true),new Vec3(0,-.03*.8f,3*.8f));
        near(TrajectoryPhysics.after(v,arrow,true,true),new Vec3(0,-.05,3*.6f));
        near(TrajectoryPhysics.after(v,arrow,false,true),new Vec3(0,-.05,3*.99f));
        near(TrajectoryPhysics.after(v,arrow,true,false),new Vec3(0,-.05,3*.6f));
        near(TrajectoryPhysics.direction(0,0,0),new Vec3(0,0,1));
        near(TrajectoryPhysics.direction(0,-90,0),new Vec3(1,0,0));
        near(TrajectoryPhysics.direction(-90,0,0),new Vec3(0,1,0));
        if(TrajectoryPhysics.direction(0,0,-20).y<=0)throw new AssertionError("Potion launch elevation missing");
        near(TrajectoryPhysics.inherit(v,new Vec3(.2,.4,.1),true),new Vec3(.2,0,3.1));
        near(TrajectoryPhysics.inherit(v,new Vec3(.2,.4,.1),false),new Vec3(.2,.4,3.1));
        var start=Vec3.ZERO;var velocity=v;
        for(int i=0;i<20;i++){start=start.add(TrajectoryPhysics.move(velocity,arrow,false));velocity=TrajectoryPhysics.after(velocity,arrow,false,false);}
        double z=3*(1-Math.pow(.99f,20))/(1-.99f);
        double y=-.05/(1-.99f)*(20-(1-Math.pow(.99f,20))/(1-.99f));
        near(start,new Vec3(0,y,z));
        System.out.println("PASS trajectory tick order, water transitions, aim axes, inherited movement and 20-tick closed form");
    }
}
