Shader "SkyboxV2/CloudBlobTest"
{
    // P1a 专用：只用来评判"形状/轮廓"，所以刻意做得干净、可控。
    // 颜色直接取自参考图的三色分解：
    //   亮 #F8F3E1  rgb(0.973, 0.953, 0.882)
    //   中 #EBE3D2  rgb(0.922, 0.890, 0.824)
    //   暗 #B1BEC5  rgb(0.694, 0.745, 0.773)
    Properties
    {
        _BrightColor  ("Bright (sunlit top)", Color) = (0.973, 0.953, 0.882, 1)
        _MidColor     ("Mid", Color)                 = (0.922, 0.890, 0.824, 1)
        _DarkColor    ("Dark (base / crevice)", Color) = (0.694, 0.745, 0.773, 1)
        _LightDir     ("Light Direction", Vector)    = (-0.5, 0.75, 0.4, 0)
        _ShadowOffset ("Shadow Threshold", Range(0,1))  = 0.38
        _ShadowSoft   ("Shadow Softness", Range(0.001,0.5)) = 0.18
        _BrightStart  ("Bright Start", Range(0,1))   = 0.70
        _AOStrength   ("AO Strength", Range(0,1))    = 0.50
        _RimStrength  ("Rim Strength", Range(0,1))   = 0.15
        _RimColor     ("Rim Color", Color)           = (1, 0.93, 0.82, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off        // P1a 先不管绕序，靠顶点法线着色

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            float4 _BrightColor, _MidColor, _DarkColor, _LightDir, _RimColor;
            float  _ShadowOffset, _ShadowSoft, _BrightStart, _AOStrength, _RimStrength;

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
                float3 l = normalize(_LightDir.xyz);

                // 半兰伯特：过渡不生硬
                float lam = saturate(dot(n, l) * 0.5 + 0.5);

                // 三段 ramp：用几何光照决定基调（暗 / 中 / 亮）
                float shadow = 1.0 - smoothstep(_ShadowOffset - _ShadowSoft, _ShadowOffset + _ShadowSoft, lam);
                float bright = smoothstep(_BrightStart, 1.0, lam);

                float3 col = _MidColor.rgb;
                col = lerp(col, _DarkColor.rgb, shadow);
                col = lerp(col, _BrightColor.rgb, bright);

                // AO 单独作用：把瓣间缝隙与底部往暗色推（不乘进 lam，免得整朵发暗）
                col = lerp(col, _DarkColor.rgb, (1.0 - i.ao) * _AOStrength);

                // 边缘光（给一点逆光呼吸感）
                float3 viewDir = normalize(i.worldPos - _WorldSpaceCameraPos);
                float rim = pow(saturate(1.0 - saturate(dot(n, -viewDir))), 3.0);
                col += _RimColor.rgb * rim * _RimStrength;

                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
