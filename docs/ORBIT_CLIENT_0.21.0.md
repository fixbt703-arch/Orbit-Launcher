# Orbit Client 0.21.0 — Interface

Bundled with Orbit Launcher 1.7.0 for Minecraft 26.2 / Fabric.

Open Orbit Client with Right Shift. Interface is the first entry in Visuals; click
its name or the expansion button to configure the menu. Settings apply immediately
and are saved on interaction or slider release. The module stays available.

- **Bind:** reassign the menu key. Escape cancels; Delete/Backspace restores Right Shift.
  Navigation keys remain reserved for keyboard access to settings.
- **Theme:** Ocean, Amethyst, Rose, Sunset or Mint.
- **Gradient:** Off, Static or Flow on category accent lines.
- **Animation speed:** 0.5–4.0; reference default 2.0.
- **Smooth changes:** interpolate theme, hover, toggle, expansion and scroll changes.
- **GUI animations:** disable motion, including menu opening/closing and flowing gradients.
- **Glow:** restrained light around category accents; independent of Enchantment Glow.
- **Tooltips:** show or hide client menu hints; narration and Minecraft menus are retained.
- **Bind messages:** optional local action-bar feedback when a module hotkey toggles it.
- **Menu background:** Blur, Dim or None. Blur respects Minecraft's blur setting.
- **Corner rounding:** 0–12 menu units, including square corners at zero.

Reset settings restores the Interface appearance defaults, preserving the chosen
menu key, HUD positions and colors, other modules and category positions.
All module names remain English; settings follow the game's Russian/English language.

Validation includes pure preference/geometry checks and the isolated Minecraft UI
workflow: live theme changes, every settings control, persistence, rebinding, reset,
native GUI scales 1–4, clipped input and RU/EN settings layouts.
