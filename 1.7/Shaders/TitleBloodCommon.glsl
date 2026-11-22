// Port of Originals/Effects/Assets/Zombieland/TitleBloodDrip.shader.
layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _MainTex_ST; // @default = vec4(0.0, 0.0, 1.0, 1.0)
    vec4 _BloodColor; // @default = vec4(0.115, 0.0015, 0.003, 1)
    vec4 _EdgeColor; // @default = vec4(0.20, 0.004, 0.007, 1)
    vec4 _HighlightColor; // @default = vec4(0.95, 0.42, 0.32, 1)
    float _Opacity; // @default = 0.99
    float _SourceVisibility; // @default = 0.96
    float _SiteCount; // @default = 14.0
    float _FeedRate; // @default = 0.055
    float _CooldownRecovery; // @default = 0.11
    float _InitialSeedSpread; // @default = 6.0
    float _Gravity; // @default = 1.17
    float _ContactTension; // @default = 480.0
    float _AirDrag; // @default = 0.18
    float _TerminalSpeed; // @default = 1.575
    float _ReleaseSpeed; // @default = 0.0525
    float _SourceRadius; // @default = 0.036
    float _BulbRadius; // @default = 0.052
    float _MaximumHangLength; // @default = 0.16
    float _MinimumNeckRadius; // @default = 0.0015
    float _ResidualMass; // @default = 0.14
    float _SurfaceNoise; // @default = 0.10
    float _ThicknessScale; // @default = 0.022
    float _NormalStrength; // @default = 1.45
    float _WetSpecular; // @default = 0.72
    float _DeltaTime; // @default = 0.0
    float _SimulationTime; // @default = 0.0
    float _TitleTime; // @default = 0.0
} uMaterial;

layout(set = 2, binding = 1) uniform sampler2D _MainTex;
layout(set = 2, binding = 2) uniform sampler2D _MaskTex;


#define saturate(value) clamp(value, 0.0, 1.0)
#define MAX_PENDANT_SITES 16

struct AttachedGeometry
{
	vec2 bulbCenter;
	vec2 bulbRadii;
	float sourceRadius;
	float neckRadius;
	float hangLength;
	float alpha;
};

float Hash(float value)
{
	return fract(sin(value * 91.3458 + 17.173) * 47453.5453);
}

vec2 SafeNormalize(vec2 value)
{
	return value * inversesqrt(max(dot(value, value), 0.000001));
}

float Aspect()
{
	vec2 maskSize = vec2(textureSize(_MaskTex, 0));
	return maskSize.x / maskSize.y;
}

vec2 MaskTexelSize()
{
	// Native GLSL does not populate Unity's *_TexelSize material vectors.
	return 1.0 / vec2(textureSize(_MaskTex, 0));
}

vec2 ToMetric(vec2 uv)
{
	return vec2(uv.x * Aspect(), uv.y);
}

int PendantSiteCount()
{
	return int(clamp(floor(uMaterial._SiteCount + 0.5), 1.0, float(MAX_PENDANT_SITES)));
}

	float SiteAnchorUvX(int siteIndex, int siteCount, float layoutVariant)
	{
		// Every site owns a disjoint horizontal lane. Variants can move an
		// anchor organically inside its lane without ever colliding with a
		// concurrently active site.
		float lanePosition = mix(
			0.38,
			0.62,
			Hash(float(siteIndex) * 19.17 + layoutVariant * 43.71 + 8.4));
		return (float(siteIndex) + lanePosition) / float(siteCount);
	}

	float StateUvX(int siteIndex)
	{
		return (float(siteIndex) + 0.5) / float(textureSize(_MainTex, 0).x);
	}

float SourceCoverageAt(vec2 uv)
{
	return smoothstep(0.04, 0.45, textureLod(_MaskTex, saturate(uv), 0.0).a);
}

vec4 ReadState(vec2 uv)
{
	// GUI samplers can filter despite FilterMode.Point. State stores packed values.
	ivec2 size = textureSize(_MainTex, 0);
	ivec2 texel = clamp(ivec2(saturate(uv) * vec2(size)), ivec2(0), size - 1);
	return texelFetch(_MainTex, texel, 0);
}

float FindSourceEdge(float sourceUvX)
{
	float edgeY = 0.48;
	float found = 0.0;
	for (int sampleIndex = 0; sampleIndex < 64; ++sampleIndex)
	{
		float sampleY = (float(sampleIndex) + 0.5) / 64.0;
		float coverage = SourceCoverageAt(vec2(sourceUvX, sampleY));
		float take = (1.0 - found) * step(0.20, coverage);
		edgeY = mix(edgeY, sampleY, take);
		found = max(found, take);
	}
		return edgeY * found;
	}

	float SiteSizeScale(float seed, float layoutVariant)
	{
		return mix(0.25, 0.75, Hash(seed * 31.19 + layoutVariant * 17.71 + 4.1));
	}

	float SiteSourceRadius(float seed, float layoutVariant)
	{
		float sizeScale = SiteSizeScale(seed, layoutVariant);
		return uMaterial._SourceRadius * mix(0.55, 1.0, sizeScale) * mix(0.90, 1.10, Hash(seed * 7.31 + 2.4));
	}

	float SiteBulbRadius(float seed, float layoutVariant)
	{
		return uMaterial._BulbRadius * SiteSizeScale(seed, layoutVariant) * mix(0.92, 1.08, Hash(seed * 5.87 + 8.1));
	}

		AttachedGeometry EvaluateAttachedGeometry(vec2 anchor, float mass, float seed, float layoutVariant)
	{
		AttachedGeometry geometry;
		float phase = saturate(mass);
			float beadGrowth = smoothstep(0.02, 0.78, phase);
			float baseGrowth = smoothstep(0.02, 0.64, phase);
			float elongation = smoothstep(0.05, 0.79, phase);
			float pinch = smoothstep(0.79, 0.84, phase);
			float fullSourceRadius = SiteSourceRadius(seed, layoutVariant);
			float sourceRadius = fullSourceRadius * mix(0.42, 1.0, baseGrowth);
			float sizeScale = SiteSizeScale(seed, layoutVariant);
			float targetRadius = SiteBulbRadius(seed, layoutVariant);
			float bulbRadius = targetRadius * mix(0.17, 1.0, beadGrowth);
			float lengthScale = mix(0.86, 1.14, Hash(seed * 11.19 + 3.7));
			float hangLength = mix(
				bulbRadius * 0.35,
				uMaterial._MaximumHangLength * lengthScale * sizeScale,
				elongation);
		hangLength += targetRadius * 0.82 * pinch;
		float sway = sin(uMaterial._SimulationTime * 0.34 + seed * 6.2831853) * 0.006 * elongation * elongation;
		float bulbRadiusX = bulbRadius * mix(1.00, 0.82, pinch);
		float bulbRadiusY = bulbRadius * mix(0.84, 1.34, pinch);
		float initialNeckRadius = min(sourceRadius * 0.82, bulbRadius * 0.82);
		float neckRadius = mix(initialNeckRadius, uMaterial._MinimumNeckRadius, pow(pinch, 1.70));

	geometry.bulbCenter = anchor + vec2(sway, -hangLength);
	geometry.bulbRadii = vec2(bulbRadiusX, bulbRadiusY);
	geometry.sourceRadius = sourceRadius;
	geometry.neckRadius = neckRadius;
	geometry.hangLength = hangLength;
		geometry.alpha = smoothstep(0.02, 0.07, phase);
	return geometry;
}

float EllipseSdf(vec2 samplePoint, vec2 center, vec2 longAxis, vec2 radii, out vec2 outwardGradient)
{
	radii = max(radii, vec2(0.00001, 0.00001));
	longAxis = SafeNormalize(longAxis);
	vec2 shortAxis = vec2(-longAxis.y, longAxis.x);
	vec2 delta = samplePoint - center;
	vec2 local = vec2(dot(delta, shortAxis), dot(delta, longAxis));
	vec2 normalizedLocal = local / radii;
	float normalizedLength = max(length(normalizedLocal), 0.00001);
	vec2 localGradient = SafeNormalize(local / (radii * radii));
	outwardGradient = SafeNormalize(shortAxis * localGradient.x + longAxis * localGradient.y);
	return (normalizedLength - 1.0) * min(radii.x, radii.y);
}

	float SmootherStep01(float value)
	{
		value = saturate(value);
		return value * value * value * (value * (value * 6.0 - 15.0) + 10.0);
	}

	float PendantRadiusSquared(
		float axialDistance,
		float neckPosition,
		float bulbPosition,
		float sourceRadius,
		float neckRadius,
		vec2 bulbRadii)
	{
		float sourceSquared = sourceRadius * sourceRadius;
		float neckSquared = neckRadius * neckRadius;
		float bulbSquared = bulbRadii.x * bulbRadii.x;
		if (axialDistance < 0.0)
			return sourceSquared - axialDistance * axialDistance;

		if (axialDistance < neckPosition)
		{
			float upperBlend = SmootherStep01(axialDistance / max(neckPosition, 0.00001));
			return mix(sourceSquared, neckSquared, upperBlend);
		}

		if (axialDistance < bulbPosition)
		{
			float lowerBlend = SmootherStep01(
				(axialDistance - neckPosition) / max(bulbPosition - neckPosition, 0.00001));
			return mix(neckSquared, bulbSquared, lowerBlend);
		}

		float lowerOffset = (axialDistance - bulbPosition) / max(bulbRadii.y, 0.00001);
		return bulbSquared * (1.0 - lowerOffset * lowerOffset);
	}

	float AxialPendantSdf(
		vec2 samplePoint,
		vec2 anchor,
		vec2 bulbCenter,
		float sourceRadius,
		float neckRadius,
		vec2 bulbRadii,
		out vec2 outwardGradient)
	{
		vec2 axisVector = bulbCenter - anchor;
		float bulbPosition = max(length(axisVector), 0.0001);
		vec2 dropAxis = axisVector / bulbPosition;
		vec2 sideAxis = vec2(-dropAxis.y, dropAxis.x);
		vec2 localPoint = samplePoint - anchor;
		float sideDistance = dot(localPoint, sideAxis);
		float axialDistance = dot(localPoint, dropAxis);
		float neckPosition = max(bulbPosition * 0.22, 0.00005);
		float radiusSquared = PendantRadiusSquared(
			axialDistance,
			neckPosition,
			bulbPosition,
			sourceRadius,
			neckRadius,
			bulbRadii);
		float field = sideDistance * sideDistance - radiusSquared;

		float derivativeStep = 0.00035;
		float radiusAbove = PendantRadiusSquared(
			axialDistance - derivativeStep,
			neckPosition,
			bulbPosition,
			sourceRadius,
			neckRadius,
			bulbRadii);
		float radiusBelow = PendantRadiusSquared(
			axialDistance + derivativeStep,
			neckPosition,
			bulbPosition,
			sourceRadius,
			neckRadius,
			bulbRadii);
		float radiusDerivative = (radiusBelow - radiusAbove) / (2.0 * derivativeStep);
		vec2 localGradient = vec2(2.0 * sideDistance, -radiusDerivative);
		outwardGradient = SafeNormalize(sideAxis * localGradient.x + dropAxis * localGradient.y);
		return field / max(length(localGradient), 0.0001);
	}

void ConsiderShape(
	float distanceToShape,
	vec2 outwardGradient,
	float shapeAlpha,
	float seed,
	inout float bestDistance,
	inout vec2 bestGradient,
	inout float bestAlpha,
	inout float bestSeed)
{
	if (shapeAlpha > 0.0001 && distanceToShape < bestDistance)
	{
		bestDistance = distanceToShape;
		bestGradient = outwardGradient;
		bestAlpha = shapeAlpha;
		bestSeed = seed;
	}
}

void EvaluateSite(
	vec2 samplePoint,
	int siteIndex,
	int siteCount,
	inout float bestDistance,
	inout vec2 bestGradient,
	inout float bestAlpha,
	inout float bestSeed)
{
		float stateUvX = StateUvX(siteIndex);
		vec4 state = ReadState(vec2(stateUvX, 0.5));
		float layoutVariant = floor(max(state.a, 0.0));
		float edgeY = fract(max(state.a, 0.0));
		if (edgeY < 0.05)
			return;

			float seed = Hash(float(siteIndex) + 2.17);
			float anchorUvX = SiteAnchorUvX(siteIndex, siteCount, layoutVariant);
			vec2 anchor = ToMetric(vec2(anchorUvX, edgeY));
			float falling = step(0.00001, max(state.g, state.b));
			float attachedMass = max(state.r, 0.0);
			AttachedGeometry attached = EvaluateAttachedGeometry(anchor, attachedMass, seed, layoutVariant);
		float attachedVisibility = mix(1.0, smoothstep(0.052, 0.075, state.g), falling);
		if (attached.alpha * attachedVisibility > 0.0001)
		{
			float renderedNeck = max(attached.neckRadius, MaskTexelSize().y * 0.72);
			vec2 attachedGradient;
			float attachedDistance = AxialPendantSdf(
				samplePoint,
				anchor,
				attached.bulbCenter,
				attached.sourceRadius,
				renderedNeck,
				attached.bulbRadii,
				attachedGradient);
			ConsiderShape(
				attachedDistance,
				attachedGradient,
				attached.alpha * attachedVisibility,
				seed,
				bestDistance,
				bestGradient,
				bestAlpha,
				bestSeed);
		}

		if (falling > 0.5)
		{
				AttachedGeometry released = EvaluateAttachedGeometry(anchor, 1.0, seed, layoutVariant);
				float dropRadius = SiteBulbRadius(seed, layoutVariant) * 1.04;
		float dropCenterY = anchor.y - released.hangLength - state.g;
		vec2 dropCenter = vec2(anchor.x, dropCenterY);
		float stretch = saturate(state.b / max(uMaterial._TerminalSpeed, 0.001)) * 0.25;
		vec2 dropRadii = vec2(dropRadius * (1.0 - stretch * 0.18), dropRadius * (1.0 + stretch * 0.42));
			vec2 dropGradient;
			float dropDistance = EllipseSdf(samplePoint, dropCenter, vec2(0.0, -1.0), dropRadii, dropGradient);

			float bridgeProgress = smoothstep(0.004, 0.065, state.g);
			float bridgeAlpha = 1.0 - smoothstep(0.052, 0.065, state.g);
			if (bridgeAlpha > 0.0001)
			{
				float bridgeNeckStart = max(uMaterial._MinimumNeckRadius, MaskTexelSize().y * 0.72);
				float bridgeNeck = mix(bridgeNeckStart, uMaterial._MinimumNeckRadius * 0.35, pow(bridgeProgress, 0.82));
				vec2 bridgeGradient;
				float bridgeDistance = AxialPendantSdf(
					samplePoint,
					anchor,
					dropCenter,
					released.sourceRadius,
					bridgeNeck,
					dropRadii,
					bridgeGradient);
				if (bridgeAlpha > 0.999)
					ConsiderShape(bridgeDistance, bridgeGradient, 1.0, seed + 0.19, bestDistance, bestGradient, bestAlpha, bestSeed);
				else
				{
					ConsiderShape(dropDistance, dropGradient, 1.0, seed + 0.37, bestDistance, bestGradient, bestAlpha, bestSeed);
					ConsiderShape(bridgeDistance, bridgeGradient, bridgeAlpha, seed + 0.19, bestDistance, bestGradient, bestAlpha, bestSeed);
				}
			}
			else
				ConsiderShape(dropDistance, dropGradient, 1.0, seed + 0.37, bestDistance, bestGradient, bestAlpha, bestSeed);
		}
}

void EvaluateDropField(
	vec2 uv,
	out float bestDistance,
	out vec2 bestGradient,
	out float bestAlpha,
	out float bestSeed)
{
	vec2 samplePoint = ToMetric(uv);
	bestDistance = 1000000.0;
	bestGradient = vec2(0.0, 1.0);
	bestAlpha = 0.0;
	bestSeed = 0.0;
	int count = PendantSiteCount();
	for (int siteIndex = 0; siteIndex < MAX_PENDANT_SITES; ++siteIndex)
	{
		if (siteIndex >= count)
			break;
		EvaluateSite(samplePoint, siteIndex, count, bestDistance, bestGradient, bestAlpha, bestSeed);
	}
}

	float SourceWetness(vec2 uv)
	{
	float pointX = uv.x * Aspect();
	float wetness = 0.06;
	int count = PendantSiteCount();
	for (int siteIndex = 0; siteIndex < MAX_PENDANT_SITES; ++siteIndex)
	{
		if (siteIndex >= count)
			break;
			float stateUvX = StateUvX(siteIndex);
			vec4 state = ReadState(vec2(stateUvX, 0.5));
			float mass = max(state.r, 0.0);
			float layoutVariant = floor(max(state.a, 0.0));
			float anchorX = SiteAnchorUvX(siteIndex, count, layoutVariant) * Aspect();
		float seed = Hash(float(siteIndex) + 2.17);
		float influenceWidth = mix(0.30, 0.40, Hash(seed * 9.1 + 4.0));
		float normalizedDistance = abs(pointX - anchorX) / influenceWidth;
		float influence = exp(-pow(normalizedDistance, 4.0));
		wetness = max(wetness, smoothstep(0.02, 0.46, mass) * influence);
	}
		return saturate(wetness);
	}

	void AccumulateWetLight(
		vec3 lightDirection,
		vec3 normal,
		inout float diffuse,
		inout float specular,
		inout float occlusion)
	{
		vec3 viewDirection = vec3(0.0, 0.0, 1.0);
		diffuse += saturate(dot(lightDirection, normal));
		specular += pow(saturate(dot(reflect(-lightDirection, normal), viewDirection)), 8.0);
		occlusion += pow(saturate(dot(reflect(lightDirection, normal), viewDirection)), 2.0);
	}
