package org.orbitlauncher.client;

import java.util.Arrays;
public final class GlowContourTest {
    private static void require(boolean ok){if(!ok)throw new AssertionError("Invalid glow silhouette");}
    public static void main(String[] args){
        require(GlowContour.trace(new boolean[16],4,4).isEmpty());
        for(int size:new int[]{1,16,32,64,128,256}){
            boolean[] full=new boolean[size*size];Arrays.fill(full,true);
            var edges=GlowContour.trace(full,size,size);require(edges.size()==4);
            require(edges.stream().mapToInt(e->Math.abs(e.x1()-e.x0())+Math.abs(e.y1()-e.y0())).sum()==size*4);
        }
        boolean[] ring={true,true,true,true,false,true,true,true,true};
        require(GlowContour.trace(ring,3,3).size()==8);
        boolean[] diagonal={true,false,false,true};require(GlowContour.trace(diagonal,2,2).size()==6);
        boolean[] sword={false,false,true,false,false,true,false,true,true,true,false,true};
        var edges=GlowContour.trace(sword,3,4);require(edges.size()>4);
        require(edges.stream().noneMatch(e->e.x0()==0&&e.y0()==0));
        System.out.println("Glow contour: empty/solid, holes, disconnected pixels, weapon silhouettes and 16–256px packs passed");
    }
}
