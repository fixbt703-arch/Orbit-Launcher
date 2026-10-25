package org.orbitlauncher.client;

import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.KeyMapping;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;
import com.mojang.blaze3d.platform.InputConstants;
import org.lwjgl.glfw.GLFW;
import java.util.*;
import java.util.function.*;
import static org.orbitlauncher.client.OrbitClient.tr;

final class ModuleScreen extends OrbitScrollScreen {
    private final Screen parent;
    private final Module module;
    private final List<IntConsumer> rows=new ArrayList<>();
    private int x,w,top,panelHeight,tab,rowX,rowWidth;
    private boolean binding,reservedBinding;
    private boolean inline;
    private int inlineX,inlineY,inlineWidth,inlineHeight,clipTop=Integer.MIN_VALUE,clipBottom=Integer.MAX_VALUE;
    private OrbitSlider minimumDistance;
    private String waypointName="Orbit";
    ModuleScreen(Screen parent,Module module) {
        super(Component.literal(module.title()));this.parent=parent;this.module=module;
        if(OrbitClient.config.waypoint!=null) waypointName=OrbitClient.config.waypoint.name;
    }
    @Override protected void init() {
        w=inline?inlineWidth:Math.min(width-24,520);x=inline?inlineX:(width-w)/2;
        panelHeight=Math.min(height-24,400);top=inline?inlineY:(height-panelHeight)/2;rows.clear();minimumDistance=null;
        var s=OrbitClient.config.state(module);
        String[] tabs=module==Module.HITBOX?new String[]{tr("Основное","General"),tr("Оформление","Appearance"),tr("Цель","Target"),tr("Отображение","Visibility")}:module==Module.CROSSHAIR?new String[]{tr("Основное","General"),tr("Оформление","Appearance"),tr("Параметры","Options"),tr("Крит","Critical")}:new String[]{tr("Основное","General"),tr("Оформление","Appearance"),tr("Параметры","Options")};
        if(module==Module.INTERFACE)tabs=new String[]{tr("Меню","Menu"),"HUD"};
        var visibleTabs=availableTabs();if(!visibleTabs.contains(tab))tab=0;
        int tabInset=inline?4:12,tabGap=inline?2:4,tabY=top+(inline?2:44);
        int tabColumns=inline&&visibleTabs.size()>2?2:visibleTabs.size(),tabRows=(visibleTabs.size()+tabColumns-1)/tabColumns;
        int tabHeight=inline?(tabRows>1?24:18):24;
        int tw=(w-tabInset*2-tabGap*(tabColumns-1))/tabColumns;
        for(int i=0;i<visibleTabs.size();i++) {
            final int selected=visibleTabs.get(i);
            int col=i%tabColumns,row=i/tabColumns;
            int tabWidth=col==tabColumns-1?w-tabInset*2-col*(tw+tabGap):tw;
            if(inline&&i==visibleTabs.size()-1&&col==0)tabWidth=w-tabInset*2;
            addRenderableWidget(new OrbitButton(x+tabInset+col*(tw+tabGap),tabY+row*(tabHeight+tabGap),tabWidth,tabHeight,tabs[selected],tab==selected,()->switchContent(()->{tab=selected;resetScroll();binding=false;OrbitClient.save();rebuildWidgets();})).asTab().focusId(module.name()+":tab:"+selected));
        }
        if(module==Module.INTERFACE&&tab==0){
            var ui=OrbitTheme.options();
            action(binding?(reservedBinding?tr("Клавиша навигации • Выберите другую","Navigation key • Choose another"):tr("Нажмите клавишу • Esc: отмена","Press a key • Esc: cancel")):tr("Бинд: ","Bind: ")+englishKeyName(),()->{binding=true;reservedBinding=false;rebuildWidgets();});
            action(tr("Тема: ","Theme: ")+tr(InterfaceOptions.THEMES_RU[ui.theme],InterfaceOptions.THEMES[ui.theme]),()->{ui.theme=(ui.theme+1)%InterfaceOptions.THEMES.length;OrbitClient.save();rebuildWidgets();});
            action(tr("Градиент: ","Gradient: ")+new String[]{tr("Нет","Off"),tr("Статичный","Static"),tr("Перелив","Flow")}[ui.gradient],()->{ui.gradient=(ui.gradient+1)%3;OrbitClient.save();rebuildWidgets();});
            slider(tr("Скорость анимации","Animation speed"),.5,4,()->ui.animationSpeed,v->ui.animationSpeed=Math.round(v*10)/10d,v->String.format(Locale.ROOT,"%.1f",v));
            toggle(tr("Плавная смена","Smooth changes"),()->ui.smoothChanges,v->ui.smoothChanges=v);
            toggle(tr("Анимации GUI","GUI animations"),()->ui.animations,v->ui.animations=v);
            toggle(tr("Свечение","Glow"),()->ui.glow,v->ui.glow=v);
            toggle(tr("Подсказки","Tooltips"),()->ui.tooltips,v->ui.tooltips=v);
            toggle(tr("Сообщения биндов","Bind messages"),()->ui.bindMessages,v->ui.bindMessages=v);
            action(tr("Фон меню: ","Menu background: ")+new String[]{tr("Размытие","Blur"),tr("Затемнение","Dim"),tr("Без фона","None")}[ui.background],()->{ui.background=(ui.background+1)%3;OrbitClient.save();rebuildWidgets();});
            if(ui.background==0)slider(tr("Сила размытия","Blur strength"),0,10,()->ui.blurRadius,v->ui.blurRadius=(int)Math.round(v),v->Math.round(v*10)+"%");
            else if(ui.background==1)slider(tr("Сила затемнения","Dim strength"),0,1,()->ui.dimStrength,v->ui.dimStrength=v,v->Math.round(v*100)+"%");
            slider(tr("Скругление углов","Corner rounding"),0,12,()->ui.cornerRadius,v->ui.cornerRadius=(int)Math.round(v),v->Long.toString(Math.round(v)));
        } else if(module==Module.INTERFACE){
            var h=OrbitTheme.options().hud;
                toggle(tr("Фон панели","Panel background"),()->h.background,v->h.background=v);
                action(tr("Стиль: ","Style: ")+new String[]{tr("Классический","Classic"),tr("Контур","Outline"),tr("Стекло","Glass"),tr("Минимальный","Minimal")}[h.hudStyle],()->{h.hudStyle=(h.hudStyle+1)%4;OrbitClient.save();rebuildWidgets();});
                slider(tr("Прозрачность фона","Background opacity"),0,1,()->h.opacity,v->h.opacity=(float)v,v->Math.round(v*100)+"%");
                toggle(tr("Рамка карточки","Card border"),()->h.hudBorder,v->h.hudBorder=v);
                toggle(tr("Акцентная полоса","Accent stripe"),()->h.accentBar,v->h.accentBar=v);
                toggle(tr("Заголовок","Title"),()->h.showTitle,v->h.showTitle=v);
                toggle(tr("Акцентный заголовок","Accent title"),()->h.accentTitle,v->h.accentTitle=v);
                slider(tr("Прозрачность текста","Text opacity"),.2,1,()->h.textOpacity,v->h.textOpacity=(float)v,v->Math.round(v*100)+"%");
                slider(tr("Внутренние отступы","Padding"),4,16,()->h.padding,v->h.padding=(int)Math.round(v),v->Long.toString(Math.round(v)));
                slider(tr("Интервал строк","Row spacing"),0,8,()->h.rowSpacing,v->h.rowSpacing=(int)Math.round(v),v->Long.toString(Math.round(v)));
                action(tr("Ширина: ","Width: ")+widthName(h.panelWidth),()->{h.panelWidth=switch(h.panelWidth){case 0->90;case 90->130;case 130->180;case 180->240;default->0;};OrbitClient.save();rebuildWidgets();});
                action(tr("Выравнивание: ","Alignment: ")+new String[]{tr("Слева","Left"),tr("По центру","Center"),tr("Справа","Right")}[h.alignment],()->{h.alignment=(h.alignment+1)%3;rebuildWidgets();});

        } else if(tab==0) {
            toggle(tr("Модуль","Module"),()->s.enabled,v->s.enabled=v);
            if(module.hasHud()) {
                slider(tr("Масштаб HUD","HUD scale"),.65,2,()->s.scale,v->s.scale=(float)v,v->Math.round(v*100)+"%");
                action(tr("Изменить положение","Edit position"),()->minecraft.gui.setScreen(new HudEditorScreen(returnTarget())));
                action(tr("Автоматическое положение","Automatic position"),()->{s.customPosition=false;OrbitClient.save();});
            }
            action(binding?tr("Нажмите клавишу • Esc: отмена","Press a key • Esc: cancel"):(module==Module.HOLD_MY_ITEMS?tr("Клавиша осмотра: ","Inspect key: "):tr("Клавиша: ","Key: "))+englishKeyName(),()->{binding=true;rebuildWidgets();});
            action(tr("Убрать назначение клавиши","Clear key binding"),()->{mapping().setKey(InputConstants.UNKNOWN);KeyMapping.resetMapping();minecraft.options.save();rebuildWidgets();});
            if(module==Module.HOLD_MY_ITEMS){
                toggle(tr("Осмотр предметов","Item inspection"),()->s.hmiInspect,v->s.hmiInspect=v);
                toggle(tr("Основная рука","Main hand"),()->s.hmiMainHand,v->s.hmiMainHand=v);
                toggle(tr("Вторая рука","Off hand"),()->s.hmiOffHand,v->s.hmiOffHand=v);
                toggle(tr("Обычные анимации вне осмотра","Vanilla animations outside inspection"),()->s.hmiVanillaCombat,v->s.hmiVanillaCombat=v);

            }
        } else if(tab==1) {
            if(module==Module.TRAJECTORIES){
                toggle(tr("Цвет темы интерфейса","Use interface theme"),()->s.trajectoryTheme,v->s.trajectoryTheme=v);
                if(!s.trajectoryTheme)action(tr("Цвет траектории","Trajectory color"),()->minecraft.gui.setScreen(new FeatureColorScreen(returnTarget(),module,false)));
                slider(tr("Непрозрачность линии","Line opacity"),.1,1,()->s.trajectoryOpacity,v->s.trajectoryOpacity=(float)v,v->Math.round(v*100)+"%");
                slider(tr("Толщина линии","Line width"),.5,4,()->s.trajectoryWidth,v->s.trajectoryWidth=(float)v,v->String.format(Locale.ROOT,"%.1f px",v));
                toggle(tr("Плавное начало линии","Fade near player"),()->s.trajectoryFade,v->s.trajectoryFade=v);
                toggle(tr("Маркер столкновения","Impact marker"),()->s.trajectoryMarker,v->s.trajectoryMarker=v);
                if(s.trajectoryMarker)slider(tr("Размер маркера","Marker radius"),.08,.5,()->s.trajectoryMarkerSize,v->s.trajectoryMarkerSize=(float)v,v->String.format(Locale.ROOT,"%.2f m",v));
            }
            if(module==Module.HOLD_MY_ITEMS){
                toggle(tr("Показывать руки","Show hands"),()->s.hmiShowHands,v->s.hmiShowHands=v);
                slider(tr("Масштаб предметов и рук","Item and hand scale"),.7,1.3,()->s.hmiScale,v->s.hmiScale=(float)v,v->Math.round(v*100)+"%");
                slider(tr("Положение по горизонтали","Horizontal position"),-.4,.4,()->s.hmiOffsetX,v->s.hmiOffsetX=(float)v,v->String.format(Locale.ROOT,"%+.2f",v));
                slider(tr("Положение по вертикали","Vertical position"),-.4,.4,()->s.hmiOffsetY,v->s.hmiOffsetY=(float)v,v->String.format(Locale.ROOT,"%+.2f",v));
                slider(tr("Расстояние от камеры","Distance from camera"),-.4,.4,()->s.hmiOffsetZ,v->s.hmiOffsetZ=(float)v,v->String.format(Locale.ROOT,"%+.2f",v));
                action(tr("Сбросить положение и масштаб","Reset position and scale"),()->{s.hmiOffsetX=s.hmiOffsetY=s.hmiOffsetZ=0;s.hmiScale=1;OrbitClient.save();rebuildWidgets();});
            }
            if(module==Module.GLINT){
                action(tr("Палитра: ","Palette: ")+glowPaletteName(s.glintColor),()->{
                    int[] colors={0xB9B8FF,0x59D7C6,0xFFAD86,0x91DDF8};int next=0;
                    for(int i=0;i<colors.length;i++)if(s.glintColor==colors[i])next=(i+1)%colors.length;
                    s.glintColor=s.glowOutlineColor=colors[next];s.glowMatchGlint=true;s.glowRainbow=false;OrbitClient.save();rebuildWidgets();
                });
                action(tr("Цвет сияния","Glint color"),()->minecraft.gui.setScreen(new FeatureColorScreen(returnTarget(),module,false)));
                slider(tr("Непрозрачность сияния","Glint opacity"),0,1,()->s.glintOpacity,v->s.glintOpacity=(float)v,v->Math.round(v*100)+"%");
                toggle(tr("Обводка зачарованных предметов","Enchanted item outline"),()->s.glowOutline,v->s.glowOutline=v);
                toggle(tr("Обводка в цвет сияния","Match outline to glint"),()->s.glowMatchGlint,v->s.glowMatchGlint=v);
                if(!s.glowMatchGlint)action(tr("Цвет обводки","Outline color"),()->minecraft.gui.setScreen(new FeatureColorScreen(returnTarget(),module,FeatureColorScreen.GLOW_OUTLINE)));
                slider(tr("Непрозрачность обводки","Outline opacity"),0,1,()->s.glowOutlineOpacity,v->s.glowOutlineOpacity=(float)v,v->Math.round(v*100)+"%");
                slider(tr("Толщина обводки","Outline width"),.5,4,()->s.glowWidth,v->s.glowWidth=(float)v,v->String.format(Locale.ROOT,"%.1f px",v));
                slider(tr("Мягкость края","Edge softness"),0,1,()->s.glowSoftness,v->s.glowSoftness=(float)v,v->Math.round(v*100)+"%");
                toggle(tr("Радужная обводка","Rainbow outline"),()->s.glowRainbow,v->s.glowRainbow=v);
                toggle(tr("Плавная пульсация","Gentle pulse"),()->s.glowPulse,v->s.glowPulse=v);
            }
            if(module==Module.HITBOX){
                toggle(tr("Обводка хитбокса","Hitbox outline"),()->s.hitboxOutline,v->s.hitboxOutline=v);
                action(tr("Цвет обводки","Outline color"),()->minecraft.gui.setScreen(new FeatureColorScreen(returnTarget(),module,false)));
                action(tr("Цвет: ","Color: ")+(s.hitboxColorMode==0?tr("Постоянный","Static"):tr("По здоровью сущности","Entity health")),()->{s.hitboxColorMode=(s.hitboxColorMode+1)%2;OrbitClient.save();rebuildWidgets();});
                slider(tr("Непрозрачность обводки","Outline opacity"),0,1,()->s.hitboxOpacity,v->s.hitboxOpacity=(float)v,v->Math.round(v*100)+"%");
                slider(tr("Толщина линии","Line width"),.5,4,()->s.hitboxWidth,v->s.hitboxWidth=(float)v,v->String.format(Locale.ROOT,"%.1f px",v));
                action(tr("Обводка: ","Outline: ")+new String[]{tr("Сплошная","Solid"),tr("Уголки","Corners"),tr("Пунктир","Dashed")}[s.hitboxStyle],()->{s.hitboxStyle=(s.hitboxStyle+1)%3;OrbitClient.save();rebuildWidgets();});
                slider(tr("Длина уголков","Corner length"),.08,.45,()->s.hitboxCornerLength,v->s.hitboxCornerLength=(float)v,v->Math.round(v*100)+"%");
                slider(tr("Делений пунктира на ребре","Dash divisions per edge"),4,32,()->s.hitboxDashCount,v->s.hitboxDashCount=(int)Math.round(v),v->Long.toString(Math.round(v)));
                toggle(tr("Верхние рёбра","Top edges"),()->s.hitboxTop,v->s.hitboxTop=v);
                toggle(tr("Нижние рёбра","Bottom edges"),()->s.hitboxBottom,v->s.hitboxBottom=v);
                toggle(tr("Вертикальные рёбра","Vertical edges"),()->s.hitboxVertical,v->s.hitboxVertical=v);
                toggle(tr("Заливка","Fill"),()->s.hitboxFill,v->s.hitboxFill=v);
                slider(tr("Непрозрачность заливки","Fill opacity"),0,.5,()->s.hitboxFillOpacity,v->s.hitboxFillOpacity=(float)v,v->Math.round(v*100)+"%");
                toggle(tr("Отдельный цвет заливки","Separate fill color"),()->s.hitboxOwnFillColor,v->s.hitboxOwnFillColor=v);
                if(s.hitboxOwnFillColor)action(tr("Цвет заливки","Fill color"),()->minecraft.gui.setScreen(new FeatureColorScreen(returnTarget(),module,FeatureColorScreen.FILL)));
                toggle(tr("Уровень глаз","Eye level"),()->s.hitboxEyes,v->s.hitboxEyes=v);
                toggle(tr("Направление взгляда","Look direction"),()->s.hitboxLook,v->s.hitboxLook=v);
                if(s.hitboxLook){
                    slider(tr("Длина линии взгляда","Look direction length"),.25,6,()->s.hitboxLookLength,v->s.hitboxLookLength=(float)v,v->String.format(Locale.ROOT,"%.2f m",v));
                    toggle(tr("Стрелка направления взгляда","Look direction arrow"),()->s.hitboxLookArrow,v->s.hitboxLookArrow=v);
                }
                toggle(tr("Отдельный цвет союзников","Separate allied color"),()->s.hitboxFriendlyTint,v->s.hitboxFriendlyTint=v);
                if(s.hitboxFriendlyTint)action(tr("Цвет союзников","Allied color"),()->minecraft.gui.setScreen(new FeatureColorScreen(returnTarget(),module,FeatureColorScreen.FRIENDLY)));
                toggle(tr("Подсвечивать получение урона","Flash on damage"),()->s.hitboxHurtTint,v->s.hitboxHurtTint=v);
                if(s.hitboxHurtTint)action(tr("Цвет при получении урона","Damage flash color"),()->minecraft.gui.setScreen(new FeatureColorScreen(returnTarget(),module,FeatureColorScreen.HURT)));
            }
            if(module==Module.SKIN_3D){
                slider(tr("Толщина второго слоя","Outer layer thickness"),1,1.5,()->s.skinThickness,v->s.skinThickness=(float)v,v->String.format(Locale.ROOT,"%.2f×",v));
                toggle(tr("Голова","Head"),()->s.skinHead,v->s.skinHead=v);
                toggle(tr("Тело","Body"),()->s.skinBody,v->s.skinBody=v);
                toggle(tr("Руки","Arms"),()->s.skinArms,v->s.skinArms=v);
                toggle(tr("Ноги","Legs"),()->s.skinLegs,v->s.skinLegs=v);
            }
            if(module.hasHud() || module==Module.CROSSHAIR) {
                if(module==Module.CROSSHAIR) {
                    action(tr("Выбрать цвет прицела","Pick crosshair colors"),()->minecraft.gui.setScreen(new CrosshairColorScreen(returnTarget())));
                    action(tr("Режим цвета: ","Color mode: ")+new String[]{tr("Постоянный","Static"),tr("По цели","Dynamic"),tr("Инверсия Minecraft","Minecraft inversion")}[s.crosshairColorMode],()->{s.crosshairColorMode=(s.crosshairColorMode+1)%3;OrbitClient.save();rebuildWidgets();});
                    if(s.crosshairColorMode!=2)toggle(tr("Радужный цвет","Chroma"),()->s.crosshairChroma,v->s.crosshairChroma=v);
                    if(s.crosshairMode!=0)action(tr("Масштаб: ","Scale: ")+new String[]{tr("Авто","Auto"),tr("Маленький","Small"),tr("Обычный","Normal"),tr("Большой","Large")}[s.crosshairScale],()->{s.crosshairScale=(s.crosshairScale+1)%4;OrbitClient.save();rebuildWidgets();});
                    if(s.crosshairColorMode==1){
                        toggle(tr("Игроки","Players"),()->s.targetPlayers,v->s.targetPlayers=v);
                        toggle(tr("Враждебные существа","Hostile mobs"),()->s.targetHostile,v->s.targetHostile=v);
                        toggle(tr("Мирные существа","Passive mobs"),()->s.targetPassive,v->s.targetPassive=v);
                        toggle(tr("Остальные сущности","Other entities"),()->s.targetOther,v->s.targetOther=v);
                        toggle(tr("Отдельный цвет союзника","Separate friendly color"),()->s.separateFriendlyColor,v->s.separateFriendlyColor=v);
                    }
                }
                if(module==Module.CROSSHAIR)slider(tr("Непрозрачность","Opacity"),0,1,()->s.opacity,v->s.opacity=(float)v,v->Math.round(v*100)+"%");
            }
        } else if(module==Module.CROSSHAIR&&tab==3){
            toggle(tr("Ванильный индикатор атаки","Vanilla attack indicator"),()->s.criticalIndicator,v->s.criticalIndicator=v);
            if(s.criticalIndicator){
                action(tr("Размер: ","Size: ")+s.criticalScale+"%"+(s.criticalScale==100?tr(" · Как в Minecraft"," · Minecraft size"):""),()->{s.criticalScale=s.criticalScale==50?75:s.criticalScale==75?100:50;OrbitClient.save();rebuildWidgets();});
                slider(tr("Отступ под прицелом","Offset below crosshair"),0,24,()->s.criticalOffset,v->s.criticalOffset=(int)Math.round(v),v->Math.round(v)+" px");
                toggle(tr("Отмечать возможность критического удара","Highlight critical opportunity"),()->s.criticalHighlight,v->s.criticalHighlight=v);
                toggle(tr("Только при наведении на живую цель","Only when aiming at a living target"),()->s.criticalTargetOnly,v->s.criticalTargetOnly=v);
            }
        } else if(module==Module.HITBOX&&tab==2){
            toggle(tr("Подсвечивать цель под прицелом","Highlight targeted entity"),()->s.hitboxHighlight,v->s.hitboxHighlight=v);
            action(tr("Цвет при наведении","Target color"),()->minecraft.gui.setScreen(new FeatureColorScreen(returnTarget(),module,true)));
            slider(tr("Непрозрачность цели","Target opacity"),0,1,()->s.hitboxTargetOpacity,v->s.hitboxTargetOpacity=(float)v,v->Math.round(v*100)+"%");
            toggle(tr("Отдельная толщина обводки цели","Separate target line width"),()->s.hitboxTargetWidthOverride,v->s.hitboxTargetWidthOverride=v);
            if(s.hitboxTargetWidthOverride)slider(tr("Толщина обводки цели","Target line width"),.5,4,()->s.hitboxTargetWidth,v->s.hitboxTargetWidth=(float)v,v->String.format(Locale.ROOT,"%.1f px",v));
            toggle(tr("Отдельный стиль обводки цели","Separate target outline style"),()->s.hitboxTargetStyleOverride,v->s.hitboxTargetStyleOverride=v);
            if(s.hitboxTargetStyleOverride)action(tr("Обводка цели: ","Target outline: ")+new String[]{tr("Сплошная","Solid"),tr("Уголки","Corners"),tr("Пунктир","Dashed")}[s.hitboxTargetStyle],()->{s.hitboxTargetStyle=(s.hitboxTargetStyle+1)%3;OrbitClient.save();rebuildWidgets();});
            toggle(tr("Отдельная заливка цели","Separate target fill"),()->s.hitboxTargetFillOverride,v->s.hitboxTargetFillOverride=v);
            if(s.hitboxTargetFillOverride){
                action(tr("Цвет заливки цели","Target fill color"),()->minecraft.gui.setScreen(new FeatureColorScreen(returnTarget(),module,FeatureColorScreen.TARGET_FILL)));
                slider(tr("Непрозрачность заливки цели","Target fill opacity"),0,.5,()->s.hitboxTargetFillOpacity,v->s.hitboxTargetFillOpacity=(float)v,v->Math.round(v*100)+"%");
            }
        } else {
            switch(module) {
                case HOLD_MY_ITEMS -> {
                    slider(tr("Сила инерции","Motion intensity"),0,1.5,()->s.hmiMotion,v->s.hmiMotion=(float)v,v->Math.round(v*100)+"%");
                    toggle(tr("Объёмные предметы и блоки","3D held items and blocks"),()->s.hmiBlocks3d,v->s.hmiBlocks3d=v);
                    toggle(tr("Частицы анимаций","Animation particles"),()->s.hmiParticles,v->s.hmiParticles=v);
                    toggle(tr("Движение камеры","Camera motion"),()->s.hmiCameraMotion,v->s.hmiCameraMotion=v);

                }
                case TRAJECTORIES -> {
                    toggle(tr("Лук","Bow"),()->s.trajectoryBow,v->s.trajectoryBow=v);
                    toggle(tr("Арбалет: центральная стрела","Crossbow: center arrow"),()->s.trajectoryCrossbow,v->s.trajectoryCrossbow=v);
                    toggle(tr("Эндер-жемчуг","Ender pearl"),()->s.trajectoryPearl,v->s.trajectoryPearl=v);
                    toggle(tr("Снежки","Snowballs"),()->s.trajectorySnowball,v->s.trajectorySnowball=v);
                    toggle(tr("Яйца","Eggs"),()->s.trajectoryEgg,v->s.trajectoryEgg=v);
                    toggle(tr("Взрывные и туманные зелья","Splash and lingering potions"),()->s.trajectoryPotion,v->s.trajectoryPotion=v);
                    toggle(tr("Трезубец без Тягуна","Trident without Riptide"),()->s.trajectoryTrident,v->s.trajectoryTrident=v);
                    toggle(tr("Пузырьки опыта","Experience bottles"),()->s.trajectoryExperience,v->s.trajectoryExperience=v);
                    toggle(tr("Лук и трезубец: при зарядке","Bow / trident: charging only"),()->s.trajectoryChargeOnly,v->s.trajectoryChargeOnly=v);
                    toggle(tr("Учитывать вторую руку","Include off hand"),()->s.trajectoryOffhand,v->s.trajectoryOffhand=v);
                    toggle(tr("Вид от третьего лица","Third-person view"),()->s.trajectoryThirdPerson,v->s.trajectoryThirdPerson=v);
                    toggle(tr("Скрывать при открытом меню","Hide in menus"),()->s.trajectoryHideMenus,v->s.trajectoryHideMenus=v);
                    slider(tr("Максимальная длина пути","Maximum path length"),16,256,()->s.trajectoryRange,v->s.trajectoryRange=(int)Math.round(v),v->Math.round(v)+" m");
                }
                case KEYS -> {toggle(tr("Кнопки мыши и CPS","Mouse buttons and CPS"),()->s.showMouse,v->s.showMouse=v);toggle(tr("Полоса пробела","Space bar"),()->s.showSpace,v->s.showSpace=v);}
                case FPS,PING -> toggle(tr("Единицы измерения","Show units"),()->s.showUnits,v->s.showUnits=v);
                case CPS -> toggle(tr("Раздельные строки кнопок мыши","Separate mouse button rows"),()->s.splitCps,v->s.splitCps=v);
                case ARMOR -> toggle(tr("Прочность в процентах","Durability percentage"),()->s.showPercent,v->s.showPercent=v);
                case EFFECTS -> slider(tr("Максимум эффектов","Maximum effects"),1,8,()->s.effectsLimit,v->s.effectsLimit=(int)Math.round(v),v->Long.toString(Math.round(v)));
                case REACH -> slider(tr("Время показа дистанции","Display duration"),1,15,()->s.reachSeconds,v->s.reachSeconds=(int)Math.round(v),v->Math.round(v)+" s");
                case COORDS -> slider(tr("Знаков после запятой","Decimal places"),0,2,()->s.decimals,v->s.decimals=(int)Math.round(v),v->Long.toString(Math.round(v)));
                case ZOOM -> slider(tr("Кратность приближения","Zoom factor"),2,10,()->s.zoomFactor,v->s.zoomFactor=(float)v,v->String.format(Locale.ROOT,"%.1f×",v));
                case CHUNKS -> {
                    slider(tr("Дальность прорисовки","Render distance"),8,32,()->s.chunkRenderDistance,v->s.chunkRenderDistance=(int)Math.round(v),v->Math.round(v)+" "+tr("чанков","chunks"));
                    toggle(tr("Сохранять полученные чанки","Retain received chunks"),()->s.retainChunks,v->s.retainChunks=v);

                }
                case GLINT -> {
                    toggle(tr("Цвет сияния предметов","Recolor item glint"),()->s.glintItems,v->s.glintItems=v);
                    toggle(tr("Цвет сияния брони","Recolor armor glint"),()->s.glintArmor,v->s.glintArmor=v);
                    toggle(tr("Обводка от первого лица","First person outline"),()->s.glowFirstPerson,v->s.glowFirstPerson=v);
                    toggle(tr("Обводка от третьего лица","Third person outline"),()->s.glowThirdPerson,v->s.glowThirdPerson=v);
                    toggle(tr("Обводка в инвентаре","Inventory outline"),()->s.glowInventory,v->s.glowInventory=v);
                }
                case SKIN_3D -> {
                    slider(tr("Дальность объёмного слоя","3D layer distance"),8,64,()->s.skinDistance,v->s.skinDistance=(int)Math.round(v),v->Math.round(v)+" m");
                    toggle(tr("Второй слой рук от первого лица","First person arm layer"),()->s.skinFirstPerson,v->s.skinFirstPerson=v);
                }
                case HITBOX -> {
                    toggle(tr("Игроки","Players"),()->s.hitboxPlayers,v->s.hitboxPlayers=v);
                    toggle(tr("Враждебные существа","Hostile mobs"),()->s.hitboxHostile,v->s.hitboxHostile=v);
                    toggle(tr("Мирные существа","Passive mobs"),()->s.hitboxPassive,v->s.hitboxPassive=v);
                    toggle(tr("Предметы и остальные сущности","Items and other entities"),()->s.hitboxOther,v->s.hitboxOther=v);
                    toggle(tr("Только цель под прицелом","Only targeted entity"),()->s.hitboxOnlyTarget,v->s.hitboxOnlyTarget=v);
                    toggle(tr("Не показывать союзников","Hide allied entities"),()->s.hitboxHideAllies,v->s.hitboxHideAllies=v);
                    toggle(tr("Свой хитбокс от третьего лица","Own hitbox in third person"),()->s.hitboxSelf,v->s.hitboxSelf=v);
                    toggle(tr("Невидимые сущности","Invisible entities"),()->s.hitboxInvisible,v->s.hitboxInvisible=v);
                    toggle(tr("Показывать за препятствиями","Show through obstacles"),()->s.hitboxThroughWalls,v->s.hitboxThroughWalls=v);
                    slider(tr("Максимальная дистанция","Maximum distance"),8,128,()->s.hitboxDistance,v->{s.hitboxDistance=(int)Math.round(v);s.hitboxMinDistance=Math.min(s.hitboxMinDistance,s.hitboxDistance-1);if(minimumDistance!=null)minimumDistance.maximum(s.hitboxDistance-1);},v->Math.round(v)+" m");
                    rows.add(yy->{minimumDistance=new OrbitSlider(rowX,yy,rowWidth,tr("Минимальная дистанция","Minimum distance"),0,s.hitboxDistance-1,s.hitboxMinDistance,v->s.hitboxMinDistance=(int)Math.round(v),v->Math.round(v)+" m");addRenderableWidget(minimumDistance);});
                    toggle(tr("Плавно исчезать вдали","Fade with distance"),()->s.hitboxFade,v->s.hitboxFade=v);
                    if(s.hitboxFade)slider(tr("Начало затухания от дальности","Fade start as range percentage"),.1,.95,()->s.hitboxFadeStart,v->s.hitboxFadeStart=(float)v,v->Math.round(v*100)+"%");
                    action(tr("Камера: ","Camera: ")+new String[]{tr("Любой вид","Any view"),tr("От первого лица","First person"),tr("От третьего лица","Third person")}[s.hitboxCameraMode],()->{s.hitboxCameraMode=(s.hitboxCameraMode+1)%3;OrbitClient.save();rebuildWidgets();});
                    toggle(tr("Скрывать при открытом меню","Hide while a menu is open"),()->s.hitboxHideMenus,v->s.hitboxHideMenus=v);
                    toggle(tr("Только с оружием ближнего боя","Only while holding a melee weapon"),()->s.hitboxWeaponOnly,v->s.hitboxWeaponOnly=v);
                    toggle(tr("Только при приседании","Only while sneaking"),()->s.hitboxCrouchOnly,v->s.hitboxCrouchOnly=v);
                }
                case CROSSHAIR -> {
                    action(tr("Готовые прицелы и редактор","Presets and pixel editor"),()->minecraft.gui.setScreen(new CrosshairScreen(returnTarget())));
                    if(s.crosshairMode==0) {
                    action(tr("Форма: ","Shape: ")+new String[]{tr("Крест","Cross"),"T",tr("Точка","Dot"),tr("Кольцо","Ring"),"X"}[s.crosshairShape],()->{s.crosshairShape=(s.crosshairShape+1)%5;rebuildWidgets();});
                    if(s.crosshairShape!=2) {
                    slider(tr("Размер","Size"),2,12,()->s.crosshairSize,v->s.crosshairSize=(int)Math.round(v),v->Long.toString(Math.round(v)));
                    slider(tr("Зазор","Gap"),0,8,()->s.crosshairGap,v->s.crosshairGap=(int)Math.round(v),v->Long.toString(Math.round(v)));
                    slider(tr("Толщина","Thickness"),1,3,()->s.crosshairThickness,v->s.crosshairThickness=(int)Math.round(v),v->Long.toString(Math.round(v)));
                    toggle(tr("Точка в центре","Center dot"),()->s.crosshairDot,v->s.crosshairDot=v);
                    }
                    if(s.crosshairDot || s.crosshairShape==2) slider(tr("Размер точки","Dot size"),1,5,()->s.dotSize,v->s.dotSize=(int)Math.round(v),v->Long.toString(Math.round(v)));
                    }
                    toggle(tr("Обводка","Outline"),()->s.crosshairOutline,v->s.crosshairOutline=v);
                    if(s.crosshairOutline) slider(tr("Толщина обводки","Outline width"),1,3,()->s.outlineWidth,v->s.outlineWidth=(int)Math.round(v),v->Long.toString(Math.round(v)));
                    if(s.crosshairMode==0)toggle(tr("Расширение при движении","Expand while moving"),()->s.crosshairDynamic,v->s.crosshairDynamic=v);
                    if(s.crosshairMode==0 && s.crosshairDynamic) slider(tr("Дополнительный зазор","Movement gap"),0,8,()->s.dynamicGap,v->s.dynamicGap=(int)Math.round(v),v->Long.toString(Math.round(v)));
                    toggle(tr("Показывать от третьего лица","Show in third person"),()->s.crosshairThirdPerson,v->s.crosshairThirdPerson=v);
                    toggle(tr("Скрывать в меню","Hide in menus"),()->s.crosshairHideMenus,v->s.crosshairHideMenus=v);
                }
                case WAYPOINT -> {
                    rows.add(y->{var field=new EditBox(font,rowX+10,y+11,rowWidth-20,10,Component.literal(tr("Название точки","Waypoint name")));field.setBordered(false);field.setTextShadow(false);field.setTextColor(OrbitTheme.TEXT);field.setMaxLength(32);field.setValue(waypointName);field.setResponder(v->waypointName=v);addRenderableWidget(field);});
                    action(tr("Сохранить текущую позицию","Save current position"),()->ClientFeatures.saveWaypoint(waypointName));
                    action(tr("Удалить точку","Remove waypoint"),()->{OrbitClient.config.waypoint=null;OrbitClient.save();});
                }
                default -> { }
            }
        }

        if(inline) {
            contentRegion(x,top,w,1);contentHeight(0);
            rowX=x+6;rowWidth=w-12;
            int bodyY=top+4+tabRows*(tabHeight+tabGap);
            int cursor=bodyY;
            for(var row:rows){
                row.accept(cursor);
                var widget=(AbstractWidget)children().getLast();
                if(module==Module.INTERFACE&&!(widget instanceof OrbitSlider))widget.setHeight(binding&&cursor==bodyY?30:20);
                cursor+=module==Module.INTERFACE?widget.getHeight()+1:32;
            }
            int footer=cursor+2;
            addRenderableWidget(new OrbitButton(rowX,footer,rowWidth,18,tr("Сбросить настройки","Reset settings"),false,()->resetSettings(s)));
            inlineHeight=footer-top+22;
            for(var child:children()) {
                if(child instanceof OrbitButton button)button.compact();
                if(child instanceof OrbitSlider slider)slider.compact();
                if(child instanceof EditBox field)field.setTextColor(0xFFE4EDF5);
            }
            clipInline(clipTop,clipBottom);
            return;
        }
        contentRegion(x+12,top+78,w-16,panelHeight-116);
        rowX=x+12;rowWidth=w-24;
        content(()->{for(int i=0;i<rows.size();i++)rows.get(i).accept(top+78+i*38);});
        contentHeight(rows.size()*38-6);
        int footer=top+panelHeight-28;
        addRenderableWidget(new OrbitButton(x+12,footer,64,22,tr("Сбросить","Reset"),false,()->resetSettings(s)));
        addRenderableWidget(new OrbitButton(x+w-76,footer,64,22,tr("Готово","Done"),true,this::onClose));
    }
    private void resetSettings(ClientConfig.State state) {
        if(module==Module.INTERFACE){
            if(tab==1)OrbitTheme.options().hud=new HudAppearance();
            else {var hud=OrbitTheme.options().hud;OrbitClient.config.interfaceOptions=new InterfaceOptions();OrbitTheme.options().hud=hud;}
            OrbitClient.save();rebuildWidgets();return;
        }
        var fresh=ClientConfig.defaults(module);
        if(module==Module.CROSSHAIR)fresh.crosshairPixels=state.crosshairPixels;
        OrbitClient.config.modules.put(module,fresh);OrbitClient.save();rebuildWidgets();
    }
    private Screen returnTarget(){return inline?parent:this;}
    void initInline(int screenWidth,int screenHeight,int x,int y,int width) {
        inline=true;inlineX=x;inlineY=y;inlineWidth=width;
        init(screenWidth,screenHeight);
    }
    int inlineHeight(){return inlineHeight;}
    boolean isBinding(){return binding;}
    void cancelBinding(){if(binding||reservedBinding){binding=false;reservedBinding=false;rebuildWidgets();}}
    void moveInline(int x,int y) {
        int dx=x-inlineX,dy=y-inlineY;
        inlineX=x;inlineY=y;this.x=x;top=y;rowX+=dx;
        for(var child:children())if(child instanceof AbstractWidget widget){widget.setX(widget.getX()+dx);widget.setY(widget.getY()+dy);}
        clipInline(clipTop,clipBottom);
    }
    void clipInline(int top,int bottom) {
        clipTop=top;clipBottom=bottom;
        for(var child:children())if(child instanceof AbstractWidget widget)widget.visible=bottom>top&&widget.getY()+widget.getHeight()>top&&widget.getY()<bottom;
    }
    void renderInline(GuiGraphicsExtractor g,int mx,int my,float delta) {
        if(clipBottom<=clipTop)return;
        g.enableScissor(inlineX,clipTop,inlineX+inlineWidth,clipBottom);
        try{
        boolean inside=mx>=inlineX&&mx<inlineX+inlineWidth&&my>=clipTop&&my<clipBottom;
        for(var child:children())if(child instanceof AbstractWidget widget&&widget.visible) {
            if(widget instanceof EditBox field) {
                g.fill(rowX,field.getY()-11,rowX+rowWidth,field.getY()+19,OrbitTheme.translucent(OrbitTheme.BASE,242));
                if(field.isFocused())g.fill(rowX,field.getY()+18,rowX+rowWidth,field.getY()+19,OrbitTheme.PRIMARY);
            }
            widget.extractRenderState(g,inside?mx:-10000,inside?my:-10000,delta);
        }
        }finally{g.disableScissor();}
    }
    void verifyInlineLayout() {
        if(!inline)throw new IllegalStateException("Inline layout check requires inline initialization");
        int selected=tab;
        try{verifyLayouts();}finally{tab=selected;rebuildWidgets();}
    }
    private void verifySettingsLayout(){if(inline)verifyInlineBounds();else verifyScrollLayouts();}
    private void verifyInlineBounds() {
        var widgets=children().stream().filter(child->child instanceof AbstractWidget).map(child->(AbstractWidget)child).toList();
        var positions=widgets.stream().map(widget->new int[]{widget.getX(),widget.getY()}).toList();
        var focused=getFocused();int oldX=inlineX,oldY=inlineY,oldTop=clipTop,oldBottom=clipBottom;
        for(var widget:widgets)if(widget.getY()<inlineY||widget.getY()+widget.getHeight()>inlineY+inlineHeight)throw new IllegalStateException("Inline widget escapes settings height");
        for(int i=0;i<widgets.size();i++)for(int j=i+1;j<widgets.size();j++) {
            var a=widgets.get(i);var b=widgets.get(j);
            if(a.getX()<b.getX()+b.getWidth()&&a.getX()+a.getWidth()>b.getX()&&a.getY()<b.getY()+b.getHeight()&&a.getY()+a.getHeight()>b.getY())throw new IllegalStateException("Inline settings widgets overlap");
        }
        moveInline(oldX+7,oldY-19);
        for(int i=0;i<widgets.size();i++)if(widgets.get(i).getX()!=positions.get(i)[0]+7||widgets.get(i).getY()!=positions.get(i)[1]-19||children().get(i)!=widgets.get(i))throw new IllegalStateException("Inline movement rebuilt or misplaced a widget");
        if(getFocused()!=focused)throw new IllegalStateException("Inline movement lost focus");
        clipInline(inlineY+20,inlineY+60);
        for(var widget:widgets)if(widget.visible!=(widget.getY()+widget.getHeight()>clipTop&&widget.getY()<clipBottom))throw new IllegalStateException("Inline clipping differs from widget bounds");
        moveInline(oldX,oldY);clipInline(oldTop,oldBottom);
    }
    private List<Integer> availableTabs(){
        if(module==Module.INTERFACE)return List.of(0,1);
        var result=new ArrayList<Integer>();result.add(0);
        if(switch(module){case TRAJECTORIES,HOLD_MY_ITEMS,GLINT,HITBOX,SKIN_3D,CROSSHAIR->true;default->false;})result.add(1);
        if(module==Module.HITBOX){result.add(2);result.add(3);}
        else if(module==Module.CROSSHAIR){result.add(2);result.add(3);}
        else if(switch(module){case TRAJECTORIES,HOLD_MY_ITEMS,KEYS,FPS,PING,CPS,ARMOR,EFFECTS,REACH,COORDS,ZOOM,CHUNKS,GLINT,SKIN_3D,CROSSHAIR,WAYPOINT->true;default->false;})result.add(2);
        return result;
    }
    private KeyMapping mapping() {return module==Module.INTERFACE?OrbitClient.menuKey:module==Module.HOLD_MY_ITEMS?OrbitClient.inspectKey:module==Module.ZOOM?OrbitClient.zoomKey:OrbitClient.moduleKeys.get(module);}
    private String widthName(int width) {return width==0?tr("Авто","Auto"):width+" px";}
    private static String glowPaletteName(int color) {return switch(color){
        case 0xB9B8FF->tr("Аметист","Amethyst");case 0x59D7C6->tr("Аврора","Aurora");
        case 0xFFAD86->tr("Закат","Sunset");case 0x91DDF8->tr("Лёд","Frost");default->tr("Свой цвет","Custom");
    };}
    private String englishKeyName() {return englishKeyName(mapping().saveString());}
    static String englishKeyName(String id) {
        if(id.equals("key.keyboard.unknown"))return "None";
        String raw=id.startsWith("key.keyboard.")?id.substring(13):id.startsWith("key.mouse.")?"mouse."+id.substring(10):id;
        return switch(raw) {
            case "mouse.left"->"Mouse Left";case "mouse.right"->"Mouse Right";case "mouse.middle"->"Mouse Middle";
            case "space"->"Space";case "enter"->"Enter";case "escape"->"Escape";case "tab"->"Tab";
            case "backspace"->"Backspace";case "delete"->"Delete";case "apostrophe"->"'";case "comma"->",";
            case "period"->".";case "semicolon"->";";case "slash"->"/";case "backslash"->"\\";
            case "left.bracket"->"[";case "right.bracket"->"]";case "minus"->"-";case "equal"->"=";
            default->{String[] words=raw.replace('_',' ').split("[ .]");StringBuilder result=new StringBuilder();for(String word:words)if(!word.isEmpty()){if(!result.isEmpty())result.append(' ');result.append(Character.toUpperCase(word.charAt(0))).append(word.substring(1));}yield result.toString();}
        };
    }
    void selectTabForTest(int value) {tab=value;}
    void verifyLayouts() {
        for(int selected:availableTabs()){tab=selected;resetScroll();rebuildWidgets();verifySettingsLayout();}
        if(module==Module.TRAJECTORIES){var s=OrbitClient.config.state(module);boolean theme=s.trajectoryTheme,marker=s.trajectoryMarker;
            try{s.trajectoryTheme=false;tab=1;for(boolean visible:new boolean[]{false,true}){s.trajectoryMarker=visible;rebuildWidgets();verifySettingsLayout();}}
            finally{s.trajectoryTheme=theme;s.trajectoryMarker=marker;}
        }
        if(module==Module.HITBOX){var s=OrbitClient.config.state(module);boolean fill=s.hitboxOwnFillColor,look=s.hitboxLook,friendly=s.hitboxFriendlyTint,hurt=s.hitboxHurtTint,width=s.hitboxTargetWidthOverride,style=s.hitboxTargetStyleOverride,targetFill=s.hitboxTargetFillOverride,fade=s.hitboxFade;
            s.hitboxOwnFillColor=s.hitboxLook=s.hitboxFriendlyTint=s.hitboxHurtTint=s.hitboxTargetWidthOverride=s.hitboxTargetStyleOverride=s.hitboxTargetFillOverride=s.hitboxFade=true;
            for(tab=1;tab<4;tab++){resetScroll();rebuildWidgets();verifySettingsLayout();}
            s.hitboxOwnFillColor=fill;s.hitboxLook=look;s.hitboxFriendlyTint=friendly;s.hitboxHurtTint=hurt;s.hitboxTargetWidthOverride=width;s.hitboxTargetStyleOverride=style;s.hitboxTargetFillOverride=targetFill;s.hitboxFade=fade;
        }
        if(module==Module.CROSSHAIR){var s=OrbitClient.config.state(module);int old=s.crosshairColorMode;boolean friendly=s.separateFriendlyColor;
            for(int mode=0;mode<3;mode++)for(boolean f:new boolean[]{false,true}){s.crosshairColorMode=mode;s.separateFriendlyColor=f;tab=1;rebuildWidgets();verifySettingsLayout();}
            s.crosshairColorMode=old;s.separateFriendlyColor=friendly;
        }
    }
    private void action(String title,Runnable action) {String id=module.name()+":"+tab+":action:"+rows.size();rows.add(y->addRenderableWidget(new OrbitButton(rowX,y,rowWidth,32,title,false,action).alignLeft().focusId(id)));}
    private void toggle(String title,BooleanSupplier get,Consumer<Boolean> set) {
        rows.add(y->{
            var toggle=new OrbitSwitch(rowX,y,rowWidth,32,title,get.getAsBoolean(),()->{set.accept(!get.getAsBoolean());OrbitClient.save();rebuildWidgets();},module.name()+":"+title);
            addRenderableWidget(toggle);
        });
    }
    private void slider(String title,double min,double max,DoubleSupplier get,DoubleConsumer set,DoubleFunction<String> format) {
        rows.add(y->addRenderableWidget(new OrbitSlider(rowX,y,rowWidth,title,min,max,get.getAsDouble(),set,format)));
    }
    @Override public boolean mouseClicked(MouseButtonEvent event,boolean twice) {
        if(inline&&(event.x()<inlineX||event.x()>=inlineX+inlineWidth||event.y()<clipTop||event.y()>=clipBottom))return false;
        return super.mouseClicked(event,twice);
    }
    @Override public boolean keyPressed(KeyEvent event) {
        if(binding) {
            if(module==Module.INTERFACE&&switch(event.key()){case GLFW.GLFW_KEY_TAB,GLFW.GLFW_KEY_ENTER,GLFW.GLFW_KEY_SPACE,GLFW.GLFW_KEY_UP,GLFW.GLFW_KEY_DOWN,GLFW.GLFW_KEY_LEFT,GLFW.GLFW_KEY_RIGHT,GLFW.GLFW_KEY_PAGE_UP,GLFW.GLFW_KEY_PAGE_DOWN,GLFW.GLFW_KEY_HOME,GLFW.GLFW_KEY_END->true;default->false;}){reservedBinding=true;rebuildWidgets();return true;}
            if(event.key()!=GLFW.GLFW_KEY_ESCAPE && (module==Module.INTERFACE||!OrbitClient.menuKey.matches(event))) {
                boolean clear=event.key()==GLFW.GLFW_KEY_BACKSPACE||event.key()==GLFW.GLFW_KEY_DELETE;
                mapping().setKey(clear?(module==Module.INTERFACE?InputConstants.Type.KEYSYM.getOrCreate(GLFW.GLFW_KEY_RIGHT_SHIFT):InputConstants.UNKNOWN):InputConstants.getKey(event));
                KeyMapping.resetMapping();minecraft.options.save();
            }
            binding=false;reservedBinding=false;rebuildWidgets();return true;
        }
        boolean handled=super.keyPressed(event);
        if(inline)clipInline(clipTop,clipBottom);
        return handled;
    }
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta) {
        if(inline){renderInline(g,mx,my,delta);return;}
        animateScroll();
        beginWindow(g,x,top,w,panelHeight);
        g.fill(x+12,top+panelHeight-34,x+w-12,top+panelHeight-33,OrbitTheme.BORDER);
        OrbitTheme.rounded(g,x+12,top+8,28,28,OrbitTheme.CARD);
        ModuleIcons.draw(g,module,x+16,top+12,20,OrbitTheme.MUTED);
        g.text(OrbitFonts.heading(),OrbitTheme.ellipsize(OrbitFonts.heading(),module.title(),w-(module==Module.CROSSHAIR?112:64)),x+49,top+20,OrbitTheme.TEXT,false);
        if(module==Module.CROSSHAIR) {HudRenderer.panel(g,x+w-54,top+3,44,32,OrbitTheme.CARD);g.pose().pushMatrix();g.pose().translate(x+w-32,top+19);g.pose().scale(.48f,.48f);CrosshairRenderer.draw(g,0,0,OrbitClient.config.state(module),false,0);g.pose().popMatrix();}
        g.enableScissor(viewX,viewY,viewX+viewWidth,viewY+viewHeight);
        for(var child:children())if(child instanceof EditBox field && field.visible)OrbitTheme.surface(g,field.getX()-10,field.getY()-11,field.getWidth()+20,32,OrbitTheme.CARD,field.isFocused()?OrbitTheme.ACCENT:OrbitTheme.BORDER);
        g.disableScissor();
        renderContent(g,mx,my,delta);
        super.extractRenderState(g,mx,my,delta);
    }
    @Override public void onClose() {OrbitClient.save();minecraft.gui.setScreen(parent);}
    @Override public boolean isPauseScreen() {return false;}
}
