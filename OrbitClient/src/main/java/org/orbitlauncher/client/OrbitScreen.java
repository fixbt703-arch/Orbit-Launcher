package org.orbitlauncher.client;

import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;
import net.fabricmc.loader.api.FabricLoader;
import java.util.*;
import static org.orbitlauncher.client.OrbitClient.tr;

/** Orbit's module library: stable navigation, clipped collection and persistent actions. */
public final class OrbitScreen extends OrbitScrollScreen {
    private record Card(Module module,int x,int y,int width,int height){}
    private final List<Card> cards=new ArrayList<>();
    private final List<OrbitButton> dynamic=new ArrayList<>();
    private final EnumMap<Module,OrbitMotion> hover=new EnumMap<>(Module.class);
    private int left,top,panelWidth,panelHeight,bodyX,bodyWidth,startY,matched,searchX,searchY,searchWidth;
    private boolean compact,inlineNav;
    private EditBox search;
    private OrbitButton clearSearch;
    private String category="ALL",query="";
    private String returnFocus;
    public OrbitScreen(){super(Component.literal("Orbit Client"));}
    @Override protected void init(){
        panelWidth=Math.min(width-24,720);panelHeight=Math.min(height-24,420);
        left=(width-panelWidth)/2;top=(height-panelHeight)/2;compact=panelHeight<310;inlineNav=!compact&&panelWidth>=620;
        bodyX=left+18;bodyWidth=panelWidth-36;startY=top+(compact?80:inlineNav?108:147);
        String[] ids={"ALL","HUD","PVP","ON"};
        String[] names={tr("Все модули","All modules"),"HUD","PVP",tr("Включены","Enabled")};
        if(panelWidth<420){names[0]=tr("Все","All");names[3]=tr("Включены","Enabled");}
        int tw=inlineNav?Math.min(102,(panelWidth-190)/4):Math.min(116,(panelWidth-46)/4);
        for(int i=0;i<ids.length;i++){
            final String id=ids[i];
            var tab=new OrbitButton(left+(inlineNav?140:18)+i*(tw+6),top+(inlineNav?16:49),tw,24,names[i],category.equals(id),()->switchContent(()->{category=id;resetScroll();rebuildWidgets();})).asTab();
            if(tw>=85)tab.withIcon(id.toLowerCase(Locale.ROOT));addRenderableWidget(tab);
        }
        searchWidth=compact?Math.min(220,panelWidth-150):inlineNav?230:panelWidth-36;
        searchX=compact?left+panelWidth-searchWidth-46:inlineNav?left+panelWidth-searchWidth-18:left+18;
        searchY=top+(compact?12:inlineNav?73:111);
        search=new EditBox(font,searchX+24,searchY+8,searchWidth-48,10,Component.literal(tr("Поиск модулей","Search modules"))){
            @Override public void extractWidgetRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){
                super.extractWidgetRenderState(g,mx,my,delta);
                // Vanilla's hint renderer always enables a shadow, even with
                // setTextShadow(false). Keep the native editor, draw only its hint here.
                if(getValue().isEmpty()&&!isFocused())g.text(OrbitFonts.ui(),OrbitTheme.ellipsize(OrbitFonts.ui(),tr("Найти модуль…","Find a module…"),getWidth()),getX(),getY(),OrbitTheme.MUTED,false);
            }
        };
        search.setBordered(false);search.setTextShadow(false);search.setTextColor(OrbitTheme.TEXT);
        search.setMaxLength(64);search.setValue(query);search.setResponder(v->{query=v;resetScroll();refreshCards();});addRenderableWidget(search);
        clearSearch=addRenderableWidget(new OrbitButton(searchX+searchWidth-22,searchY+2,20,20,tr("Очистить поиск","Clear search"),false,()->{search.setValue("");setFocused(search);}).iconOnly("close"));clearSearch.visible=!query.isEmpty();
        addRenderableWidget(new OrbitButton(bodyX+bodyWidth-22,top+(compact?13:17),22,22,tr("Закрыть","Close"),false,this::onClose).iconOnly("close"));
        int footer=top+panelHeight-29;
        addRenderableWidget(new OrbitButton(bodyX,footer,110,22,tr("Редактор HUD","HUD Editor"),false,()->show(new HudEditorScreen(this),tr("Редактор HUD","HUD Editor"))).withIcon("hud"));
        addRenderableWidget(new OrbitButton(bodyX+118,footer,108,22,tr("Оптимизация","Performance"),false,()->show(new PerformanceScreen(this),tr("Оптимизация","Performance"))));
        refreshCards();
    }
    private void show(Screen screen,String origin){returnFocus=origin;minecraft.gui.setScreen(screen);}
    private void open(Module m){show(m==Module.CROSSHAIR?new CrosshairScreen(this):new ModuleScreen(this,m),tr("Настроить: ","Configure: ")+m.title());}
    @Override protected void setInitialFocus(){
        String origin=returnFocus;returnFocus=null;
        if(origin!=null)for(var child:children())if(child instanceof OrbitButton button&&button.getMessage().getString().equals(origin)){setFocused(button);return;}
        super.setInitialFocus();
    }
    private void refreshCards(){
        int focusIndex=dynamic.indexOf(getFocused());
        if(clearSearch!=null)clearSearch.visible=!query.isEmpty();
        for(var widget:dynamic)removeWidget(widget);dynamic.clear();cards.clear();
        var modules=Arrays.stream(Module.values()).filter(m->category.equals("ALL")||category.equals(m.category)||category.equals("ON")&&OrbitClient.enabled(m))
            .filter(m->(m.ru+" "+m.en+" "+m.description()).toLowerCase(Locale.ROOT).contains(query.toLowerCase(Locale.ROOT))).toList();matched=modules.size();
        contentRegion(bodyX,startY,bodyWidth+8,panelHeight-(startY-top)-37);
        int h=Math.min(viewHeight,compact?48:60),gap=8,columns=Math.max(1,(bodyWidth+gap)/223);
        content(()->{for(int i=0;i<modules.size();i++){
            int column=i%columns,offset=column*(bodyWidth+gap)/columns,cw=(column+1)*(bodyWidth+gap)/columns-offset-gap;
            Module m=modules.get(i);int x=bodyX+offset,y=startY+i/columns*(h+gap);cards.add(new Card(m,x,y,cw,h));
            var toggle=new OrbitSwitch(x+cw-40,y+(h>=48?(h+8)/2:(h-16)/2),30,16,m.title(),OrbitClient.enabled(m),()->{OrbitClient.config.state(m).enabled=!OrbitClient.enabled(m);OrbitClient.save();refreshCards();});
            dynamic.add(toggle);addRenderableWidget(toggle);
            var button=new OrbitButton(x+cw-(h>=48?36:68),y+(h>=48?(h-42)/2:(h-22)/2),22,22,tr("Настроить: ","Configure: ")+m.title(),false,()->open(m)).iconOnly("settings");button.setTooltip(OrbitTheme.tooltip(OrbitFonts.text(m.description())));dynamic.add(button);addRenderableWidget(button);
        }
        if(modules.isEmpty()){
            var reset=new OrbitButton(bodyX+(bodyWidth-132)/2,startY+Math.min(viewHeight-20,Math.max(16,viewHeight/2+2)),132,20,tr("Показать все модули","Show all modules"),true,()->{category="ALL";query="";resetScroll();rebuildWidgets();setFocused(search);});
            dynamic.add(reset);addRenderableWidget(reset);
        }});
        contentHeight(modules.isEmpty()?viewHeight:((modules.size()+columns-1)/columns)*(h+gap)-gap);
        if(focusIndex>=0&&!dynamic.isEmpty())setFocused(dynamic.get(Math.min(focusIndex,dynamic.size()-1)));
    }
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){
        animateScroll();OrbitTheme.window(g,width,height,left,top,panelWidth,panelHeight);
        ModuleIcons.ui(g,"logo",bodyX,top+(compact?11:15),26,0xFFFFFFFF);
        g.text(OrbitFonts.brand(),"ORBIT",bodyX+34,top+(compact?22:15),OrbitTheme.ACCENT,false);
        if(!compact)g.text(font,"CLIENT",bodyX+34,top+35,OrbitTheme.MUTED,false);
        if(inlineNav)g.fill(left+1,top+54,left+panelWidth-1,top+55,OrbitTheme.BORDER);
        OrbitTheme.surface(g,searchX,searchY,searchWidth,24,0xFF101828,search.isFocused()?OrbitTheme.FOCUS:OrbitTheme.BORDER);
        ModuleIcons.ui(g,"search",searchX+7,searchY+6,12,OrbitTheme.MUTED);
        if(!compact){
            int headingY=inlineNav?searchY+10:top+93;
            g.text(OrbitFonts.heading(),tr("Модули","Modules"),bodyX,headingY,OrbitTheme.TEXT,false);
            String count=matched+" / "+Module.values().length;g.text(font,count,bodyX+OrbitFonts.heading().width(tr("Модули","Modules"))+12,headingY-3,OrbitTheme.MUTED,false);
        }
        g.enableScissor(viewX,viewY,viewX+viewWidth,viewY+viewHeight);
        for(var card:cards){
            int y=card.y-scrollOffset();if(y+card.height<=viewY||y>=viewY+viewHeight)continue;
            boolean on=OrbitClient.enabled(card.module),pointed=inContent(mx,my)&&mx>=card.x&&mx<card.x+card.width&&my>=y&&my<y+card.height;
            float transition=hover.computeIfAbsent(card.module,m->new OrbitMotion()).toward(pointed);
            OrbitTheme.surface(g,card.x,y,card.width,card.height,OrbitMotion.mix(OrbitTheme.CARD,OrbitTheme.HOVER,transition),OrbitMotion.mix(OrbitTheme.BORDER,OrbitTheme.FOCUS,transition));
            int tile=compact?24:28,tx=card.x+12,ty=y+(card.height-tile)/2,textX=tx+tile+12;
            OrbitTheme.rounded(g,tx,ty,tile,tile,OrbitTheme.CONTROL);
            ModuleIcons.draw(g,card.module,tx+(tile-16)/2,ty+(tile-16)/2,16,OrbitTheme.ACCENT);
            var strong=OrbitFonts.strong();
            int labelY=y+(card.height-(card.height>=48?26:9))/2;
            g.text(strong,OrbitTheme.ellipsize(strong,card.module.title(),card.x+card.width-(card.height>=48?48:76)-textX),textX,labelY,OrbitTheme.TEXT,false);
            if(card.height>=48)g.text(font,card.module.category,textX,labelY+17,OrbitTheme.MUTED,false);
        }
        if(cards.isEmpty())g.centeredText(font,query.isEmpty()&&category.equals("ON")?tr("Нет включённых модулей","No enabled modules"):tr("Ничего не найдено","No modules found"),bodyX+bodyWidth/2,startY+Math.max(3,viewHeight/2-15),OrbitTheme.TEXT);
        g.disableScissor();renderContent(g,mx,my,delta);
        g.fill(bodyX,top+panelHeight-36,bodyX+bodyWidth,top+panelHeight-35,OrbitTheme.BORDER);
        if(panelWidth>430){String hint=tr("Ctrl+F · Поиск","Ctrl+F · Search");g.text(font,hint,bodyX+bodyWidth-font.width(hint),top+panelHeight-22,OrbitTheme.MUTED,false);}
        super.extractRenderState(g,mx,my,delta);
    }
    @Override public boolean mouseClicked(MouseButtonEvent e,boolean twice){
        if(super.mouseClicked(e,twice))return true;
        if(e.button()==0&&inContent(e.x(),e.y()))for(var c:cards){int y=c.y-scrollOffset();if(e.x()>=c.x&&e.x()<c.x+c.width&&e.y()>=y&&e.y()<y+c.height){open(c.module);return true;}}return false;
    }
    @Override public boolean keyPressed(KeyEvent e){if(e.key()==org.lwjgl.glfw.GLFW.GLFW_KEY_F&&e.hasControlDown()){setFocused(search);search.setCursorPosition(search.getValue().length());search.setHighlightPos(0);return true;}if(OrbitClient.menuKey.matches(e)){onClose();return true;}return super.keyPressed(e);}
    void verifyLayouts(){for(String id:new String[]{"ALL","HUD","PVP","ON"}){category=id;resetScroll();rebuildWidgets();verifyScrollLayouts();}category="ALL";rebuildWidgets();}
    void verifyFiltering(){category="ALL";query="FPS";rebuildWidgets();if(matched!=1||cards.getFirst().module!=Module.FPS)throw new IllegalStateException("Search failed");search.setValue("no-such-orbit-module");if(!cards.isEmpty())throw new IllegalStateException("Empty filter failed");search.setValue("");category="ON";rebuildWidgets();if(!cards.isEmpty()){Module m=cards.getFirst().module;try{dynamic.getFirst().onClick(null,false);if(cards.stream().anyMatch(c->c.module==m))throw new IllegalStateException("Enabled filter failed");}finally{OrbitClient.config.state(m).enabled=true;OrbitClient.save();}}category="ALL";query="";resetScroll();rebuildWidgets();}
    @Override public void onClose(){OrbitClient.save();minecraft.gui.setScreen(null);}
    @Override public boolean isPauseScreen(){return false;}
    static final class PerformanceScreen extends OrbitScrollScreen {
        private final Screen parent;private int x,y,w,h;
        private static final String[][] MODS={{"sodium","Sodium"},{"lithium","Lithium"},{"ferritecore","FerriteCore"},{"entityculling","Entity Culling"},{"immediatelyfast","ImmediatelyFast"},{"skinlayers3d","Skin Layers 3D"}};
        PerformanceScreen(Screen parent){super(Component.literal("Orbit / Performance"));this.parent=parent;}
        @Override protected void init(){w=Math.min(width-24,500);h=Math.min(height-20,370);x=(width-w)/2;y=(height-h)/2;contentRegion(x+12,y+44,w-16,h-80);
            content(()->{for(int i=0;i<MODS.length;i++){var mod=FabricLoader.getInstance().getModContainer(MODS[i][0]);String status=mod.map(m->m.getMetadata().getVersion().getFriendlyString()).orElse(tr("Не установлен","Not installed"));var row=new OrbitButton(x+12,y+44+i*38,w-24,32,MODS[i][1]+"  /  "+status,false,()->{}).alignLeft();row.active=false;addRenderableWidget(row);}});contentHeight(MODS.length*38-6);addRenderableWidget(new OrbitButton(x+w-76,y+h-28,64,22,tr("Готово","Done"),true,this::onClose));}
        @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){animateScroll();OrbitTheme.window(g,width,height,x,y,w,h);g.text(font,tr("Компоненты клиента","Client components"),x+12,y+17,OrbitTheme.TEXT,false);renderContent(g,mx,my,delta);super.extractRenderState(g,mx,my,delta);}
        void verifyLayouts(){verifyScrollLayouts();}
        @Override public void onClose(){minecraft.gui.setScreen(parent);}
        @Override public boolean isPauseScreen(){return false;}
    }
}
