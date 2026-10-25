package org.orbitlauncher.client;

/** A moving color highlight and gentle breathing for the exterior pixel border. */
final class GlowStyle {
    record Sample(int core,float intensity){}
    static Sample sample(int rgb,boolean rainbow,boolean pulse,float x,float y,float z,double time){
        double phase=x*.65+y*.9+z*.35-time*.12;
        float sweep=(float)Math.pow(.5+.5*Math.sin(phase*Math.PI*2),5);
        if(rainbow){
            double hue=time/12+x*.12+y*.18+z*.06;
            rgb=java.awt.Color.HSBtoRGB((float)(hue-Math.floor(hue)),.62f,1)&0xFFFFFF;
        }
        float intensity=pulse?(float)(.89+.11*Math.sin(time*1.4)):1;
        return new Sample(mixWhite(rgb,.12f+.30f*sweep),intensity);
    }
    private static int mixWhite(int rgb,float amount){
        int r=rgb>>16&255,g=rgb>>8&255,b=rgb&255;
        return Math.round(r+(255-r)*amount)<<16|Math.round(g+(255-g)*amount)<<8|Math.round(b+(255-b)*amount);
    }
    private GlowStyle(){}
}
