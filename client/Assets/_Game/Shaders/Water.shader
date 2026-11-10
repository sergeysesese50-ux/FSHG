// Вода тестового озера для мобильных: непрозрачная, без текстур.
// - Рябь — шесть синусоид веером вокруг ветра со сбитой шумом фазой; пятна штиля и ряби.
//   Волны мельче пикселя не рисуются, а уходят в «шероховатость» — вдали нет ряби-помех.
// - Отражение: то же небо, что в скайбоксе (LakeCommon.hlsl), плюс тёмная полоса дальнего леса и холмов.
// - Дорожка от солнца или луны: блик по наклонам волн — узкая у горизонта, шире к рыбаку, с искрами.
// - Толща: цвет _LakeWaterBody, у ближнего берега светлее (мелководье).
// Все цвета задаёт LakeClock.
Shader "Fishing/Water"
{
    Properties
    {
        _WaveScale ("Wave Scale", Float) = 1
        _ShoreZ ("Far Shore Z", Float) = 176
        _ShoreHeight ("Far Shore Tree Height", Float) = 16
        _HillZ ("Hill Z", Float) = 420
        _HillHeight ("Hill Height", Float) = 44
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "LakeCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _WaveScale;
                float _ShoreZ;
                float _ShoreHeight;
                float _HillZ;
                float _HillHeight;
            CBUFFER_END

            #define WAVE_COUNT 6
            // Направления относительно ветра (x — по ветру), частоты в рад/м и наклон каждой волны.
            static const float2 WaveDirs[WAVE_COUNT] =
            {
                float2(1.000, 0.000), float2(0.906, 0.423), float2(0.848, -0.530),
                float2(0.669, 0.743), float2(0.970, -0.242), float2(0.574, -0.819)
            };
            static const float WaveFreqs[WAVE_COUNT] = { 1.7, 2.9, 4.6, 7.3, 11.3, 17.9 };
            static const float WaveSlopes[WAVE_COUNT] = { 0.020, 0.028, 0.034, 0.036, 0.034, 0.030 };
            static const float SunRadius = 0.027;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 toCamera = _WorldSpaceCameraPos - input.positionWS;
                float dist = length(toCamera);
                float3 V = toCamera / dist;
                float2 xz = input.positionWS.xz * _WaveScale;
                float2 dx = ddx(xz);
                float2 dy = ddy(xz);
                float time = _Time.y;

                // Сила ряби: ветер и медленные пятна штиля.
                float wind = _LakeWind.z;
                float2 windDir = _LakeWind.xy;
                float patch = LakeNoise(xz * 0.03 + windDir * (time * 0.04));
                float rough = (0.22 + 0.85 * wind) * lerp(0.45, 1.3, smoothstep(0.28, 0.72, patch));

                float2 warp = (float2(LakeNoise(xz * 0.23 + 11.0), LakeNoise(xz * 0.23 + 37.0)) - 0.5) * 1.6;
                float2 slope = 0;
                float lost = 0;
                [unroll]
                for (int k = 0; k < WAVE_COUNT; k++)
                {
                    float2 d = float2(WaveDirs[k].x * windDir.x - WaveDirs[k].y * windDir.y, WaveDirs[k].x * windDir.y + WaveDirs[k].y * windDir.x);
                    float f = WaveFreqs[k];
                    float footprint = abs(dot(d, dx)) + abs(dot(d, dy));
                    float keep = saturate((2.5 - f * footprint) / 1.3);
                    float phase = dot(d, xz + warp) * f - time * sqrt(9.8 * f) * 0.7;
                    float a = WaveSlopes[k] * rough;
                    slope += d * (cos(phase) * a * keep);
                    lost += a * a * 0.5 * (1.0 - keep * keep);
                }

                float sigma = sqrt(lost);
                float3 N = normalize(float3(-slope.x, 1.0, -slope.y));
                float3 R = reflect(-V, N);
                R.y = abs(R.y) + 0.004;
                R = normalize(R);
                float fresnel = 0.014 + 0.986 * pow(1.0 - saturate(dot(N, V) + sigma * 1.2), 5.5);

                // Отражение: небо, поверх — холмы и дальний лес (лучом до их плоскостей).
                half3 reflection = LakeSkyBase(R);
                half3 haze = LakeHaze(-V);
                float blur = saturate(0.2 + sigma * 6.0);
                float run = max(R.z, 0.02);

                float hillT = (_HillZ - input.positionWS.z) / run;
                float hillX = input.positionWS.x + R.x * hillT;
                float hillTop = _HillHeight * (0.45 + 0.75 * LakeNoise(float2(hillX * 0.006, 5.0)));
                float hillMask = 1.0 - smoothstep(hillTop * (1.0 - blur), hillTop, R.y * hillT);
                reflection = lerp(reflection, lerp(_LakeShoreTint.rgb, haze, saturate(0.62 * _LakeShoreTint.a)), hillMask * 0.9);

                float shoreT = (_ShoreZ - input.positionWS.z) / run;
                float shoreX = input.positionWS.x + R.x * shoreT;
                float shoreTop = _ShoreHeight * (0.6 + 0.4 * LakeNoise(float2(shoreX * 0.11, 3.0))) + 2.5 * LakeNoise(float2(shoreX * 0.9, 9.0));
                float shoreMask = (1.0 - smoothstep(shoreTop * (1.0 - blur), shoreTop, R.y * shoreT)) * step(0.0, shoreT);
                reflection = lerp(reflection, lerp(_LakeShoreTint.rgb, haze, saturate(0.2 * _LakeShoreTint.a)) * 0.8, shoreMask);

                // Толща воды; у ближнего берега (кромка z = 0,004·x²) — мелководье.
                float shoreDistance = input.positionWS.z - 0.004 * input.positionWS.x * input.positionWS.x;
                float shallow = saturate(1.0 - shoreDistance / 4.5);
                half3 body = _LakeWaterBody.rgb;
                body = lerp(body, body * 1.5 + _LakeAmbient.rgb * half3(0.16, 0.13, 0.08), shallow * shallow * 0.8);

                half3 color = lerp(body, reflection, fresnel);

                // Дорожка: вероятность того, что наклон волны отразит светило в глаз.
                float3 L = _LakeGlintDir.xyz;
                float3 H = normalize(V + L);
                float2 need = -H.xz / max(H.y, 0.05);
                float2 off = need - slope;
                float2 along = normalize(V.xz + float2(0.0, 1e-4));
                float da = dot(off, along);
                float dc = dot(off, float2(-along.y, along.x));
                float tanV = max(V.y / max(length(V.xz), 1e-3), 0.03);
                float micro = 0.00008 + 0.0004 * rough * rough;
                float discAlong = SunRadius * 0.5;
                float discCross = SunRadius * 0.5 / tanV;
                float va = lost + micro + discAlong * discAlong;
                float vc = lost + micro + discCross * discCross;
                float glint = exp(-0.5 * (da * da / va + dc * dc / vc));
                glint += 0.22 * exp(-0.5 * (da * da / (va * 7.0) + dc * dc / (vc * 4.0)));
                float energy = max(0.3, sqrt(saturate(discAlong * discCross / sqrt(va * vc))));

                // Искры: в ячейках размером в десяток пикселей — мягкие точки, вспыхивают вразнобой.
                float2 footprint2 = float2(abs(dx.x) + abs(dy.x), abs(dx.y) + abs(dy.y));
                float2 cellSize = exp2(floor(log2(max(footprint2 * 7.0, 1e-4))));
                float2 cellPos = xz / cellSize;
                float2 cell = floor(cellPos);
                float2 seed = cell + cellSize * 91.0;
                float h1 = LakeHash(seed);
                float h2 = LakeHash(seed + 57.0);
                float2 center = 0.3 + 0.4 * float2(LakeHash(seed + 13.0), LakeHash(seed + 29.0));
                float2 fromCenter = (cellPos - cell - center) / 0.28;
                float spot = saturate(1.0 - dot(fromCenter, fromCenter));
                float flash = pow(saturate(sin(time * (2.0 + 3.5 * h2) + 6.2832 * h1)), 4.0);
                float sparkle = 0.42 + 9.0 * spot * spot * flash;

                color += _LakeGlintColor.rgb * (glint * energy * sparkle * lerp(0.35, 1.0, fresnel));

                // Дымка вдаль.
                float hazeAmount = (1.0 - exp(-dist * 0.0035)) * 0.4 * _LakeShoreTint.a;
                color = lerp(color, haze, hazeAmount);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
