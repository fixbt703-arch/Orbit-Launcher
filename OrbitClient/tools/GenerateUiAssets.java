import java.awt.*;
import java.awt.geom.*;
import java.awt.image.BufferedImage;
import java.nio.file.*;
import javax.imageio.ImageIO;

/** Reproducible vector artwork for Orbit's GUI sprite atlas. */
public final class GenerateUiAssets {
    private static Graphics2D g;
    private static Area artwork;
    public static void main(String[] args) throws Exception {
        Path root=Path.of(args[0]);Files.createDirectories(root.resolve("icons"));
        mask(root,"panel",32,4);mask(root,"outline",32,4);mask(root,"window",48,8);mask(root,"window_outline",48,8);mask(root,"pill",16,7);mask(root,"pill_outline",16,7);mask(root,"circle",16,8);
        for(int radius=1;radius<=12;radius++){mask(root,"radius_"+radius,32,radius);mask(root,"radius_"+radius+"_outline",32,radius);}
        tooltip(root.getParent().resolve("tooltip"));
        Files.copy(Path.of(args[1]),root.resolve("icons/logo.png"),StandardCopyOption.REPLACE_EXISTING);
        Files.writeString(root.resolve("icons/logo.png.mcmeta"),"{\"texture\":{\"blur\":true,\"clamp\":true}}");
        for(String name:new String[]{"fps","ping","cps","keys","armor","item","effects","coords","direction","speed","clock","session","sprint","toggle_sprint","crosshair","arrows","resources","reach","server","memory","waypoint","chunks","auto_refill","zoom","skin_3d","glint","hitbox","hold_my_items","trajectories","search","close","left","right","settings","check","grid","all","hud","pvp","client","on"}) {
            BufferedImage image=new BufferedImage(96,96,BufferedImage.TYPE_INT_ARGB);g=image.createGraphics();
            g.setRenderingHint(RenderingHints.KEY_ANTIALIASING,RenderingHints.VALUE_ANTIALIAS_ON);g.scale(4,4);
            g.setColor(Color.WHITE);g.setStroke(new BasicStroke(1.8f,BasicStroke.CAP_ROUND,BasicStroke.JOIN_ROUND));
            artwork=new Area();icon(name);g.fill(artwork);g.dispose();ImageIO.write(image,"png",root.resolve("icons/"+name+".png").toFile());
            Files.writeString(root.resolve("icons/"+name+".png.mcmeta"),"{\"texture\":{\"blur\":"+!(name.equals("item")||name.equals("pvp"))+",\"clamp\":true}}");
        }
    }
    private static void pixelSword(){
        // A flat stepped mask; the UI supplies the same tint as other module icons.
        String[] pixels={
            ".............###",
            "............####",
            "...........#####",
            "..........#####.",
            ".........#####..",
            "........#####...",
            "..##...#####....",
            "..###.#####.....",
            "...#######......",
            "...######.......",
            "....####........",
            "...######.......",
            "..###.####......",
            "####....##......",
            "###.............",
            "###............."
        };
        for(int y=0;y<16;y++)for(int x=0;x<16;x++)if(pixels[y].charAt(x)=='#')artwork.add(new Area(new Rectangle2D.Double(x*1.5,y*1.5,1.5,1.5)));
    }
    private static void tooltip(Path root)throws Exception {
        Files.createDirectories(root);
        for(boolean frame:new boolean[]{false,true}){
            var image=new BufferedImage(128,128,BufferedImage.TYPE_INT_ARGB);var p=image.createGraphics();
            p.setRenderingHint(RenderingHints.KEY_ANTIALIASING,RenderingHints.VALUE_ANTIALIAS_ON);
            p.setColor(new Color(frame?0xFF303D55:0xFA171E2E,true));
            var shape=new RoundRectangle2D.Double(24,24,80,80,24,24);
            if(frame){p.setStroke(new BasicStroke(4));p.draw(shape);}else p.fill(shape);
            p.dispose();String name="ui_"+(frame?"frame":"background");
            ImageIO.write(image,"png",root.resolve(name+".png").toFile());
            Files.writeString(root.resolve(name+".png.mcmeta"),"{\"gui\":{\"scaling\":{\"type\":\"nine_slice\",\"width\":32,\"height\":32,\"border\":12,\"stretch_inner\":true}}}");
        }
    }
    private static void mask(Path root,String name,int size,int radius) throws Exception {
        BufferedImage image=new BufferedImage(size*4,size*4,BufferedImage.TYPE_INT_ARGB);Graphics2D p=image.createGraphics();
        p.setRenderingHint(RenderingHints.KEY_ANTIALIASING,RenderingHints.VALUE_ANTIALIAS_ON);p.setColor(Color.WHITE);
        if(name.endsWith("outline")){Area ring=new Area(new RoundRectangle2D.Double(0,0,size*4,size*4,radius*8,radius*8));ring.subtract(new Area(new RoundRectangle2D.Double(4,4,size*4-8,size*4-8,Math.max(0,radius-1)*8,Math.max(0,radius-1)*8)));p.fill(ring);}
        else p.fill(new RoundRectangle2D.Double(0,0,size*4,size*4,radius*8,radius*8));p.dispose();
        ImageIO.write(image,"png",root.resolve(name+".png").toFile());
        Files.writeString(root.resolve(name+".png.mcmeta"),name.equals("circle")?"{\"gui\":{\"scaling\":{\"type\":\"stretch\"}}}":"{\"gui\":{\"scaling\":{\"type\":\"nine_slice\",\"width\":"+size+",\"height\":"+size+",\"border\":"+radius+",\"stretch_inner\":true}}}");
    }
    private static void stroke(Shape shape){artwork.add(new Area(((BasicStroke)g.getStroke()).createStrokedShape(shape)));}
    private static void line(double... p){Path2D path=new Path2D.Double();path.moveTo(p[0],p[1]);for(int i=2;i<p.length;i+=2)path.lineTo(p[i],p[i+1]);stroke(path);}
    private static void circle(double x,double y,double r){stroke(new Ellipse2D.Double(x-r,y-r,r*2,r*2));}
    private static void dot(double x,double y,double r){artwork.add(new Area(new Ellipse2D.Double(x-r,y-r,r*2,r*2)));}
    private static void box(double x,double y,double w,double h){stroke(new RoundRectangle2D.Double(x,y,w,h,3,3));}
    private static void arc(double x,double y,double r,int start,int extent){stroke(new Arc2D.Double(x-r,y-r,r*2,r*2,start,extent,Arc2D.OPEN));}
    private static void curve(double... p){Path2D path=new Path2D.Double();path.moveTo(p[0],p[1]);for(int i=2;i<p.length;i+=6)path.curveTo(p[i],p[i+1],p[i+2],p[i+3],p[i+4],p[i+5]);stroke(path);}
    private static void icon(String name) {
        switch(name) {
            case "fps" -> {arc(12,13,8,0,180);line(4,13,4,16);line(20,13,20,16);line(12,13,16,9);line(8,20,16,20);}
            case "ping" -> {curve(3,8,8,3,16,3,21,8);curve(6,12,9,9,15,9,18,12);curve(9,16,11,14,13,14,15,16);dot(12,20,1.2);}
            case "cps" -> {stroke(new RoundRectangle2D.Double(6,3,12,18,11,11));line(12,3,12,9);line(6,10,18,10);}
            case "keys" -> {box(9,3,6,6);box(2,13,6,6);box(9,13,6,6);box(16,13,6,6);line(5,22,19,22);}
            case "armor" -> {curve(12,3,9,5,6,5,4,6,4,14,6,19,12,22,18,19,20,14,20,6,18,5,15,5,12,3);line(12,8,12,16);line(8,12,16,12);}
            case "item" -> pixelSword();
            case "pvp" -> pixelSword();
            case "effects" -> {line(9,3,15,3);curve(9,3,9,6,10,9,7,12,2,17,4,21,8,21);curve(15,3,15,6,14,9,17,12,22,17,20,21,16,21);line(8,21,16,21);line(6,15,18,15);dot(10,18,1);}
            case "coords" -> {curve(12,22,8,17,5,13,5,9,5,1,19,1,19,9,19,13,16,17,12,22);circle(12,9,2.5);}
            case "direction" -> {circle(12,12,9);line(15.5,8.5,14,14,8.5,15.5,10,10,15.5,8.5);}
            case "speed" -> {curve(14,3,12,6,9,8,6,13);line(6,13,12,13);curve(12,13,12,16,11,19,10,21);line(10,21,19,10,13,10,14,3);}
            case "clock" -> {circle(12,12,9);line(12,6,12,12,16,14);}
            case "session" -> {circle(12,14,7);line(9,2,15,2);line(12,2,12,5);line(18,6,20,4);line(12,9,12,14);}
            case "sprint","toggle_sprint" -> {dot(16,4,2);curve(12,8,11,10,10,12,10,14,12,15,14,16,16,17);line(16,17,16,21);curve(12,8,15,9,15,12,20,12);curve(12,8,9,7,7,9,6,11);curve(10,14,9,17,7,20,4,21);if(name.equals("toggle_sprint"))line(3,4,7,4);}
            case "crosshair" -> {circle(12,12,6);line(12,3,12,6);line(12,18,12,21);line(3,12,6,12);line(18,12,21,12);dot(12,12,1.3);}
            case "trajectories" -> {curve(3,19,6,2,16,2,21,15);line(16,13,21,15,22,10);line(2,21,7,21);}
            case "arrows" -> {line(5,19,19,5);line(12,5,19,5,19,12);line(4,14,4,20,10,20);}
            case "resources" -> {curve(3,9,4,7,5,5,7,3);line(7,3,17,3);curve(17,3,19,5,20,7,21,9);line(21,9,12,21,3,9,21,9);line(8,9,12,21,16,9);}
            case "reach" -> {stroke(new RoundRectangle2D.Double(3,7,18,10,5,5));line(7,7,7,11);line(12,7,12,13);line(17,7,17,11);}
            case "server" -> {stroke(new RoundRectangle2D.Double(3,3,18,7,5,5));stroke(new RoundRectangle2D.Double(3,14,18,7,5,5));dot(7,6.5,1);dot(7,17.5,1);line(15,6.5,17,6.5);line(15,17.5,17,17.5);}
            case "memory" -> {stroke(new RoundRectangle2D.Double(6,6,12,12,5,5));box(10,10,4,4);for(int i=8;i<=16;i+=4){line(i,3,i,6);line(i,18,i,21);line(3,i,6,i);line(18,i,21,i);}}
            case "waypoint" -> {line(5,21,5,4);curve(5,4,10,1,14,7,20,4,20,7,20,10,20,13,14,16,10,10,5,13);}
            case "chunks" -> {line(3,7,12,3,21,7,12,11,3,7);line(3,12,12,16,21,12);line(3,17,12,21,21,17);}
            case "hold_my_items" -> {box(10,3,10,9);line(15,3,15,12);curve(4,14,4,11,7,11,8,14);line(8,14,11,16,16,16);curve(16,16,19,16,19,19,16,20);line(16,20,9,21);curve(9,21,6,21,4,19,3,17);}
            case "auto_refill" -> {box(3,13,8,8);box(14,3,7,7);curve(4,8,4,4,7,3,10,4);line(8,2,10,4,8,6);curve(20,16,20,20,17,21,14,20);line(16,18,14,20,16,22);}
            case "zoom","search" -> {circle(10,10,6);line(15,15,20,20);if(name.equals("zoom")){line(7,10,13,10);line(10,7,10,13);}}
            case "close" -> {line(7,7,17,17);line(17,7,7,17);}
            case "left" -> line(14,6,8,12,14,18);
            case "right" -> line(10,6,16,12,10,18);
            case "settings" -> {line(4,7,20,7);line(4,17,20,17);artwork.add(new Area(new RoundRectangle2D.Double(8.5,4,3,6,1.5,1.5)));artwork.add(new Area(new RoundRectangle2D.Double(12.5,14,3,6,1.5,1.5)));}
            case "check","on" -> {line(5,12,10,17,19,7);}
            case "grid","all" -> {stroke(new RoundRectangle2D.Double(3,3,7,7,4,4));stroke(new RoundRectangle2D.Double(14,3,7,7,4,4));stroke(new RoundRectangle2D.Double(3,14,7,7,4,4));stroke(new RoundRectangle2D.Double(14,14,7,7,4,4));}
            case "hud" -> {stroke(new RoundRectangle2D.Double(3,4,18,14,5,5));line(8,21,16,21);line(12,18,12,21);line(7,8,10,8);line(7,12,15,12);}
            case "skin_3d" -> {line(4,7,12,3,20,7,20,17,12,21,4,17,4,7,12,11,20,7);line(12,11,12,21);}
            case "glint" -> {line(12,3,14,9,20,11,14,13,12,20,10,13,4,11,10,9,12,3);}
            case "hitbox" -> {line(3,8,3,3,8,3);line(16,3,21,3,21,8);line(21,16,21,21,16,21);line(8,21,3,21,3,16);box(8,8,8,8);}
            case "client" -> {stroke(new RoundRectangle2D.Double(3,4,18,16,6,6));line(3,10,21,10);dot(7,7,1);dot(11,7,1);line(8,14,16,14);}
            default -> throw new IllegalArgumentException(name);
        }
    }
}
