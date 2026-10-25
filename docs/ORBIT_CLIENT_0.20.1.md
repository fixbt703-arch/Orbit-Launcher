# Orbit Client 0.20.1 / Orbit Launcher 1.6.1

## GUI scale correction

Minecraft GUI scale 4 exposed a layout bug: a 1920px-wide window was passed to the client menu as about 480 GUI units. The menu treated a desktop window as a small screen, arranged categories in two rows and clipped their module lists.

The Orbit menu now uses a viewport based on the window's pixel size. Minecraft's HUD scale, inventory screens and HUD editor retain the game's chosen scale. The same menu transform applies to drawing, clipping, tooltips, pointer clicks, dragging and scrolling. Both initial opening and the native resize path configure that viewport.

Old category positions from the incompatible viewport are reset once. Module enablement, preferences, key bindings and HUD positions are preserved. New category positions remain persistent.

## Verification

- An isolated Minecraft launch at 1920×1080 / GUI scale 4 reproduced the original oversized two-row layout before the correction.
- Separate regression assertions reproduced native resize bypassing the viewport and transformed clipping rejecting visible row hover positions.
- The menu-density test covers six window resolutions and GUI scales 1–4, plus pointer-coordinate round trips.
- The in-game scale scenario exercises native GUI scales 1–4, the resize path, real menu click handlers, slider dragging, smooth panel scrolling, scoped tooltip/clipping bounds, and configuration migration. Existing bilingual UI/editor/world checks also run.
- Launcher regression checks exclude Orbit Connect. Release verification checks the exact bundled client in the existing EXE and refreshes the existing installer checksum.

No additional dependencies or network services were introduced.
