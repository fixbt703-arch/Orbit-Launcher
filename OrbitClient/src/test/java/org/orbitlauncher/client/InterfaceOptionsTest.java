package org.orbitlauncher.client;

public final class InterfaceOptionsTest {
    public static void main(String[] args) {
        var options=new InterfaceOptions();
        if(!java.util.Arrays.equals(InterfaceOptions.THEMES,new String[]{"Ocean","Emerald","Rose","Gold","Neon","Blood","Rainbow","Obsidian"}))throw new AssertionError("Requested eight themes are not available");
        options.hud=null;options.sanitize();
        if(options.hud==null||options.hud.padding!=8||!options.hud.showTitle)throw new AssertionError("Old menu-only config has no shared HUD defaults");
        var appearance=options.hud;appearance.opacity=Float.NaN;appearance.textOpacity=Float.POSITIVE_INFINITY;appearance.padding=99;appearance.panelWidth=-5;appearance.hudStyle=9;appearance.rowSpacing=-1;appearance.alignment=9;
        options.sanitize();
        if(appearance.opacity!=.72f||appearance.textOpacity!=1||appearance.padding!=16||appearance.panelWidth!=0||appearance.hudStyle!=3||appearance.rowSpacing!=0||appearance.alignment!=2)throw new AssertionError("Invalid shared HUD appearance survived");
        options.hud=new HudAppearance();
        if(options.theme!=0||options.gradient!=2||options.animationSpeed!=2||!options.animations||!options.smoothChanges||!options.glow||!options.tooltips||options.bindMessages||options.background!=0||options.cornerRadius!=12)
            throw new AssertionError("Interface defaults differ from reference");
        options.blurRadius=40;options.dimStrength=Double.NaN;options.sanitize();
        if(options.blurRadius!=10||options.dimStrength!=.45)throw new AssertionError("Invalid background strength survived");
        options.background=0;options.blurRadius=0;if(options.effectiveBlurRadius()!=0||options.backgroundAlpha()!=0)throw new AssertionError("Zero blur is not clear");
        options.background=1;options.dimStrength=0;if(options.backgroundAlpha()!=0||options.effectiveBlurRadius()!=0)throw new AssertionError("Dim zero applies blur or tint");
        options.dimStrength=1;if(options.backgroundAlpha()!=235)throw new AssertionError("Dim maximum is not applied");
        options.background=2;if(options.backgroundAlpha()!=0||options.effectiveBlurRadius()!=0)throw new AssertionError("None still changes background");
        options.theme=-40;options.gradient=9;options.animationSpeed=Double.NaN;options.background=50;options.cornerRadius=-12;options.sanitize();
        if(options.theme!=0||options.gradient!=2||options.animationSpeed!=2||options.background!=2||options.cornerRadius!=0)throw new AssertionError("Invalid Interface settings survived");
        options.animationSpeed=4;if(options.duration(220)!=110)throw new AssertionError("Speed does not change motion duration");
        options.animations=false;if(options.duration(220)!=0||options.blend(.016,21)!=1)throw new AssertionError("Disabled GUI animations must snap");
        options.animations=true;options.smoothChanges=false;if(options.blend(.016,21)!=1)throw new AssertionError("Disabled smooth changes must snap");
        for(int theme=0;theme<InterfaceOptions.THEMES.length;theme++){
            options.theme=theme;
            for(double seconds:new double[]{0,10,20,30}){
                for(int color:new int[]{options.base(seconds),options.card(seconds),options.control(seconds)}){
                    if(contrast(0xFFE4EDF5,color)<4.5||contrast(0xFFACB4C3,color)<4.5)throw new AssertionError("Unreadable panel palette: "+theme);
                }
                if(contrast(options.primary(seconds),options.card(seconds))<3)throw new AssertionError("Theme accent disappears into panel: "+theme);
            }
            if(options.primary()==options.secondary()||(options.primary()>>>24)!=255)throw new AssertionError("Incomplete theme palette");
        }
        options.theme=InterfaceOptions.RAINBOW;
        if(options.primary(0)==options.primary(10))throw new AssertionError("Rainbow palette is static");
        options.animationSpeed=.5;int slow=options.primary(1);options.animationSpeed=4;
        if(slow==options.primary(1))throw new AssertionError("Rainbow ignores animation speed");
        options.animations=false;
        if(options.primary(0)!=options.primary(10)||options.card(0)!=options.card(10))throw new AssertionError("Rainbow ignores disabled animations");
        System.out.println("PASS Interface defaults, invalid input, motion controls and theme palettes");
    }
    private static double contrast(int a,int b){double x=luminance(a),y=luminance(b);return (Math.max(x,y)+.05)/(Math.min(x,y)+.05);}
    private static double luminance(int color){
        double[] channels={(color>>16&255)/255d,(color>>8&255)/255d,(color&255)/255d};
        for(int i=0;i<3;i++)channels[i]=channels[i]<=.04045?channels[i]/12.92:Math.pow((channels[i]+.055)/1.055,2.4);
        return channels[0]*.2126+channels[1]*.7152+channels[2]*.0722;
    }
}
