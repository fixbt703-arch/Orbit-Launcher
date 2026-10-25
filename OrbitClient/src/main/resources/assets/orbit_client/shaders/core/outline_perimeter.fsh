#version 330
uniform sampler2D MaskSampler;
uniform sampler2D HorizontalSampler;
layout(std140) uniform OutlineParams { vec4 Params; };
in vec2 texCoord;
out vec4 fragColor;
void main() {
    ivec2 p = ivec2(gl_FragCoord.xy);
    // Never color the model itself, including its internal edges and joins between parts.
    if (texelFetch(MaskSampler, p, 0).a > 0.49) discard;
    ivec2 size = textureSize(MaskSampler, 0);
    int radius = int(ceil(Params.x + Params.y + 0.5));
    float best = float((radius + 1) * (radius + 1));
    vec3 color = vec3(0.0);
    ivec2 source = p;
    for (int dy = -radius; dy <= radius; ++dy) {
        ivec2 q = p + ivec2(0, dy);
        if (q.y < 0 || q.y >= size.y) continue;
        vec4 neighbor = texelFetch(HorizontalSampler, q, 0);
        if (neighbor.a == 0.0) continue;
        float dx = round(neighbor.a * Params.w) - float(radius + 1);
        float distanceSquared = dx * dx + float(dy * dy);
        if (distanceSquared < best) { best = distanceSquared; color = neighbor.rgb; source = q + ivec2(int(dx), 0); }
    }
    float alpha = 1.0 - smoothstep(Params.x - 0.5, Params.x + Params.y + 0.5, sqrt(best));
    if (alpha <= 0.0) discard;
    float visibility = clamp((texelFetch(MaskSampler, source, 0).a - 0.5) * 2.0, 0.0, 1.0);
    fragColor = vec4(color, alpha * Params.z * visibility);
}
