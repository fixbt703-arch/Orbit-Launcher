# Orbit Client 0.19.2 — exterior perimeter correction

Bundled with Orbit Launcher 1.5.8 for Minecraft 26.2 / Fabric.

Enchantment Glow now draws an ordinary pixel border around the visible silhouette. The enlarged inverted model hulls and per-face wire geometry have been removed. Item and armor geometry is submitted at its original size only to an offscreen mask, which never colors the scene directly.

The mask combines overlapping model parts into one coverage area. A two-pass screen-space filter finds the nearest silhouette pixel and draws only outside the original coverage, with configurable width and edge softness. Internal faces and joins are not traced. Original model pixels remain unchanged by this pass. The native enchantment glint, model-space moving highlights, pulse, colors and context switches remain available. No particles are added.

Mask draws wait until the end of the feature phase, using its final opaque depth without modifying it. Separate hand, world and GUI scopes preserve their projections and render targets. Fog visibility is carried separately from mask membership. Temporary targets are bounded to four sizes and released on client shutdown.

GPU regression checks capture the same frame before/after compositing and read the actual mask alpha. They assert unchanged model interiors, a bounded exterior pixel band, and full occlusion by a later opaque block-display renderer. Source and shaders are included in the public export. The bundled attribution covers the adapted outline vertex shader structure; the distance/perimeter passes are implemented in Orbit Client.

Validation passed: offline Gradle `check build`, all 126 launcher checks excluding Orbit Connect, and a fresh 440-tick bundled Minecraft world run. GPU readbacks kept 14,597 sword pixels, 91,966 pickaxe pixels and 28,538 armor pixels unchanged while drawing only their exterior bands. A later opaque block display removed the complete hidden armor mask and border. Native/HMI inspection, inventory, third-person, color persistence, zero opacity, unenchanted filtering, context switches and module settings checks also passed. Captures were inspected. Armor masks match vanilla's own depth layering; the obsolete enlarged hulls and face-contour code are removed.

External shader packs and long-duration GPU profiling remain outside this check.
