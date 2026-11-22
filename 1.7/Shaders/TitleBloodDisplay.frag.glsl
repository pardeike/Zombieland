#version 450

layout(location = 0) in vec4 vColor;
layout(location = 1) in vec2 vUv;
layout(location = 0) out vec4 outColor;

#include "TitleBloodCommon.glsl"

// The native GUI uses SrcAlpha blending, so return straight RGB here.
vec4 frag_display(vec2 uv)
			{
					uv = vec2(uv.x, uv.y * 2.0 - 1.0);
				float bestDistance;
				vec2 shapeGradient;
				float shapeWeight;
				float shapeSeed;
				EvaluateDropField(uv, bestDistance, shapeGradient, shapeWeight, shapeSeed);

				if (shapeWeight > 0.0001)
				{
					vec2 metricPoint = ToMetric(uv);
					float organicNoise =
						sin(metricPoint.x * 97.0 + metricPoint.y * 61.0 + uMaterial._SimulationTime * 0.31 + shapeSeed * 17.0) +
						sin(metricPoint.x * 53.0 - metricPoint.y * 113.0 - uMaterial._SimulationTime * 0.19 + shapeSeed * 29.0);
					bestDistance += organicNoise * (uMaterial._SurfaceNoise * 0.00018);
				}

				float antialiasWidth = max(fwidth(bestDistance) * 1.25, 0.00012);
				float shapeCoverage = 0.0;
				if (shapeWeight > 0.0001)
					shapeCoverage = (1.0 - smoothstep(-antialiasWidth, antialiasWidth, bestDistance)) * shapeWeight;

				float sourceMask = SourceCoverageAt(uv);
					float source = sourceMask * mix(0.10, 1.0, SourceWetness(uv)) * uMaterial._SourceVisibility;
					float alpha = 1.0 - (1.0 - shapeCoverage) * (1.0 - source);
					float lowerFade = smoothstep(-0.96, -0.48, uv.y);
					alpha *= uMaterial._Opacity * lowerFade;
				if (alpha < 0.002) discard;

				vec2 maskTexel = MaskTexelSize();
				float maskLeft = SourceCoverageAt(uv - vec2(maskTexel.x, 0.0));
					float maskRight = SourceCoverageAt(uv + vec2(maskTexel.x, 0.0));
					float maskDown = SourceCoverageAt(uv - vec2(0.0, maskTexel.y));
					float maskUp = SourceCoverageAt(uv + vec2(0.0, maskTexel.y));
					vec2 sourceGradient = -SafeNormalize(vec2(maskRight - maskLeft, maskUp - maskDown));
					float useShape = step(source, shapeCoverage);
					vec2 outwardGradient = SafeNormalize(mix(sourceGradient, shapeGradient, useShape));
					vec3 normal = normalize(vec3(
						-outwardGradient.x * uMaterial._NormalStrength,
						-outwardGradient.y * uMaterial._NormalStrength,
						1.0));

				float opticalDepth = source;
				if (shapeWeight > 0.0001)
					opticalDepth = max(opticalDepth, saturate(-bestDistance / max(uMaterial._ThicknessScale, 0.00001)));

					float diffuse = 0.0;
					float specular = 0.0;
					float occlusion = 0.0;
					AccumulateWetLight(normalize(vec3(0.811, -0.580, 0.058)), normal, diffuse, specular, occlusion);
					AccumulateWetLight(normalize(vec3(-0.811, -0.580, 0.058)), normal, diffuse, specular, occlusion);
					AccumulateWetLight(normalize(vec3(0.192, 0.962, 0.192)), normal, diffuse, specular, occlusion);
					float illumination = saturate(0.50 + diffuse * 0.10 - occlusion * 0.026);
					specular = saturate(specular * 0.34) * uMaterial._WetSpecular;
					specular *= mix(0.38, 1.0, smoothstep(0.02, 0.42, opticalDepth));
					vec3 blood = mix(uMaterial._EdgeColor.rgb, uMaterial._BloodColor.rgb, smoothstep(0.04, 0.72, opticalDepth));
					float highlightPeak = smoothstep(0.12, 0.62, specular);
					vec3 wetHighlight = mix(uMaterial._HighlightColor.rgb, vec3(1.0, 0.76, 0.66), highlightPeak);
					vec3 color = blood * illumination + wetHighlight * specular;
				return vec4(color, alpha);
			}

void main()
{
    outColor = frag_display(vUv * uMaterial._MainTex_ST.zw + uMaterial._MainTex_ST.xy);
}
