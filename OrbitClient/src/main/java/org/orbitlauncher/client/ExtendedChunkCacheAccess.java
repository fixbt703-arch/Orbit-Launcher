package org.orbitlauncher.client;

/** Implemented by the client chunk cache mixin so settings can be applied without reconnecting. */
public interface ExtendedChunkCacheAccess {
    void orbit$refreshRadius();
}
