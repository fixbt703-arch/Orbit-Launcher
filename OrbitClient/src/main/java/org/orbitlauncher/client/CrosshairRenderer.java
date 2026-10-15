package org.orbitlauncher.client;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.world.entity.animal.Animal;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.monster.Enemy;
import net.minecraft.world.entity.player.Player;
import java.util.*;

public final class CrosshairRenderer {
    private record Key(int mode,int shape,int preset,String pixels,int size,int gap,int thickness,int dot,int outline){}
    private record Span(int x,int y,int end){}
    private record Mask(List<Span> body,List<Span> outline){}
    private static final Map<Key,Mask> CACHE=new LinkedHashMap<>() {
        @Override protected boolean removeEldestEntry(Map.Entry<Key,Mask> e){return size()>64;}
    };
    private CrosshairRenderer() {}
    public static void draw(GuiGraphicsExtractor g,int x,int y,ClientConfig.State s,boolean moving) {
        draw(g,x,y,s,moving,-1);
    }
    /** previewTarget: -1 = actual game target, 0 = no target, 1 = target, 2 = friendly. */
    static void draw(GuiGraphicsExtractor g,int x,int y,ClientConfig.State s,boolean moving,int previewTarget) {
        int gap=s.crosshairGap+(s.crosshairDynamic && moving?s.dynamicGap:0);
        var key=new Key(s.crosshairMode,s.crosshairShape,s.crosshairPreset,s.crosshairMode==2?CrosshairPattern.pixels(s):"",s.crosshairSize,gap,s.crosshairThickness,s.crosshairDot||s.crosshairShape==2?s.dotSize:0,s.crosshairOutline?s.outlineWidth:0);
        var mask=CACHE.computeIfAbsent(key,CrosshairRenderer::mask);
        int rgb=ClientConfig.accent(s);
        var mc=Minecraft.getInstance();var target=mc.crosshairPickEntity;
        if(s.crosshairChroma)rgb=java.awt.Color.HSBtoRGB((System.currentTimeMillis()%6000)/6000f,.75f,1);
        if(s.crosshairColorMode==1) {
            if(previewTarget>0)rgb=previewTarget==2 && s.separateFriendlyColor?s.friendlyColor:s.enemyColor;
            else if(previewTarget<0 && target!=null && mc.player!=null && targetAllowed(target,s))rgb=s.separateFriendlyColor && (target.isAlliedTo(mc.player) || target instanceof Animal)?s.friendlyColor:s.enemyColor;
        }
        int alpha=(int)(s.opacity*255)<<24;
        float scale=s.crosshairMode==0 || s.crosshairScale==0?1:(float)(s.crosshairScale*2/mc.getWindow().getGuiScale());
        g.pose().pushMatrix();g.pose().translate(x,y);g.pose().scale(scale,scale);
        // Each pixel is emitted once, preventing double alpha or inversion cancellation.
        for(var a:mask.outline)g.fill(a.x,a.y,a.end,a.y+1,alpha|0x05080D);
        var pipeline=s.crosshairColorMode==2?RenderPipelines.GUI_INVERT:RenderPipelines.GUI;
        int c=alpha|(s.crosshairColorMode==2?0xFFFFFF:rgb&0xFFFFFF);
        for(var a:mask.body)g.fill(pipeline,a.x,a.y,a.end,a.y+1,c);
        g.pose().popMatrix();
    }
    static boolean targetAllowed(Entity target,ClientConfig.State s) {
        if(target instanceof Player)return s.targetPlayers;
        if(target instanceof Enemy)return s.targetHostile;
        if(target instanceof LivingEntity)return s.targetPassive;
        return s.targetOther;
    }
    private static Mask mask(Key k) {
        int n=81,center=n/2;boolean[][] body=new boolean[n][n],edge=new boolean[n][n];
        String pixels=k.mode==1?CrosshairPattern.preset(k.preset):k.pixels;
        for(int y=-center;y<=center;y++)for(int x=-center;x<=center;x++) {
            int a=Math.abs(x),b=Math.abs(y),r2=x*x+y*y;boolean on=false;
            if(k.mode!=0){if(a<=7&&b<=7)on=pixels.charAt((y+7)*15+x+7)=='1';}
            else {
                boolean h=b<k.thickness && a>=k.gap && a<k.gap+k.size;
                boolean v=a<k.thickness && b>=k.gap && b<k.gap+k.size;
                on=switch(k.shape) {
                    case 1 -> h || v&&y>=0;
                    case 2 -> r2<=k.dot*k.dot;
                    case 3 -> r2<=Math.pow(k.gap+k.size+k.thickness,2)&&r2>=Math.pow(Math.max(0,k.gap+k.size-k.thickness),2);
                    case 4 -> Math.abs(a-b)<k.thickness && Math.max(a,b)>=k.gap&&Math.max(a,b)<k.gap+k.size;
                    default -> h||v;
                };
                if(k.dot>0 && r2<=k.dot*k.dot)on=true;
            }
            body[y+center][x+center]=on;
        }
        if(k.outline>0)for(int y=0;y<n;y++)for(int x=0;x<n;x++)if(body[y][x]) {
            for(int dy=-k.outline;dy<=k.outline;dy++)for(int dx=-k.outline;dx<=k.outline;dx++) {
                int xx=x+dx,yy=y+dy;if(xx>=0&&yy>=0&&xx<n&&yy<n&&!body[yy][xx])edge[yy][xx]=true;
            }
        }
        return new Mask(spans(body,center),spans(edge,center));
    }
    private static List<Span> spans(boolean[][] a,int center) {
        var result=new ArrayList<Span>();
        for(int y=0;y<a.length;y++)for(int x=0;x<a.length;x++)if(a[y][x]) {
            int start=x;while(x+1<a.length&&a[y][x+1])x++;result.add(new Span(start-center,y-center,x+1-center));
        }return result;
    }
    static void pattern(GuiGraphicsExtractor g,String pixels,int x,int y,int cell,int color) {
        for(int row=0;row<15;row++)for(int col=0;col<15;col++)if(pixels.charAt(row*15+col)=='1')g.fill(x+col*cell,y+row*cell,x+(col+1)*cell,y+(row+1)*cell,color);
    }
}
