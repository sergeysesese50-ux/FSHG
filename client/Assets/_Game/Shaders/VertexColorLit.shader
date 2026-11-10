// Простое освещение для процедурных мешей (берег, лес, камыш, рыба):
// цвет из вершин × (солнце с мягким обёртыванием + общий свет озера) + туман.
// _BendAmp/_BendFreq/_BendSpeed — волна вдоль тела рыбы (голова в +X, хвост в -X).
// _SwayAmp — покачивание камыша по ветру, растёт с высотой (локальный Y).
// Вариант _SILHOUETTE — дальний берег силуэтами: без света, цвет берега из LakeClock (_LakeShoreTint)
// × цвет вершины, альфа вершины — сколько дымки (0 — рядом, 1 — растворился в небе у горизонта).
Shader "Fishing/VertexColorLit"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _BendAmp ("Bend Amplitude", Float) = 0
        _BendFreq ("Bend Frequency", Float) = 7
        _BendSpeed ("Bend Speed", Float) = 8
        _SwayAmp ("Sway Amplitude", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_local _ _SILHOUETTE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "LakeCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _BendAmp;
                float _BendFreq;
                float _BendSpeed;
                float _SwayAmp;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                half4 color : COLOR;
                float fogFactor : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 p = input.positionOS.xyz;

                float tailWeight = saturate(0.6 - p.x);
                p.z += sin(p.x * _BendFreq - _Time.y * _BendSpeed) * _BendAmp * tailWeight;

                float3 positionWS = TransformObjectToWorld(p);
                float gust = 0.6 + 0.4 * sin(_Time.y * 2.1 + positionWS.x * 0.7 + positionWS.z * 0.3);
                positionWS.xz += _LakeWind.xy * (_LakeWind.z * _SwayAmp * p.y * p.y * gust);

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color * _Color;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
            #if defined(_SILHOUETTE)
                float3 view = normalize(input.positionWS - _WorldSpaceCameraPos);
                half3 haze = LakeHaze(view);
                half3 shade = _LakeShoreTint.rgb * input.color.rgb;
                half amount = saturate(input.color.a * _LakeShoreTint.a);
                half3 silhouette = lerp(shade, haze, amount);
                // Контровой свет: силуэты рядом с солнцем чуть светятся.
                half toSun = saturate(dot(view, _LakeSunDir.xyz));
                silhouette += _LakeGlow.rgb * (pow(toSun, 40.0) * 0.16 * (0.4 + amount));
                return half4(silhouette, 1);
            #else
                Light light = GetMainLight();
                float3 n = normalize(input.normalWS);
                half wrap = saturate(dot(n, light.direction) * 0.6 + 0.4);
                half3 color = input.color.rgb * (light.color * wrap + _LakeAmbient.rgb);
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            #endif
            }
            ENDHLSL
        }
    }
}
