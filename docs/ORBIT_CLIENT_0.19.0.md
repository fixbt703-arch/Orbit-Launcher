# Orbit Client 0.19.0 — 2026-10-21

Bundled with Orbit Launcher 1.5.6 for the existing Minecraft 26.2 / Fabric target.

## Interface

- Module cards show short functional descriptions in Russian/English, with up to two lines, instead of repeating HUD/PVP. The grid uses wider cards to keep descriptions readable.
- Removed the Performance button and its informational screen. Installed optimization components remain managed by the launcher.
- The PVP tab uses the pixel sword artwork previously used by Held item.
- Module library, settings and crosshair windows open with a 220 ms ease-out, restrained scale/lift and backdrop fade. Rebuilding widgets does not restart the animation.
- Settings labels use the same font weight, size and left alignment across switches, sliders and actions. Long labels wrap; exceptional overflow is ellipsized with full accessible text/tooltips. Navigation headings retain their own type role.
- Removed Held item, Clock, Sprint status, Arrow counter, Resource counter and Direction, including their HUD/settings logic. Old configuration entries are removed before deserialization; surviving module settings are retained. Toggle Sprint and Hold My Items are separate features and remain available.

## Enchantment Glow

A feathered colored halo surrounds a saturated rim and a thin luminous core. A slow highlight moves along the model rather than flashing the whole outline. Five outer-to-inner feather bands retain a defined item silhouette and readable texture. Optional rainbow varies spatially; pulse remains restrained. Amethyst, Aurora, Sunset and Frost presets set coherent glint/outline colors; custom colors and width, opacity, diffusion and context controls remain available.

The existing resource-pack alpha contours, bounded geometry cache, HMI pose transforms, enchantment filtering and depth-tested render pipeline are preserved. No external shader pack or additional dependency is required. Rendering remains cosmetic.

Visual research: author descriptions for [Item Glint](https://modrinth.com/mod/item-glint), [Outline Effects](https://modrinth.com/mod/outline-effects) and [Custom Glow](https://github.com/MoubaEZPZ/custom-glow-enchantment-outline). These informed layered falloff and moving-light behavior; their code and assets were not copied.

## Validation

- Gradle `check build`: crosshair roundtrips, alpha contour geometry, feather/color continuity, 216 HUD layout combinations and all 22 module definitions.
- Actual isolated Minecraft UI run: Russian/English layouts at six GUI sizes, 4,632 screen/scroll checks, search and empty-result recovery, keyboard navigation, focus restoration, state persistence, removed-module migration and bounded animation. Rendered library, settings and opening frames were inspected.
- Actual isolated world run: native first-person items, HMI inspection, inventory and third-person glow; zero alpha, disabled contexts, unenchanted items, settings persistence and all module settings layouts. All four palette actions persist both colors. Day/night captures were inspected and the halo/core balance refined. HMI's existing native/inspection regression checks pass.
- All 126 launcher regression checks pass, excluding Orbit Connect, which this update does not modify. The published EXE contains the exact verified application DLL and client JAR; its version is 1.5.6.0. The existing installer is rebuilt with that EXE and a matching SHA256SUMS.txt.

Game checks use fictional offline sessions in fresh directories and worlds, with bundled components. Real accounts and installed user instances are not used. Arbitrary third-party shader/resource packs and long-duration GPU performance were not exhaustively tested.
