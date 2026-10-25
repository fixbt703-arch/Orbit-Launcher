package org.orbitlauncher.client;

import net.minecraft.client.gui.GuiGraphicsExtractor;

/** Short content reveal; navigation and input bounds stay fixed throughout. */
final class OrbitPageTransition {
    private long started;
    void start(){started=System.nanoTime();}
    void finish(GuiGraphicsExtractor g,int x,int y,int width,int height){
        if(started==0)return;
        double progress=OrbitTheme.elapsed(started,160);
        if(progress>=1){started=0;return;}
        int alpha=(int)Math.round(220*Math.pow(1-progress,3));
        if(alpha>0&&width>0&&height>0)g.fill(x,y,x+width,y+height,(alpha<<24)|(OrbitTheme.BASE&0xFFFFFF));
    }
}
