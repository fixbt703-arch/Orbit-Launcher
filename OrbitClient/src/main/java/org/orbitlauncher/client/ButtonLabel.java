package org.orbitlauncher.client;

import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import java.util.List;

/** Preserve the whole label: wrap long settings labels, then fit inside the padded area. */
final class ButtonLabel {
    record Layout(List<String> lines,float scale){}
    static Layout layout(Font font,String text,int width,int height) {
        List<String> lines=List.of(text);
        if(height>=24 && font.width(text)>width*1.15) {
            int split=-1,best=Integer.MAX_VALUE;
            for(int i=1;i<text.length()-1;i++)if(text.charAt(i)==' ') {
                int extent=Math.max(font.width(text.substring(0,i)),font.width(text.substring(i+1)));
                if(extent<best){best=extent;split=i;}
            }
            if(split>0)lines=List.of(text.substring(0,split),text.substring(split+1));
        }
        int max=lines.stream().mapToInt(font::width).max().orElse(0);
        float scale=Math.min(1,Math.min(Math.max(1,width)/(float)Math.max(1,max),Math.max(1,height)/(float)(lines.size()*10-1)));
        return new Layout(lines,scale);
    }
    static void draw(GuiGraphicsExtractor g,Font font,String text,int x,int y,int width,int height,int color) {
        if(text.isEmpty() || width<=0 || height<=0)return;
        var layout=layout(font,text,width,height);float scale=layout.scale();
        float blockHeight=(layout.lines().size()*10-1)*scale;
        g.pose().pushMatrix();g.pose().translate(x+width/2f,y+(height-blockHeight)/2f);g.pose().scale(scale,scale);
        for(int i=0;i<layout.lines().size();i++){String line=layout.lines().get(i);g.text(font,line,-font.width(line)/2,i*10,color,false);}
        g.pose().popMatrix();
    }
    private ButtonLabel(){}
    static void left(GuiGraphicsExtractor g,Font font,String text,int x,int y,int width,int height,int color) {
        if(text.isEmpty() || width<=0 || height<=0)return;
        var layout=settingLayout(font,text,width,height);
        g.pose().pushMatrix();g.pose().translate(x,y+(height-(layout.lines().size()*10-1)*layout.scale())/2f);
        g.pose().scale(layout.scale(),layout.scale());
        for(int i=0;i<layout.lines().size();i++)g.text(font,layout.lines().get(i),0,i*10,color,false);
        g.pose().popMatrix();
    }
    static Layout settingLayout(Font font,String text,int width,int height) {
        if(height<19)return layout(font,text,width,height);
        List<String> lines=List.of(text);
        if(font.width(text)>width){
            int split=-1,best=Integer.MAX_VALUE;
            for(int i=1;i<text.length()-1;i++)if(text.charAt(i)==' '){
                int extent=Math.max(font.width(text.substring(0,i)),font.width(text.substring(i+1)));
                if(extent<best){best=extent;split=i;}
            }
            if(split>0)lines=List.of(text.substring(0,split),text.substring(split+1));
        }
        return new Layout(lines.stream().map(line->OrbitTheme.ellipsize(font,line,width)).toList(),1);
    }
}
