# Orbit Client 0.22.0 — Shared HUD appearance

Bundled with Orbit Launcher 1.8.0 for Minecraft 26.2 / Fabric.

Interface now has **Menu** and **HUD** pages. Theme, gradient, glow and corner
rounding are shared with gameplay HUD widgets. Themes continue updating during
gameplay. Flowing gradients respect the animation switch and speed.

The HUD page controls background, Classic/Outline/Glass/Minimal style, background
opacity, border, accent stripe, title, accent title, text opacity, padding, row
spacing, width and alignment. Reset affects the selected page. HUD reset preserves
the menu theme; menu reset preserves HUD appearance.

HUD modules no longer expose individual Appearance pages or color editors.
Position, scale and displayed data remain per widget. Existing saved positions,
scales, enabled states and functional preferences are retained. Old per-widget
appearance fields are ignored by HUD rendering; theme colors are shared.
Feature-specific Crosshair, Enchantment Glow and Hitbox appearance remains separate.

The search clear icon has a transparent surface and an inset hover/focus indicator
inside the rounded field. Clearing search or recovering an empty result returns
keyboard input to the field. Changing settings tabs safely restores visibility
against the original widgets. Editor controls preserve focus after rebuilding.
Leaving a key-capture setting cancels pending capture. Visiting the HUD editor or
closing it after Auto no longer converts automatic positions to custom positions.

Validation: Java preference/geometry checks, launcher regression suite excluding
Connect, isolated Minecraft pointer/keyboard workflows, RU/EN layouts, GUI scales
1–4, and frame-to-frame shared HUD rendering checks. Release installer is compiled
without installing it over the user's application.
