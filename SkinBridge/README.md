# Orbit Skin Bridge

Texture-only Java agent for Microsoft-authenticated Minecraft sessions. It also
works with Vanilla; no mod loader or Ely.by login is needed to see other players' skins.

The launcher enables it by default for licensed sessions. **Skins → Ely.by skins
for other players** disables it for subsequent launches. An Ely.by account session
continues to use the separate official authlib-injector integration instead.
If a modded instance already contains CustomSkinLoader, its existing configuration
is used instead of installing a second texture interceptor. That mod and its settings
are preserved. Disabled mod files and inactive Vanilla mods do not disable the bridge.

## Scope

- Hooks only Authlib's texture methods and records GameProfile UUID/name pairs.
- Never changes authentication URLs, login/refresh/joinServer, user API, chat keys,
  the player's access token, UUID or nickname.
- The Microsoft viewer UUID and nickname are excluded from Ely.by lookup, including
  when an offline-mode server assigns another UUID. Existing server/Mojang
  texture properties take precedence. Missing other-player textures fall back to Ely.by.
- `getPackedTextures` returns a stable deferred property immediately. Minecraft's
  existing background texture worker resolves it; the render thread does not wait for HTTP.
- The official signed profile endpoint is queried over HTTPS. The Ely.by RSA signature,
  returned nickname and texture hosts are checked before accepting a texture. HTTP texture
  URLs in signed profiles are upgraded to HTTPS. No credentials are sent to Ely.by.
  Valid Ely.by-signed texture properties supplied by a server are also accepted;
  tampered signatures retain vanilla behavior. Mojang signature checking is unchanged.
- IO has two daemon workers, a bounded queue, response/time limits and positive/negative
  caches. Missing skins, malformed responses and network errors fall back to default skins.
- Own code targets Java 8 bytecode. Modern adapters are verified with Minecraft 26.2's
  Authlib 9.0.75 and 26.3's Authlib 10.0.77; other layouts fail softly when methods differ.

## Build

Run `SkinBridge/build.ps1` with a JDK 25+. It compiles the source and creates the
launcher resource `Assets/SkinBridge/orbit-skin-bridge.jar`. Normal launcher builds
use the committed JAR and do not need a JDK.

The build downloads ASM 9.11 and ASM Commons 9.11 from Maven Central, checks their
pinned SHA-256 hashes and relocates ASM into the bridge's private package. Its
classes cannot replace the game's or Fabric/Forge's ASM. ASM's BSD license and
Orbit's MIT license are included in the JAR. Dependency caches and build output
are excluded from public source exports.

## Verification

Bridge 1.0.1 fixes Fabric's `NoClassDefFoundError` during `GameProfile` construction.
Authlib hooks use cached JDK MethodHandles resolved explicitly from the bootstrap
loader. Game classes do not directly link to the agent package; Fabric's classpath
isolation remains enabled. No loader whitelist or game classpath is changed.

`tests/IsolatedLoaderProbe.java` loads actual Authlib in a game loader that rejects
agent-package references. Version 1.0.0 fails this check; 1.0.1 passes construction,
deferred/signed texture resolution and viewer exclusion.

`tests/ModernProbe.java` tests against actual Authlib with the public Ely.by sample
profile: signed skin acceptance, viewer exclusion, server-property priority,
identity preservation, stable cache key and render-thread timing. It uses no login token.
`tests/SkinManagerProbe.java` exercises Minecraft 26.2's actual async SkinManager
with a download recorder, validating that the texture reaches its rendering pipeline.
Run either probe with the corresponding Minecraft libraries on the classpath,
`-javaagent:orbit-skin-bridge.jar`,
`-Dorbit.skinbridge.viewer=11111111222243338444555555555555` and
`-Dorbit.skinbridge.viewerName=OrbitViewer`. These are fictional viewer identifiers.

`dotnet run --project Tests/OrbitLauncher.Tests.csproj -c Release -- --skin-launch-smoke <new-test-directory> <agent-jar> <mods-directory>`
performs a full cached Minecraft 26.2 / Fabric 0.19.5 launch with copied mod JARs,
a fictional session and Orbit Client's UI smoke checks. It requires those versions
and Orbit Client to be installed. The test directory must not already exist; real
accounts, configuration and worlds are never copied.

References: [Ely.by skin API](https://docs.ely.by/en/skins-system.html),
[ASM](https://asm.ow2.io/).
