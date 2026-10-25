package org.orbitlauncher.client;

enum ClickGuiGroup {
    VISUALS("Visuals",Module.INTERFACE,Module.TRAJECTORIES,Module.CROSSHAIR,Module.GLINT,Module.HOLD_MY_ITEMS,Module.SKIN_3D,Module.CHUNKS),
    MOVEMENT("Movement",Module.TOGGLE_SPRINT,Module.ZOOM,Module.AUTO_REFILL),
    COMBAT("Combat",Module.HITBOX,Module.REACH,Module.CPS,Module.KEYS,Module.ARMOR,Module.EFFECTS),
    HUD("HUD",Module.FPS,Module.PING,Module.COORDS,Module.SPEED,Module.SESSION,Module.SERVER,Module.MEMORY,Module.WAYPOINT);

    final String title;
    final Module[] modules;
    ClickGuiGroup(String title,Module... modules){this.title=title;this.modules=modules;}
}
