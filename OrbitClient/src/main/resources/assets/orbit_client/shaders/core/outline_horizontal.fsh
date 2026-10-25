#version 330
uniform sampler2D MaskSampler;
layout(std140) uniform OutlineParams { vec4 Params; };
in vec2 texCoord;
out vec4 fragColor;
void main() {
    ivec2 size = textureSize(MaskSampler, 0);
    ivec2 p = ivec2(gl_FragCoord.xy);
    int radius = int(ceil(Params.x + Params.y + 0.5));
    int best = radius + 1;
    int offset = 0;
    vec3 color = vec3(0.0);
    for (int dx = -radius; dx <= radius; ++dx) {
        ivec2 q = p + ivec2(dx, 0);
        if (q.x < 0 || q.x >= size.x) continue;
        vec4 sampleColor = texelFetch(MaskSampler, q, 0);
        if (sampleColor.a > 0.49 && abs(dx) < best) {
            best = abs(dx);
            offset = dx;
            color = sampleColor.rgb;
        }
    }
    // Half-float preserves these integer distances exactly; zero denotes no covered neighbor.
    fragColor = best <= radius ? vec4(color, float(offset + radius + 1) / Params.w) : vec4(0.0);
}
