# Hold My Items embedded in Orbit Client

Original: Hold My Items 5.1.1 by sapling (Thesapling), downloaded from
https://modrinth.com/mod/hold-my-items/version/KZm1qTcU . The official JAR
contains `LICENSE_holdmyitems` (CC0-1.0) and declares CC0-1.0 in fabric.mod.json.
The hosting page's generic ARR label differs from those version-specific files.

26.2 migration base: ThinkofRain1213/HMI-26.2,
commit dfdd72e7349f0b87f7ebd4ffae3f18fff8611c82 (CC0-1.0).
https://github.com/ThinkofRain1213/HMI-26.2

Original artwork credits: Cyberia, Radiati0nal; project contributors Cubert1,
_Hellay. All original names, Lua animation tracks and models are retained.
The embedded build is an unofficial adaptation, not a new original mod.

Orbit changes: runtime enable/disable, hand selection, native combat option,
view offsets/scale, hand/particle/block toggles, inertia strength and a native
rebindable inspect key. Vanilla hand selection is preserved rather than
permanently mutating the HandRenderSelection enum. Disabled HMI bypasses its
render/property/camera overrides. LuaJ remains nested with its own license.

Build: OrbitClient/build.ps1 builds this project from source and nests the JAR
inside Orbit Client. No precompiled HMI download is shipped as the adaptation.

Orbit gates all 35 upstream item definitions with a runtime conditional model property and restores their Minecraft 26.2 fallback when disabled. The original end-crystal model was renamed to avoid overriding vanilla while disabled. Ten invalid animation metadata files referring to multiple frames on static bucket UV textures were removed; the original pixels are unchanged.

Orbit adaptation 5.1.2+26.2-orbit.2: the vanilla-animation option now keeps the complete native idle/attack/use/recovery path outside R inspection. Renderer transitions clear Lua globals, model poses, item/dispatcher caches and camera transforms. All HMI model/camera/use overrides share this gate. A callback emits Orbit's independently implemented enchanted-item outline after the original animated foil geometry, before clearing its poses.
