package org.orbitlauncher.client;

/** Menu density follows the window, independently of Minecraft's HUD scale. */
record ClickGuiViewport(int width,int height,double scale) {
    static ClickGuiViewport fit(int guiWidth,int guiHeight,int minecraftScale){
        guiWidth=Math.max(1,guiWidth);guiHeight=Math.max(1,guiHeight);minecraftScale=Math.max(1,minecraftScale);
        double pixelsPerUnit=Math.max(1,Math.min(guiWidth*minecraftScale/960d,guiHeight*minecraftScale/540d));
        double scale=pixelsPerUnit/minecraftScale;
        return new ClickGuiViewport((int)Math.ceil(guiWidth/scale),(int)Math.ceil(guiHeight/scale),scale);
    }
    double local(double coordinate){return coordinate/scale;}
    double screen(double coordinate){return coordinate*scale;}
}
