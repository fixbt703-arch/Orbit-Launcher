# Orbit Client 0.23.0 — Complete themes and background strength

Bundled with Orbit Launcher 1.9.0 for Minecraft 26.2 / Fabric.

Interface offers eight themes, in this order: Ocean, Emerald, Rose, Gold, Neon,
Blood, Rainbow and Obsidian. Names follow the Russian/English UI language. Each
palette defines dark base, panel and control surfaces, readable foregrounds,
accent, hover, selection and borders. Search, category panels, inline settings,
editors and gameplay HUD use the same palette. Theme changes interpolate together.
Rainbow adds a spectrum accent and changing surface tint; its speed follows
Animation speed and it freezes when GUI animations are disabled.

The Menu background option shows a strength slider for its selected mode:

- **Blur:** 0–100%, mapped to Minecraft's native radius 0–10.
- **Dim:** 0–100%, with no blur.
- **None:** clear background with no strength slider.

The two strengths are saved independently. The common Orbit screen pass applies
the background once. A scoped native blur-radius getter supplies the render
uniform while an Orbit screen is open; Minecraft's saved option is never changed.

Interface settings use tighter, consistent row spacing. Opening its page makes
room for the controls in an ordinary window, retaining the group's saved position
when the page closes. Groups remain draggable and overflow remains scrollable.
Rectangular row, expansion and compact-slider focus borders remain rectangular.

Old palettes migrate to their nearest replacement: Amethyst to Neon, Mint to
Emerald, Sunset to Gold; Ocean and Rose retain their identities. The migration
preserves HUD positions, scale, enabled states and module behavior.

Validation includes eight palette contrast checks, Rainbow speed/freeze checks,
preference bounds, live controls and persistence in RU/EN, native blur render-state
assertions, screenshots of all eight themes and blur/dim extremes, and actual
Minecraft GUI scale/input/layout regressions. Installer compilation does not
install over the user's system.
