using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// v2 天空 blockout 装配器（Step 2）
///
/// 选型：平面云层（不是穹顶壳）。理由见讨论：
///   - 近大远小、地平线密/天顶疏、平底 都是平面层的自然结果
///   - 相机平移时云之间有真视差（穹顶壳钉在相机上 = 零视差）
///   - 远景可退化为 impostor（天然 LOD 分界）
///
/// 三层（覆盖由密到疏，对应"从下到上从多到少"）：
///   LowBand   高度 120  半径 260~900   仰角约 7~24 度   最密
///   MidLayer  高度 240  半径 140~650   仰角约 20~60 度   中
///   HighSparse高度 400  半径 0~380     仰角约 46~90 度   最疏
///
/// 本阶段只对"分布"，不管单朵形状与着色。
/// </summary>
public static class CloudSkyBuilder_V2
{
    public const string RootName = "SkyClouds_V2_Blockout";
    public const string VariantFolder = "Assets/Scene/Models/Blockout";
    public const string MatPath = "Assets/Scene/Mat/CloudBlobTest_V2.mat";

    public class LayerParams
    {
        public string name = "Layer";
        public float altitude = 120f;
        public float innerRadius = 260f;
        public float outerRadius = 900f;
        public int count = 60;
        public float sizeMin = 45f;
        public float sizeMax = 105f;
        [Tooltip("水平相对高度的拉伸范围（宽扁/高大）")]
        public float flatMin = 0.8f;
        public float flatMax = 1.5f;
        [Tooltip("覆盖率场阈值：噪声 > 该值的位置不放云")]
        public float coverage = 0.68f;
        public float coverageScale = 0.0035f;
        public int seed = 1;
    }

    public class SkyParams
    {
        public int variantCount = 4;
        public int variantSeedBase = 20260915;
        public int variantResolution = 48;
        [Tooltip("同层内两朵云的最小间距（相对 sizeMax）")]
        public float minSpacing = 1.15f;
        public List<LayerParams> layers = new List<LayerParams>();
    }

    public static SkyParams DefaultParams()
    {
        var p = new SkyParams();
        p.layers.Add(new LayerParams { name = "LowBand",    altitude = 140f, innerRadius = 300f, outerRadius = 900f, count = 48, sizeMin = 22f, sizeMax = 55f,  flatMin = 0.85f, flatMax = 1.55f, coverage = 0.56f, coverageScale = 0.0042f, seed = 101 });
        p.layers.Add(new LayerParams { name = "MidLayer",   altitude = 280f, innerRadius = 150f, outerRadius = 700f, count = 28, sizeMin = 40f, sizeMax = 90f,  flatMin = 0.75f, flatMax = 1.40f, coverage = 0.40f, coverageScale = 0.0060f, seed = 202 });
        p.layers.Add(new LayerParams { name = "HighSparse", altitude = 460f, innerRadius = 0f,   outerRadius = 420f, count = 10, sizeMin = 65f, sizeMax = 150f, flatMin = 0.70f, flatMax = 1.20f, coverage = 0.25f, coverageScale = 0.0090f, seed = 303 });
        return p;
    }

    [MenuItem("Tools/Skybox Clouds V2/Build Sky Blockout", false, 20)]
    public static void BuildDefault()
    {
        Build(DefaultParams());
    }

    [MenuItem("Tools/Skybox Clouds V2/Clear Sky Blockout", false, 21)]
    public static void Clear()
    {
        var old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        SceneView.RepaintAll();
        Debug.Log("[CloudSkyBuilder_V2] 已清除 blockout");
    }

    public static void Build(SkyParams p)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            Debug.LogError("[CloudSkyBuilder_V2] 找不到材质 " + MatPath);
            return;
        }

        var variants = BuildVariants(p);
        if (variants.Length == 0)
        {
            Debug.LogError("[CloudSkyBuilder_V2] 变体生成失败");
            return;
        }

        var old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
        var root = new GameObject(RootName);

        int total = 0;
        var report = "";

        foreach (var L in p.layers)
        {
            var rand = new System.Random(L.seed);
            var placed = new List<Vector2>();
            int made = 0, attempts = 0;
            int maxAttempts = Mathf.Max(200, L.count * 40);
            float minD = L.sizeMax * p.minSpacing;

            while (made < L.count && attempts < maxAttempts)
            {
                attempts++;

                float u = (float)rand.NextDouble();
                float r = Mathf.Sqrt(Mathf.Lerp(L.innerRadius * L.innerRadius, L.outerRadius * L.outerRadius, u));
                float ang = (float)rand.NextDouble() * Mathf.PI * 2f;
                float x = Mathf.Cos(ang) * r;
                float z = Mathf.Sin(ang) * r;

                // 覆盖率场：噪声高的地方留空 -> 成簇 + 缝隙，不是均匀铺
                float cov = ValueNoise2(x * L.coverageScale, z * L.coverageScale, L.seed);
                if (cov > L.coverage) continue;

                bool tooClose = false;
                for (int k = 0; k < placed.Count; k++)
                {
                    if ((placed[k] - new Vector2(x, z)).sqrMagnitude < minD * minD) { tooClose = true; break; }
                }
                if (tooClose) continue;

                placed.Add(new Vector2(x, z));

                float size = Mathf.Lerp(L.sizeMin, L.sizeMax, (float)rand.NextDouble());
                float fx = Mathf.Lerp(L.flatMin, L.flatMax, (float)rand.NextDouble());
                float fz = Mathf.Lerp(L.flatMin, L.flatMax, (float)rand.NextDouble());
                float yaw = (float)rand.NextDouble() * 360f;

                var go = new GameObject(L.name + "_" + made.ToString("D3"));
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(x, L.altitude, z);
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                go.transform.localScale = new Vector3(size * fx, size, size * fz);

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = variants[rand.Next(variants.Length)];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                made++; total++;
            }

            report += "  " + L.name.PadRight(11) + " 高度 " + L.altitude.ToString("F0").PadLeft(4)
                    + "  半径 " + L.innerRadius.ToString("F0") + "~" + L.outerRadius.ToString("F0")
                    + "  目标 " + L.count + "  实际 " + made + "\n";
        }

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        SceneView.RepaintAll();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        EditorApplication.QueuePlayerLoopUpdate();

        Debug.Log("[CloudSkyBuilder_V2] blockout 完成，共 " + total + " 朵（变体 " + variants.Length + " 个）\n" + report);
    }

    // ============================================================

    private static Mesh[] BuildVariants(SkyParams p)
    {
        if (!AssetDatabase.IsValidFolder(VariantFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scene/Models"))
                AssetDatabase.CreateFolder("Assets/Scene", "Models");
            AssetDatabase.CreateFolder("Assets/Scene/Models", "Blockout");
        }

        var list = new List<Mesh>();
        for (int i = 0; i < p.variantCount; i++)
        {
            var bp = new CloudBlobBuilder_V2.Params();
            bp.seed = p.variantSeedBase + i * 137;
            bp.resolution = p.variantResolution;
            bp.targetHeight = 1f;   // 单位高度，世界尺寸靠 transform.localScale

            var r = CloudBlobBuilder_V2.Build(bp);
            if (r.mesh == null) continue;

            string path = VariantFolder + "/CloudVariant_" + i.ToString("D2") + ".asset";
            // 已知坑：Mesh 不能 CopySerialized 就地更新，必须删旧建新
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null)
                AssetDatabase.DeleteAsset(path);

            r.mesh.name = "CloudVariant_" + i.ToString("D2");
            AssetDatabase.CreateAsset(r.mesh, path);
            list.Add(r.mesh);
        }
        AssetDatabase.SaveAssets();
        return list.ToArray();
    }

    private static float Hash2(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1274126177;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x7fffffff) / (float)0x7fffffff;
        }
    }

    private static float ValueNoise2(float x, float y, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float xf = x - xi, yf = y - yi;
        float u = xf * xf * (3f - 2f * xf);
        float v = yf * yf * (3f - 2f * yf);
        float a = Hash2(xi, yi, seed), b = Hash2(xi + 1, yi, seed);
        float c = Hash2(xi, yi + 1, seed), d = Hash2(xi + 1, yi + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }
}
