#version 330
#moj_import <minecraft:fog.glsl>
uniform sampler2D Sampler0;
in float sphericalVertexDistance;
in float cylindricalVertexDistance;
in vec4 vertexColor;
in vec2 texCoord0;
out vec4 fragColor;
void main() {
    // Coverage occupies the upper alpha half; fog visibility is separate from silhouette membership.
    if (texture(Sampler0, texCoord0).a < 0.1) discard;
    float visibility = 1.0 - total_fog_value(sphericalVertexDistance, cylindricalVertexDistance,
        FogEnvironmentalStart, FogEnvironmentalEnd, FogRenderDistanceStart, FogRenderDistanceEnd) * FogColor.a;
    fragColor = vec4(vertexColor.rgb, 0.5 + 0.5 * visibility);
}
