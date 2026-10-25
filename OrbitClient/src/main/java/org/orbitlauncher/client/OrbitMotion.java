package org.orbitlauncher.client;

/** Time-based transitions never animate hit areas or invalidate layout. */
final class OrbitMotion {
    private double value;
    private long frame;
    OrbitMotion(){}
    OrbitMotion(boolean active){value=active?1:0;}
    void snap(boolean active){value=active?1:0;frame=System.nanoTime();}
    float toward(boolean active) {
        long now=System.nanoTime();double dt=frame==0?0:Math.min(.1,(now-frame)/1e9);frame=now;
        value+=((active?1:0)-value)*OrbitTheme.options().blend(dt,21);
        if(Math.abs(value-(active?1:0))<.001)value=active?1:0;
        return (float)value;
    }
    static int mix(int from,int to,float amount) {
        amount=Math.clamp(amount,0,1);int result=0;
        for(int shift:new int[]{0,8,16,24})result|=Math.round(((from>>>shift)&255)*(1-amount)+((to>>>shift)&255)*amount)<<shift;
        return result;
    }
}
