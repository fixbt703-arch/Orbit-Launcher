# Orbit Client 0.19.1 — Enchantment Glow silhouette

Bundled with Orbit Launcher 1.5.7, Minecraft 26.2 / Fabric.

Enchantment Glow uses filled inverted hulls for a colored exterior silhouette, replacing the previous lines along individual model faces. The item texture and original scrolling enchantment glint remain visible. Model-space moving highlights, gentle pulse, custom colors, rainbow, opacity, width, diffusion and context controls remain available. No particles or new item effects are added.

Held-item outlines follow native first-person, third-person, inventory and Hold My Items inspection poses. Enchanted armor uses its posed cuboids and visibility, preserving the original armor and glint submissions. Item and armor switches remain separate. Outline width is a model-relative multiplier rather than a claimed fixed screen-pixel size. Geometry/model/render-type caches are bounded.

The outline shader alpha-tests the original texture independently of pulse and opacity. Minecraft 26.2 reversed-Z depth testing hides outlines behind nearer surfaces. Resource-pack texture atlases are selected per quad.

The inverted-hull approach and shader structure are adapted from [Enchantment Glint Outline](https://github.com/Da0neDatGotAway/better-enchants/tree/bbe453f40781fb93018e85a6c35b04e94fbc069d), CC0-1.0. The client JAR includes its license and attribution. No separate outline mod or shader pack is required.

## Validation

- Offline Gradle `check build` passed, including hull offsets/degenerate geometry, moving highlights and pulse, color continuity, module definitions and HUD layouts.
- The final bundled JAR passed a fresh 395-tick Minecraft world run with all bundled performance mods: native first person, HMI idle/inspection, inventory and third person, enchanted armor, color persistence, zero alpha, non-enchanted filtering and independent item/armor/context switches. Pickaxe, sword and armor captures were inspected. The fixture switches to survival before testing the inventory avatar.
- All 126 launcher checks passed with Orbit Connect checks excluded. All 25 recorded Orbit Connect file hashes remain unchanged.
- Published EXE version 1.5.7.0 contains the exact verified application DLL and Orbit Client 0.19.1 JAR, including shaders and attribution. The existing installer is rebuilt from that EXE.

Checks use fictional offline sessions in fresh test directories. Arbitrary external shader/resource packs and long-duration GPU performance were not exhaustively tested.
