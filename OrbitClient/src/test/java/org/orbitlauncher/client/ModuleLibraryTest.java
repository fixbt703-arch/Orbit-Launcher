package org.orbitlauncher.client;

import java.util.Set;

public final class ModuleLibraryTest {
    public static void main(String[] args) throws Exception {
        var retired=Set.of("ITEM","CLOCK","SPRINT","ARROWS","RESOURCES","DIRECTION");
        for(var module:Module.values()) {
            if(retired.contains(module.name()))throw new AssertionError("Retired module remains available: "+module.name());
            if(module.descriptionRu.isBlank()||module.descriptionEn.isBlank()
                ||module.descriptionRu.equals(module.category)||module.descriptionEn.equals(module.category))
                throw new AssertionError("Module needs a functional description: "+module.name());
        }
        if(Module.values().length!=24||Module.INTERFACE.hasHud()||ClickGuiGroup.VISUALS.modules[0]!=Module.INTERFACE)throw new AssertionError("Interface must be the first Visuals module, without a HUD");
        if(Module.TRAJECTORIES.hasHud()||Module.TRAJECTORIES.defaultEnabled)throw new AssertionError("Trajectories must be opt-in without a HUD");
        if(Module.TOGGLE_SPRINT.hasHud())throw new AssertionError("Sprint toggle became a retired status HUD");
        var grouped=java.util.EnumSet.noneOf(Module.class);
        for(var group:ClickGuiGroup.values())for(var module:group.modules){
            if(!grouped.add(module))throw new AssertionError("Duplicate module group: "+module);
            if(!module.title().equals(module.en)||module.title().matches(".*[А-Яа-я].*"))throw new AssertionError("Module title must always be English");
        }
        if(grouped.size()!=Module.values().length)throw new AssertionError("Ungrouped module");
        for(int[] size:new int[][]{{320,180},{427,240},{550,270},{600,300},{800,450},{960,540}}){
            for(int i=0;i<4;i++){
                var frame=ClickGuiLayout.frame(size[0],size[1],i);
                if(frame.x()<6||frame.y()<52||frame.x()+frame.width()>size[0]-6||frame.y()+frame.maxHeight()>size[1]-36)
                    throw new AssertionError("Panel outside safe area: "+frame);
            }
            if(ClickGuiLayout.x(Double.NaN,size[0],140)<6||ClickGuiLayout.y(Double.POSITIVE_INFINITY,size[1])<52)throw new AssertionError("Invalid saved position");
        }
        for(int[] pixels:new int[][]{{1280,720},{1600,900},{1920,1080},{1920,1010},{2560,1440},{3840,2160}}){
            double baseline=0;
            for(int scale:new int[]{1,2,3,4}){
                var viewport=ClickGuiViewport.fit((pixels[0]+scale-1)/scale,(pixels[1]+scale-1)/scale,scale);
                if(viewport.width()<600||viewport.height()<360)throw new AssertionError("Minecraft GUI scale makes the menu oversized: "+scale);
                double rowPixels=20*viewport.scale()*scale;if(scale==1)baseline=rowPixels;
                if(Math.abs(rowPixels-baseline)>.2)throw new AssertionError("Menu density depends on HUD scale");
                for(double x:new double[]{0,6,158,viewport.width()-.5})if(Math.abs(viewport.local(viewport.screen(x))-x)>.00001)throw new AssertionError("Pointer transform differs from rendering");
                if(ClickGuiLayout.frame(viewport.width(),viewport.height(),0).y()!=ClickGuiLayout.frame(viewport.width(),viewport.height(),3).y())throw new AssertionError("Desktop categories unexpectedly wrap");
            }
        }
        var tiny=ClickGuiViewport.fit(320,180,1);if(tiny.width()!=320||tiny.height()!=180)throw new AssertionError("Real small windows must retain a readable minimum scale");
        ClientConfigMigrationTest.main(args);
        System.out.println("PASS 24 modules, group coverage and independent menu density at 6 resolutions / GUI scales 1-4");
    }
}
