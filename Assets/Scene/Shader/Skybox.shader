Shader "Unlit/Skybox"
{
    Properties
    {
        _MoonTex ("Moon Texture", 2D) = "white" {}
        _StarNoise3D ("Star Noise 3D", 3D) = "white" {}
        _StarNoise3D_ST ("Star Noise 3D ST", Vector) = (1, 1, 1)
        _StarDensity ("Star Density", Range(0, 10)) = 1.0
        _StarThreshold ("Star Threshold", Range(0, 1)) = 0.5
        _StarBrightness ("Star Brightness", Range(0, 10)) = 1.0
        _StarSize ("Star Size", Range(0, 0.1)) = 0.01
        _GalaxyTex ("Galaxy Texture", 2D) = "white" {}
        _GalaxyNoiseTex ("Galaxy Noise Texture", 2D) = "white" {}
        _GalaxyTex_ST ("Galaxy Texture ST", Vector) = (1, 1, 0, 0)
        _GalaxyNoiseTex_ST ("Galaxy Noise Texture ST", Vector) = (1, 1, 0, 0)
        _GalaxyColor ("Galaxy Color", Color) = (0.8, 0.2, 0.5, 1)
        _GalaxyColor1 ("Galaxy Color 1", Color) = (0.2, 0.8, 1.0, 1)
        _NoiseScale ("Noise Scale", Vector) = (1, 1, 0, 0)
        _NoiseSpeed ("Noise Speed", Vector) = (0.1, 0.05, 0, 0)
        _NoiseErosion ("Noise Erosion", Range(0, 2)) = 1.0
        _Distortion ("Distortion", Range(0, 0.2)) = 0.05
        _Brightness ("Brightness", Range(0, 5)) = 1.5
        _FlowSpeed ("Flow Speed", Range(0, 2)) = 0.2
        _SunRadius ("Sun Radius", Range(0.001, 0.5)) = 0.05
        _SunColor ("Sun Color", Color) = (1, 1, 1, 1)
        _SunEdgeSoft ("Sun Edge Soft", Range(0.05, 1.0)) = 0.35
        _SunGlowSize ("Sun Glow Size", Range(1.0, 6.0)) = 2.5
        _SunGlowStrength ("Sun Glow Strength", Range(0, 2)) = 0.6
        _MoonRadius ("Moon Radius", Range(0.001, 0.5)) = 0.03
        _MoonColor ("Moon Color", Color) = (0.8, 0.8, 0.9, 1)
        _MoonOffset ("Moon Offset", Range(0.0, 0.1)) = 0.02
        _MoonTex_ST ("Moon Texture ST", Vector) = (1, 1, 0, 0)
        _DayBottomColor ("Day Bottom Color", Color) = (0.5, 0.6, 0.7, 1)
        _DayMidColor ("Day Mid Color", Color) = (0.3, 0.5, 0.8, 1)
        _DayTopColor ("Day Top Color", Color) = (0.1, 0.2, 0.4, 1)
        _NightBottomColor ("Night Bottom Color", Color) = (0.05, 0.05, 0.1, 1)
        _NightTopColor ("Night Top Color", Color) = (0.01, 0.01, 0.03, 1)
        _DayHorWidth ("Day Horizon Width", Range(0.0, 0.5)) = 0.1
        _DayHorStrenth ("Day Horizon Strength", Range(0.0, 1.0)) = 0.5
        _DayHorColor ("Day Horizon Color", Color) = (1.0, 0.8, 0.5, 1)
        _NightHorWidth ("Night Horizon Width", Range(0.0, 0.5)) = 0.1
        _NightHorStrenth ("Night Horizon Strength", Range(0.0, 1.0)) = 0.3
        _NightHorColor ("Night Horizon Color", Color) = (0.2, 0.3, 0.5, 1)
        _StarFadeStart ("Star Fade Start Height", Range(-1.0, 1.0)) = -0.05
        _StarFadeEnd ("Star Fade End Height", Range(-1.0, 1.0)) = 0.15
        _GalaxyFadeStart ("Galaxy Fade Start Height", Range(-1.0, 1.0)) = -0.1
        _GalaxyFadeEnd ("Galaxy Fade End Height", Range(-1.0, 1.0)) = 0.1
        // ===== Mie 大气散射（日出日落）=====
        _MieColor ("Mie Color", Color) = (0.75, 0.82, 1.0, 1)
        _MieStrength ("Mie Strength", Range(0, 10)) = 2.0
        _MieG ("Mie Anisotropy (G)", Range(0, 0.99)) = 0.8
        // 0 = 不做色调映射（交给后处理，推荐，避免和后处理 ACES 叠两次）
        // 1 = 保留旧的 ACES 高光压缩（单独看天空时更柔和）
        _ScatterCompress ("Scatter Highlight Compress (0=none,1=ACES)", Range(0, 1)) = 0.0
        _MieExtinction ("Mie Extinction", Range(0, 3)) = 0.8
        _SunsetRange ("Sunset Range", Range(0.05, 1)) = 0.35
        _SunsetPower ("Sunset Power", Range(0.5, 8)) = 2.0
        _AirMassHeight ("Air Mass Height", Range(0.01, 1)) = 0.15
        _SunDirection ("Sun Direction", Vector) = (0, 1, 0, 0)

        // ===== 天空球云层（平面投影：uv = dir.xz / dir.y）=====
        // 近地平线投影被压缩 -> 云自动变密变小，"从下到上从多到少"免费获得
        _CloudTex ("Cloud Field (R=Density G=Height)", 2D) = "black" {}
        _CloudStrength ("Cloud Strength", Range(0,1)) = 1.0
        _CloudScale ("Cloud Scale", Float) = 2.0
        _CloudDetailScale ("Detail Scale", Float) = 5.0
        _CloudCoverageHorizon ("Coverage @ Horizon", Range(0,1)) = 0.40
        _CloudCoverageZenith ("Coverage @ Zenith", Range(0,1)) = 0.66
        _CloudSoftness ("Softness", Range(0.005,0.5)) = 0.16
        _CloudErode ("Detail Erode", Range(0,1)) = 0.45
        _CloudWindDir ("Wind Dir (xy)", Vector) = (1, 0.3, 0, 0)
        _CloudWindSpeed ("Wind Speed", Float) = 0.004
        _CloudBrightColor ("Cloud Bright", Color) = (0.973, 0.953, 0.882, 1)
        _CloudMidColor ("Cloud Mid", Color) = (0.922, 0.890, 0.824, 1)
        _CloudDarkColor ("Cloud Dark", Color) = (0.694, 0.745, 0.773, 1)
        _CloudNightColor ("Cloud Night", Color) = (0.12, 0.14, 0.22, 1)
        _CloudSunSide ("Sun Side Boost", Range(0,1)) = 0.35
        _CloudMinY ("Min dir.y (anti-alias)", Range(0.01,0.4)) = 0.10
        _CloudPerspective ("Perspective (0=贴天球, 1=平面投影)", Range(0,1)) = 0.45
        _CloudHazeStart ("Haze Start (dir.y)", Range(0,0.5)) = 0.01
        _CloudHazeEnd ("Haze End (dir.y)", Range(0,0.5)) = 0.16
        _CloudHazeColor ("Haze Color", Color) = (0.78, 0.85, 0.97, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background" }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float3 uv : TEXCOORD0;
                float fogCoord : TEXCOORD1;
                float4 vertex : SV_POSITION;
            };

            TEXTURE2D(_MoonTex);
            SAMPLER(sampler_MoonTex);
            float4 _MoonTex_ST;
            
            TEXTURE3D(_StarNoise3D);
            SAMPLER(sampler_StarNoise3D);
            float4 _StarNoise3D_ST;
            float _StarDensity;
            float _StarThreshold;
            float _StarBrightness;
            float _StarSize;
            
            TEXTURE2D(_GalaxyTex);
            SAMPLER(sampler_GalaxyTex);
            float4 _GalaxyTex_ST;
            
            TEXTURE2D(_GalaxyNoiseTex);
            SAMPLER(sampler_GalaxyNoiseTex);
            float4 _GalaxyNoiseTex_ST;
            
            float4 _GalaxyColor;
            float4 _GalaxyColor1;
            float4 _NoiseScale;
            float4 _NoiseSpeed;
            float _NoiseErosion;
            float _Distortion;
            float _Brightness;
            float _FlowSpeed;
            float _SunRadius;
            float4 _SunColor;
            float _SunEdgeSoft;
            float _SunGlowSize;
            float _SunGlowStrength;
            float _MoonRadius;
            float4 _MoonColor;
            float _MoonOffset;
            float4x4 _SunLocalToWorld;
            float4 _DayBottomColor;
            float4 _DayMidColor;
            float4 _DayTopColor;
            float4 _NightBottomColor;
            float4 _NightTopColor;
            float _DayHorWidth;
            float _DayHorStrenth;
            float4 _DayHorColor;
            float _NightHorWidth;
            float _NightHorStrenth;
            float4 _NightHorColor;
            float _StarFadeStart;
            float _StarFadeEnd;
            float _GalaxyFadeStart;
            float _GalaxyFadeEnd;
            float4 _SunDirection;
            float4 _MieColor;
            float _MieStrength;
            float _MieG;
            float _ScatterCompress;
            float _MieExtinction;
            float _SunsetRange;
            float _SunsetPower;
            float _AirMassHeight;

            // ---- 天空球云层 ----
            TEXTURE2D(_CloudTex);
            SAMPLER(sampler_CloudTex);
            float _CloudStrength;
            float _CloudScale;
            float _CloudDetailScale;
            float _CloudCoverageHorizon;
            float _CloudCoverageZenith;
            float _CloudSoftness;
            float _CloudErode;
            float4 _CloudWindDir;
            float _CloudWindSpeed;
            float4 _CloudBrightColor;
            float4 _CloudMidColor;
            float4 _CloudDarkColor;
            float4 _CloudNightColor;
            float _CloudSunSide;
            float _CloudMinY;
            float _CloudPerspective;
            float _CloudHazeStart;
            float _CloudHazeEnd;
            float4 _CloudHazeColor;

            // Henyey-Greenstein 相函数：描述 Mie 散射的方向性
            // g 越大散射越集中在前向 —— 这就是日出日落时太阳周围的 halo
            float MiePhaseFunction(float cosTheta, float g)
            {
                float g2 = g * g;
                float denom = 1.0 + g2 - 2.0 * g * cosTheta;
                return (1.0 - g2) / (4.0 * PI * pow(max(denom, 1e-4), 1.5));
            }

            // Narkowicz 2015 的 ACES 近似曲线
            // 用于压缩散射光的高光，比线性叠加更接近胶片的过渡
            float3 ACESFilm(float3 x)
            {
                const float a = 2.51;
                const float b = 0.03;
                const float c = 2.43;
                const float d = 0.59;
                const float e = 0.14;
                return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv = v.vertex.xyz;
                o.fogCoord = ComputeFogFactor(o.vertex.z);
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                // 获取主光源方向
                Light mainLight = GetMainLight();
                float3 lightDir = mainLight.direction;
                
                // 如果没有主光源，使用自定义的太阳方向
                if (length(lightDir) < 0.001)
                {
                    lightDir = normalize(_SunDirection.xyz);
                }

                // 天空方向：必须归一化。天空盒顶点是立方体，未归一化的坐标长度不恒定，
                // 高频的星空/银河采样会在立方体面边界露出竖向接缝
                float3 dir = normalize(i.uv.xyz);

                // ===== 太阳（软边圆盘 + 外辉光，融入 Mie 光晕）=====
                float sun = distance(i.uv.xyz, lightDir);
                float sunSoft = max(_SunEdgeSoft, 1e-3);
                float sunDisc = 1.0 - smoothstep(_SunRadius * (1.0 - sunSoft), _SunRadius * (1.0 + sunSoft), sun);
                float sunGlow = pow(saturate(1.0 - sun / (_SunRadius * max(_SunGlowSize, 1.0) + 1e-4)), 2.5);

                // ===== 月亮（清晰圆盘） =====
                float moon = distance(i.uv.xyz, -lightDir);
                float moonDisc = 1 - step(_MoonRadius, moon);

                // 新月效果
                float crescentMoon = distance(float3(i.uv.x + _MoonOffset, i.uv.yz), -lightDir);
                float crescentMoonDisc = 1 - step(_MoonRadius, crescentMoon);
                float newMoonDisc = saturate(moonDisc - crescentMoonDisc);

                // ===== 月亮纹理采样 =====
                float3 sunUV = mul(_SunLocalToWorld, float4(i.uv.xyz, 0.0)).xyz;
                float2 moonUV = sunUV.xy * _MoonTex_ST.xy + _MoonTex_ST.zw;
                float4 moonTex = SAMPLE_TEXTURE2D(_MoonTex, sampler_MoonTex, moonUV);
                float3 finalMoonColor = (_MoonColor.rgb * moonTex.rgb * moonTex.a) * newMoonDisc;

                // ===== 天空颜色 =====
                float sunNightStep = smoothstep(-0.3, 0.25, lightDir.y);
                
                // DAY NIGHT
                float3 gradientDay = lerp(_DayBottomColor.rgb, _DayMidColor.rgb, saturate(i.uv.y)) * step(0, -i.uv.y)
                                    + lerp(_DayMidColor.rgb, _DayTopColor.rgb, saturate(i.uv.y)) * step(0, i.uv.y);
                float verticalPos = saturate(i.uv.y * 0.5 + 0.5);
                float3 gradientNight = lerp(_NightBottomColor.rgb, _NightTopColor.rgb, verticalPos);
                float3 skyGradients = lerp(gradientNight, gradientDay, sunNightStep);

                // HORIZONTAL
                float horWidth = lerp(_NightHorWidth, _DayHorWidth, sunNightStep);
                float horStrenth = lerp(_NightHorStrenth, _DayHorStrenth, sunNightStep);
                float horLineMask = smoothstep(-horWidth, 0, i.uv.y) * smoothstep(-horWidth, 0, -i.uv.y);
                float3 horLineGradients = lerp(_NightHorColor.rgb, _DayHorColor.rgb, sunNightStep);

                // ===== 银河效果 =====
                float2 uv = dir.xz;

                // 1. 采样基础银河贴图 (决定基础位置和大致轮廓)
                float4 galaxyBase = SAMPLE_TEXTURE2D(_GalaxyTex, sampler_GalaxyTex, uv * _GalaxyTex_ST.xy + _GalaxyTex_ST.zw);
                
                // 2. 采样动态噪波 (决定最终细节形态和镂空)
                float2 noiseUV = uv * _GalaxyNoiseTex_ST.xy * _NoiseScale.xy + _Time.y * _NoiseSpeed.xy * _FlowSpeed;
                float noise = SAMPLE_TEXTURE2D(_GalaxyNoiseTex, sampler_GalaxyNoiseTex, noiseUV).r;

                // 3. 用噪波来"雕刻/塑形"银河贴图
                float maskErosion = smoothstep(0.2, 0.8, noise * _NoiseErosion);
                
                float shapedR = galaxyBase.r * maskErosion;
                float shapedG = galaxyBase.g * saturate(maskErosion * 1.2);

                // 4. 提取内外两层区域
                float outerMask = saturate(shapedR - shapedG);
                float innerMask = shapedG;

                // 5. 颜色合成与明暗控制
                float3 finalGalaxyColor = (outerMask * _GalaxyColor.rgb + innerMask * _GalaxyColor1.rgb) * _Brightness;
                
                // 银河的整体强度掩码
                float galaxyMask = saturate(shapedR * 2.0);

                // ===== 使用3D噪声纹理映射 =====
                // 直接使用3D空间坐标采样，不依赖UV
                float3 starSamplePos = dir * _StarNoise3D_ST.xyz * _StarDensity;
                
                // 可以添加轻微的视差效果
                starSamplePos += _Time.x * 0.01;
                
                // 采样3D噪声纹理
                float starNoise = SAMPLE_TEXTURE3D(_StarNoise3D, sampler_StarNoise3D, starSamplePos).r;
                
                // 使用smoothstep创建星星的清晰边缘
                float starPattern = smoothstep(_StarThreshold, _StarThreshold + _StarSize, starNoise);
                
                // 增强星星的亮度
                float starBrightness = starPattern * _StarBrightness;
                
                // 可以采样多个不同的3D纹理位置来创建更多层次的星星
                float3 starSamplePos2 = dir * _StarNoise3D_ST.xyz * _StarDensity * 2.0;
                float starNoise2 = SAMPLE_TEXTURE3D(_StarNoise3D, sampler_StarNoise3D, starSamplePos2).r;
                float starPattern2 = smoothstep(_StarThreshold * 0.8, _StarThreshold * 0.8 + _StarSize, starNoise2);
                float starBrightness2 = starPattern2 * _StarBrightness * 0.5;
                
                // 合并两个层次的星星
                float totalStarBrightness = saturate(starBrightness + starBrightness2);
                
                // ===== 根据太阳高度控制星星和银河的可见度 =====
                float sunHeight = lightDir.y;
                
                // 星星的淡出（使用更早的开始和更平滑的过渡）
                float starVisibility = 1.0 - smoothstep(_StarFadeStart, _StarFadeEnd, sunHeight);
                
                // 银河的淡出（比星星稍晚淡出，但最终也会完全消失）
                float galaxyVisibility = 1.0 - smoothstep(_GalaxyFadeStart, _GalaxyFadeEnd, sunHeight);
                
                // 可选：使用更平滑的曲线让过渡更自然
                // starVisibility = starVisibility * starVisibility * (3.0 - 2.0 * starVisibility); // smoothstep曲线
                // galaxyVisibility = galaxyVisibility * galaxyVisibility * (3.0 - 2.0 * galaxyVisibility);
                
                // 基础天空颜色
                float3 finalColor = skyGradients + horLineGradients * horLineMask * horStrenth;

                // 添加银河（使用独立的银河可见度控制）
                finalColor += finalGalaxyColor * galaxyVisibility * galaxyMask;

                // 添加星星（使用3D噪声结果和星星可见度）
                finalColor += totalStarBrightness * starVisibility;

                // ===== 太阳 / 月亮 / 大气散射 =====
                float3 viewDir = dir;
                float cosTheta = dot(viewDir, lightDir);

                // 1) Mie 相函数 -> 太阳周围的光晕（halo）
                float miePhase = MiePhaseFunction(cosTheta, _MieG);

                // 2) 大气质量：解析近似，替代 raymarching 的 D(PA) 积分
                //    视线越贴近地平线，穿过的大气越厚
                float airMass = exp(-abs(viewDir.y) / max(_AirMassHeight, 1e-3));

                // 3) 日落因子：太阳越贴近地平线，霞光越强
                float sunsetRaw = saturate(1.0 - abs(lightDir.y) / max(_SunsetRange, 1e-3));
                float sunsetMask = pow(sunsetRaw, _SunsetPower);

                // 4) 阳光染色：太阳越低，穿过的大气越厚，蓝光衰减越多 -> 越红
                //    用衰减系数近似，不做真正的 D(CP) 积分
                float sunAirMass = 1.0 / (max(lightDir.y, 0.0) + _AirMassHeight);
                float3 sunTint = exp(-sunAirMass * _MieExtinction * float3(0.25, 0.55, 1.0));

                // 5) 散射光：解析近似，替代 IntegrateInscattering 的 raymarching 循环
                //    霞光 = 地平线大气增厚，仅日落时出现
                //    光晕 = Mie 前向散射，全天都有
                float sunSide = saturate(cosTheta * 0.5 + 0.5);
                float3 glow = airMass * (0.35 + 0.65 * sunSide) * sunsetMask;
                float3 halo = miePhase * 0.5;
                float3 inscattering = glow + halo;

                // 6) 颜色控制：_MieColor * _MieStrength * inscattering
                //    _ScatterCompress 只在单独看天空时才建议开；开了后处理就保持 0，
                //    否则天空自己的 ACES 会和后处理的 ACES 叠两次，太阳周边比天空其它处压得更狠。
                float3 scatter = lerp(inscattering, ACESFilm(inscattering), saturate(_ScatterCompress));
                float3 scatteringColor = _MieColor.rgb * _MieStrength * scatter;

                // 合成：太阳圆盘（软边）+ 外辉光 + 月亮 + Mie 散射
                float3 sunCol = _SunColor.rgb * lerp(float3(1.0, 1.0, 1.0), sunTint, sunsetRaw);
                finalColor += sunDisc * sunCol;
                finalColor += sunGlow * sunCol * _SunGlowStrength;
                finalColor += finalMoonColor;
                finalColor += scatteringColor;

                // ===== 天空球云层（平面投影）=====
                // 把视线投影到虚拟的水平云平面：uv = dir.xz / dir.y
                // 近地平线 dir.y -> 0，uv 迅速变大 -> 投影压缩 -> 云自动变密变小
                {
                    float cy = max(dir.y, _CloudMinY);
                    // _CloudPerspective: 0 = 正交（云像贴在天球上，圆润、不拉伸，就是 cubemap 的观感）
                    //                    1 = 平面投影（透视强，但 cot(仰角) 会把云拉成横向长条）
                    // 因为有 _CloudMinY 兜底，半径天然有界，不会无限重复
                    float inv = lerp(1.0, 1.0 / cy, _CloudPerspective);
                    float2 cuv = dir.xz * inv * _CloudScale;
                    cuv += _CloudWindDir.xy * _CloudWindSpeed * _Time.y;

                    // 烘出来的云场：R = 密度(覆盖率)  G = 形体高度
                    float4 cls = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, cuv);

                    // 覆盖率随仰角变化：地平线密、天顶疏（对齐参考图实测）
                    float cov = lerp(_CloudCoverageHorizon, _CloudCoverageZenith, smoothstep(0.0, 0.80, dir.y));
                    float d = smoothstep(cov, cov + _CloudSoftness, cls.r);

                    // 伪高度直接用烘出来的形体（来自参考图的明暗），不再靠启发式猜
                    float h = saturate(cls.g);

                    float3 cc = lerp(_CloudDarkColor.rgb, _CloudMidColor.rgb, smoothstep(0.0, 0.42, h));
                    cc = lerp(cc, _CloudBrightColor.rgb, smoothstep(0.42, 1.0, h));

                    // 太阳侧受光 / 背阳侧压暗
                    float cs = saturate(dot(dir, lightDir));
                    cc = lerp(cc * (1.0 - _CloudSunSide * 0.5), cc, cs);

                    // 贴地平线融入天空色（顺带压掉投影拉伸的走样）
                    float chaze = 1.0 - smoothstep(_CloudHazeStart, _CloudHazeEnd, dir.y);
                    cc = lerp(cc, _CloudHazeColor.rgb, chaze);

                    // 昼夜
                    cc *= lerp(_CloudNightColor.rgb, float3(1.0, 1.0, 1.0), sunNightStep);

                    float cmask = d * _CloudStrength * smoothstep(_CloudHazeStart, _CloudHazeEnd, dir.y);
                    finalColor = lerp(finalColor, cc, saturate(cmask));
                }

                // 应用雾效
                finalColor = MixFog(finalColor, i.fogCoord);
                
                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}