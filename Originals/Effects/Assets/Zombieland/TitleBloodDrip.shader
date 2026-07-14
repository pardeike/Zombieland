Shader "Custom/ZombielandTitleBloodDrip"
{
	Properties
	{
		_MainTex ("Pendant State", 2D) = "black" {}
		_MaskTex ("Drip Source Mask", 2D) = "black" {}
		_BloodColor ("Thick Blood Color", Color) = (0.115, 0.0015, 0.003, 1)
		_EdgeColor ("Thin Blood Color", Color) = (0.20, 0.004, 0.007, 1)
		_HighlightColor ("Wet Highlight", Color) = (0.95, 0.42, 0.32, 1)
		_Opacity ("Opacity", Range(0, 1)) = 0.99
		_SourceVisibility ("Mask Film Visibility", Range(0, 1)) = 0.96
		_SiteCount ("Pendant Sites", Range(1, 16)) = 14
		_FeedRate ("Mass Feed Rate", Range(0.005, 0.2)) = 0.055
		_CooldownRecovery ("Cooldown Recovery", Range(0.01, 0.5)) = 0.11
		_InitialSeedSpread ("Initial Seed Spread Seconds", Range(0, 9)) = 6
		_Gravity ("Drop Gravity", Range(0, 3)) = 1.17
		_ContactTension ("Mask Contact Tension", Range(0, 1000)) = 480
		_AirDrag ("Air Drag", Range(0, 3)) = 0.18
		_TerminalSpeed ("Terminal Speed", Range(0.1, 3)) = 1.575
		_ReleaseSpeed ("Release Speed", Range(0, 0.5)) = 0.0525
		_SourceRadius ("Source Radius", Range(0.005, 0.1)) = 0.036
		_BulbRadius ("Bulb Radius", Range(0.01, 0.1)) = 0.052
		_MaximumHangLength ("Maximum Hang Length", Range(0.02, 0.35)) = 0.16
		_MinimumNeckRadius ("Minimum Neck Radius", Range(0.0002, 0.02)) = 0.0015
		_ResidualMass ("Residual Base Mass", Range(0.01, 0.35)) = 0.14
		_SurfaceNoise ("Surface Noise", Range(0, 1)) = 0.10
		_ThicknessScale ("Optical Thickness", Range(0.001, 0.08)) = 0.022
		_NormalStrength ("Normal Strength", Range(0.1, 4)) = 1.45
		_WetSpecular ("Wet Specular Strength", Range(0, 2)) = 0.72
	}

	SubShader
	{
		Tags { "Queue" = "Overlay" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
		ZTest Always
		ZWrite Off
		Cull Off

		CGINCLUDE
		#include "UnityCG.cginc"

			#define MAX_PENDANT_SITES 16

		sampler2D _MainTex;
		float4 _MainTex_ST;
		float4 _MainTex_TexelSize;
		sampler2D _MaskTex;
		float4 _MaskTex_TexelSize;
		float4 _BloodColor;
		float4 _EdgeColor;
		float4 _HighlightColor;
		float _Opacity;
		float _SourceVisibility;
		float _SiteCount;
		float _FeedRate;
		float _CooldownRecovery;
		float _InitialSeedSpread;
		float _Gravity;
		float _ContactTension;
		float _AirDrag;
		float _TerminalSpeed;
		float _ReleaseSpeed;
		float _SourceRadius;
		float _BulbRadius;
		float _MaximumHangLength;
		float _MinimumNeckRadius;
		float _ResidualMass;
			float _SurfaceNoise;
			float _ThicknessScale;
			float _NormalStrength;
			float _WetSpecular;
		float _DeltaTime;
		float _SimulationTime;

		struct appdata
		{
			float4 vertex : POSITION;
			float2 uv : TEXCOORD0;
		};

		struct v2f
		{
			float4 vertex : SV_POSITION;
			float2 uv : TEXCOORD0;
		};

		struct AttachedGeometry
		{
			float2 bulbCenter;
			float2 bulbRadii;
			float sourceRadius;
			float neckRadius;
			float hangLength;
			float alpha;
		};

		v2f vert(appdata input)
		{
			v2f output;
			output.vertex = UnityObjectToClipPos(input.vertex);
			output.uv = TRANSFORM_TEX(input.uv, _MainTex);
			return output;
		}

		float Hash(float value)
		{
			return frac(sin(value * 91.3458 + 17.173) * 47453.5453);
		}

		float2 SafeNormalize(float2 value)
		{
			return value * rsqrt(max(dot(value, value), 0.000001));
		}

		float Aspect()
		{
			return max(abs(_MaskTex_TexelSize.z / max(_MaskTex_TexelSize.w, 1.0)), 0.0001);
		}

		float2 ToMetric(float2 uv)
		{
			return float2(uv.x * Aspect(), uv.y);
		}

		int PendantSiteCount()
		{
			return (int)clamp(floor(_SiteCount + 0.5), 1.0, (float)MAX_PENDANT_SITES);
		}

			float SiteAnchorUvX(int siteIndex, int siteCount, float layoutVariant)
			{
				// Every site owns a disjoint horizontal lane. Variants can move an
				// anchor organically inside its lane without ever colliding with a
				// concurrently active site.
				float lanePosition = lerp(
					0.38,
					0.62,
					Hash((float)siteIndex * 19.17 + layoutVariant * 43.71 + 8.4));
				return ((float)siteIndex + lanePosition) / (float)siteCount;
			}

			float StateUvX(int siteIndex)
			{
				return ((float)siteIndex + 0.5) / max(_MainTex_TexelSize.z, 1.0);
			}

		float SourceCoverageAt(float2 uv)
		{
			return smoothstep(0.04, 0.45, tex2Dlod(_MaskTex, float4(saturate(uv), 0.0, 0.0)).a);
		}

		float4 ReadState(float2 uv)
		{
			return tex2D(_MainTex, saturate(uv));
		}

		float FindSourceEdge(float sourceUvX)
		{
			float edgeY = 0.48;
			float found = 0.0;
			[loop]
			for (int sampleIndex = 0; sampleIndex < 64; ++sampleIndex)
			{
				float sampleY = ((float)sampleIndex + 0.5) / 64.0;
				float coverage = SourceCoverageAt(float2(sourceUvX, sampleY));
				float take = (1.0 - found) * step(0.20, coverage);
				edgeY = lerp(edgeY, sampleY, take);
				found = max(found, take);
			}
				return edgeY * found;
			}

			float SiteSizeScale(float seed, float layoutVariant)
			{
				return lerp(0.25, 0.75, Hash(seed * 31.19 + layoutVariant * 17.71 + 4.1));
			}

			float SiteSourceRadius(float seed, float layoutVariant)
			{
				float sizeScale = SiteSizeScale(seed, layoutVariant);
				return _SourceRadius * lerp(0.55, 1.0, sizeScale) * lerp(0.90, 1.10, Hash(seed * 7.31 + 2.4));
			}

			float SiteBulbRadius(float seed, float layoutVariant)
			{
				return _BulbRadius * SiteSizeScale(seed, layoutVariant) * lerp(0.92, 1.08, Hash(seed * 5.87 + 8.1));
			}

				AttachedGeometry EvaluateAttachedGeometry(float2 anchor, float mass, float seed, float layoutVariant)
			{
				AttachedGeometry geometry;
				float phase = saturate(mass);
					float beadGrowth = smoothstep(0.02, 0.78, phase);
					float baseGrowth = smoothstep(0.02, 0.64, phase);
					float elongation = smoothstep(0.05, 0.79, phase);
					float pinch = smoothstep(0.79, 0.84, phase);
					float fullSourceRadius = SiteSourceRadius(seed, layoutVariant);
					float sourceRadius = fullSourceRadius * lerp(0.42, 1.0, baseGrowth);
					float sizeScale = SiteSizeScale(seed, layoutVariant);
					float targetRadius = SiteBulbRadius(seed, layoutVariant);
					float bulbRadius = targetRadius * lerp(0.17, 1.0, beadGrowth);
					float lengthScale = lerp(0.86, 1.14, Hash(seed * 11.19 + 3.7));
					float hangLength = lerp(
						bulbRadius * 0.35,
						_MaximumHangLength * lengthScale * sizeScale,
						elongation);
				hangLength += targetRadius * 0.82 * pinch;
				float sway = sin(_SimulationTime * 0.34 + seed * 6.2831853) * 0.006 * elongation * elongation;
				float bulbRadiusX = bulbRadius * lerp(1.00, 0.82, pinch);
				float bulbRadiusY = bulbRadius * lerp(0.84, 1.34, pinch);
				float initialNeckRadius = min(sourceRadius * 0.82, bulbRadius * 0.82);
				float neckRadius = lerp(initialNeckRadius, _MinimumNeckRadius, pow(pinch, 1.70));

			geometry.bulbCenter = anchor + float2(sway, -hangLength);
			geometry.bulbRadii = float2(bulbRadiusX, bulbRadiusY);
			geometry.sourceRadius = sourceRadius;
			geometry.neckRadius = neckRadius;
			geometry.hangLength = hangLength;
				geometry.alpha = smoothstep(0.02, 0.07, phase);
			return geometry;
		}

		float CircleSdf(float2 samplePoint, float2 center, float radius, out float2 outwardGradient)
		{
			float2 delta = samplePoint - center;
			outwardGradient = SafeNormalize(delta);
			return length(delta) - max(radius, 0.00001);
		}

		float EllipseSdf(float2 samplePoint, float2 center, float2 longAxis, float2 radii, out float2 outwardGradient)
		{
			radii = max(radii, float2(0.00001, 0.00001));
			longAxis = SafeNormalize(longAxis);
			float2 shortAxis = float2(-longAxis.y, longAxis.x);
			float2 delta = samplePoint - center;
			float2 local = float2(dot(delta, shortAxis), dot(delta, longAxis));
			float2 normalizedLocal = local / radii;
			float normalizedLength = max(length(normalizedLocal), 0.00001);
			float2 localGradient = SafeNormalize(local / (radii * radii));
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
				float2 bulbRadii)
			{
				float sourceSquared = sourceRadius * sourceRadius;
				float neckSquared = neckRadius * neckRadius;
				float bulbSquared = bulbRadii.x * bulbRadii.x;
				if (axialDistance < 0.0)
					return sourceSquared - axialDistance * axialDistance;

				if (axialDistance < neckPosition)
				{
					float upperBlend = SmootherStep01(axialDistance / max(neckPosition, 0.00001));
					return lerp(sourceSquared, neckSquared, upperBlend);
				}

				if (axialDistance < bulbPosition)
				{
					float lowerBlend = SmootherStep01(
						(axialDistance - neckPosition) / max(bulbPosition - neckPosition, 0.00001));
					return lerp(neckSquared, bulbSquared, lowerBlend);
				}

				float lowerOffset = (axialDistance - bulbPosition) / max(bulbRadii.y, 0.00001);
				return bulbSquared * (1.0 - lowerOffset * lowerOffset);
			}

			float AxialPendantSdf(
				float2 samplePoint,
				float2 anchor,
				float2 bulbCenter,
				float sourceRadius,
				float neckRadius,
				float2 bulbRadii,
				out float2 outwardGradient)
			{
				float2 axisVector = bulbCenter - anchor;
				float bulbPosition = max(length(axisVector), 0.0001);
				float2 dropAxis = axisVector / bulbPosition;
				float2 sideAxis = float2(-dropAxis.y, dropAxis.x);
				float2 localPoint = samplePoint - anchor;
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
				float2 localGradient = float2(2.0 * sideDistance, -radiusDerivative);
				outwardGradient = SafeNormalize(sideAxis * localGradient.x + dropAxis * localGradient.y);
				return field / max(length(localGradient), 0.0001);
			}

		void ConsiderShape(
			float distanceToShape,
			float2 outwardGradient,
			float shapeAlpha,
			float seed,
			inout float bestDistance,
			inout float2 bestGradient,
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
			float2 samplePoint,
			int siteIndex,
			int siteCount,
			inout float bestDistance,
			inout float2 bestGradient,
			inout float bestAlpha,
			inout float bestSeed)
		{
				float stateUvX = StateUvX(siteIndex);
				float4 state = ReadState(float2(stateUvX, 0.5));
				float layoutVariant = floor(max(state.a, 0.0));
				float edgeY = frac(max(state.a, 0.0));
				if (edgeY < 0.05)
					return;

					float seed = Hash((float)siteIndex + 2.17);
					float anchorUvX = SiteAnchorUvX(siteIndex, siteCount, layoutVariant);
					float2 anchor = ToMetric(float2(anchorUvX, edgeY));
					float falling = step(0.00001, max(state.g, state.b));
					float attachedMass = max(state.r, 0.0);
					AttachedGeometry attached = EvaluateAttachedGeometry(anchor, attachedMass, seed, layoutVariant);
				float attachedVisibility = lerp(1.0, smoothstep(0.052, 0.075, state.g), falling);
				if (attached.alpha * attachedVisibility > 0.0001)
				{
					float renderedNeck = max(attached.neckRadius, abs(_MaskTex_TexelSize.y) * 0.72);
					float2 attachedGradient;
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
				float2 dropCenter = float2(anchor.x, dropCenterY);
				float stretch = saturate(state.b / max(_TerminalSpeed, 0.001)) * 0.25;
				float2 dropRadii = float2(dropRadius * (1.0 - stretch * 0.18), dropRadius * (1.0 + stretch * 0.42));
					float2 dropGradient;
					float dropDistance = EllipseSdf(samplePoint, dropCenter, float2(0.0, -1.0), dropRadii, dropGradient);

					float bridgeProgress = smoothstep(0.004, 0.065, state.g);
					float bridgeAlpha = 1.0 - smoothstep(0.052, 0.065, state.g);
					if (bridgeAlpha > 0.0001)
					{
						float bridgeNeckStart = max(_MinimumNeckRadius, abs(_MaskTex_TexelSize.y) * 0.72);
						float bridgeNeck = lerp(bridgeNeckStart, _MinimumNeckRadius * 0.35, pow(bridgeProgress, 0.82));
						float2 bridgeGradient;
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
			float2 uv,
			out float bestDistance,
			out float2 bestGradient,
			out float bestAlpha,
			out float bestSeed)
		{
			float2 samplePoint = ToMetric(uv);
			bestDistance = 1000000.0;
			bestGradient = float2(0.0, 1.0);
			bestAlpha = 0.0;
			bestSeed = 0.0;
			int count = PendantSiteCount();
			[loop]
			for (int siteIndex = 0; siteIndex < MAX_PENDANT_SITES; ++siteIndex)
			{
				if (siteIndex >= count)
					break;
				EvaluateSite(samplePoint, siteIndex, count, bestDistance, bestGradient, bestAlpha, bestSeed);
			}
		}

			float SourceWetness(float2 uv)
			{
			float pointX = uv.x * Aspect();
			float wetness = 0.06;
			int count = PendantSiteCount();
			[loop]
			for (int siteIndex = 0; siteIndex < MAX_PENDANT_SITES; ++siteIndex)
			{
				if (siteIndex >= count)
					break;
					float stateUvX = StateUvX(siteIndex);
					float4 state = ReadState(float2(stateUvX, 0.5));
					float mass = max(state.r, 0.0);
					float layoutVariant = floor(max(state.a, 0.0));
					float anchorX = SiteAnchorUvX(siteIndex, count, layoutVariant) * Aspect();
				float seed = Hash((float)siteIndex + 2.17);
				float influenceWidth = lerp(0.30, 0.40, Hash(seed * 9.1 + 4.0));
				float normalizedDistance = abs(pointX - anchorX) / influenceWidth;
				float influence = exp(-pow(normalizedDistance, 4.0));
				wetness = max(wetness, smoothstep(0.02, 0.46, mass) * influence);
			}
				return saturate(wetness);
			}

			void AccumulateWetLight(
				float3 lightDirection,
				float3 normal,
				inout float diffuse,
				inout float specular,
				inout float occlusion)
			{
				float3 viewDirection = float3(0.0, 0.0, 1.0);
				diffuse += saturate(dot(lightDirection, normal));
				specular += pow(saturate(dot(reflect(-lightDirection, normal), viewDirection)), 8.0);
				occlusion += pow(saturate(dot(reflect(lightDirection, normal), viewDirection)), 2.0);
			}

			ENDCG

		Pass
		{
			Name "UpdatePendantState"
			Blend Off

			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment frag_update

			float4 frag_update(v2f input) : SV_Target
			{
				int count = PendantSiteCount();
				int stateWidth = max((int)floor(_MainTex_TexelSize.z + 0.5), 1);
				int siteIndex = min((int)floor(saturate(input.uv.x) * (float)stateWidth), stateWidth - 1);
				if (siteIndex >= count)
					return float4(0.0, 0.0, 0.0, 0.0);
				float4 previous = ReadState(float2(StateUvX(siteIndex), 0.5));
					float uninitialized = 1.0 - step(
						0.00001,
						dot(abs(previous), float4(1.0, 1.0, 1.0, 1.0)));
					float mass = previous.r;
					float fallDistance = max(previous.g, 0.0);
					float fallVelocity = max(previous.b, 0.0);
					float layoutVariant = floor(max(previous.a, 0.0));
					float edgeY = frac(max(previous.a, 0.0));
					float anchorUvX = SiteAnchorUvX(siteIndex, count, layoutVariant);
				if (edgeY < 0.05)
				{
					edgeY = FindSourceEdge(anchorUvX);
					if (uninitialized > 0.5)
					{
						// A rotated golden-ratio sequence gives every fresh title a
						// different spatial order while keeping initial activations
						// evenly spread instead of allowing random clusters.
						float startupRotation = Hash(floor(_Time.y * 8.0) + 4.1);
						float startupOrder = frac(
							((float)siteIndex + 0.5) * 0.61803398875 + startupRotation);
						mass = -min(
							1.0,
							_CooldownRecovery * _InitialSeedSpread * startupOrder);
					}
				}

				float seed = Hash((float)siteIndex + 2.17);
				float2 anchor = ToMetric(float2(anchorUvX, edgeY));
				if (fallDistance > 0.00001 || fallVelocity > 0.00001)
				{
					fallVelocity += _Gravity * _DeltaTime;
					fallVelocity *= exp(-_AirDrag * _DeltaTime);
					fallVelocity = min(fallVelocity, _TerminalSpeed);
						fallDistance += fallVelocity * _DeltaTime * 1.5;
					mass = lerp(mass, 0.008, 1.0 - exp(-4.2 * _DeltaTime));

						AttachedGeometry released = EvaluateAttachedGeometry(anchor, 1.0, seed, layoutVariant);
						float dropCenterY = edgeY - released.hangLength - fallDistance;
							if (dropCenterY + SiteBulbRadius(seed, layoutVariant) < -0.96)
						{
							mass = -1.0;
							fallDistance = 0.0;
							fallVelocity = 0.0;
							float layoutAdvance = 1.0 + floor(Hash(seed * 41.7 + _SimulationTime * 0.37) * 7.0);
							layoutVariant = fmod(layoutVariant + layoutAdvance, 8.0);
							edgeY = 0.0;
						}
				}
				else if (mass < 0.0)
				{
					mass = min(0.0, mass + _CooldownRecovery * _DeltaTime);
				}
				else
				{
						float feedVariation = lerp(
							1.35,
							2.0,
							Hash(seed * 13.7 + layoutVariant * 23.9 + 5.0));
						mass = min(1.20, mass + _FeedRate * feedVariation * _DeltaTime);
						AttachedGeometry attached = EvaluateAttachedGeometry(anchor, mass, seed, layoutVariant);
					float weight = mass * _Gravity;
					float contactSupport = _ContactTension * attached.neckRadius * lerp(0.92, 1.08, Hash(seed * 3.1 + 9.0));
					float visualFailureRadius = max(
						_MinimumNeckRadius * 3.5,
						abs(_MaskTex_TexelSize.y) * 2.8);
					float neckFailed = step(attached.neckRadius, visualFailureRadius);
						if (mass > 0.60 && weight >= contactSupport || mass > 0.70 && neckFailed > 0.5)
					{
						mass = _ResidualMass;
						fallDistance = 0.002;
						fallVelocity = _ReleaseSpeed;
					}
				}

					return float4(
						clamp(mass, -1.0, 1.20),
						fallDistance,
						fallVelocity,
						layoutVariant + edgeY);
			}
			ENDCG
		}

		Pass
		{
			Name "DisplayBlood"
			Blend One OneMinusSrcAlpha

			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment frag_display

			fixed4 frag_display(v2f input) : SV_Target
			{
					float2 uv = float2(input.uv.x, input.uv.y * 2.0 - 1.0);
				float bestDistance;
				float2 shapeGradient;
				float shapeWeight;
				float shapeSeed;
				EvaluateDropField(uv, bestDistance, shapeGradient, shapeWeight, shapeSeed);

				if (shapeWeight > 0.0001)
				{
					float2 metricPoint = ToMetric(uv);
					float organicNoise =
						sin(metricPoint.x * 97.0 + metricPoint.y * 61.0 + _SimulationTime * 0.31 + shapeSeed * 17.0) +
						sin(metricPoint.x * 53.0 - metricPoint.y * 113.0 - _SimulationTime * 0.19 + shapeSeed * 29.0);
					bestDistance += organicNoise * (_SurfaceNoise * 0.00018);
				}

				float antialiasWidth = max(fwidth(bestDistance) * 1.25, 0.00012);
				float shapeCoverage = 0.0;
				if (shapeWeight > 0.0001)
					shapeCoverage = (1.0 - smoothstep(-antialiasWidth, antialiasWidth, bestDistance)) * shapeWeight;

				float sourceMask = SourceCoverageAt(uv);
					float source = sourceMask * lerp(0.10, 1.0, SourceWetness(uv)) * _SourceVisibility;
					float alpha = 1.0 - (1.0 - shapeCoverage) * (1.0 - source);
					float lowerFade = smoothstep(-0.96, -0.48, uv.y);
					alpha *= _Opacity * lowerFade;
				clip(alpha - 0.002);

				float2 maskTexel = abs(_MaskTex_TexelSize.xy);
				float maskLeft = SourceCoverageAt(uv - float2(maskTexel.x, 0.0));
					float maskRight = SourceCoverageAt(uv + float2(maskTexel.x, 0.0));
					float maskDown = SourceCoverageAt(uv - float2(0.0, maskTexel.y));
					float maskUp = SourceCoverageAt(uv + float2(0.0, maskTexel.y));
					float2 sourceGradient = -SafeNormalize(float2(maskRight - maskLeft, maskUp - maskDown));
					float useShape = step(source, shapeCoverage);
					float2 outwardGradient = SafeNormalize(lerp(sourceGradient, shapeGradient, useShape));
					float3 normal = normalize(float3(
						-outwardGradient.x * _NormalStrength,
						-outwardGradient.y * _NormalStrength,
						1.0));

				float opticalDepth = source;
				if (shapeWeight > 0.0001)
					opticalDepth = max(opticalDepth, saturate(-bestDistance / max(_ThicknessScale, 0.00001)));

					float diffuse = 0.0;
					float specular = 0.0;
					float occlusion = 0.0;
					AccumulateWetLight(normalize(float3(0.811, -0.580, 0.058)), normal, diffuse, specular, occlusion);
					AccumulateWetLight(normalize(float3(-0.811, -0.580, 0.058)), normal, diffuse, specular, occlusion);
					AccumulateWetLight(normalize(float3(0.192, 0.962, 0.192)), normal, diffuse, specular, occlusion);
					float illumination = saturate(0.50 + diffuse * 0.10 - occlusion * 0.026);
					specular = saturate(specular * 0.34) * _WetSpecular;
					specular *= lerp(0.38, 1.0, smoothstep(0.02, 0.42, opticalDepth));
					float3 blood = lerp(_EdgeColor.rgb, _BloodColor.rgb, smoothstep(0.04, 0.72, opticalDepth));
					float highlightPeak = smoothstep(0.12, 0.62, specular);
					float3 wetHighlight = lerp(_HighlightColor.rgb, float3(1.0, 0.76, 0.66), highlightPeak);
					float3 color = blood * illumination + wetHighlight * specular;
				return fixed4(color * alpha, alpha);
			}
			ENDCG
		}
	}

	Fallback Off
}
