using UnityEditor;
using UnityEngine;

/// <summary>
/// P1a 调参台：拖滑杆 -> 重建单朵云 -> 直接看轮廓。
///
/// 对照目标（从参考图量化得到）：
///   主参考图  宽高比 1.84  填充率 69.3%
///   宽扁      宽高比 2.26  填充率 66.4%
///   高大      宽高比 1.11  填充率 62.4%
/// </summary>
public class CloudBlobLab_V2 : EditorWindow
{
    private const string ObjectName = "CloudBlobTest_V2";
    private const string MatPath = "Assets/Scene/Mat/CloudBlobTest_V2.mat";
    private const string MeshPath = "Assets/Scene/Models/CloudBlobTest_V2.asset";
    private static readonly Vector3 SpawnPos = new Vector3(0f, 18f, 0f);

    private readonly CloudBlobBuilder_V2.Params _p = new CloudBlobBuilder_V2.Params();
    private CloudBlobBuilder_V2.Result _last;
    private Vector2 _scroll;

    [MenuItem("Tools/Skybox Clouds V2/Blob Cloud Lab (P1a)", false, 10)]
    public static void Open()
    {
        var w = GetWindow<CloudBlobLab_V2>("Blob Cloud Lab");
        w.minSize = new Vector2(470, 640);
    }

    /// <summary>不开窗直接按默认参数重建（方便脚本/菜单调用）。</summary>
    [MenuItem("Tools/Skybox Clouds V2/Rebuild Test Cloud (P1a)", false, 11)]
    public static void RebuildDefault()
    {
        var w = CreateInstance<CloudBlobLab_V2>();
        w.Rebuild();
        Object.DestroyImmediate(w);
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("单朵云 · 形状调参（P1a）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "目标：做出参考图那种「多瓣 + 平底 + 宽扁」的积云轮廓。\n" +
            "改完点重建，云会出现在世界坐标 (0, 18, 0)。",
            MessageType.Info);

        _p.seed = EditorGUILayout.IntField("随机种子", _p.seed);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("种子 +1 重建")) { _p.seed++; Rebuild(); }
            if (GUILayout.Button("换一批（+7）")) { _p.seed += 7; Rebuild(); }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("球体布局", EditorStyles.boldLabel);
        _p.blobCount = EditorGUILayout.IntSlider("球数（=瓣数）", _p.blobCount, 3, 24);
        _p.radiusMin = EditorGUILayout.Slider("最小球半径", _p.radiusMin, 0.1f, 1.0f);
        _p.radiusMax = EditorGUILayout.Slider("最大球半径", _p.radiusMax, 0.1f, 1.2f);
        _p.spread = EditorGUILayout.Slider("穹顶半径", _p.spread, 0.2f, 2.0f);
        _p.depthRatio = EditorGUILayout.Slider("纵深压缩 Z/X", _p.depthRatio, 0.3f, 1.2f);
        _p.heightBias = EditorGUILayout.Slider("穹顶高度", _p.heightBias, 0.1f, 1.4f);
        _p.blobInset = EditorGUILayout.Slider("球心内收（越小鼓包越明显）", _p.blobInset, 0f, 0.6f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("场的形状", EditorStyles.boldLabel);
        _p.smoothK = EditorGUILayout.Slider("融合度 smin k", _p.smoothK, 0.01f, 0.8f);
        _p.basePlane = EditorGUILayout.Slider("平底高度", _p.basePlane, -0.4f, 0.6f);
        _p.resolution = EditorGUILayout.IntSlider("体素分辨率", _p.resolution, 12, 80);
        _p.targetHeight = EditorGUILayout.FloatField("输出高度（世界单位）", _p.targetHeight);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("AO", EditorStyles.boldLabel);
        _p.aoInset = EditorGUILayout.Slider("AO 探测深度", _p.aoInset, 0f, 0.8f);
        _p.aoScale = EditorGUILayout.Slider("AO 强度刻度", _p.aoScale, 1f, 6f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("噪声位移（P1b 菜花）", EditorStyles.boldLabel);
        _p.noiseAmp = EditorGUILayout.Slider("位移幅度（0=关）", _p.noiseAmp, 0f, 0.25f);
        _p.noiseFreq = EditorGUILayout.Slider("噪声频率", _p.noiseFreq, 0.5f, 12f);
        _p.noiseOctaves = EditorGUILayout.IntSlider("噪声层数", _p.noiseOctaves, 1, 5);
        _p.noiseVertical = EditorGUILayout.Slider("额外 Y 位移", _p.noiseVertical, 0f, 1.5f);
        _p.noiseBaseFade = EditorGUILayout.Slider("平底衰减高度", _p.noiseBaseFade, 0f, 0.6f);

        EditorGUILayout.Space();
        if (GUILayout.Button("生成 / 重建", GUILayout.Height(38))) Rebuild();

        // ---------- 结果 ----------
        if (_last != null && _last.mesh != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("上次生成", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"  顶点 {_last.vertexCount}   三角 {_last.triangleCount}   耗时 {_last.ms:F1} ms");
            EditorGUILayout.LabelField($"  体素网格 {_last.gridX}×{_last.gridY}×{_last.gridZ}   球数 {_last.blobCount}");
            EditorGUILayout.LabelField($"  尺寸 {_last.size.x:F2} × {_last.size.y:F2} × {_last.size.z:F2}");
            EditorGUILayout.LabelField($"  侧视宽高比 {_last.aspectXY:F2}   投影填充率 {_last.fillXY:F1}%");
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("参考目标", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("  主参考图   宽高比 1.84   填充率 69.3%");
        EditorGUILayout.LabelField("  宽扁       宽高比 2.26   填充率 66.4%");
        EditorGUILayout.LabelField("  高大       宽高比 1.11   填充率 62.4%");

        EditorGUILayout.EndScrollView();
    }

    private void Rebuild()
    {
        var r = CloudBlobBuilder_V2.Build(_p);
        if (r.mesh == null)
        {
            Debug.LogWarning("[CloudBlobLab_V2] 生成为空，检查参数（球数/半径/裁切面）");
            return;
        }
        Persist(r);
        _last = r;

        AssetDatabase.SaveAssets();
        SceneView.RepaintAll();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        EditorApplication.QueuePlayerLoopUpdate();

        Debug.Log($"[CloudBlobLab_V2] seed={_p.seed} 球={r.blobCount} -> 顶点 {r.vertexCount} / 三角 {r.triangleCount}" +
                  $"  耗时 {r.ms:F1}ms  网格 {r.gridX}x{r.gridY}x{r.gridZ}\n" +
                  $"  尺寸 {r.size.x:F2}x{r.size.y:F2}x{r.size.z:F2}" +
                  $"  宽高比 {r.aspectXY:F2}  填充率 {r.fillXY:F1}%   (目标 1.84/69.3% 或 2.26/66.4% 或 1.11/62.4%)");
    }

    /// <summary>
    /// 把生成结果落盘并挂到测试物体上。
    ///
    /// 注意：Mesh 不能用 EditorUtility.CopySerialized 就地更新 ——
    /// 它不会刷新 GPU 顶点缓冲，渲染会一直显示旧形状（踩过这个坑）。
    /// 所以这里必须「删掉旧资产 + 新建」，让 MeshFilter 拿到一个全新的 Mesh 对象。
    /// </summary>
    private static void Persist(CloudBlobBuilder_V2.Result r)
    {
        // ---- mesh 资产：删旧建新 ----
        if (AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath) != null)
            AssetDatabase.DeleteAsset(MeshPath);
        AssetDatabase.CreateAsset(r.mesh, MeshPath);

        // ---- 材质 ----
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            var sh = Shader.Find("SkyboxV2/CloudBlobTest");
            if (sh == null)
            {
                Debug.LogError("[CloudBlobLab_V2] 找不到 shader 'SkyboxV2/CloudBlobTest'");
                return;
            }
            mat = new Material(sh) { name = "CloudBlobTest_V2" };
            AssetDatabase.CreateAsset(mat, MatPath);
        }

        // ---- GameObject ----
        var go = GameObject.Find(ObjectName);
        if (go == null) go = new GameObject(ObjectName);
        go.transform.position = SpawnPos;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var mf = go.GetComponent<MeshFilter>();
        if (mf == null) mf = go.AddComponent<MeshFilter>();
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) mr = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        EditorUtility.SetDirty(go);
    }
}
