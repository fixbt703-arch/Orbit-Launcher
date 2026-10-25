package org.orbitlauncher.client;

/** All four headers remain reachable even at Minecraft's smallest supported GUI size. */
final class ClickGuiLayout {
    record Frame(int x,int y,int width,int maxHeight){}
    static Frame frame(int width,int height,int index){
        int columns=width>=600?4:2,rows=4/columns,gap=height<210?6:12;
        int panelWidth=Math.min(158,(width-24-gap*(columns-1))/columns);
        int startX=(width-columns*panelWidth-gap*(columns-1))/2;
        int top=rows==1?Math.max(64,(int)(height*.265)):height<210?52:54;
        int slot=Math.max(36,(height-(height<210?38:42)-top-gap*(rows-1))/rows);
        return new Frame(startX+(index%columns)*(panelWidth+gap),top+(index/columns)*(slot+gap),panelWidth,slot);
    }
    static int x(double normalized,int width,int panelWidth){return (int)Math.round(Math.clamp(Double.isFinite(normalized)?normalized:0,0,1)*Math.max(0,width-panelWidth-12))+6;}
    static int y(double normalized,int height){return 52+(int)Math.round(Math.clamp(Double.isFinite(normalized)?normalized:0,0,1)*Math.max(0,height-132));}
    private ClickGuiLayout(){}
}
