#version 450

layout(location = 0) in vec2 vUv;
layout(location = 1) in vec2 vWorldXZ;
layout(location = 0) out vec4 outColor;
layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color; // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    vec4 _MainTex_ST; // @default = vec4(0.0, 0.0, 1.0, 1.0)
    float _SymbiantOpacityMin; // @default = 0.42
    float _SymbiantOpacityMax; // @default = 0.76
    float _SymbiantNoiseScale; // @default = 2.0
    float _SymbiantFlowSpeed; // @default = 0.45
    float _SymbiantWaveShadeStrength; // @default = 0.68
    float _SymbiantEdgeContrast; // @default = 0.95
    float _SymbiantNoiseTime; // @default = 0.0
} uMaterial;
layout(set = 2, binding = 1) uniform sampler2D _MainTex;

float clamp01(float x) { return clamp(x, 0.0, 1.0); }

float GooWave(vec2 worldXZ)
{
	float scale = max(uMaterial._SymbiantNoiseScale, 0.001);
	float phase = uMaterial._SymbiantNoiseTime * uMaterial._SymbiantFlowSpeed;
	vec2 dirA = normalize(vec2(1.0, 1.0));
	vec2 dirB = normalize(vec2(1.0, -1.0));
	vec2 q = worldXZ;
	vec2 warp = vec2(
		sin(dot(q, vec2(0.31, 0.73)) * scale * 0.42 + 1.10),
		cos(dot(q, vec2(-0.79, 0.26)) * scale * 0.39 - 0.60)
	) * ((0.18 + 0.04 * sin(phase * 0.29 + 0.70)) / scale);
	vec2 p = q + warp;
	float x = p.x * scale;
	float z = p.y * scale;
	float waveX = sin(x * 1.00 + 0.35) * sin(phase * 0.61 + 0.20);
	float waveZ = sin(z * 1.04 + 1.85) * sin(phase * 0.55 + 1.60);
	float waveA = sin(dot(p, dirA) * scale * 1.02 + 2.55) * sin(phase * 0.47 + 2.80);
	float waveB = sin(dot(p, dirB) * scale * 0.98 - 1.20) * sin(phase * 0.43 + 4.10);
	float waveCross = sin(x * 0.92 + 0.80) * sin(z * 0.96 - 1.30) * sin(phase * 0.37 + 2.20);
	float field = waveX * 0.25 + waveZ * 0.25 + waveA * 0.22 + waveB * 0.22 + waveCross * 0.18;
	return smoothstep(-0.50, 0.50, field);
}

vec4 shadeSymbiant()
{
	vec4 color = texture(_MainTex, (vUv * uMaterial._MainTex_ST.zw + uMaterial._MainTex_ST.xy)) * uMaterial._Color;
	if (color.a <= 0.001)
		return color;

	float maskAlpha = color.a;
	float cluster = GooWave(vWorldXZ);
	float shadeBand = clamp01(1.0 - abs(cluster - 0.50) / 0.42);
	shadeBand = shadeBand * shadeBand * (3.0 - 2.0 * shadeBand);
	vec3 waveShade = mix(vec3(1.0, 1.0, 1.0), vec3(0.03, 0.20, 0.04), clamp01(shadeBand * uMaterial._SymbiantWaveShadeStrength));
	float edgeCore = smoothstep(0.012, 0.055, maskAlpha) * (1.0 - smoothstep(0.115, 0.235, maskAlpha));
	float edgeFeather = smoothstep(0.004, 0.025, maskAlpha) * (1.0 - smoothstep(0.210, 0.360, maskAlpha));
	float edgeBand = clamp01(edgeCore * 1.55 + edgeFeather * 0.35);
	float edgeImpact = clamp01(edgeBand * uMaterial._SymbiantEdgeContrast);
	vec3 edgeShade = mix(vec3(1.0, 1.0, 1.0), vec3(0.00, 0.04, 0.00), edgeImpact);
	color.rgb *= waveShade * edgeShade;

	float minOpacity = clamp01(min(uMaterial._SymbiantOpacityMin, uMaterial._SymbiantOpacityMax));
	float maxOpacity = clamp01(max(uMaterial._SymbiantOpacityMin, uMaterial._SymbiantOpacityMax));
	float opacity = mix(minOpacity, maxOpacity, cluster);
	color.a = clamp01(color.a * opacity + edgeImpact * (1.0 - clamp01(maskAlpha * 1.65)) * 0.12);
	return color;
}

void main() { outColor = shadeSymbiant(); }
