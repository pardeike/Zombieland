Shader "Custom/ZombieBlob"
{
   SubShader
   {
      Tags { "Queue"="Transparent" }
      Blend SrcAlpha OneMinusSrcAlpha
      ZWrite off

      Pass
      {
         CGPROGRAM

         #include "UnityCG.cginc"

         #pragma target 5.0
         #pragma vertex vert_img
         #pragma fragment frag

         struct Metaball
         {
            float  radius;
            float  size;
            float  power;
            float2 position;
            float2 direction;
            float4 color;
         };

         static const float4 color_bg = float4(0.0, 0.0, 0.0, 0.0);
         static const float4 color_inner = float4(0.0, 0.3, 0.1, 1.0);
         static const float4 color_highlight = float4(1.0, 1.0, 0.0, 1.0);
         static const float4 color_outer = float4(0.0, 0.0, 0.0, 0.8);

         static const float2 cellSize = float2(0.058, 0.08);

         static const float tMax = 0.35;
         static const float tMin = tMax - 0.1;

         static const float2 powerExponent = float2(1.0, 3.0);

         static const int cellCount = 18;
         static const float2 positions[cellCount] =
         {
             // bottom left
             float2(0.45, 0.5),
             float2(0.6, 0.5),
             float2(0.75, 0.5),
             float2(0.9, 0.5),

             // bottom right
             float2(1.35, 0.5),
             float2(1.35, 0.65),
             float2(1.5, 0.5),
             float2(1.65, 0.5),
             float2(1.65, 0.65),

             // top left
             float2(0.45, 1.5),
             float2(0.6, 1.5),
             float2(0.6, 1.35),
             float2(0.75, 1.35),
             float2(0.9, 1.35),
             float2(1.05, 1.35),
             float2(0.9, 1.5),
             float2(1.05, 1.5),

             // top right
             float2(1.5, 1.5)
         };

         StructuredBuffer<Metaball> _MetaballBuffer;

         float2 cellPower(int idx, float2 coord, float2 pos)
         {
             // test jiggle
             float offset1 = 300.0 * idx;
             float offset2 = 700.0 * idx;
             pos.x += sin(_Time * 60.0 + offset1) / 200.0;
             pos.y += cos(_Time * 60.0 + offset2) / 200.0;

             float2 len = coord - pos;
             float2 power = cellSize * cellSize / dot(len, len);
             power *= pow(power, powerExponent);
             return power;
         }

         float2 powerAt(float2 coord)
         {
            float2 pos;
            float2 power = float2(0.0, 0.0);

            for(int idx = 0; idx != cellCount; ++idx)
            {
               pos = positions[idx];
               power += cellPower(idx, coord, pos);
            }

            // test simulate expansion
            float f = sin(_Time * 30.0);
            if (f >= 0.0)
            {
               f = min(1.0, 1.5 * f);
               float step = f * 0.15;

               // bottom left
               pos = float2(0.45 - step, 0.5);
    	         power += cellPower(cellCount, coord, pos) * f;

               // bottom right
               pos = float2(1.5, 0.5 + step);
    	         power += cellPower(cellCount + 1, coord, pos) * f;

               // top left
               pos = float2(0.75, 1.35 - step);
    	         power += cellPower(cellCount + 2, coord, pos) * f;

               // top right
               pos = float2(1.5 + step, 1.5);
    	         power += cellPower(cellCount + 3, coord, pos) * f;
            }

            return power;
         }

         float4 colorAt(float2 coord)
         {
            float2 power = powerAt(coord);
            float2 power2 = powerAt(coord - float2(0.01, 0.02));

            float4 color = lerp(color_bg, color_outer, smoothstep(tMin, tMax, power.y));
            color = lerp(color, color_inner, smoothstep(tMin, tMax, power.x));

            if (power.x > 0.25 && power.y > 0.25)
            {
               float f = 1.0 / dot(power, power);
               float f2 = 1.0 / dot(power2, power2);
               color -= color * 0.7 * pow(f, 0.1);
               color += lerp(color_highlight, color, pow(f2, 0.03)) * 0.25;
               color.w = lerp(0.2, 0.65, pow(f, 0.01));
            }

            return color;
         }

         fixed4 frag (v2f_img input) : SV_Target
         {
            float2 coord = lerp(float2(0.0, 0.0), float2(2.0, 2.0), input.uv);
            return colorAt(coord);
         }
         ENDCG
      }
   }
}
