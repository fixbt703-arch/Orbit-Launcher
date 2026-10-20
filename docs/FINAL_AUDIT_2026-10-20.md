# Orbit Launcher 1.5.5 final audit

Scope: launcher, catalogs, downloads, instances, accounts/skins, updates and Orbit Client. Orbit Connect is excluded at the user's request; its regression entry was skipped and its sources/assets were preserved.

## Fixed

- NeoForge discovery previously stopped at an unrelated damaged or exclusively locked profile before reaching a valid exact match. Each unusable candidate is now skipped without modifying it. Matching still requires the correct profile id, Minecraft inheritance when present, and exact NeoForge artifact version. Eight malformed/locked cases reproduced the failure before the fix and pass afterward.
- A temporarily locked data-pack ZIP was silently abandoned by the world-creation bridge. Healthy ZIPs now continue copying while transient failures trigger the existing bounded retry loop. Invalid ZIPs remain excluded; downloaded originals and per-launch temporary-directory cleanup are preserved. A real watcher/lock regression failed before the fix and passes afterward.
- WPF test fixtures now use a plain Application instead of invoking the production App startup from the test assembly. This prevents an unrelated main-window resource error and keeps fixture execution isolated.

## Verification

- Release build: zero warnings/errors; 126 non-Connect regression tests passed. Includes instance persistence, archive validation, dependency resolution/repair, download verification/cancellation, account/skin behavior and update handling.
- Actual WPF fixtures passed: asynchronous create/edit dialogs, three catalog pages from both providers, layout at 1060/1320/1600, skins/account UI, English/Russian states, keyboard focus, high contrast, and update cancellation/retry. Rendered library and account screens were inspected.
- Live read-only probes passed: Mojang manifest, Fabric loaders, NeoForge metadata in both transport modes; two CurseForge pages with decoded icons; pinned Ely.by authentication component download and public skin/head. An initial upstream connection timeout passed on a fresh retry.
- Orbit Client offline Gradle checks passed, including crosshair serialization, contour rendering and 216 HUD layout combinations.
- A fresh isolated Minecraft 26.2 / Fabric 0.19.5 world with the bundled Orbit Client passed real Stack Refill server transactions, frame checks for an empty hand, critical-indicator assertions and a clean game exit. Existing worlds/configuration were not used.

Run the main suite with:

```powershell
dotnet run --project Tests/OrbitLauncher.Tests.csproj -c Release -- --exclude-connect
```

Focused regressions: `--final-neoforge` and `--final-datapack`.

## Delivery and limits

`build.bat` publishes to the existing `dist/OrbitLauncher.exe`; the launcher version is 1.5.5 and Orbit Client remains 0.18.7. Public source export includes the new regressions and this report, excluding private configuration and user data.

The published EXE contains the exact application DLL, .NET core library, Skia native library and bundled client/skin resources. SHA-256: `10D1CEC330BEBA6961E1F68BFCC4A3C5368B3F788C779F7D12226A94B89D8D47`. All 25 snapshotted Connect service/UI/asset files are unchanged. The exported public launcher source also builds with zero warnings/errors.

Actual Microsoft/Ely.by sign-in and every Minecraft/loader combination were not exercised. The published EXE is validated by its build, version and bundle structure; UI execution uses isolated WPF fixtures rather than normal startup against the user's accounts and instances. External services and drivers can still fail independently. No claim of zero bugs or guaranteed availability is made.
