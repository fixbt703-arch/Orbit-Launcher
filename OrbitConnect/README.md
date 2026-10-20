# Orbit Connect game adapter

Java 8 agent launched only for an enabled Orbit Launcher instance. Native networking
lives in owned external EasyTier/Terracotta processes. The agent does not include a
VPN, Netty, Fabric API or a replacement authentication service.

`src` contains fail-soft ASM hooks for the real Netty NIO LAN bind, known
ServerAddress parsers and LAN classification of adapter-owned invitations. All
network preparation and TCP bridges run on bounded daemon work; parser hooks return
a loopback address immediately. Private bearer IPC provides game PID/LAN port and
status. ASM is shaded to avoid conflicts with loaders and mods.

`build.ps1` uses the cached/pinned ASM 9.11 artifacts and JDK 25 to compile Java 8
bytecode. `test.ps1` runs actual Netty fixtures; use `-NettyVersion 4.0.23.Final
-RunJava <java8.exe>` for the cached legacy fixture. No native networking is loaded
into the game process.

`live-test` is an isolated development-only Fabric 26.2 mod, built separately by
`build-live-test.ps1`. It is never packaged with the released agent/launcher.

See [delivery and compatibility](../docs/ORBIT_CONNECT.md), including unverified
Minecraft/loader combinations and the launcher-panel compatibility path.
