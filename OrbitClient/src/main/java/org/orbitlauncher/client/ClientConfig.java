package org.orbitlauncher.client;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import net.fabricmc.loader.api.FabricLoader;
import java.nio.file.*;
import java.io.IOException;
import java.util.EnumMap;

public final class ClientConfig {
    private static final Gson JSON = new GsonBuilder().setPrettyPrinting().create();
    private static final Path FILE = FabricLoader.getInstance().getConfigDir().resolve("orbit-client.json");
    public EnumMap<Module, State> modules = new EnumMap<>(Module.class);
    public int layoutVersion;
    public Waypoint waypoint;
    public boolean hudSnap=true;
    public static final int[] COLORS = {0xFFB39CFF,0xFF79D7EF,0xFF74B9FF,0xFFF4CD87,0xFFFF91AA,0xFFF4F6FF};
    public static class State {
        public boolean enabled;
        public double x, y;
        public float scale=1, opacity=.72f;
        public int color=0;
        public boolean customPosition, background=true, showTitle=true, shadow;
        public boolean hudBorder=true, accentBar=true, accentTitle=true;
        public int hudStyle, panelWidth, rowSpacing;
        public int backgroundColor=0x171E2E, textColor=0xF4F6FF;
        public float textOpacity=1;
        public boolean showMouse=true, showSpace=true, showSeconds=true, showPercent;
        public boolean crosshairDot, crosshairOutline=true;
        public int decimals=1, crosshairSize=5, crosshairGap=2, crosshairThickness=1;
        public float zoomFactor=4;
        public int customColor=-1, padding=8, alignment=0, effectsLimit=8, reachSeconds=5;
        public boolean showUnits=true, splitCps, showNames=true, clock24=true;
        public int crosshairShape=0, dotSize=1, outlineWidth=1, dynamicGap=3;
        public boolean crosshairDynamic, crosshairThirdPerson, crosshairHideMenus=true;
        public int crosshairMode=0, crosshairPreset=0, crosshairColorMode=0, crosshairScale=0;
        public boolean crosshairChroma;
        public boolean separateFriendlyColor;
        public boolean targetPlayers=true, targetHostile=true, targetPassive=true, targetOther;
        public int enemyColor=0xFF5868, friendlyColor=0x5EE2A0;
        public String crosshairPixels="";
        public int chunkRenderDistance=24;
        public boolean retainChunks=true;
        public boolean refillSameBlockFirst=true, refillAnyBlock=true;
        public boolean skinHead=true,skinBody=true,skinArms=true,skinLegs=true,skinFirstPerson=true;
        public float skinThickness=1.1f;
        public int skinDistance=24,glintColor=0xB9B8FF;
        public float glintOpacity=.7f;
        public boolean glintItems=true,glintArmor=true;
        public boolean glowOutline=true,glowMatchGlint=true,glowFirstPerson=true,glowThirdPerson=true,glowInventory;
        public boolean glowRainbow,glowPulse;
        public int glowOutlineColor=0xB9B8FF;
        public float glowOutlineOpacity=.8f,glowWidth=1.6f,glowSoftness=.65f;
        public boolean hmiInspect=true,hmiMainHand=true,hmiOffHand=true,hmiShowHands=true,hmiBlocks3d=true;
        public boolean hmiParticles=true,hmiCameraMotion=true,hmiVanillaCombat;
        public float hmiOffsetX,hmiOffsetY,hmiOffsetZ,hmiScale=1,hmiMotion=1;
        public int hitboxColor=0xB9B8FF,hitboxTargetColor=0x48D6C5,hitboxDistance=48,hitboxStyle;
        public float hitboxOpacity=.65f,hitboxTargetOpacity=1,hitboxWidth=1.5f,hitboxFillOpacity=.1f;
        public boolean hitboxPlayers=true,hitboxHostile=true,hitboxPassive=true,hitboxOther;
        public boolean hitboxFill,hitboxEyes,hitboxLook,hitboxThroughWalls,hitboxSelf,hitboxOnlyTarget,hitboxInvisible;
        public boolean hitboxOutline=true,hitboxTop=true,hitboxBottom=true,hitboxVertical=true,hitboxHighlight=true;
        public boolean hitboxOwnFillColor,hitboxTargetWidthOverride,hitboxTargetStyleOverride,hitboxTargetFillOverride;
        public boolean hitboxFriendlyTint,hitboxHurtTint,hitboxHideAllies,hitboxHideMenus,hitboxWeaponOnly,hitboxCrouchOnly,hitboxFade,hitboxLookArrow;
        public int hitboxFillColor=0xB9B8FF,hitboxTargetFillColor=0x48D6C5,hitboxFriendlyColor=0x74B9FF,hitboxHurtColor=0xFF718B;
        public int hitboxColorMode,hitboxTargetStyle,hitboxDashCount=10,hitboxMinDistance,hitboxCameraMode;
        public float hitboxTargetWidth=2.5f,hitboxTargetFillOpacity=.15f,hitboxCornerLength=.22f,hitboxLookLength=1.5f,hitboxFadeStart=.65f;
    }
    public static class Waypoint {
        public String name="Orbit", scope="";
        public double x,y,z;
    }
    public State state(Module module) { return modules.computeIfAbsent(module, ClientConfig::defaults); }
    public static State defaults(Module module) {
        State s = new State(); s.enabled=module.defaultEnabled; s.scale=.85f;
        s.x=.02; s.y=.04 + module.ordinal()*.05;
        if (module==Module.FPS) s.y=.03;
        if (module==Module.PING) s.y=.15;
        if (module==Module.CPS) s.y=.27;
        if (module==Module.KEYS) { s.x=.02; s.y=.40; }
        if (module==Module.ARMOR) { s.x=.77; s.y=.20; }
        return s;
    }
    public static ClientConfig load() {
        ClientConfig config = new ClientConfig();
        if (Files.exists(FILE)) {
            try (var reader=Files.newBufferedReader(FILE)) {
                ClientConfig loaded=parse(com.google.gson.JsonParser.parseReader(reader));
                if (loaded != null && loaded.modules != null) config=loaded;
            } catch (Exception e) {
                OrbitClient.LOG.warn("Could not read Orbit Client configuration; keeping a backup",e);
                try { Files.copy(FILE,FILE.resolveSibling("orbit-client.invalid.json"),StandardCopyOption.REPLACE_EXISTING); }
                catch(IOException ignored) { }
            }
        }
        for (Module module:Module.values()) {
            State s=config.state(module);
            if(config.layoutVersion<3){
                if(s.backgroundColor==0x101927)s.backgroundColor=0x171E2E;
                if(s.textColor==0xF1F5FA)s.textColor=0xF4F6FF;
            }
            s.hmiOffsetX=(float)finite(s.hmiOffsetX,-.4,.4,0);s.hmiOffsetY=(float)finite(s.hmiOffsetY,-.4,.4,0);s.hmiOffsetZ=(float)finite(s.hmiOffsetZ,-.4,.4,0);
            s.hmiScale=(float)finite(s.hmiScale,.7,1.3,1);s.hmiMotion=(float)finite(s.hmiMotion,0,1.5,1);
            if(config.layoutVersion<2) {
                State old=defaults(module);
                s.customPosition=Math.abs(s.x-old.x)>.001 || Math.abs(s.y-old.y)>.001;
                if(Math.abs(s.scale-1)<.001) s.scale=.85f;
            }
            s.x=finite(s.x,0,1,.02); s.y=finite(s.y,0,1,.04);
            s.scale=(float)finite(s.scale,.65,2,1); s.opacity=(float)finite(s.opacity,0,1,.72);
            s.textOpacity=(float)finite(s.textOpacity,.2,1,1);
            s.color=Math.floorMod(s.color,COLORS.length);
            s.decimals=Math.clamp(s.decimals,0,2);
            s.crosshairSize=Math.clamp(s.crosshairSize,2,12);
            s.crosshairGap=Math.clamp(s.crosshairGap,0,8);
            s.crosshairThickness=Math.clamp(s.crosshairThickness,1,3);
            s.zoomFactor=(float)finite(s.zoomFactor,2,10,4);
            s.customColor=s.customColor<0?-1:Math.clamp(s.customColor,0,0xFFFFFF);
            s.padding=Math.clamp(s.padding,4,16);s.alignment=Math.clamp(s.alignment,0,2);
            s.hudStyle=Math.clamp(s.hudStyle,0,3);s.panelWidth=s.panelWidth==0?0:Math.clamp(s.panelWidth,90,240);
            s.rowSpacing=Math.clamp(s.rowSpacing,0,8);s.backgroundColor=Math.clamp(s.backgroundColor,0,0xFFFFFF);s.textColor=Math.clamp(s.textColor,0,0xFFFFFF);
            s.effectsLimit=Math.clamp(s.effectsLimit,1,8);s.reachSeconds=Math.clamp(s.reachSeconds,1,15);
            s.crosshairShape=Math.clamp(s.crosshairShape,0,4);s.dotSize=Math.clamp(s.dotSize,1,5);
            s.outlineWidth=Math.clamp(s.outlineWidth,1,3);s.dynamicGap=Math.clamp(s.dynamicGap,0,8);
            s.crosshairMode=Math.clamp(s.crosshairMode,0,2);s.crosshairPreset=Math.clamp(s.crosshairPreset,0,13);
            s.crosshairColorMode=Math.clamp(s.crosshairColorMode,0,2);s.crosshairScale=Math.clamp(s.crosshairScale,0,3);
            s.enemyColor=Math.clamp(s.enemyColor,0,0xFFFFFF);s.friendlyColor=Math.clamp(s.friendlyColor,0,0xFFFFFF);
            s.chunkRenderDistance=Math.clamp(s.chunkRenderDistance,8,32);
            s.skinThickness=(float)finite(s.skinThickness,1,1.5,1.1);s.skinDistance=Math.clamp(s.skinDistance,8,64);
            s.glintColor=Math.clamp(s.glintColor,0,0xFFFFFF);s.glintOpacity=(float)finite(s.glintOpacity,0,1,.7);
            s.glowOutlineColor=Math.clamp(s.glowOutlineColor,0,0xFFFFFF);
            s.glowOutlineOpacity=(float)finite(s.glowOutlineOpacity,0,1,.8);s.glowWidth=(float)finite(s.glowWidth,.5,4,1.6);s.glowSoftness=(float)finite(s.glowSoftness,0,1,.65);
            s.hitboxColor=Math.clamp(s.hitboxColor,0,0xFFFFFF);s.hitboxTargetColor=Math.clamp(s.hitboxTargetColor,0,0xFFFFFF);
            s.hitboxOpacity=(float)finite(s.hitboxOpacity,0,1,.65);s.hitboxTargetOpacity=(float)finite(s.hitboxTargetOpacity,0,1,1);
            s.hitboxWidth=(float)finite(s.hitboxWidth,.5,4,1.5);s.hitboxFillOpacity=(float)finite(s.hitboxFillOpacity,0,.5,.1);
            s.hitboxDistance=Math.clamp(s.hitboxDistance,8,128);s.hitboxStyle=Math.clamp(s.hitboxStyle,0,2);
            s.hitboxFillColor=Math.clamp(s.hitboxFillColor,0,0xFFFFFF);s.hitboxTargetFillColor=Math.clamp(s.hitboxTargetFillColor,0,0xFFFFFF);
            s.hitboxFriendlyColor=Math.clamp(s.hitboxFriendlyColor,0,0xFFFFFF);s.hitboxHurtColor=Math.clamp(s.hitboxHurtColor,0,0xFFFFFF);
            s.hitboxColorMode=Math.clamp(s.hitboxColorMode,0,1);s.hitboxCameraMode=Math.clamp(s.hitboxCameraMode,0,2);s.hitboxTargetStyle=Math.clamp(s.hitboxTargetStyle,0,2);
            s.hitboxDashCount=Math.clamp(s.hitboxDashCount,4,32);s.hitboxMinDistance=Math.clamp(s.hitboxMinDistance,0,s.hitboxDistance-1);
            s.hitboxTargetWidth=(float)finite(s.hitboxTargetWidth,.5,4,2.5);s.hitboxTargetFillOpacity=(float)finite(s.hitboxTargetFillOpacity,0,.5,.15);
            s.hitboxCornerLength=(float)finite(s.hitboxCornerLength,.08,.45,.22);s.hitboxLookLength=(float)finite(s.hitboxLookLength,.25,6,1.5);
            s.hitboxFadeStart=(float)finite(s.hitboxFadeStart,.1,.95,.65);
            if(s.crosshairPixels==null || !s.crosshairPixels.matches("[01]{225}"))s.crosshairPixels="";
        }
        config.layoutVersion=3;
        if(config.waypoint!=null && (config.waypoint.scope==null || config.waypoint.name==null
            || !Double.isFinite(config.waypoint.x) || !Double.isFinite(config.waypoint.y) || !Double.isFinite(config.waypoint.z))) config.waypoint=null;
        return config;
    }
    static ClientConfig parse(com.google.gson.JsonElement tree) {
        if(tree.isJsonObject()) {
            var modules=tree.getAsJsonObject().getAsJsonObject("modules");
            // Remove the retired entry before Gson reads the EnumMap, preserving every other setting.
            if(modules!=null)modules.remove("HP_INDICATOR");
            if(modules!=null && modules.has("COOL_ANIM")) {
                var old=modules.remove("COOL_ANIM");
                if(!modules.has("HOLD_MY_ITEMS")) modules.add("HOLD_MY_ITEMS",old);
            }
        }
        return JSON.fromJson(tree,ClientConfig.class);
    }
    private static double finite(double v,double min,double max,double fallback) {
        return Double.isFinite(v) ? Math.clamp(v,min,max) : fallback;
    }
    public static int accent(State state) {return state.customColor<0?COLORS[state.color]:0xFF000000|state.customColor;}
    public boolean save() {
        try {
            Files.createDirectories(FILE.getParent());
            Path temp=FILE.resolveSibling("orbit-client.json.tmp");
            try(var writer=Files.newBufferedWriter(temp)) { JSON.toJson(this,writer); }
            try { Files.move(temp,FILE,StandardCopyOption.REPLACE_EXISTING,StandardCopyOption.ATOMIC_MOVE); }
            catch(AtomicMoveNotSupportedException e) { Files.move(temp,FILE,StandardCopyOption.REPLACE_EXISTING); }
            return true;
        } catch(Exception e) { OrbitClient.LOG.error("Could not save Orbit Client settings",e); return false; }
    }
}
