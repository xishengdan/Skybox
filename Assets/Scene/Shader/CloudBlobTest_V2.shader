Shader "SkyboxV2/CloudBlobTest"
{
    // v2 云着色（Stage 3）
    //
    // 明暗结构来自对参考图的实测（天顶散云，最干净的一张）：
    //   亮半 #E6DECC rgb(231,222,204)  hue 40  (暖)
    //   暗半 #A0A5AF rgb(160,166,175)  hue 217 (冷)
    //   -> 色相翻转约 177 度：不是渐变，是「色相切换」
    //   -> 亮端被压缩（p75=230 p95=240），暖区几乎是平的
    // 所以 ramp 做成：暖区（中→亮，几乎平） + 一个窄带陡降到冷色。
    //
    // 另外两项独立于法线光照：
    //   - 底部压暗：云底"看不到天"，参考里平底是明显冷色
    //   - 空气透视：按世界距离把云色推向天空色，地平线云带才读得出来
    Properties
    {
        _BrightColor ("亮（受光顶面）", Color)   = (0.930, 0.900, 0.830, 1)
        _MidColor    ("中（暖区下半）", Color)   = (0.860, 0.830, 0.760, 1)
        _DarkColor   ("暗（底/缝，冷）", Color)   = (0.600, 0.640, 0.700, 1)
        _CloudLightDir ("云光照方向（美术指定，与太阳解耦，指向光源）", Vector) = (-0.36, 0.90, 0.21, 0)
        _NightColor    ("夜晚染色（v1 同值，越暗夜越黑）", Color) = (0.12, 0.14, 0.22, 1)

        _ColdEdge    ("冷区起点（半兰伯特）", Range(0,1))     = 0.50
        _ColdSoft    ("冷区过渡宽度（越小越硬）", Range(0.001,0.4)) = 0.055
        _BrightEdge  ("亮部起点", Range(0,1))                = 0.72
        _BrightSoft  ("亮部过渡宽度", Range(0.001,0.6))       = 0.22

        _AOThreshold ("AO 硬台阶阈值", Range(0,1))   = 0.70
        _AOStrength  ("AO 缝内压暗", Range(0,1))     = 0.55
        _BottomShade ("底部压暗（用 n.y）", Range(0,1)) = 0.50
        _TopBoost    ("顶面提亮", Range(0,1))        = 0.30

        _HazeStart   ("空气透视起点（世界单位）", Float) = 350
        _HazeEnd     ("空气透视终点（世界单位）", Float) = 1100
        _HazeColor   ("空气透视色（天空色）", Color) = (0.62, 0.76, 0.86, 1)
        _HazeStrength("空气透视强度", Range(0,1))     = 0.85
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
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
                float3 normal : NORMAL;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos         : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos    : TEXCOORD1;
                float  ao          : TEXCOORD2;
            };

            float4 _BrightColor, _MidColor, _DarkColor, _CloudLightDir, _HazeColor, _NightColor;
            float  _ColdEdge, _ColdSoft, _BrightEdge, _BrightSoft;
            float  _AOThreshold, _AOStrength, _BottomShade, _TopBoost;
            float  _HazeStart, _HazeEnd, _HazeStrength;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.worldNormal = TransformObjectToWorldNormal(v.normal);
                o.worldPos = TransformObjectToWorld(v.vertex.xyz);
                o.ao = v.color.r;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 l = normalize(_CloudLightDir.xyz);   // 与太阳解耦，由美术指定

                float lam = saturate(dot(n, l) * 0.5 + 0.5);   // 半兰伯特

                // ---- ramp：暖区（几乎平） + 窄带陡降到冷 ----
                float warm = smoothstep(_ColdEdge - _ColdSoft, _ColdEdge + _ColdSoft, lam);
                float3 col = lerp(_DarkColor.rgb, _MidColor.rgb, warm);

                float bright = smoothstep(_BrightEdge - _BrightSoft, _BrightEdge + _BrightSoft, lam);
                col = lerp(col, _BrightColor.rgb, bright);

                // 顶面再提一点（参考里顶面是最亮的）
                float up = saturate(n.y);
                col = lerp(col, _BrightColor.rgb, up * _TopBoost * warm);

                // ---- 底部压暗：独立于法线的「看不到天」 ----
                float down = saturate(-n.y);
                col = lerp(col, _DarkColor.rgb, down * _BottomShade);

                // ---- 缝内 AO：硬台阶（风格化要台阶，不要连续灰度） ----
                float aoQ = i.ao >= _AOThreshold ? 1.0 : 0.0;
                col = lerp(col, _DarkColor.rgb, (1.0 - aoQ) * _AOStrength);

                // ---- 空气透视：按世界距离推向天空色 ----
                float dist = distance(i.worldPos, _WorldSpaceCameraPos);
                float haze = saturate((dist - _HazeStart) / max(1.0, _HazeEnd - _HazeStart));
                col = lerp(col, _HazeColor.rgb, haze * _HazeStrength);

                // ---- 随时段的强度调制（照 v1 CloudBillboard 的做法）----
                // 太阳在地平线下 -> 0（夜），升起来 -> 1（昼）；夜晚整朵乘 _NightColor
                Light mainLight = GetMainLight();
                float sunNightStep = smoothstep(-0.3, 0.25, mainLight.direction.y);
                col *= lerp(_NightColor.rgb, float3(1.0, 1.0, 1.0), sunNightStep);

                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
