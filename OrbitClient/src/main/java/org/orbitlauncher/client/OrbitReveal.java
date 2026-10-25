package org.orbitlauncher.client;

/** A short ease-out without bounce or restarts when widgets are rebuilt. */
final class OrbitReveal {
    private long started;
    float progress() {
        long now=System.nanoTime();
        if(started==0)started=now;
        return ease(OrbitTheme.elapsed(started,220));
    }
    static float ease(double progress) {
        double remaining=1-Math.clamp(progress,0,1);
        return (float)(1-remaining*remaining*remaining);
    }
}
