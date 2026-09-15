using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 在一颗球面上随机排布若干“云面片”（billboard quad），每个面片从云图集里挑一朵云。
/// 所有分布参数都是公开常量，可在窗口里调；也可用 Auto 菜单一键生成。
/// 这样云是离散的、随机位置的，不会有天空盒平铺造成的对称和竖向接缝。
/// </summary>
public class CloudDomeGenerator : EditorWindow
{
    // ===== 分布常量（可调）=====
    public int cloudCount = 46;             // 云朵数量（均匀散布）
    public float radius = 900f;             // 球面半径（要大于场景尺寸、小于相机远裁剪面）
    public float elevationMinDeg = 5f;      // 最低仰角
    public float elevationMaxDeg = 32f;     // 最高仰角
    public float elevationShiftDeg = 0f;    // 云层整体抬高（单独调高/调低）
    public int seed = 20260911;             // 随机种子
    public float sizeMin = 120f;            // 云片最小高度
    public float sizeMax = 300f;            // 云片最大高度
    public float minSizeFloor = 200f;       // 最小尺寸阈值（低于此值抬到阈值，去碎片飞屑）
    public float maxCloudAngleDeg = 15f;    // 云最大半张角（限制超大面片，防“刀片”斜边）
    public float widthAspect = 1.6f;        // 宽高比（接近圆润的积云）
    public float maxRollDeg = 8f;           // 非巨型云最大倾斜角（巨型保持水平）
    public float mirrorChance = 0.5f;       // 水平镜像概率（增加造型变化）
    public int atlasCols = 2;               // 图集列数
    public int atlasRows = 4;               // 图集行数

    // ===== 尺寸层级（幂律 + 巨型）=====
    public float giantChance = 0.03f;       // 巨型云概率
    public float giantMul = 2.0f;           // 巨型倍率
    public float mediumChance = 0.10f;      // 中大型概率
    public float mediumMul = 1.4f;          // 中大型倍率
    public float farSquash = 0.8f;          // 远云高度额外压扁

    // ===== 深度分层 =====
    public bool depthTiers = true;          // true=量化到近/中/远三档；false=连续
    public bool elevationDrivesDepth = true; // 低仰角=远（天然近大远小+大气透视）
    public float depthJitter = 0.12f;       // 深度随机抖动
    public float nearSizeScale = 1.0f;      // 近云尺寸倍率
    public float farSizeScale = 0.75f;      // 远云尺寸倍率

    // ===== 分布模式 =====
    public bool useClusters = true;         // 成团分布 -> 一朵朵积云，而不是满天真
    public int clusterCount = 15;           // 簇数量（多→每团更少更圆）
    public float clusterSpreadDeg = 7f;     // 簇内散布角度（越小越聚）
    public float horizonBias = 1.3f;        // 仰角偏置：>1 越靠地平线（云带）
    public float sizePower = 1.3f;          // 尺寸偏置：>1 越偏大云

    // ===== 高度带：近大远小 =====
    public bool elevationSizeFalloff = true;    // 越低（越靠地平线）越小
    public float elevationSizeAtMin = 0.45f;    // 最低仰角处的尺寸倍率
    public float elevationSizeAtMax = 1.0f;     // 最高仰角处的尺寸倍率

    // ===== 多瓣（一朵云 = 几个面片叠成）=====
    public int puffCountMin = 2;
    public int puffCountMax = 4;
    public float puffSpread = 0.65f;
    public float puffVariance = 0.4f;

    // ===== 深度分层（视差壳）=====
    public bool useShells = true;
    public float shellRadiusNear = 380f;
    public float shellRadiusMid = 640f;
    public float shellRadiusFar = 900f;

    public string objectName = "SkyClouds";
    public string meshPath = "Assets/Scene/Models/CloudDome.asset";
    public string materialPath = "Assets/Scene/Mat/CloudBillboard.mat";
    public string atlasPath = "Assets/Scene/Tex/CloudPacked.png";
    public string layoutPath = "Assets/Scene/Models/CloudLayout.asset";   // 布局资产（可逐朵编辑）
    public int sortingOrder = 0;            // 透明排序层级（多层云时定前后，越小越靠后）
    public string coverageMaskPath = "Assets/Scene/Tex/SkyCoverageMask.png";  // 空间分布遮罩
    [Range(0f, 1f)] public float coverageMaskStrength = 1f;

    private Vector2 scroll;

    [MenuItem("Tools/Skybox Clouds/Cloud Dome Generator")]
    public static void Open()
    {
        var w = GetWindow<CloudDomeGenerator>("Cloud Dome");
        w.minSize = new Vector2(440, 560);
    }

    [MenuItem("Tools/Skybox Clouds/Generate Cloud Dome (Auto)")]
    public static void GenerateDefault()
    {
        var w = CreateInstance<CloudDomeGenerator>();
        w.Generate();
        DestroyImmediate(w);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("球面随机分布常量", EditorStyles.boldLabel);
        cloudCount = EditorGUILayout.IntSlider("云朵数量", cloudCount, 1, 400);
        radius = EditorGUILayout.FloatField("球面半径", radius);
        elevationMinDeg = EditorGUILayout.Slider("最低仰角(°)", elevationMinDeg, -10f, 89f);
        elevationMaxDeg = EditorGUILayout.Slider("最高仰角(°)", elevationMaxDeg, -10f, 89f);
        elevationShiftDeg = EditorGUILayout.Slider("整体抬高(°)", elevationShiftDeg, -20f, 50f);
        seed = EditorGUILayout.IntField("随机种子", seed);
        sizeMin = EditorGUILayout.FloatField("最小尺寸", sizeMin);
        sizeMax = EditorGUILayout.FloatField("最大尺寸", sizeMax);
        minSizeFloor = EditorGUILayout.FloatField("最小尺寸阈值", minSizeFloor);
        maxCloudAngleDeg = EditorGUILayout.Slider("最大半张角(°)", maxCloudAngleDeg, 5f, 40f);
        widthAspect = EditorGUILayout.Slider("宽高比", widthAspect, 0.5f, 4f);
        maxRollDeg = EditorGUILayout.Slider("非巨型倾斜(°)", maxRollDeg, 0f, 30f);
        mirrorChance = EditorGUILayout.Slider("镜像概率", mirrorChance, 0f, 1f);
        sizePower = EditorGUILayout.Slider("尺寸偏置(>1偏大)", sizePower, 0.3f, 4f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("尺寸层级（巨型云）", EditorStyles.boldLabel);
        giantChance = EditorGUILayout.Slider("巨型概率", giantChance, 0f, 0.3f);
        giantMul = EditorGUILayout.Slider("巨型倍率", giantMul, 1f, 4f);
        mediumChance = EditorGUILayout.Slider("中大型概率", mediumChance, 0f, 0.5f);
        mediumMul = EditorGUILayout.Slider("中大型倍率", mediumMul, 1f, 3f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("深度分层", EditorStyles.boldLabel);
        elevationDrivesDepth = EditorGUILayout.Toggle("低仰角=远（推荐）", elevationDrivesDepth);
        depthJitter = EditorGUILayout.Slider("深度抖动", depthJitter, 0f, 0.5f);
        depthTiers = EditorGUILayout.Toggle("量化近/中/远三档", depthTiers);
        nearSizeScale = EditorGUILayout.Slider("近云尺寸倍率", nearSizeScale, 0.3f, 2f);
        farSizeScale = EditorGUILayout.Slider("远云尺寸倍率", farSizeScale, 0.3f, 2f);
        farSquash = EditorGUILayout.Slider("远云压扁", farSquash, 0.3f, 1f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("成团分布（更整）", EditorStyles.boldLabel);
        useClusters = EditorGUILayout.Toggle("成团分布", useClusters);
        clusterCount = EditorGUILayout.IntSlider("簇数量", clusterCount, 1, 40);
        clusterSpreadDeg = EditorGUILayout.Slider("簇内散布(°)", clusterSpreadDeg, 1f, 30f);
        horizonBias = EditorGUILayout.Slider("地平线偏置(>1偏下)", horizonBias, 0.3f, 4f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("高度带：近大远小", EditorStyles.boldLabel);
        elevationSizeFalloff = EditorGUILayout.Toggle("尺寸随仰角衰减", elevationSizeFalloff);
        elevationSizeAtMin = EditorGUILayout.Slider("最低处倍率", elevationSizeAtMin, 0.1f, 1.5f);
        elevationSizeAtMax = EditorGUILayout.Slider("最高处倍率", elevationSizeAtMax, 0.1f, 2f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("多瓣（一朵云 = 几瓣叠成）", EditorStyles.boldLabel);
        puffCountMin = EditorGUILayout.IntSlider("最少瓣数", puffCountMin, 1, 9);
        puffCountMax = EditorGUILayout.IntSlider("最多瓣数", puffCountMax, 1, 9);
        puffSpread = EditorGUILayout.Slider("横向铺开", puffSpread, 0.1f, 1.5f);
        puffVariance = EditorGUILayout.Slider("高度起伏", puffVariance, 0f, 1f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("深度分层（视差壳）", EditorStyles.boldLabel);
        useShells = EditorGUILayout.Toggle("启用分层", useShells);
        shellRadiusNear = EditorGUILayout.FloatField("近壳半径", shellRadiusNear);
        shellRadiusMid = EditorGUILayout.FloatField("中壳半径", shellRadiusMid);
        shellRadiusFar = EditorGUILayout.FloatField("远壳半径", shellRadiusFar);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("图集", EditorStyles.boldLabel);
        atlasCols = EditorGUILayout.IntSlider("列数", atlasCols, 1, 8);
        atlasRows = EditorGUILayout.IntSlider("行数", atlasRows, 1, 8);
        atlasPath = EditorGUILayout.TextField("云图集", atlasPath);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("输出", EditorStyles.boldLabel);
        objectName = EditorGUILayout.TextField("物体名", objectName);
        meshPath = EditorGUILayout.TextField("Mesh 路径", meshPath);
        materialPath = EditorGUILayout.TextField("材质路径", materialPath);
        layoutPath = EditorGUILayout.TextField("布局资产路径", layoutPath);
        sortingOrder = EditorGUILayout.IntField("排序层级(越小越靠后)", sortingOrder);

        EditorGUILayout.Space();
        if (GUILayout.Button("生成 / 重建云穹顶", GUILayout.Height(40)))
            Generate();

        if (GUILayout.Button("选中布局资产（逐朵改大小）", GUILayout.Height(26)))
        {
            var la = AssetDatabase.LoadAssetAtPath<CloudLayout>(layoutPath);
            if (la != null) { Selection.activeObject = la; EditorGUIUtility.PingObject(la); }
            else Debug.LogWarning("[CloudDomeGenerator] 还没生成过布局资产，请先点上面的生成");
        }

        EditorGUILayout.HelpBox(
            "每朵云由 3~5 个面片（瓣）叠成一团，底边对齐同一条平底基线，UV0.zw 指定每瓣用图集里的哪朵云。\n" +
            "depth01 决定它落在近/中/远哪个壳半径上（纵深）。\n" +
            "生成后会把每朵云存进“布局资产”（CloudLayout），打开它即可逐朵改 size/位置/瓣数，再点“重建网格”。", MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    public void Generate()
    {
        var rand = new System.Random(seed);
        // 先生成"布局"（每朵云一条），再交给 CloudMeshBuilder 建网格
        var layout = ScriptableObject.CreateInstance<CloudLayout>();
        layout.radius = radius;
        layout.maxCloudAngleDeg = maxCloudAngleDeg;
        layout.atlasCols = atlasCols;
        layout.atlasRows = atlasRows;
        layout.atlasPath = atlasPath;
        layout.objectName = objectName;
        layout.meshPath = meshPath;
        layout.materialPath = materialPath;
        layout.useShells = useShells;
        layout.shellRadiusNear = shellRadiusNear;
        layout.shellRadiusMid = shellRadiusMid;
        layout.shellRadiusFar = shellRadiusFar;
        layout.sortingOrder = sortingOrder;
        layout.coverageMaskPath = coverageMaskPath;
        layout.coverageMaskStrength = coverageMaskStrength;

        int pMin = Mathf.Clamp(puffCountMin, 1, 9);
        int pMax = Mathf.Clamp(Mathf.Max(puffCountMin, puffCountMax), 1, 9);

        float elevMin = Mathf.Clamp(Mathf.Min(elevationMinDeg, elevationMaxDeg) + elevationShiftDeg, 0f, 85f);
        float elevMax = Mathf.Clamp(Mathf.Max(elevationMinDeg, elevationMaxDeg) + elevationShiftDeg, 0f, 85f);

        // ---- 预生成簇中心（成团分布）----
        int clusters = Mathf.Max(1, clusterCount);
        Vector3[] seeds = new Vector3[clusters];
        for (int c = 0; c < clusters; c++)
        {
            float e = Mathf.Deg2Rad * Mathf.Lerp(elevMin, elevMax,
                      Mathf.Pow((float)rand.NextDouble(), Mathf.Max(0.01f, horizonBias)));
            float a = (float)rand.NextDouble() * Mathf.PI * 2f;
            seeds[c] = new Vector3(
                Mathf.Cos(e) * Mathf.Cos(a),
                Mathf.Sin(e),
                Mathf.Cos(e) * Mathf.Sin(a)).normalized;
        }

        for (int i = 0; i < cloudCount; i++)
        {
            Vector3 dir;
            if (useClusters)
            {
                // 在某个簇中心附近散布 -> 云成团，不再满天真
                Vector3 s = seeds[rand.Next(0, clusters)];
                Vector3 tr = Vector3.Cross(Vector3.up, s);
                if (tr.sqrMagnitude < 1e-4f) tr = Vector3.right;
                tr.Normalize();
                Vector3 tu = Vector3.Cross(s, tr).normalized;

                float ang = Mathf.Deg2Rad * clusterSpreadDeg * Gaussian(rand);
                float phi = (float)rand.NextDouble() * Mathf.PI * 2f;
                dir = (s + (tr * Mathf.Cos(phi) + tu * Mathf.Sin(phi)) * Mathf.Tan(Mathf.Clamp(ang, -1.4f, 1.4f))).normalized;
            }
            else
            {
                float elev = Mathf.Deg2Rad * Mathf.Lerp(elevMin, elevMax, (float)rand.NextDouble());
                float az = (float)rand.NextDouble() * Mathf.PI * 2f;
                dir = new Vector3(
                    Mathf.Cos(elev) * Mathf.Cos(az),
                    Mathf.Sin(elev),
                    Mathf.Cos(elev) * Mathf.Sin(az)).normalized;
            }

            // 别掉到地平线以下
            if (dir.y < 0.02f)
            {
                dir.y = 0.02f;
                dir.Normalize();
            }

            // ---- 深度分层：0=近 1=远 ----
            float elevDeg = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
            float depth01;
            if (elevationDrivesDepth)
            {
                // 低仰角 = 远：越靠近地平线的云越远、越小、越淡（天然的近大远小 + 大气透视）
                float elevT = Mathf.InverseLerp(elevMin, elevMax, elevDeg);
                depth01 = Mathf.Lerp(0.88f, 0.12f, elevT);
                depth01 = Mathf.Clamp01(depth01 + ((float)rand.NextDouble() - 0.5f) * depthJitter);
                if (depthTiers)
                    depth01 = depth01 < 0.34f ? 0.15f : (depth01 < 0.67f ? 0.5f : 0.85f);
            }
            else if (depthTiers)
            {
                double tr = rand.NextDouble();
                depth01 = tr < 0.3 ? 0.15f : (tr < 0.7 ? 0.5f : 0.85f);
            }
            else depth01 = (float)rand.NextDouble();

            // ---- 尺寸：幂律 + 巨型/中大型，并按深度衰减 ----
            float baseSize = Mathf.Lerp(sizeMin, sizeMax, Mathf.Pow((float)rand.NextDouble(), sizePower));
            double gr = rand.NextDouble();
            bool isGiant = gr < giantChance;
            float sizeMul = isGiant ? giantMul : (gr < giantChance + mediumChance ? mediumMul : 1f);
            float depthSize = Mathf.Lerp(nearSizeScale, farSizeScale, depth01);
            float h0 = baseSize * sizeMul * depthSize;
            h0 = Mathf.Max(h0, minSizeFloor);                    // 最小尺寸阈值，去小碎片

            // 近大远小：越低（越靠地平线）越小
            if (elevationSizeFalloff)
            {
                float en = Mathf.InverseLerp(elevMin, elevMax, elevDeg);
                h0 *= Mathf.Lerp(elevationSizeAtMin, elevationSizeAtMax, en);
            }

            float h = h0 * Mathf.Lerp(1f, farSquash, depth01);   // 远云压扁
            float w = h0 * widthAspect;
            float rollDeg = isGiant ? 0f : ((float)rand.NextDouble() * 2f - 1f) * maxRollDeg;

            int cx = rand.Next(0, Mathf.Max(1, atlasCols));
            int cy = rand.Next(0, Mathf.Max(1, atlasRows));
            float seedVal = (float)rand.NextDouble();
            float brightHash = (float)rand.NextDouble();
            bool mirror = rand.NextDouble() < mirrorChance;

            layout.clouds.Add(new CloudEntry
            {
                enabled = true,
                elevationDeg = elevDeg,
                azimuthDeg = Mathf.Repeat(Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg, 360f),
                size = h,
                widthAspect = h > 1e-3f ? w / h : widthAspect,
                rollDeg = rollDeg,
                mirror = mirror,
                cellX = cx,
                cellY = cy,
                depth01 = depth01,
                brightHash = brightHash,
                dissolveSeed = seedVal,
                puffCount = rand.Next(pMin, pMax + 1),
                puffSpread = puffSpread,
                puffVariance = puffVariance,
            });
        }

        // ---- 保存/更新布局资产，再由布局构建网格 ----
        var existing = AssetDatabase.LoadAssetAtPath<CloudLayout>(layoutPath);
        if (existing == null)
        {
            string dir = System.IO.Path.GetDirectoryName(layoutPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            AssetDatabase.CreateAsset(layout, layoutPath);
        }
        else
        {
            EditorUtility.CopySerialized(layout, existing);
            EditorUtility.SetDirty(existing);
            DestroyImmediate(layout);
        }

        var saved = AssetDatabase.LoadAssetAtPath<CloudLayout>(layoutPath);
        var builtMesh = CloudMeshBuilder.Build(saved);

        EditorUtility.SetDirty(saved);
        AssetDatabase.SaveAssets();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        Debug.Log($"[CloudDomeGenerator] 生成 {saved.clouds.Count} 朵云 -> 网格 {builtMesh.vertexCount / 4} 面片；布局已存到 {layoutPath}");
    }

    // Box-Muller 标准正态分布（簇内散布用）
    private static float Gaussian(System.Random r)
    {
        double u1 = 1.0 - r.NextDouble();
        double u2 = 1.0 - r.NextDouble();
        return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Sin(2.0 * System.Math.PI * u2));
    }
}
