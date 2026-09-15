using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 手画“哪片天多云”。
///
/// 遮罩约定：
///   横向 = 方位角 0..360°（左右可循环）
///   纵向 = 仰角，顶是 90°(天顶)，底是 -5°(地平线以下)
///   白 = 这里云更实，黑 = 这里云被吃掉
///
/// 遮罩烘进每朵云的顶点色 alpha，CloudBillboard.shader 用它去拉 coverage。
/// 全白 = 和以前完全一样。
/// </summary>
public class SkyCoveragePainter : EditorWindow
{
    private const string MaskPath = "Assets/Scene/Tex/SkyCoverageMask.png";
    private const int W = 256;
    private const int H = 128;

    private Texture2D _tex;
    private float _brush = 30f;
    private float _strength = 1f;
    private float _hardness = 0.5f;
    private Vector2 _scroll;

    [MenuItem("Tools/Skybox Clouds/Sky Coverage Painter", false, 20)]
    public static void Open()
    {
        var w = GetWindow<SkyCoveragePainter>("Sky Coverage");
        w.minSize = new Vector2(600, 460);
        w.Reload();
    }

    private void OnEnable() => Reload();

    // ================= 载入 / 保存 =================

    private void Reload()
    {
        if (!File.Exists(MaskPath))
        {
            _tex = NewMask(1f);
            Save();
            return;
        }

        var ti = AssetImporter.GetAtPath(MaskPath) as TextureImporter;
        if (ti != null && (!ti.isReadable || ti.wrapModeU != TextureWrapMode.Repeat || ti.mipmapEnabled))
        {
            ti.isReadable = true;
            ti.wrapModeU = TextureWrapMode.Repeat;
            ti.wrapModeV = TextureWrapMode.Clamp;
            ti.mipmapEnabled = false;
            ti.sRGBTexture = false;
            ti.SaveAndReimport();
        }

        _tex = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
        if (_tex == null) { _tex = NewMask(1f); Save(); }
    }

    private static Texture2D NewMask(float v)
    {
        var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "SkyCoverageMask" };
        var arr = new Color[W * H];
        var c = new Color(v, v, v, 1f);
        for (int i = 0; i < arr.Length; i++) arr[i] = c;
        t.SetPixels(arr);
        t.Apply();
        return t;
    }

    private void Save()
    {
        string dir = Path.GetDirectoryName(MaskPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        File.WriteAllBytes(MaskPath, _tex.EncodeToPNG());
        AssetDatabase.ImportAsset(MaskPath, ImportAssetOptions.ForceUpdate);

        var ti = AssetImporter.GetAtPath(MaskPath) as TextureImporter;
        if (ti != null)
        {
            ti.isReadable = true;
            ti.wrapModeU = TextureWrapMode.Repeat;
            ti.wrapModeV = TextureWrapMode.Clamp;
            ti.mipmapEnabled = false;
            ti.sRGBTexture = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }
        Debug.Log("[SkyCoveragePainter] 遮罩已保存: " + MaskPath);
    }

    // ================= UI =================

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("空间分布遮罩", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "横向 = 方位角 0..360°（左右循环）    纵向 = 仰角（顶 90° 天顶 → 底 -5°）\n" +
            "左键拖 = 画白（这里云更实）    Shift+左键 / 右键拖 = 画黑（这里云被吃掉）",
            MessageType.Info);

        DrawCanvas();

        EditorGUILayout.Space();
        _brush = EditorGUILayout.Slider("笔刷半径", _brush, 2f, 80f);
        _strength = EditorGUILayout.Slider("笔刷强度", _strength, 0.05f, 1f);
        _hardness = EditorGUILayout.Slider("笔刷硬度", _hardness, 0f, 1f);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("全白（还原）")) { Fill(1f); }
            if (GUILayout.Button("全黑（清空）")) { Fill(0f); }
            if (GUILayout.Button("上密下疏")) { Gradient(); }
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("保存", GUILayout.Height(30))) Save();
            if (GUILayout.Button("保存并应用到所有云层", GUILayout.Height(30))) { Save(); ApplyToLayers(); }
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawCanvas()
    {
        float dw = position.width - 44f;
        float dh = Mathf.Clamp(dw * (float)H / W, 80f, 300f);
        Rect rect = GUILayoutUtility.GetRect(dw, dh);

        EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f, 1f));
        GUI.DrawTexture(rect, _tex, ScaleMode.StretchToFill, false);
        Handles.color = new Color(1f, 1f, 1f, 0.15f);
        for (int i = 1; i < 4; i++) Handles.DrawLine(new Vector3(rect.x + rect.width * i / 4f, rect.y), new Vector3(rect.x + rect.width * i / 4f, rect.yMax));

        GUI.Label(new Rect(rect.x + 4, rect.yMax - 18, 160, 16), "0°");
        GUI.Label(new Rect(rect.xMax - 46, rect.yMax - 18, 46, 16), "360°");
        GUI.Label(new Rect(rect.x + 4, rect.y + 2, 120, 16), "90° 天顶");
        GUI.Label(new Rect(rect.x + 4, rect.yMax - 34, 120, 16), "-5°");

        Event e = Event.current;
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) &&
            (e.button == 0 || e.button == 1) && rect.Contains(e.mousePosition))
        {
            bool erase = e.button == 1 || e.shift;
            float u = Mathf.Clamp01((e.mousePosition.x - rect.x) / rect.width);
            float v = Mathf.Clamp01(1f - (e.mousePosition.y - rect.y) / rect.height);
            float scale = (float)W / rect.width;
            Paint(u * W, v * H, erase ? 0f : 1f, scale);
            e.Use();
            Repaint();
        }
    }

    // ================= 绘制 =================

    private void Paint(float cx, float cy, float value, float scale)
    {
        var px = _tex.GetPixels();
        int r = Mathf.CeilToInt(_brush * scale);
        int cxi = Mathf.RoundToInt(cx);
        int cyi = Mathf.RoundToInt(cy);

        for (int y = -r; y <= r; y++)
        {
            int py = cyi + y;
            if (py < 0 || py >= H) continue;
            for (int x = -r; x <= r; x++)
            {
                int pxx = ((cxi + x) % W + W) % W;                  // 方位角方向循环
                float d = Mathf.Sqrt((float)(x * x + y * y)) / Mathf.Max(1f, r);
                if (d > 1f) continue;

                float fall = Mathf.Pow(1f - d, Mathf.Lerp(4f, 0.25f, _hardness));
                float a = Mathf.Clamp01(fall * _strength);

                int i = py * W + pxx;
                float nv = Mathf.Lerp(px[i].r, value, a);
                px[i] = new Color(nv, nv, nv, 1f);
            }
        }
        _tex.SetPixels(px);
        _tex.Apply();
    }

    private void Fill(float v)
    {
        var arr = new Color[W * H];
        var c = new Color(v, v, v, 1f);
        for (int i = 0; i < arr.Length; i++) arr[i] = c;
        _tex.SetPixels(arr);
        _tex.Apply();
        Repaint();
    }

    // 低仰角密、高仰角疏：给“地平线云带 + 抬头零星”的默认形态打个底
    private void Gradient()
    {
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
        {
            float v = (float)y / (H - 1);                   // 0 = 底(y=0, -5°) .. 1 = 顶(90°)
            float val = Mathf.Lerp(1f, 0.25f, Mathf.Pow(v, 0.8f));
            for (int x = 0; x < W; x++) px[y * W + x] = new Color(val, val, val, 1f);
        }
        _tex.SetPixels(px);
        _tex.Apply();
        Repaint();
    }

    // ================= 应用到云层 =================

    private void ApplyToLayers()
    {
        string[] guids = AssetDatabase.FindAssets("t:CloudLayout");
        int n = 0;
        foreach (string g in guids)
        {
            var layout = AssetDatabase.LoadAssetAtPath<CloudLayout>(AssetDatabase.GUIDToAssetPath(g));
            if (layout == null) continue;

            layout.coverageMaskPath = MaskPath;
            if (layout.coverageMaskStrength <= 0.001f) layout.coverageMaskStrength = 1f;
            EditorUtility.SetDirty(layout);
            CloudMeshBuilder.Build(layout);
            n++;
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[SkyCoveragePainter] 遮罩已应用到 {n} 个云层布局并重建网格");
    }
}
