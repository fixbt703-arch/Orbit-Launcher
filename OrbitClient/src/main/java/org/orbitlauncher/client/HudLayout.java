package org.orbitlauncher.client;

import java.util.*;

/** Pixel-measured layout shared by normal rendering and the drag editor. */
public final class HudLayout {
    public record Spec(int id,int width,int height,boolean custom,double x,double y) { }
    public record Box(int x,int y,int width,int height) {
        public boolean intersects(Box other,int gap) {
            return x<other.x+other.width+gap && x+width+gap>other.x
                && y<other.y+other.height+gap && y+height+gap>other.y;
        }
    }
    public record Result(Map<Integer,Box> boxes,float scale) { }
    private HudLayout() { }

    public static Result arrange(int width,int height,List<Spec> specs) {
        return arrange(width,height,specs,-1);
    }
    public static Result arrange(int width,int height,List<Spec> specs,int priorityId) {
        if(width<16 || height<48) return new Result(Map.of(),1);
        float scale=Math.min(1,Math.min(width/700f,height/400f));
        var ordered=new ArrayList<>(specs);
        ordered.sort(Comparator.comparingInt((Spec s)->s.id==priorityId?0:s.custom?1:2));
        for(int attempt=0;attempt<32;attempt++,scale*=.9f) {
            Map<Integer,Box> placed=new LinkedHashMap<>();
            boolean fits=true;
            for(var spec:ordered) {
                int w=Math.max(1,(int)Math.ceil(spec.width*scale));
                int h=Math.max(1,(int)Math.ceil(spec.height*scale));
                Box found=null;
                if(spec.custom) {
                    var desired=new Box((int)Math.round(Math.clamp(spec.x*width,4,Math.max(4,width-w-4))),
                        (int)Math.round(Math.clamp(spec.y*height,4,Math.max(4,height-h-48))),w,h);
                    if(free(desired,width,height,placed.values())) found=desired;
                }
                if(found==null) {
                    var xs=new TreeSet<Integer>();var ys=new TreeSet<Integer>();xs.add(4);ys.add(4);
                    for(var box:placed.values()) {xs.add(box.x+box.width+4);ys.add(box.y+box.height+4);}
                    search: for(int x:xs) for(int y:ys) {
                        var candidate=new Box(x,y,w,h);
                        if(free(candidate,width,height,placed.values())) {found=candidate;break search;}
                    }
                }
                if(found==null) {fits=false;break;}
                placed.put(spec.id,found);
            }
            if(fits) return new Result(Collections.unmodifiableMap(placed),scale);
        }
        return new Result(Map.of(),scale);
    }
    private static boolean free(Box b,int width,int height,Collection<Box> placed) {
        if(b.x<4 || b.y<4 || b.x+b.width>width-4 || b.y+b.height>height-48) return false;
        for(var existing:placed) if(b.intersects(existing,4)) return false;
        return true;
    }
}
