// Небо тестового озера: градиент от горизонта к зениту, диск солнца со свечением,
// луна с пятнами и ореолом, звёзды ночью, лёгкие перистые облака, подсвеченные с солнечной стороны.
// Все цвета и направления задаёт LakeClock (см. LakeCommon.hlsl).
Shader "Fishing/LakeSky"
{
    Properties
    {
        _SunRadius ("Sun Radius (rad)", Float) = 0.027
        _MoonRadius ("Moon Radius (rad)", Float) = 0.023
        _CloudAmount ("Cloud Amount", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "LakeCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _SunRadius;
                float _MoonRadius;
                float _CloudAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            // Диск светила: 1 внутри, мягкий край. offset — вектор от центра диска в его плоскости, в радиусах.
            float Disc(float3 dir, float3 center, float radius, out float2 offset)
            {
                float3 right = normalize(cross(float3(0, 1, 0), center));
                float3 up = cross(center, right);
                offset = float2(dot(dir, right), dot(dir, up)) / radius;
                float facing = step(0.0, dot(dir, center));
                return facing * (1.0 - smoothstep(0.93, 1.04, length(offset)));
            }

            float Stars(float3 dir)
            {
                float3 p = dir * 110.0;
                float3 cell = floor(p);
                float h = LakeHash3(cell);
                float3 jitter = float3(LakeHash3(cell + 17.0), LakeHash3(cell + 43.0), LakeHash3(cell + 71.0)) - 0.5;
                float d = length(p - cell - 0.5 - jitter * 0.6);
                float size = 0.05 + 0.07 * LakeHash3(cell + 5.0);
                float star = (1.0 - smoothstep(0.0, size, d)) * step(0.90, h);
                float twinkle = 0.65 + 0.35 * sin(_Time.y * (1.5 + 3.0 * h) + h * 90.0);
                return star * twinkle * (0.4 + 0.6 * LakeHash3(cell + 9.0));
            }

            // Перистые облака: вытянутые вдоль горизонта полосы, три октавы шума.
            float Clouds(float3 dir)
            {
                float2 uv = dir.xz / (dir.y + 0.12);
                uv = float2(uv.x * 0.55, uv.y * 1.9) + _LakeWind.xy * (_Time.y * 0.004);
                float n = LakeNoise(uv) * 0.55 + LakeNoise(uv * 2.3 + 7.1) * 0.30 + LakeNoise(uv * 5.1 + 3.7) * 0.15;
                float cover = smoothstep(0.62 - 0.22 * _CloudAmount, 0.86, n);
                return cover * smoothstep(0.015, 0.11, dir.y);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.direction);
                half3 color = LakeSkyBase(dir);

                float night = _LakeNight;
                float horizonFade = smoothstep(0.0, 0.16, dir.y);
                color += Stars(dir) * night * horizonFade * half3(0.85, 0.92, 1.0) * 1.6;

                float cloud = Clouds(dir) * _CloudAmount;
                float towardSun = saturate(dot(dir, _LakeSunDir.xyz));
                half3 cloudLit = lerp(_LakeSkyMid.rgb, _LakeHorizon.rgb, 0.35) * 1.08 + _LakeGlow.rgb * (0.10 + 0.55 * towardSun * towardSun);
                half3 cloudNight = _LakeHorizon.rgb * 1.5 + _LakeMoonDisc.rgb * 0.02;
                color = lerp(color, lerp(cloudLit, cloudNight, night), cloud * 0.55);

                float2 offset;
                float sun = Disc(dir, _LakeSunDir.xyz, _SunRadius, offset);
                color = lerp(color, _LakeSunDisc.rgb, sun * saturate(_LakeSunDisc.a) * (1.0 - cloud * 0.5));

                float moon = Disc(dir, _LakeMoonDir.xyz, _MoonRadius, offset);
                float seas = smoothstep(0.50, 0.72, LakeNoise(offset * 1.7 + 4.0)) * 0.22 + smoothstep(0.55, 0.8, LakeNoise(offset * 3.9 + 11.0)) * 0.10;
                float limb = sqrt(saturate(1.0 - dot(offset, offset) * 0.55));
                half3 moonColor = _LakeMoonDisc.rgb * (1.0 - seas) * (0.72 + 0.28 * limb);
                color = lerp(color, moonColor, moon * saturate(_LakeMoonDisc.a));

                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
