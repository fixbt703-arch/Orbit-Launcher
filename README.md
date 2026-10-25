# Orbit Launcher

WPF launcher for Windows with Fabric, Forge, NeoForge and Quilt instances, mod catalogs, mod compatibility repair, Microsoft accounts and skins.

Orbit Connect is an optional, independent world-sharing integration using EasyTier
and Terracotta with verified upstream downloads, route recovery, and a simple LAN
invite flow. Enable it under Orbit Client in instance settings. See
[usage, measured tests and exact compatibility](docs/ORBIT_CONNECT.md).

## Build

Requires .NET SDK 8 for Windows. Run `build.bat` to replace `dist/OrbitLauncher.exe` with a self-contained Windows x64 executable.

## Source and release packaging

Personal publication scripts are excluded from open source. Public snapshots use
an explicit file allowlist and reject local credentials, account data, game runtime
files and EXE files. Microsoft application IDs are public configuration, not
account credentials. Git history containing private configuration must be cleaned
before the repository is made public; deleting a file in a later commit alone
does not remove older versions.

To create an installer and a separate source package, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-GitHub.ps1
```

The output is the sibling `OrbitLauncher-GitHub` folder: `source/` contains public
code; `release/OrbitLauncherInstaler.exe` belongs in GitHub Releases. This operation
does not publish to GitHub. It requires .NET SDK 8, Inno Setup 7, local service
configuration and API connectivity. Its build uses `Utilities/AppConfiguration.Local.cs`
only for the installer; the file and temporary project reference are excluded
from source. The actual embedded application and a live CurseForge request are
verified before packaging. Embedded desktop keys can be extracted from binaries.
A failed build preserves the previous marked export. Do not store personal files
in that generated folder, and do not run `cleaning.bat` to publish.

To build the open-source launcher yourself, copy
`Utilities/AppConfiguration.Local.cs.example` to `Utilities/AppConfiguration.Local.cs`
and supply your own application credentials. This private file is git-ignored.

For changes to the Minecraft mod, run `client-build.bat` first, then `build.bat`. Orbit Client targets Minecraft 26.2, Fabric Loader 0.19.3+ and Java 25+. Its source is in `OrbitClient/`, and its bundled JAR is in `Assets/OrbitClient/`.

Run launcher checks with `dotnet run --project Tests/OrbitLauncher.Tests.csproj -c Release`.

The next public launcher release is 1.6.0, bundling Orbit Client 0.24.1 and Skin Bridge 1.0.2. Settings shows the latest published GitHub release, with a persistent offline cache. The [25 October audit](docs/FINAL_AUDIT_2026-10-25.md) fixes offline Ely skins, dependency closure/repair, portable icons and client settings migration. The [Trajectories module](docs/ORBIT_CLIENT_0.24.0.md) adds visual projectile flight and impact previews with theme-aware styling, per-item filters and bounded vanilla physics. The [complete theme update](docs/ORBIT_CLIENT_0.23.0.md) adds eight palettes for panel surfaces and HUD, native blur/dim strength controls and denser Interface settings. The [focus border correction](docs/ORBIT_CLIENT_0.22.1.md) makes rectangular controls use rectangular focus borders. The [shared HUD update](docs/ORBIT_CLIENT_0.22.0.md) unifies HUD appearance under Interface, applies themes and corners to gameplay widgets, and fixes search focus, tab rebuilds, editor focus and automatic HUD positioning. The [Interface module](docs/ORBIT_CLIENT_0.21.0.md) adds live menu themes, gradient accents, motion controls, tooltips, local binding messages, background modes and adjustable corners. The [GUI-scale correction](docs/ORBIT_CLIENT_0.20.1.md) keeps the module menu compact independently of Minecraft HUD scale and preserves correct input, clipping, tooltips and resizing. The [client workspace redesign](docs/ORBIT_CLIENT_0.20.0.md) introduces draggable Visuals, Movement, Combat and HUD panels, inline settings, English module names and a permanent HUD Editor action. The [Enchantment Glow perimeter correction](docs/ORBIT_CLIENT_0.19.2.md) replaces enlarged model hulls with an exterior pixel border from the visible silhouette. Model interiors remain unchanged; scrolling glint, moving highlights and pulse are preserved. The [client visual update](docs/ORBIT_CLIENT_0.19.0.md) adds functional module descriptions, consistent settings typography, a short opening transition and a redesigned Enchantment Glow. The [preceding audit](docs/FINAL_AUDIT_2026-10-20.md) fixed NeoForge profile discovery and transient data-pack copy failures. Orbit Client text has no shadows, including HUD item counts, tooltips and native text fields hosted by the client UI. Stack Refill uses a normal predicted vanilla SWAP immediately after a successful last-block placement, without waiting for a round trip or delaying two extra ticks. Server inventory updates remain authoritative and are never filtered.

Crosshair settings include a **Critical** tab using Minecraft's own sword textures and live HUD attack-indicator extraction. The native clock, 100% charge, target-alive and attack-range checks are preserved; enabling Orbit's custom crosshair replaces only the crosshair artwork rather than cancelling the entire native indicator. The compact default is 75% of vanilla size (12 logical pixels wide, half the previous 24px bar). Size options are 50/75/100%, with a separate vertical offset and optional target-only visibility. The original white sword denotes a charged attack; an optional green tint marks the vanilla falling-critical opportunity. Unsupported held items remain hidden. No attacks are automated and combat rules are unchanged. The indicator also works when Minecraft's own attack-indicator option is off, because the Orbit checkbox controls it locally without rewriting that option.

See [RELEASE_AUDIT.md](RELEASE_AUDIT.md) for the preceding audit's fixes,
verification and remaining limits. Async dialog regression checks run with
`dotnet run --project Tests/OrbitLauncher.Tests.csproj -c Release -- --dialog-async-smoke`.
Checks use temporary fixtures and do not require a Microsoft account. One component
integration check is explicitly skipped when the author's protected Skin Layers 3D
JAR is absent, as in the public source package; see `OrbitClient/libs/README.md`.

## Ely.by skins

**Unlicensed account** in Settings supports Ely.by sign-in, nicknames and skins.
Upload and choose skins from the existing **Skins** tab. The token is protected
with Windows DPAPI; passwords are not saved. See [ELY_SKINS.md](ELY_SKINS.md)
for the website's Wear step, two-factor sign-in and game integration.

Microsoft-authenticated players also see missing other-player skins from Ely.by
by default, including in Vanilla. The Skins tab provides a switch. Your own skin,
Microsoft sign-in and server authentication stay unchanged; the texture-only
component and source are described in [SkinBridge/README.md](SkinBridge/README.md).

## Automatic launcher updates

The launcher checks public GitHub Releases at startup and every six hours while
open. A newer stable release offers **Update now** or **Later**; Settings also
contains a manual check. Downloads are cancellable, verified against SHA-256 and
the installer's Windows version, then passed to the installer in the current
launcher folder. Instances and accounts remain in their separate data directory.
See [UPDATING.md](UPDATING.md) for release publishing requirements and verification.

Catalog regression check: append three pages in both sources, scroll through the actual WPF cards and resize the window with `dotnet run --project Tests/OrbitLauncher.Tests.csproj -c Release -- --catalog-pagination`. The optional `--probe-curseforge` checks two pages and their icons against the live service using local build configuration.

## Network compatibility and Stack Refill

Launcher and Minecraft components share a bounded HTTP/1.1 transport. Failed
connections and file downloads retry with TLS 1.2; stalled bodies time out.
Minecraft downloads use four workers and replace files only after size/hash
verification. No Zapret services, driver settings, proxy settings or domain lists
are changed. An incompatible Zapret strategy can still block traffic outside the
launcher's control. See [NETWORK_REFILL_FIXES.md](NETWORK_REFILL_FIXES.md).

## Orbit Client 0.18.5

Hold My Items replaces Flair in the PvP filter. Enable it in Right Shift → PvP and hold **R** to inspect items using the original animation pack. Settings include main/off hand rendering, hand visibility, scale, XYZ position, motion intensity, 3D blocks, particles, camera motion and vanilla animations outside inspection. With that option enabled, idle, attacks, item use and their recovery use the native renderer; only holding R starts Hold My Items. Switching renderers clears Lua/model/camera state to prevent animation mixing. Q and F retain their normal actions. Existing Flair enabled state and the inspection key are migrated.

Original mod: [Hold My Items by sapling](https://modrinth.com/mod/hold-my-items), original download 5.1.1 for 1.21.11 (Modrinth version KZm1qTcU). The downloaded JAR explicitly licenses the code/resources as CC0-1.0. The bundled component is compiled from the [unofficial 26.2 port by ThinkofRain1213](https://github.com/ThinkofRain1213/HMI-26.2), commit dfdd72e7349f0b87f7ebd4ffae3f18fff8611c82, with original Lua animations/models and Orbit configuration integration. Source and notices are in `OrbitClient/third-party/holdmyitems`; its JAR and LuaJ runtime are nested inside Orbit Client. It is an unofficial integration, not an author release. `OrbitClient/gradlew.bat :runClient -PorbitHmiSmoke` checks live rendering, toggles, input gating, persistence and settings layouts in an isolated test world.

The client menu, HUD settings, crosshair lists and component list use clipped, smooth vertical scrolling. Mouse wheel, scrollbar dragging and Page Up/Down are supported. Colors follow the launcher palette; borders are drawn within the same bounds as their surfaces.

Stack Refill replaces only the exact block and item components after its last placement, through the normal server-synchronized inventory swap. Enchantment Glow (previously Glint Colorizer) retains item/armor recoloring and adds an independently implemented enchanted-item contour. Outline settings include a separate RGB color or matching the glint, opacity, width, a soft halo, rainbow cycling and gentle pulsing. First-person, third-person and inventory visibility are independent. The contour follows opaque texture pixels on baked item models, respects depth, and follows Hold My Items poses. Resource reloads invalidate cached contours. Module settings contain controls only; descriptions remain on the module catalog cards, and empty settings tabs are hidden. Hitbox provides normal/target colors, opacity, line width, solid/corner/dashed outlines, fill, eye level, look direction, entity filters and distance.

Hitbox settings are grouped into General, Appearance, Target and Visibility, with smooth scrolling. Appearance includes independent top/bottom/vertical edges, corner length, dash divisions, custom fill color, look-vector length and arrow, a health-based color gradient, allied color and damage flash. The target can override line width, outline style and fill. Visibility includes minimum/maximum distance, distance fade, camera perspective, hiding menus/allies, and conditions for holding a melee weapon or sneaking. Allies use Minecraft's scoreboard teams. Target highlighting takes precedence over damage, allied and health colors; damage takes precedence over allied and health colors. These settings change only visualization, never collision bounds or attack reach.

Target highlighting and the target-only filter use Minecraft's actual entity hit result and the held item's attack range. Distant entities retain their normal appearance; showing hitboxes through obstacles does not make obstructed entities eligible for target highlighting.

3D Mod controls [Skin Layers 3D 1.11.3 by tr7zw](https://github.com/tr7zw/3d-Skin-Layers). The launcher downloads the unmodified component from the author's Modrinth CDN and verifies its SHA-512; it is not redistributed inside Orbit's JAR or executable. It uses the [tr7zw Protective License](https://github.com/tr7zw/3d-Skin-Layers/blob/main/LICENSE). A local build dependency and its license are in `OrbitClient/libs/`; do not redistribute that dependency as part of Orbit source packages. Orbit controls part toggles, thickness and distance at runtime.

Development checks: `OrbitClient/gradlew.bat build`, or `OrbitClient/gradlew.bat :runClient -PorbitWorldSmoke` for actual world rendering, texture recoloring, skin meshes, inventory synchronization and UI checks. The world check creates an isolated test world under `OrbitClient/run/`.

The dedicated `OrbitClient/gradlew.bat :runClient -PorbitGlowSmoke` regression checks native idle/attack/recovery, R inspection, item use, full HMI rendering, outline visibility/alpha/foil filtering, inventory and third-person passes, RGB persistence and settings layouts in both languages. Run development client commands from `OrbitClient/` (use `gradlew.bat` there).

## Recovery

The deleted project was reconstructed from the saved launcher build and recorded source changes. Some C# files and XAML were recovered from compiled assemblies, so their formatting, comments and file organization can differ from the original. Orbit Client's Java code was rebuilt and checked against the saved 0.8.0 JAR.

Minecraft instances, accounts and user settings remain in `%LOCALAPPDATA%/OrbitLauncher`; they are separate from the project directory.
