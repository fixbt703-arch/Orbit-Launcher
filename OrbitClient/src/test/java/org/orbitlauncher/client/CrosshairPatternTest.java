package org.orbitlauncher.client;
public final class CrosshairPatternTest {
    public static void main(String[] args) {
        for(int i=0;i<14;i++) {
            String p=CrosshairPattern.preset(i);
            if(p.length()!=225||p.indexOf('1')<0)throw new AssertionError("Empty preset "+i);
            if(!CrosshairPattern.decode(CrosshairPattern.encode(p)).equals(p))throw new AssertionError("Preset roundtrip "+i);
        }
        String[] values={"0".repeat(225),"1".repeat(225),"0".repeat(224)+"1"};
        for(var value:values)if(!CrosshairPattern.decode(CrosshairPattern.encode(value)).equals(value))throw new AssertionError("Grid roundtrip");
        for(String invalid:new String[]{"","LCCH-15-AAAA","ORBCH-15-!!!","ORBCH-15-AAAA","x".repeat(500)}) {
            try{CrosshairPattern.decode(invalid);throw new AssertionError("Invalid code accepted");}catch(IllegalArgumentException expected){}
        }
        System.out.println("Crosshair: 14 presets, roundtrips, empty/full grids and invalid codes passed");
    }
}
