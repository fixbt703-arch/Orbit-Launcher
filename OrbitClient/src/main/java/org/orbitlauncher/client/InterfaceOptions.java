package org.orbitlauncher.client;

/** Shared menu and HUD preferences; gameplay settings remain independent. */
public final class InterfaceOptions {
    static final String[] THEMES={"Ocean","Emerald","Rose","Gold","Neon","Blood","Rainbow","Obsidian"};
    static final String[] THEMES_RU={"Океан","Изумруд","Роза","Золото","Неон","Кровь","Радуга","Обсидиан"};
    static final int RAINBOW=6;
    private static final int[] PRIMARY={0xFF49D4E8,0xFF52E49A,0xFFF59DBB,0xFFF3CC75,0xFFBF98FF,0xFFFF7381,0xFF6DE8ED,0xFFC4BEDD};
    private static final int[] SECONDARY={0xFF819FF5,0xFFB3EFA0,0xFFD0A3F8,0xFFFFE7AC,0xFF6DE8ED,0xFFFFB39C,0xFFDC98FF,0xFF949FBC};
    private static final int[] BASES={0xFF081923,0xFF091D15,0xFF21121B,0xFF211B0E,0xFF1C142B,0xFF260E15,0xFF0D191E,0xFF111116};
    private static final int[] CARDS={0xFF112D3C,0xFF122E23,0xFF361E2D,0xFF362D19,0xFF2C203F,0xFF3A1A23,0xFF182B32,0xFF202029};
    private static final int[] CONTROLS={0xFF1B4051,0xFF1D4231,0xFF4C2B3E,0xFF4C4025,0xFF402E59,0xFF522732,0xFF233D47,0xFF30303C};
    public HudAppearance hud=new HudAppearance();
    public int theme,themeSetVersion=1,gradient=2,background,cornerRadius=12,blurRadius=6;
    public double animationSpeed=2,dimStrength=.45;
    public boolean smoothChanges=true,animations=true,glow=true,tooltips=true,bindMessages;
    void sanitize(){
        if(hud==null)hud=new HudAppearance();hud.sanitize();
        theme=Math.clamp(theme,0,THEMES.length-1);gradient=Math.clamp(gradient,0,2);
        background=Math.clamp(background,0,2);cornerRadius=Math.clamp(cornerRadius,0,12);
        animationSpeed=Double.isFinite(animationSpeed)?Math.clamp(animationSpeed,.5,4):2;
        blurRadius=Math.clamp(blurRadius,0,10);
        dimStrength=Double.isFinite(dimStrength)?Math.clamp(dimStrength,0,1):.45;
    }
    int primary(){return primary(System.nanoTime()/1e9);}
    int secondary(){return secondary(System.nanoTime()/1e9);}
    int primary(double seconds){return theme==RAINBOW?rainbow(seconds,0,.55f,.95f):PRIMARY[theme];}
    int secondary(double seconds){return theme==RAINBOW?rainbow(seconds,.32,.45f,.98f):SECONDARY[theme];}
    int base(double seconds){return theme==RAINBOW?rainbow(seconds,0,.45f,.10f):BASES[theme];}
    int card(double seconds){return theme==RAINBOW?rainbow(seconds,0,.45f,.19f):CARDS[theme];}
    int control(double seconds){return theme==RAINBOW?rainbow(seconds,0,.40f,.27f):CONTROLS[theme];}
    int rainbow(double seconds,double offset,float saturation,float brightness){
        double hue=.52+(animations?seconds*.025*animationSpeed/2:0)+offset;
        return java.awt.Color.HSBtoRGB((float)(hue-Math.floor(hue)),saturation,brightness);
    }
    int effectiveBlurRadius(){return background==0?blurRadius:0;}
    int backgroundAlpha(){return background==1?(int)Math.round(dimStrength*235):background==0&&blurRadius>0?24:0;}
    double duration(double milliseconds){return animations?milliseconds*2/animationSpeed:0;}
    double blend(double seconds,double rate){return animations&&smoothChanges?1-Math.exp(-Math.min(.1,seconds)*rate*animationSpeed/2):1;}
}
