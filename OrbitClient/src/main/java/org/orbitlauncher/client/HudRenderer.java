package org.orbitlauncher.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.resources.Identifier;
import net.minecraft.client.gui.Hud;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.world.effect.MobEffectInstance;
import net.minecraft.world.effect.MobEffects;
import java.util.*;

public final class HudRenderer {
    public record Bounds(int x,int y,int width,int height) {
        public boolean contains(double px,double py) { return px>=x && py>=y && px<x+width && py<y+height; }
    }
    public static final EnumMap<Module,Bounds> bounds=new EnumMap<>(Module.class);
    private record Row(String text,ItemStack item,Identifier sprite) {
        Row(String text){this(text,ItemStack.EMPTY,null);}
        boolean hasIcon(){return !item.isEmpty() || sprite!=null;}
        int height(){return hasIcon()?22:12;}
    }
    private record View(Module module,ClientConfig.State state,List<Row> rows,int width,int height) { }
    public static boolean customCrosshairVisible() {
        var mc=Minecraft.getInstance();
        var state=OrbitClient.config.state(Module.CROSSHAIR);
        return state.enabled && mc.player!=null && !mc.player.isSpectator()
            && (state.crosshairThirdPerson || mc.options.getCameraType().isFirstPerson())
            && (!state.crosshairHideMenus || mc.gui.screen()==null);
    }
    public static void render(GuiGraphicsExtractor g,boolean editor) {
        render(g,editor,-1,-1);
    }
    static boolean drawing;
    static void render(GuiGraphicsExtractor g,boolean editor,int mouseX,int mouseY) {
        boolean previous=drawing;drawing=true;
        try{draw(g,editor,mouseX,mouseY);}finally{drawing=previous;}
    }
    private static void draw(GuiGraphicsExtractor g,boolean editor,int mouseX,int mouseY) {
        var mc=Minecraft.getInstance();
        OrbitTheme.update();var a=OrbitTheme.options().hud;
        if(Boolean.getBoolean("orbit.client.refill-smoke"))RefillSmoke.frame(mc);
        bounds.clear();
        if((mc.player==null && !editor) || (!editor && (mc.gui.hud.isHidden() || mc.gui.screen() instanceof HudEditorScreen))) return;
        var views=new ArrayList<View>();
        var specs=new ArrayList<HudLayout.Spec>();
        for(Module m:Module.values()) {
            var s=OrbitClient.config.state(m);
            if(!s.enabled || !m.hasHud()) continue;
            List<Row> rows=rows(m,editor);
            int titleWidth=a.showTitle?OrbitFonts.strong().width(m.title())+17:0;
            int naturalWidth=m==Module.KEYS?Math.max(108,titleWidth+a.padding*2):Math.max(90,Math.max(titleWidth,rows.stream().mapToInt(r->OrbitFonts.ui().width(r.text)+(r.hasIcon()?24:0)).max().orElse(70))+a.padding*2);
            int w=a.panelWidth==0?Math.min(naturalWidth,210):a.panelWidth;
            int header=a.showTitle?a.padding+13:a.padding;
            int h=header+(m==Module.KEYS?(s.showMouse?75:50)+(s.showSpace?8:2):rows.stream().mapToInt(Row::height).sum()+Math.max(0,rows.size()-1)*a.rowSpacing)+a.padding;
            views.add(new View(m,s,rows,w,h));
            specs.add(new HudLayout.Spec(m.ordinal(),(int)Math.ceil(w*s.scale),(int)Math.ceil(h*s.scale),s.customPosition,s.x,s.y));
        }
        int priority=editor && mc.gui.screen() instanceof HudEditorScreen screen?screen.selectedOrdinal():-1;
        var layout=HudLayout.arrange(g.guiWidth(),g.guiHeight(),specs,priority);
        for(var view:views) {
            var m=view.module;var s=view.state;int w=view.width,h=view.height;
            var b=layout.boxes().get(m.ordinal());
            if(b==null) continue;
            bounds.put(m,new Bounds(b.x(),b.y(),b.width(),b.height()));
            float scale=s.scale*layout.scale();
            g.pose().pushMatrix(); g.pose().translate(b.x(),b.y()); g.pose().scale(scale,scale);
            int bg=((int)(a.opacity*255)<<24)|(OrbitTheme.CARD&0xFFFFFF);
            if(a.background) {
                if(a.hudStyle==0 || a.hudStyle==2)panel(g,0,0,w,h,a.hudStyle==2?(Math.min(90,(int)(a.opacity*255))<<24)|(OrbitTheme.CARD&0xFFFFFF):bg);
                if(a.hudStyle!=3&&(a.hudBorder || a.hudStyle==1))outline(g,0,0,w,h,((int)(Math.max(.35,a.opacity)*255)<<24)|(a.hudStyle==1?OrbitTheme.PRIMARY&0xFFFFFF:OrbitTheme.BORDER&0xFFFFFF));
                if(a.accentBar&&a.hudStyle!=3)OrbitTheme.accentLine(g,a.padding,a.showTitle?a.padding+10:2,Math.max(1,w-a.padding*2));
            }
            int textAlpha=(int)(a.textOpacity*255)<<24;
            if(a.showTitle) {
                int headingColor=textAlpha|(a.accentTitle?OrbitTheme.PRIMARY&0xFFFFFF:(OrbitTheme.TEXT&0xFFFFFF));
                ModuleIcons.draw(g,m,a.padding,a.padding-2,12,headingColor);
                g.text(OrbitFonts.strong(),OrbitFonts.strong().plainSubstrByWidth(m.title(),Math.max(1,w-a.padding*2-17)),a.padding+17,a.padding,headingColor,false);
            }
            int header=a.showTitle?a.padding+13:a.padding;
            if(m==Module.KEYS) keys(g,s,w,header);
            else for(var row:view.rows) {
                int iconWidth=row.hasIcon()?24:0;
                String text=OrbitFonts.ui().plainSubstrByWidth(row.text,w-a.padding*2-iconWidth);
                int groupWidth=OrbitFonts.ui().width(text)+iconWidth;
                int textX=a.alignment==1?(w-groupWidth)/2:a.alignment==2?w-a.padding-groupWidth:a.padding;
                if(!row.item.isEmpty())g.item(row.item,textX,header+1);
                if(row.sprite!=null)g.blitSprite(RenderPipelines.GUI_TEXTURED,row.sprite,textX,header,18,18);
                g.text(OrbitFonts.ui(),text,textX+iconWidth,header+(row.hasIcon()?5:0),textAlpha|(OrbitTheme.TEXT&0xFFFFFF),false);
                header+=row.height()+a.rowSpacing;
            }
            if(editor) {
                boolean highlighted=m.ordinal()==priority||bounds.get(m).contains(mouseX,mouseY);
                OrbitTheme.outline(g,0,0,w,h,highlighted?OrbitTheme.ACCENT:OrbitTheme.FOCUS);
            }
            g.pose().popMatrix();
        }
        if(customCrosshairVisible()) CrosshairRenderer.draw(g,g.guiWidth()/2,g.guiHeight()/2,
            OrbitClient.config.state(Module.CROSSHAIR),mc.player.getDeltaMovement().horizontalDistance()>.02);
    }
    private static List<Row> rows(Module m,boolean editor) {
        var p=Minecraft.getInstance().player;var s=OrbitClient.config.state(m);
        var result=new ArrayList<Row>();
        if(m==Module.ARMOR) {
            var items=p==null?List.<ItemStack>of():List.of(p.getItemBySlot(EquipmentSlot.HEAD),p.getItemBySlot(EquipmentSlot.CHEST),p.getItemBySlot(EquipmentSlot.LEGS),p.getItemBySlot(EquipmentSlot.FEET));
            if(editor && p!=null && items.stream().allMatch(ItemStack::isEmpty))items=List.of(new ItemStack(Items.DIAMOND_HELMET),new ItemStack(Items.DIAMOND_CHESTPLATE),new ItemStack(Items.DIAMOND_LEGGINGS),new ItemStack(Items.DIAMOND_BOOTS));
            for(var item:items)if(!item.isEmpty())result.add(new Row(durability(item,s.showPercent),item.copy(),null));
            if(result.isEmpty())result.add(new Row(OrbitClient.tr("Броня не надета","No armor equipped")));
            return result;
        }
        if(m==Module.EFFECTS) {
            var effects=p==null?List.<MobEffectInstance>of():p.getActiveEffects().stream().sorted(Comparator.comparing(e->e.getEffect().getRegisteredName())).toList();
            if(editor && effects.isEmpty())effects=List.of(new MobEffectInstance(MobEffects.SPEED,2400),new MobEffectInstance(MobEffects.STRENGTH,1200,1));
            for(var effect:effects.stream().limit(s.effectsLimit).toList()) {
                int sec=Math.max(0,(effect.getDuration()+19)/20);
                String time=effect.isInfiniteDuration()?"∞":String.format(Locale.ROOT,"%d:%02d",sec/60,sec%60);
                result.add(new Row((effect.getAmplifier()>0?(effect.getAmplifier()+1)+" • ":"")+time,ItemStack.EMPTY,Hud.getMobEffectSprite(effect.getEffect())));
            }
            if(result.isEmpty())result.add(new Row(OrbitClient.tr("Нет эффектов","No active effects")));
            return result;
        }
        return lines(m).stream().map(Row::new).toList();
    }
    private static void keys(GuiGraphicsExtractor g,ClientConfig.State s,int w,int y) {
        var o=Minecraft.getInstance().options;
        int x=(w-72)/2;
        key(g,x+25,y,22,"W",o.keyUp.isDown(),s);
        key(g,x,y+25,22,"A",o.keyLeft.isDown(),s);
        key(g,x+25,y+25,22,"S",o.keyDown.isDown(),s);
        key(g,x+50,y+25,22,"D",o.keyRight.isDown(),s);
        if(s.showMouse) {
            key(g,x,y+50,35,"L "+OrbitClient.cps(false),o.keyAttack.isDown(),s);
            key(g,x+38,y+50,34,"R "+OrbitClient.cps(true),o.keyUse.isDown(),s);
        }
        int spaceY=y+(s.showMouse?76:51);
        if(s.showSpace) OrbitTheme.rounded(g,x,spaceY,72,3,o.keyJump.isDown()?OrbitTheme.PRIMARY:OrbitTheme.CONTROL);
    }
    private static void key(GuiGraphicsExtractor g,int x,int y,int w,String label,boolean down,ClientConfig.State s) {
        panel(g,x,y,w,22,down?OrbitTheme.PRIMARY:OrbitTheme.translucent(OrbitTheme.CARD,(int)(OrbitTheme.options().hud.opacity*255)));
        var font=OrbitFonts.ui();
        g.centeredText(font,font.plainSubstrByWidth(label,w-4),x+w/2,y+7,OrbitTheme.translucent(down?OrbitTheme.ON_PRIMARY:OrbitTheme.TEXT,(int)(OrbitTheme.options().hud.textOpacity*255)));
    }
    public static void panel(GuiGraphicsExtractor g,int x,int y,int w,int h,int color) {
        OrbitTheme.rounded(g,x,y,w,h,color);
    }
    private static void outline(GuiGraphicsExtractor g,int x,int y,int w,int h,int color) {
        OrbitTheme.outline(g,x,y,w,h,color);
    }
    private static List<String> lines(Module m) {
        var mc=Minecraft.getInstance(); var p=mc.player;
        if(p==null) return m==Module.ARMOR || m==Module.EFFECTS?List.of("—","—","—","—"):List.of("—");
        return switch(m) {
            case FPS -> List.of(mc.getFps()+(OrbitClient.config.state(m).showUnits?" FPS":""));
            case PING -> List.of(PingMonitor.label(OrbitClient.config.state(m).showUnits));
            case CPS -> OrbitClient.config.state(m).splitCps?List.of("L: "+OrbitClient.cps(false),"R: "+OrbitClient.cps(true)):List.of(OrbitClient.cps(false)+" L  /  "+OrbitClient.cps(true)+" R");
            case COORDS -> {int d=OrbitClient.config.state(m).decimals;yield List.of(String.format(Locale.ROOT,"X %."+d+"f  Y %."+d+"f",p.getX(),p.getY()),String.format(Locale.ROOT,"Z %."+d+"f",p.getZ()));}
            case SPEED -> List.of(String.format(Locale.ROOT,"%.2f blocks/s",p.getDeltaMovement().horizontalDistance()*20));
            case SESSION -> { long sec=Math.max(0,(System.nanoTime()-OrbitClient.sessionStart)/1_000_000_000L); yield List.of(String.format(Locale.ROOT,"%02d:%02d:%02d",sec/3600,sec/60%60,sec%60)); }
            case REACH -> List.of(ClientFeatures.lastAttack==0 || System.nanoTime()-ClientFeatures.lastAttack>OrbitClient.config.state(m).reachSeconds*1_000_000_000L?"—":String.format(Locale.ROOT,"%.2f m",ClientFeatures.lastReach));
            case SERVER -> List.of(mc.getCurrentServer()==null?OrbitClient.tr("Одиночная игра","Singleplayer"):mc.getCurrentServer().ip);
            case MEMORY -> {var rt=Runtime.getRuntime();long used=rt.totalMemory()-rt.freeMemory();yield List.of(used/1048576+" / "+rt.maxMemory()/1048576+" MB");}
            case WAYPOINT -> {
                var point=OrbitClient.config.waypoint;
                if(point==null) yield List.of(OrbitClient.tr("Добавьте точку в настройках","Add a waypoint in settings"));
                if(!point.scope.equals(ClientFeatures.scope())) yield List.of(OrbitClient.tr("Точка в другом мире","Waypoint is in another world"));
                double distance=Math.sqrt(Math.pow(p.getX()-point.x,2)+Math.pow(p.getY()-point.y,2)+Math.pow(p.getZ()-point.z,2));
                yield List.of(point.name+String.format(Locale.ROOT,"  %.0f m",distance),String.format(Locale.ROOT,"%.0f / %.0f / %.0f",point.x,point.y,point.z));
            }
            default -> List.of();
        };
    }
    private static String durability(ItemStack item,boolean percent) {
        return item.isDamageableItem()?(percent?Math.round(100d*(item.getMaxDamage()-item.getDamageValue())/item.getMaxDamage())+"%":(item.getMaxDamage()-item.getDamageValue())+" / "+item.getMaxDamage()):"×"+item.getCount();
    }
}
