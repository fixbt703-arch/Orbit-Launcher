# Ely.by in Orbit Launcher

## Licensed accounts: seeing other players' skins

Starting with **1.4.4**, Microsoft-authenticated launches enable Orbit Skin Bridge
by default. It supplements missing other-player textures with signed Ely.by skins;
your own Minecraft skin and server-provided texture properties take precedence.
No Ely.by login or mod loader is required, including in Vanilla. The **Skins** tab
has an **Ely.by skins for other players** switch; changes apply on the next game launch.

This component is embedded in the launcher, extracted to a separate cache and verified
against the embedded bytes before execution. It is independent of authlib-injector:
Microsoft authentication, server joins, UUIDs and access tokens are unchanged.
See [SkinBridge/README.md](SkinBridge/README.md) for source, licenses and verification.

## Accounts without a license

In Settings, enable **Unlicensed account**. In **Skins**, choose **Sign in to Ely.by**.
The nickname and UUID returned by Ely.by are used for the game session. Disconnecting
Ely.by restores the previously configured local nickname. Microsoft sessions are separate.

## Uploading and choosing skins

The Skins tab embeds the official Ely.by website. **Upload PNG** validates a 64×64 PNG
(maximum 192 KiB) and passes only that selected image to the site's original file input.
The official site performs the upload; finish by selecting **Wear** there. The website
may ask you to sign in separately: use the same Ely.by account shown above the page.
Return with **Back to my skin** to fetch the current head from Ely.by. The launcher
does not report a successful upload or skin assignment before the website completes it.
Changes apply when starting Minecraft again; Ely.by may briefly cache textures.

The website uses a separate WebView2 profile for each Ely.by UUID under `%LOCALAPPDATA%\OrbitLauncher\ely-web`.
Microsoft Edge WebView2 Runtime is required. If unavailable, the tab explains how to
recover and offers Retry. Website navigation is restricted to official Ely.by HTTPS
hosts; browser downloads, host objects, notifications and other device permissions are disabled.
No website cookies are exposed to the launcher authentication service.

## Authentication and game integration

- Official Yggdrasil endpoints: `https://authserver.ely.by/auth/`.
- Two-factor authentication is supported through Ely.by's documented password/code format.
- Passwords are never saved. The game token and client token are encrypted with Windows
  DPAPI for the current Windows user in `ely-session.dat`. Logout removes the stored
  session, invalidates that token and clears the isolated browser data.
- Each Ely.by game launch refreshes the session. An expired session requires another login;
  the launcher never silently switches that launch to a different identity.
- Official skins endpoint: `https://skinsystem.ely.by/skins/{nickname}.png?version=2`.
  Its head cache is isolated from Microsoft skins.
- Ely.by launches use [authlib-injector 1.2.8](https://github.com/yushijinhun/authlib-injector/releases/tag/v1.2.8)
  with API root `https://account.ely.by/api/authlib-injector/`. The JAR is downloaded from
  the author's GitHub release on first use, verified before execution and cached separately.
  SHA-256: `9c7f4343e6c82034958ffb48c14a2cb0c85928be7283103ce17da00c6d5a7b10`.
  It is not inserted into other accounts' launches or used to modify shared Minecraft libraries.
  Upstream source and GPL-3.0 license: [authlib-injector](https://github.com/yushijinhun/authlib-injector/tree/v1.2.8).

Public source export uses an allowlist: it excludes DPAPI files, browser profiles,
account files and user skins. The previous settings JSON names are retained for migration.

References: [authentication](https://docs.ely.by/en/minecraft-auth.html),
[skin system](https://docs.ely.by/en/skins-system.html),
[authlib-injector integration](https://docs.ely.by/en/authlib-injector.html).

## Verification for 1.4.3

- 88 launcher regression checks passed, including nine Ely.by authentication,
  persistence, expiry, cancellation, URL validation and agent-integrity scenarios.
- Actual WPF screens rendered at 1320×820 and 1060×680, in Russian and English.
  Connected/disconnected providers, account head, keyboard focus and 2FA recovery passed.
- The official Ely.by upload page loaded in the embedded WebView2; its original PNG
  file input was found. Switching game accounts created a separate browser profile.
- The upstream JAR matched its official SHA-256. A Java 25 probe using Minecraft 26.2's
  actual authlib 9.0.75 resolved Ely.by's public sample profile and accepted its signed skin.
- A real user's password login, PNG upload and Wear action were not performed during
  these checks. They require that user's Ely.by account; no test skin was published.

## Verification for 1.4.4 (licensed skin bridge)

### 1.4.5 startup fix

Bridge 1.0.0 could crash Fabric during game initialization: the game loader refused
its direct reference to `org.orbitlauncher.skinbridge.Bridge`. Version 1.0.1 uses
cached JDK MethodHandles resolved through the bootstrap loader. Fabric's isolation
is preserved and no user mod or configuration needs to be removed.

The regression probe reproduces the old failure in an isolated loader and passes
with the new bridge, including signed textures and own-skin exclusion. A full
Minecraft 26.2 / Fabric 0.19.5 launch with the installed mod JARs also completed
Orbit Client's UI smoke checks and exited normally in a separate test directory.
It used a fictional session and did not access the user's account or worlds.

### Earlier library and UI checks

- 91 launcher regressions passed, including extraction/hash repair, isolated launch
  arguments, migration of the default setting and existing CustomSkinLoader detection.
- Real Authlib 9.0.75 (Minecraft 26.2) and 10.0.77 (26.3) resolved Ely.by's signed
  public sample skin, preserved supplied texture properties and excluded the viewer UUID
  and nickname, including server-assigned UUIDs.
  Microsoft service/discovery URLs stayed unchanged.
- Minecraft 26.2's actual SkinManager loaded the profile asynchronously, downloaded
  a valid 64-pixel PNG and produced a secure PlayerSkin. The probe substituted only GPU
  registration with a recorder; it did not start a game window or join a server.
- The new licensed-account switch was rendered at 1060×680 and checked for focus,
  initial enabled state and settings synchronization. Earlier Ely account UI checks passed.
- No live Microsoft account token or server join was used for these checks.
