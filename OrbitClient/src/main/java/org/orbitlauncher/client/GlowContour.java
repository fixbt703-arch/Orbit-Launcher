package org.orbitlauncher.client;

import java.util.ArrayList;
import java.util.List;

/** Traces exposed pixel edges and merges straight runs; holes stay transparent. */
final class GlowContour {
    record Edge(int x0,int y0,int x1,int y1){}
    static List<Edge> trace(boolean[] solid,int width,int height){
        if(width<1 || height<1 || solid.length!=width*height)throw new IllegalArgumentException("Invalid silhouette");
        var result=new ArrayList<Edge>();
        for(int y=0;y<=height;y++){
            int start=-1;
            for(int x=0;x<=width;x++){
                boolean edge=x<width && ((y>0&&solid[(y-1)*width+x]) != (y<height&&solid[y*width+x]));
                if(edge&&start<0)start=x;
                if(!edge&&start>=0){result.add(new Edge(start,y,x,y));start=-1;}
            }
        }
        for(int x=0;x<=width;x++){
            int start=-1;
            for(int y=0;y<=height;y++){
                boolean edge=y<height && ((x>0&&solid[y*width+x-1]) != (x<width&&solid[y*width+x]));
                if(edge&&start<0)start=y;
                if(!edge&&start>=0){result.add(new Edge(x,start,x,y));start=-1;}
            }
        }
        return result;
    }
    private GlowContour(){}
}
