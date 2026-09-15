using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把云原画转成云面片需要的“通道打包”贴图：
///   R = 暗部1   G = 暗部2   B = 高光   A = 羽化密度（0 背景 -> 1 云心，边缘软过渡，用于消散）
/// 说明：A 用“羽化密度”而不是二值 SDF，是为了避免把原画轮廓的毛刺直接切成 alpha。
/// 只给一张原画时按高度场自动推导明暗；也可手动指定 R/G/B。
/// </summary>
public class SkyboxCloudBaker : EditorWindow
{
    private Texture2D shapeTex;                 // 云原画（白=云；优先用 alpha，否则用亮度）
    private Texture2D dark1Tex;                 // 可选，留空自动推导
    private Texture2D dark2Tex;                 // 可选
    private Texture2D highlightTex;             // 可选

    private int size = 1024;
    private string outputPath = "Assets/Scene/Tex/CloudPacked.png";
    private string assignMaterialPath = "Assets/Scene/Mat/CloudBillboard.mat";
    private bool autoAssign = true;

    private float edgeFeather = 12f;            // 边缘羽化半径（平滑去毛刺/填小洞）
    private float heightBlur = 28f;             // 高度场模糊半径（影响明暗过渡）
    private float normalStrength = 8f;          // 法线强度（越大明暗对比越强）
    private Vector3 lightDir = new Vector3(-0.6f, 0.75f, 0.5f);
    private float highlightPower = 3.0f;

    // ===== 保留原画明暗（把画色烘进 R/G/B，而不是只留形状）=====
    private bool useArtShading = true;          // 用原画亮度驱动明暗通道
    private float artBgCut = 0.05f;             // 背景判定阈值（低于此亮度算背景）
    private float artBgSoft = 0.08f;            // 背景过渡宽度
    private float lumBlack = 0.30f;             // 原画里"最暗（描边）"对应的亮度
    private float lumWhite = 0.98f;             // 原画里"最亮（云顶）"对应的亮度

    private Vector2 scroll;

    [MenuItem("Tools/Skybox Clouds/Bake Channel-Packed Cloud")]
    public static void Open()
    {
        var w = GetWindow<SkyboxCloudBaker>("Cloud Baker");
        w.minSize = new Vector2(460, 560);
    }

    [MenuItem("Tools/Skybox Clouds/Bake From Selected Texture (Auto)")]
    public static void BakeFromSelection()
    {
        var sel = Selection.activeObject as Texture2D;
        if (sel == null)
        {
            Debug.LogWarning("[SkyboxCloudBaker] 请先在 Project 窗口选中一张云原画，再执行该菜单");
            return;
        }
        var w = CreateInstance<SkyboxCloudBaker>();
        w.shapeTex = sel;
        w.BakeFromShape();
        DestroyImmediate(w);
    }

    [MenuItem("Tools/Skybox Clouds/Generate Placeholder Cloud (Auto)")]
    public static void GeneratePlaceholderMenu()
    {
        var w = CreateInstance<SkyboxCloudBaker>();
        w.GeneratePlaceholder();
        if (w != null) DestroyImmediate(w);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("云原画 -> 通道打包", EditorStyles.boldLabel);
        shapeTex = (Texture2D)EditorGUILayout.ObjectField("云原画 (白=云)", shapeTex, typeof(Texture2D), false);
        EditorGUILayout.HelpBox(
            "只给一张原画时：按形状自动推导 暗部1/暗部2/高光，并生成 SDF 到 A 通道。\n" +
            "若你有分层的明暗图，可在下面覆盖。", MessageType.Info);

        EditorGUILayout.Space();
        dark1Tex = (Texture2D)EditorGUILayout.ObjectField("R 暗部1 (可选)", dark1Tex, typeof(Texture2D), false);
        dark2Tex = (Texture2D)EditorGUILayout.ObjectField("G 暗部2 (可选)", dark2Tex, typeof(Texture2D), false);
        highlightTex = (Texture2D)EditorGUILayout.ObjectField("B 高光 (可选)", highlightTex, typeof(Texture2D), false);

        EditorGUILayout.Space();
        outputPath = EditorGUILayout.TextField("输出路径", outputPath);
        size = EditorGUILayout.IntPopup("输出尺寸", size, new[] { "512", "1024", "2048" }, new[] { 512, 1024, 2048 });
        autoAssign = EditorGUILayout.Toggle("自动赋给材质", autoAssign);
        assignMaterialPath = EditorGUILayout.TextField("材质路径", assignMaterialPath);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("保留原画明暗", EditorStyles.boldLabel);
        useArtShading = EditorGUILayout.Toggle("用原画亮度做明暗", useArtShading);
        artBgCut = EditorGUILayout.Slider("背景阈值", artBgCut, 0f, 0.3f);
        artBgSoft = EditorGUILayout.Slider("背景过渡", artBgSoft, 0.01f, 0.3f);
        lumBlack = EditorGUILayout.Slider("最暗(描边)亮度", lumBlack, 0f, 0.6f);
        lumWhite = EditorGUILayout.Slider("最亮(云顶)亮度", lumWhite, 0.5f, 1f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("通道参数（高度场模式用）", EditorStyles.boldLabel);
        edgeFeather = EditorGUILayout.Slider("边缘羽化", edgeFeather, 0f, 40f);
        heightBlur = EditorGUILayout.Slider("高度模糊", heightBlur, 0f, 80f);
        normalStrength = EditorGUILayout.Slider("法线强度", normalStrength, 0.5f, 24f);
        lightDir = EditorGUILayout.Vector3Field("光照方向", lightDir);
        highlightPower = EditorGUILayout.Slider("高光幂", highlightPower, 0.5f, 8f);

        EditorGUILayout.Space();
        GUI.enabled = shapeTex != null;
        if (GUILayout.Button("生成打包贴图", GUILayout.Height(38)))
            BakeFromShape();
        GUI.enabled = true;

        if (GUILayout.Button("生成占位云 (不需要原画)", GUILayout.Height(30)))
            GeneratePlaceholder();

        EditorGUILayout.EndScrollView();
    }

    // ================= 从原画打包 =================
    private void BakeFromShape()
    {
        var src = LoadPixels(shapeTex, size);
        if (src == null) return;

        float[] mask, artLum;
        BuildArtMaskAndLum(src, out mask, out artLum);
        if (!useArtShading) artLum = null;

        float[] r = dark1Tex ? ResampleGray(dark1Tex) : null;
        float[] g = dark2Tex ? ResampleGray(dark2Tex) : null;
        float[] b = highlightTex ? ResampleGray(highlightTex) : null;

        BakeAndSave(mask, r, g, b, artLum);
    }

    // 形状 = 非背景区域（含描边）；明暗 = 原画亮度按 lumBlack..lumWhite 归一化
    private void BuildArtMaskAndLum(Color32[] src, out float[] mask, out float[] lum)
    {
        int n = src.Length;
        mask = new float[n];
        lum = new float[n];

        byte amin = 255, amax = 0;
        for (int i = 0; i < n; i++) { var c = src[i]; if (c.a < amin) amin = c.a; if (c.a > amax) amax = c.a; }
        bool hasAlpha = (amax - amin) > 16;

        float bgSoft = Mathf.Max(artBgSoft, 1e-3f);
        for (int i = 0; i < n; i++)
        {
            var c = src[i];
            float l = (c.r * 0.299f + c.g * 0.587f + c.b * 0.114f) / 255f;
            lum[i] = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lumBlack, lumWhite, l));

            float shape = hasAlpha ? c.a / 255f : l;
            mask[i] = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(artBgCut, artBgCut + bgSoft, shape));
        }
    }

    // ================= 占位云 =================
    private void GeneratePlaceholder()
    {
        int N = size;
        float[] mask = new float[N * N];

        // 域扭曲的 FBM，做出蓬松的云团
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                float u = (float)x / N;
                float v = (float)y / N;

                // 大尺度域扭曲
                float warpX = Fbm(u * 2.0f + 11.3f, v * 2.0f + 3.7f, 4, 2);
                float warpY = Fbm(u * 2.0f + 5.2f, v * 2.0f + 8.1f, 4, 2);

                // 主体形状：低频大云团
                float baseN = Fbm((u + (warpX - 0.5f) * 0.5f) * 3.0f,
                                  (v + (warpY - 0.5f) * 0.5f) * 3.0f, 4, 3);
                // 细节：高频碎絮（频率=周期，保证无缝平铺）
                float detail = Fbm(u * 9.0f, v * 9.0f, 3, 9);
                float n = baseN * 0.82f + detail * 0.18f;

                // 云团分布：制造大片空隙
                float cluster = Fbm(u * 2.0f + 30.1f, v * 2.0f + 17.7f, 2, 2);

                float cloud = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.72f, n));
                cloud *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.55f, cluster));
                mask[y * N + x] = cloud;
            }
        }

        BakeAndSave(mask, null, null, null, null);
    }

    // ================= 核心打包 =================
    private void BakeAndSave(float[] mask, float[] inR, float[] inG, float[] inB, float[] artLum)
    {
        int N = size;
        Color32[] outPx = new Color32[N * N];

        // 1) 羽化密度：A 通道不再做二值 SDF 抠边（那会把原画轮廓的毛刺直接切成 alpha）
        EditorUtility.DisplayProgressBar("Cloud Bake", "羽化形状...", 0.25f);
        float[] feather = GaussianBlur(mask, N, N, edgeFeather);

        // 2) 高度场（用于法线/明暗）
        EditorUtility.DisplayProgressBar("Cloud Bake", "生成明暗...", 0.45f);
        float[] height = GaussianBlur(mask, N, N, heightBlur);
        Vector3 L = lightDir.sqrMagnitude > 1e-6f ? lightDir.normalized : new Vector3(-0.6f, 0.75f, 0.5f);

        for (int y = 0; y < N; y++)
        {
            if ((y & 63) == 0)
                EditorUtility.DisplayProgressBar("Cloud Bake", "打包通道 " + y + "/" + N, 0.5f + 0.45f * y / N);

            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;

                // A = 羽化密度（0 背景 -> 1 云心，边缘软过渡）
                float density = Mathf.Clamp01(feather[i]);
                float a = density;

                float R, G, B;
                if (inR != null || inG != null || inB != null)
                {
                    R = inR != null ? inR[i] : density;
                    G = inG != null ? inG[i] : density;
                    B = inB != null ? inB[i] : density;
                }
                else if (artLum != null)
                {
                    // 原画明暗 -> 双色阶梯：中间调(R) / 深部+描边(G)，亮部 = 1-R-G
                    float l = Mathf.Clamp01(artLum[i]);
                    float s = 1f - l;
                    R = s * Mathf.Clamp01(l * 2f) * density;
                    G = s * Mathf.Clamp01(1f - l * 2f) * density;
                    B = l * l * density;
                }
                else
                {
                    // 从高度场推导法线 -> 明暗
                    int xm = Mathf.Max(x - 1, 0), xp = Mathf.Min(x + 1, N - 1);
                    int ym = Mathf.Max(y - 1, 0), yp = Mathf.Min(y + 1, N - 1);
                    float gx = (height[y * N + xp] - height[y * N + xm]) * normalStrength;
                    float gy = (height[yp * N + x] - height[ym * N + x]) * normalStrength;
                    Vector3 nrm = new Vector3(-gx, -gy, 1f).normalized;

                    // 半兰伯特 + 天光/环境遮蔽：云顶亮、云底和斜坡暗，做出体积感
                    float lit = Mathf.Clamp01(Vector3.Dot(nrm, L) * 0.5f + 0.5f);
                    float ao = Mathf.Clamp01(0.5f + nrm.y * 0.5f);
                    lit *= Mathf.Lerp(0.75f, 1f, ao);

                    float shadow = 1f - lit;
                    R = Mathf.Clamp01(shadow * 1.15f) * density;   // 暗部1
                    G = Mathf.Clamp01(shadow * shadow) * density;  // 暗部2
                    B = Mathf.Pow(lit, highlightPower) * density;  // 高光
                }

                outPx[i] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(R * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(G * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(B * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
            }
        }

        try
        {
            SavePNG(outPx, N, N, outputPath);
            ImportSettings(outputPath);
            if (autoAssign) AssignToMaterial(outputPath);
            AssetDatabase.Refresh();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Debug.Log("[SkyboxCloudBaker] 打包贴图已生成: " + outputPath);
    }

    // ================= mask 提取（关键） =================
    // 白雲+黑底、且无透明通道的原画：alpha 全是 1，必须走亮度，否则整图变全白。
    private static float[] BuildMaskFromPixels(Color32[] src)
    {
        int n = src.Length;
        float[] mask = new float[n];

        byte amin = 255, amax = 0;
        float lmin = 1f, lmax = 0f;
        for (int i = 0; i < n; i++)
        {
            var c = src[i];
            if (c.a < amin) amin = c.a;
            if (c.a > amax) amax = c.a;
            float lum = (c.r * 0.299f + c.g * 0.587f + c.b * 0.114f) / 255f;
            if (lum < lmin) lmin = lum;
            if (lum > lmax) lmax = lum;
        }

        // alpha 有明显变化（存在透明背景）时才用 alpha，否则用亮度
        bool hasAlpha = (amax - amin) > 16;

        for (int i = 0; i < n; i++)
        {
            var c = src[i];
            mask[i] = hasAlpha
                ? c.a / 255f
                : (c.r * 0.299f + c.g * 0.587f + c.b * 0.114f) / 255f;
        }

        // 自动拉伸对比度，低对比原画也能出形状
        float mn = hasAlpha ? amin / 255f : lmin;
        float mx = hasAlpha ? amax / 255f : lmax;
        float range = Mathf.Max(mx - mn, 1e-3f);
        for (int i = 0; i < n; i++)
            mask[i] = Mathf.Clamp01((mask[i] - mn) / range);

        return mask;
    }

    // ================= 材质赋值 =================
    private void AssignToMaterial(string texPath)
    {
        if (string.IsNullOrEmpty(assignMaterialPath)) return;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(assignMaterialPath);
        if (mat == null)
        {
            Debug.LogWarning("[SkyboxCloudBaker] 找不到材质: " + assignMaterialPath);
            return;
        }

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (mat.HasProperty("_CloudTex")) mat.SetTexture("_CloudTex", tex);

        // 占位噪声：优先用项目已有的 GalaxyNoiseTex
        if (mat.HasProperty("_CloudNoiseTex") && mat.GetTexture("_CloudNoiseTex") == null)
        {
            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Scene/Tex/GalaxyNoiseTex.png");
            if (noise != null) mat.SetTexture("_CloudNoiseTex", noise);
        }

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
    }

    // ================= 工具 =================
    private Color32[] LoadPixels(Texture2D tex, int target)
    {
        string path = AssetDatabase.GetAssetPath(tex);
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!t.LoadImage(File.ReadAllBytes(path)))
        {
            EditorUtility.DisplayDialog("错误", "读取原画失败", "OK");
            DestroyImmediate(t);
            return null;
        }

        Color32[] src = t.GetPixels32();
        int sw = t.width, sh = t.height;
        Color32[] dst = new Color32[target * target];
        for (int y = 0; y < target; y++)
        {
            int sy = Mathf.Clamp(y * sh / target, 0, sh - 1);
            for (int x = 0; x < target; x++)
            {
                int sx = Mathf.Clamp(x * sw / target, 0, sw - 1);
                dst[y * target + x] = src[sy * sw + sx];
            }
        }
        DestroyImmediate(t);
        return dst;
    }

    private float[] ResampleGray(Texture2D tex)
    {
        var px = LoadPixels(tex, size);
        if (px == null) return null;
        return BuildMaskFromPixels(px);
    }

    private static void SavePNG(Color32[] px, int W, int H, string path)
    {
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        DestroyImmediate(tex);
    }

    private static void ImportSettings(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = false;            // 数据贴图，不要 sRGB
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    // ================= FBM / 噪声 =================
    private static float Fbm(float x, float y, int octaves, int period)
    {
        float value = 0f, amp = 0.5f, freq = 1f, norm = 0f;
        int per = Mathf.Max(1, period);
        for (int i = 0; i < octaves; i++)
        {
            value += amp * ValueNoise(x * freq, y * freq, per);
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
            per *= 2;
        }
        return norm > 0f ? value / norm : 0f;
    }

    private static float ValueNoise(float x, float y, int period)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        float sx = fx * fx * (3f - 2f * fx);
        float sy = fy * fy * (3f - 2f * fy);

        float n00 = Hash(x0, y0, period);
        float n10 = Hash(x0 + 1, y0, period);
        float n01 = Hash(x0, y0 + 1, period);
        float n11 = Hash(x0 + 1, y0 + 1, period);

        float a = Mathf.Lerp(n00, n10, sx);
        float b = Mathf.Lerp(n01, n11, sx);
        return Mathf.Lerp(a, b, sy);
    }

    private static float Hash(int x, int y, int period)
    {
        x = ((x % period) + period) % period;
        y = ((y % period) + period) % period;
        unchecked
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            h = h ^ (h >> 16);
            return (h & 0x7fffffff) / (float)0x7fffffff;
        }
    }

    // ================= 高斯模糊 =================
    private static float[] GaussianBlur(float[] src, int W, int H, float radius)
    {
        if (radius <= 0.01f) return (float[])src.Clone();

        int r = Mathf.CeilToInt(radius * 2f);
        int sizeK = r * 2 + 1;
        float[] kernel = new float[sizeK];
        float sigma = radius;
        float sum = 0f;
        for (int i = 0; i < sizeK; i++)
        {
            int dx = i - r;
            kernel[i] = Mathf.Exp(-(dx * dx) / (2f * sigma * sigma));
            sum += kernel[i];
        }
        for (int i = 0; i < sizeK; i++) kernel[i] /= sum;

        float[] tmp = new float[W * H];
        float[] dst = new float[W * H];

        for (int y = 0; y < H; y++)
        {
            int row = y * W;
            for (int x = 0; x < W; x++)
            {
                float acc = 0f;
                for (int k = -r; k <= r; k++)
                {
                    int xx = Mathf.Clamp(x + k, 0, W - 1);
                    acc += src[row + xx] * kernel[k + r];
                }
                tmp[row + x] = acc;
            }
        }
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float acc = 0f;
                for (int k = -r; k <= r; k++)
                {
                    int yy = Mathf.Clamp(y + k, 0, H - 1);
                    acc += tmp[yy * W + x] * kernel[k + r];
                }
                dst[y * W + x] = acc;
            }
        }
        return dst;
    }
}
