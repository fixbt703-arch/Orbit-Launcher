package org.orbitlauncher.client;

public final class GlowStyleTest {
    private static void require(boolean ok,String why){if(!ok)throw new AssertionError(why);}
    public static void main(String[] args){
        var a=GlowStyle.sample(0x20C8A0,false,false,0,0,0,0);
        var b=GlowStyle.sample(0x20C8A0,false,false,.6f,.6f,0,0);
        require(a.core()!=b.core(),"Highlight must travel across the model, not flash uniformly");
        var later=GlowStyle.sample(0x20C8A0,false,false,0,0,0,2);
        require(a.core()!=later.core(),"Highlight must move over time even with pulse off");
        var near=GlowStyle.sample(0x20C8A0,false,false,.00001f,0,0,.00001);
        require(Math.abs((a.core()&255)-(near.core()&255))<=1,"Highlight must remain continuous at joined edges");
        require((a.core()>>8&255)>(a.core()>>16&255),"Selected green tint must remain green");
        require(a.intensity()==1,"Pulse off must preserve opacity");
        require(Math.abs(GlowStyle.sample(0x20C8A0,false,true,0,0,0,0).intensity()-GlowStyle.sample(0x20C8A0,false,true,0,0,0,1).intensity())>.05f,"Enabled pulse must actually breathe over time");
        for(int i=0;i<100;i++){
            var amethyst=GlowStyle.sample(0xB9B8FF,false,false,0,0,0,i*.1);
            require((amethyst.core()>>16&255)<230,"Amethyst core must retain its tint instead of turning white");
            var pulse=GlowStyle.sample(0x20C8A0,true,true,0,0,0,i*.1);
            require(pulse.intensity()>=.78f&&pulse.intensity()<=1,"Pulse must breathe without blinking");
            require((pulse.core()&0xFF000000)==0,"Style colors must remain RGB");
        }
        require(GlowStyle.sample(0x20C8A0,true,false,0,0,0,0).core()!=GlowStyle.sample(0x20C8A0,true,false,0,0,0,3).core(),"Rainbow must cycle");
        require(GlowStyle.sample(0x20C8A0,true,false,0,0,0,1_200_000_000).core()!=GlowStyle.sample(0x20C8A0,true,false,.6f,.6f,0,1_200_000_000).core(),"Long system uptime must not erase the rainbow gradient");
        System.out.println("Glow style: continuous traveling highlight, chosen tint, restrained pulse and rainbow passed");
    }
}
