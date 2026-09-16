Shader "Skybox/CloudBillboard"
{
    // 球面随机排布的云：每个 quad 用 uv0.zw 指定图集里的一朵云，uv0.xy 为 quad 内局部 uv。
    // 云图通道：R=暗部1  G=暗部2  B=高光  A=密度（0=背景，1=云心，边缘软过渡）
    Properties
    {
        _CloudTex ("Cloud Atlas (R:Dark1 G:Dark2 B:Highlight A:Density)", 2D) = "black" {}
        _GridCols ("Atlas Columns", Float) = 2
        _GridRows ("Atlas Rows", Float) = 4
        _CellInset ("Cell Inset", Range(0, 0.2)) = 0.04

        // ---- 形状 / 透明度 ----
        _Coverage ("Coverage", Range(0, 1)) = 0.25
        _CoverageGlobal ("Coverage Global (weather)", Range(0, 2)) = 1.0
        _Softness ("Softness", Range(0.001, 0.8)) = 0.5
        _Opacity ("Opacity", Range(0, 1)) = 1.0
        _HorizonFade ("Horizon Fade", Range(0, 0.5)) = 0.06

        // ---- 消散 ----
        _Dissolve ("Dissolve Range", Range(0, 1)) = 0.25
        _DissolveSpeed ("Dissolve Speed", Range(0, 2)) = 0.0
        _DissolvePhaseSpread ("Dissolve Phase Spread", Range(0, 1)) = 1.0

        // ---- 明暗（通道 R/G/B 共用增益）----
        _BrightColor ("Bright Color", Color) = (1, 1, 1, 1)
        _DarkColor ("Dark Color", Color) = (0.5, 0.57, 0.72, 1)
        _SecDarkColor ("Second Dark Color", Color) = (0.72, 0.78, 0.93, 1)
        _ShadingScale ("Shading Scale (R/G/B gain)", Range(0, 2)) = 1.0
        _BottomShade ("Bottom Shade", Range(0, 1)) = 0.35
        _BrightVariation ("Bright Variation", Range(0, 0.5)) = 0.12

        // ---- 实时太阳受光 ----
        // _BlobBump：把图集的密度场当高度场求法线的强度，越大越立体
        _BlobBump ("Blob Bump", Range(0, 20)) = 6.0
        _SunLightIntensity ("Sun Light Intensity", Range(0, 3)) = 1.0
        _CloudAmbient ("Cloud Ambient", Color) = (0.62, 0.70, 0.85, 1)

        // ---- 受光（太阳为唯一主光）----
        _RimColor ("Rim Color", Color) = (1, 0.96, 0.88, 1)
        _SunRim ("Sun Rim", Range(0, 3)) = 1.0
        _AntiSunShade ("Anti Sun Shade", Range(0, 1)) = 0.8

        // ---- 日夜（夜晚色，越暗越黑）----
        _NightColor ("Night Color", Color) = (0.12, 0.14, 0.22, 1)

        // ---- 远近 / 大气透视（逐像素；距离由网格构建器写入）----
        _AerialColor ("Aerial Color", Color) = (0.78, 0.85, 0.97, 1)
        _AerialStrength ("Aerial Strength (horizon)", Range(0, 1)) = 0.35
        _DepthHazeMul ("Depth Haze Mul", Range(0, 3)) = 1.5
        _FarNearDist ("Far Near Dist", Float) = 380
        _FarFarDist ("Far Far Dist", Float) = 900
        _FarOpacity ("Far Opacity", Range(0, 1)) = 0.85
        _FarSaturation ("Far Saturation", Range(0, 1)) = 0.85

        // ---- 全局天气（由脚本用 MaterialPropertyBlock 驱动）----
        _CloudTintGlobal ("Cloud Tint Global", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 uv     : TEXCOORD0;   // xy = quad 内局部 uv, zw = 图集格原点
                float2 meta   : TEXCOORD1;   // x = depth01, y = brightHash
                float4 color   : COLOR;
                float3 normal  : NORMAL;     // 面片朝外（≈ 朝相机）
                float4 tangent : TANGENT;    // xyz = 面片右向量
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float4 uv       : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float2 meta     : TEXCOORD2;
                float4 color    : COLOR;
                float3 normal   : TEXCOORD3;
                float3 tangent  : TEXCOORD4;
            };

            TEXTURE2D(_CloudTex);
            SAMPLER(sampler_CloudTex);

            float _GridCols;
            float _GridRows;
            float _CellInset;
            float4 _BrightColor;
            float4 _DarkColor;
            float4 _SecDarkColor;
            float _ShadingScale;
            float4 _RimColor;
            float _Coverage;
            float _Softness;
            float _Opacity;
            float _Dissolve;
            float _DissolveSpeed;
            float _DissolvePhaseSpread;
            float _SunRim;
            float4 _NightColor;
            float _HorizonFade;
            float _BottomShade;
            float4 _AerialColor;
            float _AerialStrength;
            float _DepthHazeMul;
            float _FarNearDist;
            float _FarFarDist;
            float _FarOpacity;
            float _FarSaturation;
            float _BrightVariation;
            float _AntiSunShade;
            float _CoverageGlobal;
            float4 _CloudTintGlobal;
            float _BlobBump;
            float _SunLightIntensity;
            float4 _CloudAmbient;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.uv = v.uv;
                o.color = v.color;
                o.worldPos = TransformObjectToWorld(v.vertex.xyz);
                o.meta = v.meta;
                o.normal = TransformObjectToWorldNormal(v.normal);
                o.tangent = TransformObjectToWorldDir(v.tangent.xyz);
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                // 图集格采样（带内缩，避免相邻格的边缘渗色）
                float2 cellSize = float2(1.0 / _GridCols, 1.0 / _GridRows);
                float2 local = lerp(_CellInset, 1.0 - _CellInset, saturate(i.uv.xy));
                float2 uv = i.uv.zw + local * cellSize;

                float4 c = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv);

                // 平滑消散循环：用 sin 乒乓（0 -> 1 -> 0）代替 frac 锯齿。
                // frac 会在 1 处瞬间跳回 0，造成“闪烁回弹”；sin 全程连续，只有平滑过渡。
                // _DissolvePhaseSpread 控制每朵云相位错开程度（0=所有云同步，1=完全随机）。
                float phase = _Time.y * _DissolveSpeed + i.color.g * _DissolvePhaseSpread;
                float wave = 0.5 - 0.5 * cos(phase * 6.2831853);   // 0->1->0，无缝无跳变
                float dissolve = _Dissolve * (_DissolveSpeed > 1e-4 ? wave : 0.0);

                // 每朵云的覆盖系数（来自空间分布遮罩，烘在顶点色 alpha；默认 1 = 老行为）
                // mask=1 -> 用全局 _Coverage；mask=0 -> threshold 顶到 1，这朵云被完全吃掉
                float cloudMask = saturate(i.color.a);
                float threshold = saturate(lerp(1.0, _Coverage * _CoverageGlobal, cloudMask) + dissolve);

                // 每朵云的深度 / 亮度 hash（4 顶点同值 -> per-cloud 常数）
                float brightHash = i.meta.y;

                // SDF -> 实心云（远云略降透明度）
                float alpha = smoothstep(threshold, threshold + _Softness, c.a) * _Opacity;

                // 云的球面方向（相对相机），用于 rim、地平线淡出、远近分级
                float3 dir = normalize(i.worldPos - _WorldSpaceCameraPos);
                alpha *= smoothstep(0.0, max(_HorizonFade, 1e-3), dir.y);

                // 逐像素的"远近"代理：用于地平线淡出。
                float elev01 = smoothstep(0.015, 0.55, dir.y);

                // 远近分级：按"到相机的实际距离"逐像素算，不能按仰角。
                // 按仰角的话高空的 Mid/High 拿到 elev01≈1（不衰减），地平线的 SkyClouds 拿到 ≈0（被削），
                // 三层颜色就会不一样。各层都在同一半径上时 far01 是同一个常数。
                // 也不用逐面片常量 depth01 —— 那样面片边界会突变成硬边，云团上切出细直横线。
                float dist = length(i.worldPos - _WorldSpaceCameraPos);
                float far01 = saturate((dist - _FarNearDist) / max(1.0, _FarFarDist - _FarNearDist));
                alpha *= lerp(1.0, _FarOpacity, far01);

                // 通道缩放：不改原画也能统一校准明暗强度
                float d1 = saturate(c.r * _ShadingScale);
                float d2 = saturate(c.g * _ShadingScale);
                float hl = saturate(c.b * _ShadingScale);

                // 着色：亮部 = 1 - 暗部1 - 暗部2
                float bright = saturate(1.0 - d1 - d2);
                float3 col = _BrightColor.rgb * bright
                           + _DarkColor.rgb * d1
                           + _SecDarkColor.rgb * d2;

                // 每朵亮度扰动
                col *= 1.0 + (brightHash - 0.5) * _BrightVariation;

                // ---- 云团法线 ----
                // 面片没有真实法线，做不了实时受光。把图集的密度场当高度场求一次梯度当法线，
                // 这是 billboard 云做实时光照的常规做法。
                float2 uvOff = cellSize * 0.004;
                float hC = c.a;
                float hR = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv + float2(uvOff.x, 0)).a;
                float hU = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv + float2(0, uvOff.y)).a;

                float3 Nw = normalize(i.normal);              // 面片朝外（≈ 朝相机）
                float3 T  = normalize(i.tangent);
                float3 Bn = normalize(cross(Nw, T));
                float3 Nb = normalize(Nw + (T * (hC - hR) + Bn * (hC - hU)) * _BlobBump);

                // ---- 实时太阳 ----
                Light mainLight = GetMainLight();
                float3 sunDir = mainLight.direction;
                float ndl  = dot(Nb, sunDir);
                float wrap = saturate(ndl * 0.65 + 0.35);     // 半兰伯特：云有次表面散射感，暗面不会被压死

                // 用 lerp 而不是相乘：受光面 = 太阳色，背光面 = 环境色。
                // 相乘会把图集里云自身的明暗结构一起压掉，整朵云变成一块灰饼。
                float3 litCol = lerp(_CloudAmbient.rgb, mainLight.color * _SunLightIntensity, wrap);
                col *= litCol;

                // ---- 边缘光：朝太阳那一侧的轮廓发光 ----
                // （之前用 dir 点太阳方向：dir 整张面片几乎不变，那只是"整朵云提亮"，不是边缘）
                float fres = pow(1.0 - saturate(dot(Nb, Nw)), 2.0);
                float rimSide = saturate(ndl * 0.5 + 0.5);
                col += _RimColor.rgb * mainLight.color * _SunRim * fres * rimSide;

                // 日夜因子（夜晚染色放到雾化之后，避免被 AerialColor 提亮）
                float sunNightStep = smoothstep(-0.3, 0.25, sunDir.y);

                // 全局天气色调（脚本用 MaterialPropertyBlock 驱动）
                col *= _CloudTintGlobal.rgb;

                // 地平线大气透视（逐像素，用仰角）
                float haze = saturate((1.0 - elev01) * (_AerialStrength + _DepthHazeMul * 0.25));
                col = lerp(col, _AerialColor.rgb, haze);

                // 远云降饱和：同样按实际距离（逐像素）
                float luma = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(float3(luma, luma, luma), col, lerp(1.0, _FarSaturation, far01));

                // 夜晚染色（_NightColor 越暗，夜晚越暗；不影响白天）
                col *= lerp(_NightColor.rgb, float3(1, 1, 1), sunNightStep);

                // 底部压暗：云底更暗，接近平底，体积更整
                col *= lerp(1.0 - _BottomShade, 1.0, saturate(i.uv.y));

                return float4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
