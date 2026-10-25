package org.orbitlauncher.client;

/** Shared HUD appearance. Position, scale and displayed data remain per widget. */
public final class HudAppearance {
    public boolean background=true,hudBorder=true,accentBar=true,showTitle=true,accentTitle=true;
    public float opacity=.72f,textOpacity=1;
    public int hudStyle,panelWidth,rowSpacing,padding=8,alignment;
    void sanitize(){
        opacity=Float.isFinite(opacity)?Math.clamp(opacity,0,1):.72f;
        textOpacity=Float.isFinite(textOpacity)?Math.clamp(textOpacity,.2f,1):1;
        hudStyle=Math.clamp(hudStyle,0,3);padding=Math.clamp(padding,4,16);
        rowSpacing=Math.clamp(rowSpacing,0,8);alignment=Math.clamp(alignment,0,2);
        if(panelWidth!=0&&panelWidth!=90&&panelWidth!=130&&panelWidth!=180&&panelWidth!=240)panelWidth=0;
    }
}
