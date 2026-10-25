# Orbit Client 0.20.0 / Orbit Launcher 1.6.0

The module library is now a compact navy/cyan workspace inspired by the supplied screenshots and the panel interactions in https://www.youtube.com/watch?v=vExCR8NvBZ0. No code or artwork from that client is bundled.

## Controls

- Right Shift opens or closes Orbit Client.
- Left click a module name to toggle it. Right click its row, or click +, to expand its settings inside the category.
- Drag a category header to move it. Click the header to collapse or expand; right click also collapses it.
- Each category scrolls independently with the wheel or its scrollbar. Page Up, Page Down, Home and End scroll the focused category.
- Search sits at the top. Ctrl+F focuses it. An empty result includes a reset action.
- HUD Editor stays at the bottom center. The bottom-right reset action restores category positions without resetting modules or HUD layout.
- Tab/Shift+Tab traverse controls. Focus raises the owning panel; arrow keys move a focused category header.

All 22 module names are English in both menu and key-binding descriptions. Settings and functional descriptions still follow the Minecraft language. Module IDs, bindings, module preferences, HUD positions and the existing Enchantment Glow rendering are retained.

## Layout and motion

Categories keep separate scroll and selected settings tabs. Their normalized positions and collapsed state are saved in `config/orbit-client.json`, independently of HUD coordinates. Small viewports use two rows. Opening a settings panel raises it and makes room above if necessary; closing restores its previous vertical anchor. Expansion, hovering, switch changes, scrolling, opening and closing use bounded time-based transitions. Settings use one fixed text size, wrapping long labels. The client UI has no text shadows.

No launcher account, world or Connect transport logic was changed by the redesign.

## Verification

Build and actual isolated Minecraft UI/world verification results are recorded with the delivery in the task workspace. Tests cover group coverage, English names, persistent settings, RU/EN, six GUI sizes from 320×180 to 960×540, inline/standalone settings, keyboard binding capture, editor return focus, scroll clipping, drag limits and resource reload. Release packaging verifies the exact bundled client payload in the existing EXE; installer checksum is refreshed.
