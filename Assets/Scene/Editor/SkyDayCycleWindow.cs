using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 一天的预览台 + 天空烘焙。
///
/// 解决“改参数要开窗口 → 点生成 → 切场景看”的问题：
/// 拖时间滑杆会直接 ApplyTime 并刷新场景视图，配合曲线可以当场看日出日落。
/// 「烘 Cubemap」把当前天空烤成一张 Cubemap 资产（给反射探针 / 低配模式用）。
/// </summary>
public class SkyDayCycleWindow : EditorWindow
{
    private const string CubemapPath = "Assets/Scene/Tex/SkyCubemap.cubemap";

    private SkyDayNightController _ctl;
    private int _bakeSize = 512;
    private bool _assignToRenderSettings;

    [MenuItem("Tools/Skybox Clouds/Sky Day Cycle", false, 21)]
    public static void Open()
    {
        var w = GetWindow<SkyDayCycleWindow>("Sky Day Cycle");
        w.minSize = new Vector2(430, 420);
        w.Refresh();
    }

    private void OnFocus() => Refresh();
    private void OnSelectionChange() => Refresh();
    private void Refresh() => _ctl = Object.FindObjectOfType<SkyDayNightController>();

    private void OnGUI()
    {
        if (_ctl == null)
        {
            EditorGUILayout.HelpBox("场景里没找到 SkyDayNightController（一般挂在 Directional Light 上）。", MessageType.Warning);
            if (GUILayout.Button("重新查找")) Refresh();
            return;
        }

        EditorGUILayout.LabelField("时间", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        float t = EditorGUILayout.Slider("时刻 (小时)", _ctl.timeOfDay, 0f, 24f);
        if (EditorGUI.EndChangeCheck())
        {
            _ctl.timeOfDay = t;
            Apply();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            foreach (float q in new[] { 0f, 4f, 6f, 9f, 12f, 15f, 18f, 21f })
                if (GUILayout.Button($"{q:0}h")) { _ctl.timeOfDay = q; Apply(); }
        }

        EditorGUILayout.Space();
        EditorGUI.BeginChangeCheck();
        _ctl.autoAdvance = EditorGUILayout.Toggle("自动推进时间", _ctl.autoAdvance);
        _ctl.previewInEditMode = EditorGUILayout.Toggle("编辑模式也推进（预览用）", _ctl.previewInEditMode);
        _ctl.dayLengthSeconds = EditorGUILayout.FloatField("一整天(秒)", _ctl.dayLengthSeconds);
        if (EditorGUI.EndChangeCheck()) Apply();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("太阳曲线", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _ctl.useCurves = EditorGUILayout.Toggle("用曲线驱动", _ctl.useCurves);
        using (new EditorGUI.DisabledScope(!_ctl.useCurves))
        {
            _ctl.intensityCurve = EditorGUILayout.CurveField("强度 (0..24h)", _ctl.intensityCurve);
            _ctl.curveIntensityScale = EditorGUILayout.Slider("强度倍率", _ctl.curveIntensityScale, 0f, 6f);
            _ctl.colorGradient = EditorGUILayout.GradientField("颜色 (0..24h)", _ctl.colorGradient);
        }
        if (EditorGUI.EndChangeCheck()) Apply();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("云层 / 环境光", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _ctl.rotateClouds = EditorGUILayout.Toggle("云层自转", _ctl.rotateClouds);
        _ctl.cloudSpinDegPerSec = EditorGUILayout.Slider("自转速度 (度/秒)", _ctl.cloudSpinDegPerSec, 0f, 10f);
        _ctl.updateEnvironment = EditorGUILayout.Toggle("天空变了就刷新环境光", _ctl.updateEnvironment);
        if (EditorGUI.EndChangeCheck()) Apply();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("烘焙", EditorStyles.boldLabel);
        _bakeSize = EditorGUILayout.IntPopup("Cubemap 尺寸", _bakeSize,
            new[] { "128", "256", "512", "1024", "2048" }, new[] { 128, 256, 512, 1024, 2048 });
        _assignToRenderSettings = EditorGUILayout.Toggle("同时设为场景反射源", _assignToRenderSettings);

        if (GUILayout.Button("把当前天空烘成 Cubemap", GUILayout.Height(32)))
            BakeCubemap(_bakeSize, _assignToRenderSettings);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Tips:\n" +
            "• 拖时刻滑杆会即时刷新场景视图，配合曲线当场调日出日落。\n" +
            "• 环境光默认是「烘焙那一刻」的；勾上刷新后拖时间才不会出现天色变了但物体还亮着。\n" +
            "• Cubemap 烘的是纯天空（不含场景物体），给反射探针或低配模式用。",
            MessageType.Info);
    }

    private void Apply()
    {
        if (_ctl == null) return;
        _ctl.ApplyTime(0f);
        EditorUtility.SetDirty(_ctl);
        SceneView.RepaintAll();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        EditorApplication.QueuePlayerLoopUpdate();
        Repaint();
    }

    // ==================== Cubemap 烘焙 ====================

    private static readonly Vector3[] FaceDirs =
    {
        Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
    };

    private static readonly CubemapFace[] Faces =
    {
        CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY,
        CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ
    };

    public static void BakeCubemap(int size, bool assignToRenderSettings)
    {
        // 纯天空相机：只清成 skybox，不画任何物体
        var go = new GameObject("__SkyBakeCamera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.cullingMask = 0;
        cam.fieldOfView = 90f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 100f;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        cam.enabled = false;
        cam.aspect = 1f;
        cam.transform.position = Vector3.zero;

        var cube = new Cubemap(size, TextureFormat.RGBA32, false);
        var rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
        var prevActive = RenderTexture.active;

        try
        {
            cam.targetTexture = rt;
            for (int i = 0; i < 6; i++)
            {
                Vector3 fwd = FaceDirs[i];
                Vector3 up = Mathf.Abs(fwd.y) > 0.5f ? Vector3.back : Vector3.up;
                cam.transform.rotation = Quaternion.LookRotation(fwd, up);

                cam.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0f, 0f, size, size), 0, 0);
                tex.Apply();
                cube.SetPixels(tex.GetPixels(), Faces[i]);
                Object.DestroyImmediate(tex);
            }
            cube.Apply();
        }
        finally
        {
            cam.targetTexture = null;
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(go);
        }

        string dir = Path.GetDirectoryName(CubemapPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var existing = AssetDatabase.LoadAssetAtPath<Cubemap>(CubemapPath);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(cube, CubemapPath);
        }
        else
        {
            EditorUtility.CopySerialized(cube, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(cube);
            cube = existing;
        }
        AssetDatabase.SaveAssets();

        if (assignToRenderSettings) RenderSettings.customReflection = cube;

        Debug.Log($"[SkyDayCycle] 天空已烘成 {size}px Cubemap: {CubemapPath}" +
                  (assignToRenderSettings ? "（同时设为场景反射源）" : ""));
    }
}
