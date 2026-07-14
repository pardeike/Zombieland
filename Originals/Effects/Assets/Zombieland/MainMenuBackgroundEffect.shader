Shader "Custom/ZombielandMainMenuBackgroundEffect"
{
	Properties
	{
		_MultiplyColor ("Multiply Color", Color) = (0.922, 0.682, 0.682, 1)
		_ColorBurnColor ("Color Burn Color", Color) = (0.831, 0.831, 0.831, 1)
		_DarkerColor ("Darker Color", Color) = (0.282, 0.667, 0.282, 1)
		_MultiplyStrength ("Multiply Strength", Range(0, 1)) = 0.95
		_ColorBurnStrength ("Color Burn Strength", Range(0, 1)) = 0.85
		_DarkerColorStrength ("Darker Color Strength", Range(0, 1)) = 0.9
		_GlitchInterval ("Glitch Average Interval", Range(3, 8)) = 4.5
		_GlitchIntervalJitter ("Glitch Interval Jitter", Range(0, 2)) = 1.5
		_GlitchRepeatChance ("Glitch Repeat Chance", Range(0, 1)) = 0.2
		_GlitchAmount ("Glitch Amount", Range(0, 1)) = 0.92
		_GlitchEnabled ("Glitch Enabled", Range(0, 1)) = 1
		_GlitchLayerSpread ("Glitch Layer Spread", Range(0, 0.16)) = 0.055
		_GlitchLayerJitter ("Glitch Layer Jitter", Range(0, 0.12)) = 0.045
		_VignetteColor ("Vignette Color", Color) = (0.06, 0.08, 0.06, 1)
		_VignetteStrength ("Vignette Strength", Range(0, 1)) = 0.46
		_VignetteRadius ("Vignette Radius", Range(0, 1)) = 0.42
		_VignetteSoftness ("Vignette Softness", Range(0.01, 1)) = 0.58
	}

	SubShader
	{
		Tags { "Queue" = "Overlay" "IgnoreProjector" = "True" "RenderType" = "Overlay" }

		CGINCLUDE
		#include "UnityCG.cginc"

		fixed4 _MultiplyColor;
		fixed4 _ColorBurnColor;
		fixed4 _DarkerColor;
		fixed4 _VignetteColor;
		float _MultiplyStrength;
		float _ColorBurnStrength;
		float _DarkerColorStrength;
		float _GlitchInterval;
		float _GlitchIntervalJitter;
		float _GlitchRepeatChance;
		float _GlitchAmount;
		float _GlitchEnabled;
		float _GlitchLayerSpread;
		float _GlitchLayerJitter;
		float _VignetteStrength;
		float _VignetteRadius;
		float _VignetteSoftness;

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

		float Hash(float value)
		{
			return frac(sin(value) * 43758.5453123);
		}

		float GlitchEventTime(float eventIndex)
		{
			return eventIndex * _GlitchInterval + (Hash(eventIndex + 11.7) - 0.5) * _GlitchIntervalJitter;
		}

		float GlitchPulseAt(float start, float eventIndex, float passSeed, float amountScale)
		{
			float durationSeed = Hash(eventIndex + passSeed * 61.7 + 53.9);
			float duration = lerp(0.032, 0.56, pow(durationSeed, 4.25));
			float pulseIn = smoothstep(start, start + 0.018, _Time.y);
			float pulseOut = 1.0 - smoothstep(start + duration, start + duration + 0.028, _Time.y);
			float subtleReduction = lerp(0.28, 0.54, Hash(eventIndex + 73.1));
			float strongReduction = lerp(0.72, 1.0, Hash(eventIndex + 79.3));
			float eventAmount = lerp(subtleReduction, strongReduction, step(0.5, Hash(eventIndex + 89.7)));
			float passAmount = lerp(0.82, 1.0, Hash(eventIndex + passSeed * 23.1));
			return saturate(pulseIn * pulseOut * eventAmount * passAmount * amountScale);
		}

		float GlitchPulse(float eventIndex, float passSeed)
		{
			float protectedLayer = floor(Hash(eventIndex + 149.5) * 3.0) + 1.0;
			if (passSeed < 3.5 && abs(passSeed - protectedLayer) < 0.1)
				return 0.0;

			float orderedOffset = (passSeed - 2.5) * _GlitchLayerSpread;
			float randomOffset = (Hash(eventIndex + passSeed * 17.9) - 0.5) * _GlitchLayerJitter;
			float layerOffset = orderedOffset + randomOffset;
			float start = GlitchEventTime(eventIndex) + layerOffset;
			float primary = GlitchPulseAt(start, eventIndex, passSeed, 1.0);
			float repeatChance = step(Hash(eventIndex + 91.7), _GlitchRepeatChance);
			float repeatDelay = lerp(0.4, 0.8, Hash(eventIndex + 107.3));
			float repeatAmount = lerp(0.62, 0.9, Hash(eventIndex + 131.9));
			float repeat = GlitchPulseAt(start + repeatDelay, eventIndex + 97.0, passSeed, repeatAmount);
			return max(primary, repeat * repeatChance);
		}

		float CoordinatedGlitch(float passSeed)
		{
			if (_GlitchEnabled < 0.5)
				return 0.0;
			float eventIndex = floor(_Time.y / max(_GlitchInterval, 0.1));
			return max(GlitchPulse(eventIndex - 1.0, passSeed), max(GlitchPulse(eventIndex, passSeed), GlitchPulse(eventIndex + 1.0, passSeed)));
		}

		fixed4 LayerColor(fixed4 color, float strength, float passSeed)
		{
			float layer = saturate(strength * (1.0 - CoordinatedGlitch(passSeed) * _GlitchAmount));
			return fixed4(lerp(float3(1.0, 1.0, 1.0), color.rgb, layer), 1.0);
		}

		v2f vert(appdata input)
		{
			v2f output;
			output.vertex = UnityObjectToClipPos(input.vertex);
			output.uv = input.uv;
			return output;
		}
		ENDCG

		Pass
		{
			Name "Multiply"
			ZTest Always
			ZWrite Off
			Cull Off
			Blend DstColor Zero

			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment frag_multiply

			fixed4 frag_multiply(v2f input) : SV_Target
			{
				return LayerColor(_MultiplyColor, _MultiplyStrength, 1.0);
			}
			ENDCG
		}

		Pass
		{
			Name "ColorBurnApproximation"
			ZTest Always
			ZWrite Off
			Cull Off
			Blend DstColor Zero

			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment frag_color_burn

			fixed4 frag_color_burn(v2f input) : SV_Target
			{
				return LayerColor(_ColorBurnColor, _ColorBurnStrength, 2.0);
			}
			ENDCG
		}

		Pass
		{
			Name "DarkerColorApproximation"
			ZTest Always
			ZWrite Off
			Cull Off
			BlendOp Min
			Blend One One

			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment frag_darker_color

			fixed4 frag_darker_color(v2f input) : SV_Target
			{
				return LayerColor(_DarkerColor, _DarkerColorStrength, 3.0);
			}
			ENDCG
		}

		Pass
		{
			Name "Vignette"
			ZTest Always
			ZWrite Off
			Cull Off
			Blend DstColor Zero

			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment frag_vignette

			fixed4 frag_vignette(v2f input) : SV_Target
			{
				float2 centered = input.uv * 2.0 - 1.0;
				centered.x *= _ScreenParams.x / max(_ScreenParams.y, 1.0);
				float distanceFromCenter = length(centered);
				float vignette = smoothstep(_VignetteRadius, _VignetteRadius + _VignetteSoftness, distanceFromCenter);
				float layer = saturate(vignette * _VignetteStrength * (1.0 - CoordinatedGlitch(4.0) * _GlitchAmount));
				return fixed4(lerp(float3(1.0, 1.0, 1.0), _VignetteColor.rgb, layer), 1.0);
			}
			ENDCG
		}
	}
}
