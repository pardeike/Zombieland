#version 450

layout(location = 0) in vec4 vColor;
layout(location = 1) in vec2 vUv;
layout(location = 0) out vec4 outColor;

#include "TitleBloodCommon.glsl"

vec4 frag_update(vec2 uv)
			{
				int count = PendantSiteCount();
				int stateWidth = textureSize(_MainTex, 0).x;
				int siteIndex = min(int(floor(saturate(uv.x) * float(stateWidth))), stateWidth - 1);
				if (siteIndex >= count)
					return vec4(0.0, 0.0, 0.0, 0.0);
				vec4 previous = ReadState(vec2(StateUvX(siteIndex), 0.5));
					float uninitialized = 1.0 - step(
						0.00001,
						dot(abs(previous), vec4(1.0, 1.0, 1.0, 1.0)));
					float mass = previous.r;
					float fallDistance = max(previous.g, 0.0);
					float fallVelocity = max(previous.b, 0.0);
					float layoutVariant = floor(max(previous.a, 0.0));
					float edgeY = fract(max(previous.a, 0.0));
					float anchorUvX = SiteAnchorUvX(siteIndex, count, layoutVariant);
				if (edgeY < 0.05)
				{
					edgeY = FindSourceEdge(anchorUvX);
					if (uninitialized > 0.5)
					{
						// A rotated golden-ratio sequence gives every fresh title a
						// different spatial order while keeping initial activations
						// evenly spread instead of allowing random clusters.
						float startupRotation = Hash(floor(uMaterial._TitleTime * 8.0) + 4.1);
						float startupOrder = fract(
							(float(siteIndex) + 0.5) * 0.61803398875 + startupRotation);
						mass = -min(
							1.0,
							uMaterial._CooldownRecovery * uMaterial._InitialSeedSpread * startupOrder);
					}
				}

				float seed = Hash(float(siteIndex) + 2.17);
				vec2 anchor = ToMetric(vec2(anchorUvX, edgeY));
				if (fallDistance > 0.00001 || fallVelocity > 0.00001)
				{
					fallVelocity += uMaterial._Gravity * uMaterial._DeltaTime;
					fallVelocity *= exp(-uMaterial._AirDrag * uMaterial._DeltaTime);
					fallVelocity = min(fallVelocity, uMaterial._TerminalSpeed);
						fallDistance += fallVelocity * uMaterial._DeltaTime * 1.5;
					mass = mix(mass, 0.008, 1.0 - exp(-4.2 * uMaterial._DeltaTime));

						AttachedGeometry released = EvaluateAttachedGeometry(anchor, 1.0, seed, layoutVariant);
						float dropCenterY = edgeY - released.hangLength - fallDistance;
							if (dropCenterY + SiteBulbRadius(seed, layoutVariant) < -0.96)
						{
							mass = -1.0;
							fallDistance = 0.0;
							fallVelocity = 0.0;
							float layoutAdvance = 1.0 + floor(Hash(seed * 41.7 + uMaterial._SimulationTime * 0.37) * 7.0);
							layoutVariant = mod(layoutVariant + layoutAdvance, 8.0);
							edgeY = 0.0;
						}
				}
				else if (mass < 0.0)
				{
					mass = min(0.0, mass + uMaterial._CooldownRecovery * uMaterial._DeltaTime);
				}
				else
				{
						float feedVariation = mix(
							1.35,
							2.0,
							Hash(seed * 13.7 + layoutVariant * 23.9 + 5.0));
						mass = min(1.20, mass + uMaterial._FeedRate * feedVariation * uMaterial._DeltaTime);
						AttachedGeometry attached = EvaluateAttachedGeometry(anchor, mass, seed, layoutVariant);
					float weight = mass * uMaterial._Gravity;
					float contactSupport = uMaterial._ContactTension * attached.neckRadius * mix(0.92, 1.08, Hash(seed * 3.1 + 9.0));
					float visualFailureRadius = max(
						uMaterial._MinimumNeckRadius * 3.5,
						MaskTexelSize().y * 2.8);
					float neckFailed = step(attached.neckRadius, visualFailureRadius);
						if (mass > 0.60 && weight >= contactSupport || mass > 0.70 && neckFailed > 0.5)
					{
						mass = uMaterial._ResidualMass;
						fallDistance = 0.002;
						fallVelocity = uMaterial._ReleaseSpeed;
					}
				}

					return vec4(
						clamp(mass, -1.0, 1.20),
						fallDistance,
						fallVelocity,
						layoutVariant + edgeY);
			}

void main()
{
    outColor = frag_update(vUv * uMaterial._MainTex_ST.zw + uMaterial._MainTex_ST.xy);
}
