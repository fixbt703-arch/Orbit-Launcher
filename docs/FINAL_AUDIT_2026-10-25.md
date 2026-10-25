# Final audit — 25 October 2026

Release numbering resumes the public sequence: next build **1.6.0**, with Orbit Client **0.24.1** and Skin Bridge **1.0.2**. No release was uploaded automatically.

## Published version

Settings displays only the latest published GitHub release number, independently of the local assembly version. A validated local cache preserves the last observed release during outages. The first offline launch shows an explicit unavailable state rather than inventing a release number.

When GitHub API requests are rate limited, a bounded HTTPS request to this repository's public release page can recover the display number. This fallback never authorizes an update installer. Existing origin, SHA-256, size and executable-version checks are retained.

## Ely.by skins

Plain offline sessions previously received no skin component. They now receive the texture bridge and can resolve their own public Ely.by skin as well as other players' missing textures. Microsoft sessions keep their own skin protected by default.

Ely-authenticated sessions retain the official authlib-injector and additionally use the texture-only adapter to upgrade verified Ely texture URLs to HTTPS. Existing CustomSkinLoader instances keep their original integration. Login, account tokens, UUIDs, names and secure-chat behavior are unchanged.

Public sample profiles were used for tests; no real account credentials were read or sent. The exact friend's computer and account were not available.

## Other fixes

- Required Modrinth dependency closure is resolved before optional branches. Conflicting optional subtrees roll back without blocking mandatory dependencies.
- Repair follows transitive required dependencies on Forge, NeoForge and Quilt, including version-only references and cycles, with a bounded work queue.
- ZIP import preserves nested custom instance icons while rejecting escaping paths.
- Orbit Client ignores unknown module keys after legacy migration, retaining recognized settings.
- Trajectory color picker opacity now has the same 10% minimum as its settings and persisted configuration.

## Verification

146 deterministic launcher tests excluding Orbit Connect, Java check/build including the new unknown-module regression, public Authlib 9/10 profile probes, isolated-loader probes, the native Minecraft SkinManager pipeline and rendered Ely/settings UI. Full native client runs cover offline own-skin rendering, trajectory settings and the bilingual GUI scale regression.

Orbit Connect was excluded from inspection and execution. Distribution files are replaced at their existing paths; final payload versions/hashes and completed native runs are recorded in the task's delivery file.
