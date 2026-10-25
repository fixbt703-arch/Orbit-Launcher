# Orbit Client 0.22.1 — Focus border geometry

Bundled with Orbit Launcher 1.8.1 for Minecraft 26.2 / Fabric.

Rectangular module rows, expansion buttons and compact sliders now use rectangular
focus borders. Previously their fills were rectangular while their focus borders
incorrectly inherited the global corner radius. Rounded controls continue using
the same radius for fill and border.

The correction preserves the shared HUD appearance introduced in
[0.22.0](ORBIT_CLIENT_0.22.0.md). Validation captures the actual focused module row
and compact slider with corner rounding set to 12 in an isolated Minecraft run.
