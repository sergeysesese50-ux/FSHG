// Общие величины озера для неба, воды и силуэтов берега. Их задаёт LakeClock (глобальные свойства):
// градиент неба, направления и цвета солнца и луны, свечение у горизонта, цвет берега и воды.
// Вода и дымка берега читают то же небо, что рисует скайбокс, — поэтому отражение и даль совпадают с ним.
#ifndef FISHING_LAKE_COMMON_INCLUDED
#define FISHING_LAKE_COMMON_INCLUDED

float4 _LakeHorizon;    // цвет неба у горизонта
float4 _LakeSkyMid;     // цвет неба на ~20° над горизонтом
float4 _LakeZenith;     // цвет неба в зените
float4 _LakeGlow;       // свечение вокруг солнца
float4 _LakeSunDir;     // xyz — направление на солнце
float4 _LakeMoonDir;    // xyz — направление на луну
float4 _LakeSunDisc;    // цвет диска солнца (HDR), a = 0 — солнца нет
float4 _LakeMoonDisc;   // цвет диска луны (HDR), a = 0 — луны нет
float4 _LakeAmbient;    // общий свет
float4 _LakeWind;       // xy — направление ветра, z — сила 0…1
float4 _LakeShoreTint;  // rgb — цвет силуэтов берега, a — сила дымки
float4 _LakeWaterBody;  // цвет толщи воды
float4 _LakeGlintDir;   // xyz — направление на источник дорожки (солнце или луна)
float4 _LakeGlintColor; // цвет дорожки на воде (HDR)
float _LakeNight;       // 0 день … 1 ночь

float LakeHash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float LakeHash3(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

// Гладкий шум 0…1.
float LakeNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = p - i;
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = LakeHash(i);
    float b = LakeHash(i + float2(1, 0));
    float c = LakeHash(i + float2(0, 1));
    float d = LakeHash(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// Небо без дисков и звёзд: градиент и свечение вокруг солнца и луны.
// Этим же цветом вода отражает небо, а дальние холмы тонут в дымке.
half3 LakeSkyBase(float3 dir)
{
    float y = dir.y;
    float low = 1.0 - saturate(y / 0.34);
    half3 color = lerp(_LakeSkyMid.rgb, _LakeHorizon.rgb, low * low);
    color = lerp(color, _LakeZenith.rgb, smoothstep(0.22, 1.0, y));
    color = lerp(color, _LakeHorizon.rgb * 0.72, saturate(-y * 5.0));

    float sun = saturate(dot(dir, _LakeSunDir.xyz));
    float sun2 = sun * sun;
    float sun8 = sun2 * sun2 * sun2 * sun2;
    float hug = exp(-max(y, 0.0) * 5.0);
    float glow = sun8 * 0.22 * hug + pow(sun, 48.0) * 0.38 + pow(sun, 420.0) * 0.55;
    color += _LakeGlow.rgb * glow;

    float moon = saturate(dot(dir, _LakeMoonDir.xyz));
    color += _LakeMoonDisc.rgb * (pow(moon, 90.0) * 0.035 + pow(moon, 900.0) * 0.07);
    return color;
}

// Цвет дымки в сторону взгляда: небо у самого горизонта.
half3 LakeHaze(float3 viewDir)
{
    float2 flat = normalize(viewDir.xz + float2(0.0, 1e-4));
    return LakeSkyBase(normalize(float3(flat.x, 0.035, flat.y)));
}

#endif
