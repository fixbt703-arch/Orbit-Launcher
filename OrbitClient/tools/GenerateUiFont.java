import java.awt.*;
import java.awt.image.BufferedImage;
import java.nio.file.*;
import java.util.*;
import java.util.List;
import javax.imageio.ImageIO;

/** Rasterized UI lettering from the build machine's installed Segoe UI; no font binary is shipped. */
public final class GenerateUiFont {
    public static void main(String[] args)throws Exception {
        Path root=Path.of(args[0]);Path textures=root.resolve("textures/font");Files.createDirectories(textures);Files.createDirectories(root.resolve("font"));
        var installed=Arrays.asList(GraphicsEnvironment.getLocalGraphicsEnvironment().getAvailableFontFamilyNames(Locale.ROOT));
        String family=installed.contains("Segoe UI")?"Segoe UI":"SansSerif";
        var codes=new ArrayList<Integer>();
        for(int[] range:new int[][]{{33,126},{161,383},{1024,1279},{0x2010,0x204a},{0x2190,0x2199},{0x2212,0x2212},{0x25a0,0x25a1},{0x2665,0x2665}})
            for(int code=range[0];code<=range[1];code++)codes.add(code);
        final int cell=56,columns=16;int rows=(codes.size()+columns-1)/columns;
        var bitmap=new BufferedImage(columns*cell,rows*cell,BufferedImage.TYPE_INT_ARGB);
        var g=bitmap.createGraphics();g.setColor(Color.WHITE);g.setFont(new Font(family,Font.PLAIN,44));
        g.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING,RenderingHints.VALUE_TEXT_ANTIALIAS_ON);
        g.setRenderingHint(RenderingHints.KEY_FRACTIONALMETRICS,RenderingHints.VALUE_FRACTIONALMETRICS_ON);
        var chars=new ArrayList<String>();
        for(int row=0;row<rows;row++) {
            var line=new StringBuilder("\"");
            for(int col=0;col<columns;col++) {
                int index=row*columns+col,code=index<codes.size()?codes.get(index):0;
                line.append(String.format("\\u%04x",code));
                if(code!=0)g.drawString(Character.toString(code),col*cell,row*cell+43);
            }
            chars.add(line.append('"').toString());
        }
        g.dispose();ImageIO.write(bitmap,"png",textures.resolve("ui.png").toFile());
        Files.writeString(root.resolve("font/ui_bitmap.json"),"{\"providers\":[{\"type\":\"space\",\"advances\":{\" \":3,\"\\u00a0\":3}},{\"type\":\"bitmap\",\"file\":\"orbit_client:font/ui.png\",\"height\":12,\"ascent\":9,\"chars\":["+String.join(",",chars)+"]},{\"type\":\"reference\",\"id\":\"minecraft:include/unifont\"}]}");
        face(root,"ui","system-ui.ttf",9.5);
        face(root,"ui_strong","system-ui-semibold.ttf",10);
        face(root,"ui_heading","system-ui-semibold.ttf",14);
        face(root,"ui_brand","system-brand.ttf",14);
        System.out.println("Orbit UI font: "+family+", "+codes.size()+" glyphs, antialiased at 4x resolution");
    }
    private static void face(Path root,String name,String file,double size)throws Exception {
        Files.writeString(root.resolve("font/"+name+".json"),"{\"providers\":[{\"type\":\"ttf\",\"file\":\"orbit_client:"+file+"\",\"size\":"+size+",\"oversample\":4,\"shift\":[0,0]},{\"type\":\"reference\",\"id\":\"orbit_client:ui_bitmap\"}]}");
    }
}
