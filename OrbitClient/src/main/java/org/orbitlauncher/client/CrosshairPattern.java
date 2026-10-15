package org.orbitlauncher.client;
import java.util.Base64;

/** Portable pixel patterns. Coordinates are integral and symmetric. */
public final class CrosshairPattern {
    public static final int SIZE=15;
    private static final String[] PRESETS=new String[14];
    static {
        for(int p=0;p<14;p++) {
            var b=new StringBuilder(225);
            for(int y=-7;y<=7;y++)for(int x=-7;x<=7;x++) {
                int a=Math.abs(x),d=Math.abs(y),r=Math.max(a,d),sum=a+d;
                boolean on=switch(p) {
                    case 0 -> (x==0 || y==0) && r<=6;
                    case 1 -> ((x==0 || y==0)&&r>=2&&r<=6) || (r==4&&Math.min(a,d)>=2);
                    case 2 -> y>=-2 && y<=2 && a==y+2;
                    case 3 -> r<=2 && sum<=3;
                    case 4 -> x*x+y*y>=19 && x*x+y*y<=29;
                    case 5 -> r==3 || r==0;
                    case 6 -> ((x==0 || y==0)&&r<=5) || (r==5&&Math.min(a,d)<=2);
                    case 7 -> a==d&&r>=2&&r<=5 || (r==5&&Math.min(a,d)>=3);
                    case 8 -> ((x==0 || y==0)&&r>=3&&r<=6) || (r==3&&Math.min(a,d)>=2) || r==0;
                    case 9 -> ((x==0 || y==0)&&r>=2&&r<=6) || (r==5&&Math.min(a,d)==2);
                    case 10 -> (x*x+y*y>=7&&x*x+y*y<=13) || y==0&&a<=6 || r==0;
                    case 11 -> a<=5&&d<=5&&Math.abs(a-d)<=1;
                    case 12 -> r<=5&&a>=2&&d>=2&&(Math.abs(a-d)<=1 || r==4);
                    default -> sum==4 || sum==3 || r==0;
                };b.append(on?'1':'0');
            }PRESETS[p]=b.toString();
        }
    }
    private CrosshairPattern(){}
    public static String preset(int id){return PRESETS[Math.clamp(id,0,13)];}
    public static String pixels(ClientConfig.State s){return s.crosshairPixels!=null && s.crosshairPixels.length()==225?s.crosshairPixels:preset(s.crosshairPreset);}
    public static String encode(String pixels) {
        if(pixels==null || !pixels.matches("[01]{225}"))throw new IllegalArgumentException("Invalid pixel grid");
        byte[] bytes=new byte[29];for(int i=0;i<225;i++)if(pixels.charAt(i)=='1')bytes[i/8]|=(byte)(1<<(i%8));
        return "ORBCH-15-"+Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);
    }
    public static String decode(String code) {
        if(code==null || code.length()>80 || !code.startsWith("ORBCH-15-"))throw new IllegalArgumentException("Not an Orbit crosshair code");
        byte[] bytes=Base64.getUrlDecoder().decode(code.substring(9).trim());
        if(bytes.length!=29 || (bytes[28]&0xFE)!=0)throw new IllegalArgumentException("Invalid pixel grid");
        var b=new StringBuilder(225);for(int i=0;i<225;i++)b.append((bytes[i/8]&(1<<(i%8)))!=0?'1':'0');return b.toString();
    }
}
