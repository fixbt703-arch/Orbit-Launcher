package org.orbitlauncher.client;

import java.util.*;

public final class HudLayoutTest {
    public static void main(String[] args) {
        int cases=0;
        for(int[] viewport:new int[][]{{320,180},{427,240},{480,270},{640,360},{960,540},{1920,1080}}) {
            for(int count:new int[]{5,15,22}) for(boolean custom:new boolean[]{false,true}) for(double scale:new double[]{.65,.85,2}) for(boolean priority:new boolean[]{false,true}) {
                var specs=new ArrayList<HudLayout.Spec>();
                for(int i=0;i<count;i++) specs.add(new HudLayout.Spec(i,(int)Math.ceil((i%3==0?210:108)*scale),(int)Math.ceil((i%4==0?180:36)*scale),custom,.02,.03));
                var result=HudLayout.arrange(viewport[0],viewport[1],specs,priority?count-1:-1);
                if(result.boxes().size()!=count) throw new AssertionError("Missing modules");
                var boxes=new ArrayList<>(result.boxes().values());
                for(int i=0;i<boxes.size();i++) {
                    var a=boxes.get(i);
                    if(a.x()<0 || a.y()<0 || a.x()+a.width()>viewport[0] || a.y()+a.height()>viewport[1]-28) throw new AssertionError("Clipped panel");
                    for(int j=i+1;j<boxes.size();j++) if(a.intersects(boxes.get(j),0)) throw new AssertionError("Overlapping panels");
                }
                cases++;
            }
        }
        var pixel=HudLayout.arrange(427,240,List.of(new HudLayout.Spec(1,80,30,true,61.0/427,49.0/240))).boxes().get(1);
        if(pixel.x()!=61 || pixel.y()!=49)throw new AssertionError("Pixel-precise positioning drifted");
        System.out.println("HUD layout: "+cases+" viewport/module combinations and pixel precision passed");
    }
}
