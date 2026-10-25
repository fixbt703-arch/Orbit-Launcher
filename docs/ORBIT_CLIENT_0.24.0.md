# Orbit Client 0.24.0 — Trajectories

Bundled with Orbit Launcher 1.10.0 for Minecraft 26.2 / Fabric.

Open Orbit Client with Right Shift, expand **Visuals → Trajectories**, and enable the module. It starts disabled. A binding can be assigned in General.

## Flight preview

- Bow: follows the current draw strength. Optional full-charge preview while held.
- Charged crossbow: central arrow; no ballistic preview for firework rockets.
- Ender pearls, snowballs, all three egg colors, splash/lingering potions and experience bottles.
- Tridents once charged; Riptide is excluded because it launches the player rather than a projectile.

The thin depth-tested line stops at the first block or entity collision. A small ring and cross mark the impact surface. The module reads local world state and never aims, fires, throws, changes movement or sends packets.

## Settings

**Appearance:** interface theme or custom HEX/HSV color, line opacity (10–100%), width (0.5–4 pixels), fade near the player, impact marker and its radius (0.08–0.5 blocks).

**Options:** individual projectile types, charge-only bow/trident preview, offhand, third-person visibility, hide in menus and maximum path length (16–256 blocks). Prediction is always capped at 200 ticks.

Defaults: theme color, 1.5-pixel line at 85% opacity, subtle start fade, 0.18-block impact ring, 128-block path, hide in menus. Instant throwables preview while held; charging-only applies to bow/trident.

## Accuracy and limits

Uses the installed vanilla launch constants, gravity/drag order, player momentum and water behavior. Block shapes use the projectile position, not player equipment. Entity collision margin increases with projectile age. Arrow friendly-fire restrictions are respected.

This is a nominal prediction: random shot spread, targets moving after release, network delay, deflections, piercing after the first target, server plugins and custom projectile mechanics can change the result. Crossbow multishot displays the central arrow. No promise of exact future hits is made.

Prediction ends without an impact marker at unknown chunks, dimension portals, bubble columns, lava or its maximum length/time. It never loads or generates chunks. Offhand is suppressed when a mainhand shield, food or targeted block placement would take priority.

Server permission is determined by that server's rules; a visual-only implementation is not universal permission to use trajectory assistance.

## Verification

Java deterministic physics tests, isolated native projectile comparisons, collision/render/configuration checks, bilingual settings geometry, launcher regression suite excluding Orbit Connect, and distribution payload verification. See the task's delivery record for executed results.
