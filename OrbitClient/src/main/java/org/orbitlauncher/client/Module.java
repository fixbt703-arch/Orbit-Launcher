package org.orbitlauncher.client;

public enum Module {
    FPS("FPS", "FPS", "Частота кадров", "Frames per second", "HUD", true),
    PING("Пинг", "Ping", "Задержка до сервера", "Server latency", "HUD", true),
    CPS("CPS", "CPS", "Клики левой и правой кнопки за секунду", "Left and right clicks per second", "PVP", true),
    KEYS("Нажатия клавиш", "Keystrokes", "WASD, прыжок и кнопки мыши", "Movement keys, jump and mouse buttons", "PVP", true),
    ARMOR("Броня", "Armor", "Остаток прочности вашей брони", "Remaining armor durability", "PVP", true),
    EFFECTS("Эффекты", "Potion effects", "Активные эффекты и оставшееся время", "Active effects and remaining time", "PVP", false),
    COORDS("Координаты", "Coordinates", "Положение игрока X / Y / Z", "Player position X / Y / Z", "HUD", false),
    SPEED("Скорость", "Speed", "Горизонтальная скорость, блоков/с", "Horizontal speed in blocks per second", "HUD", false),
    SESSION("Время игры", "Session time", "Время текущей игровой сессии", "Elapsed time in the current session", "HUD", false),
    CROSSHAIR("Прицел", "Crosshair", "Цветной прицел: масштаб и цвет", "Custom crosshair size and color", "PVP", false),
    ZOOM("Приближение", "Zoom", "Удерживайте C; клавишу можно переназначить", "Hold C; rebind in Minecraft controls", "PVP", true),
    TOGGLE_SPRINT("Переключение бега", "Toggle Sprint", "Штатный бег по нажатию, без удержания клавиши", "Native sprint toggle instead of holding the key", "PVP", false),
    REACH("Дистанция атаки", "Reach display", "Локальное расстояние до цели последней атаки; не меняет дальность", "Local distance to your last attacked target; does not change reach", "PVP", false),
    SERVER("Сервер", "Server", "Адрес текущего сервера или одиночный мир", "Current server address or singleplayer world", "HUD", false),
    MEMORY("Память", "Memory", "Использование выделенной Minecraft памяти", "Minecraft heap memory usage", "HUD", false),
    WAYPOINT("Путевая точка", "Waypoint", "Сохранённые координаты и расстояние в текущем мире", "Saved coordinates and distance in the current world", "HUD", false),
    CHUNKS("Дальние чанки", "Extended Chunks", "Показывает посещённые чанки дальше лимита сервера", "Renders visited chunks beyond the server limit", "HUD", false),
    AUTO_REFILL("Stack Refill", "Stack Refill", "Заменяет закончившийся стак таким же блоком из инвентаря", "Refills an exhausted block stack with the same block", "PVP", false),
    SKIN_3D("3D Mod", "3D Mod", "Объёмный второй слой скина", "Voxel outer skin layer", "HUD", false),
    GLINT("Enchantment Glow", "Enchantment Glow", "Мягкое свечение зачарованных предметов", "Soft, flowing glow on enchanted items", "PVP", false),
    HITBOX("Hitbox", "Hitbox", "Настраиваемые границы сущностей и выделение цели", "Entity bounds and target highlighting", "PVP", false),
    // Keep the serialized ID so existing enabled states and key bindings survive the rename.
    HOLD_MY_ITEMS("Hold My Items", "Hold My Items", "Оригинальные анимации рук и предметов", "Original hand and item animations", "PVP", false),
    INTERFACE("Интерфейс", "Interface", "Темы, анимации и оформление меню клиента", "Client menu themes, motion and appearance", "HUD", true),
    TRAJECTORIES("Траектории", "Trajectories", "Прогноз полёта ваших снарядов и первой точки столкновения", "Your projectile flight and first impact preview", "PVP", false);

    public final String ru, en, descriptionRu, descriptionEn, category;
    public final boolean defaultEnabled;
    Module(String ru, String en, String dr, String de, String category, boolean enabled) {
        this.ru=ru; this.en=en; descriptionRu=dr; descriptionEn=de;
        this.category=category; defaultEnabled=enabled;
    }
    public String title() { return en; }
    public String description() { return OrbitClient.tr(descriptionRu,descriptionEn); }
    public boolean hasHud() { return this != TRAJECTORIES && this != INTERFACE && this != ZOOM && this != CROSSHAIR && this != TOGGLE_SPRINT && this != CHUNKS && this != AUTO_REFILL && this != SKIN_3D && this != GLINT && this != HITBOX && this != HOLD_MY_ITEMS; }
}
