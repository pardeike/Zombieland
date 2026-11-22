Shader "Custom/Metaballs"
{
	SubShader
	{
		Tags { "Queue"="Transparent" "RenderType"="Transparent" }
		Blend One Zero
		ZWrite Off
		Cull Off

		Pass
		{
			CGPROGRAM
			#include "UnityCG.cginc"

			#pragma target 5.0
			#pragma vertex vert_img
			#pragma fragment frag

				struct Metaball
				{
					float4 shape;
					float4 motion;
					float4 tint;
				};

				StructuredBuffer<Metaball> _MetaballBuffer;
				int _MetaballCount;
				float4 _MetaballWorldSize;

			float4 frag(v2f_img input) : SV_Target
			{
				float field = 0.0;
				float3 color = 0.0;
					for (int i = 0; i < _MetaballCount; i++)
					{
						Metaball ball = _MetaballBuffer[i];
						float radius = ball.shape.x * saturate(ball.shape.y);
						if (radius <= 0.0001)
							continue;

						float2 delta = (input.uv - ball.motion.xy) * max(_MetaballWorldSize.xy, float2(0.0001, 0.0001));
						float contribution = (radius * radius) / max(dot(delta, delta), 0.0001);
						contribution *= contribution;
					contribution *= max(ball.shape.z, 0.0);
					field += contribution;
					color += ball.tint.rgb * contribution;
				}

				float alpha = smoothstep(0.45, 1.20, field) * 0.40;
				float edge = smoothstep(0.45, 1.80, field);
				float3 body = field > 0.0001 ? color / field : float3(0.0, 0.8, 0.0);
				body = lerp(float3(0.0, 0.0, 0.0), body, edge);
				return float4(body, alpha);
			}
			ENDCG
		}
	}
}