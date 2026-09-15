using UnityEditor;
using UnityEngine;

/// <summary>
/// 按 CloudSkyPreset 生成云层。
///
/// 以前这里把仰角带/数量/seed/尺寸写死成 4 份常数，和 CloudDomeGenerator 的字段重复；
/// 现在全部从预设资产读，改预设即可，代码不再持有参数。
///
/// 每层都是独立的 CloudLayout + 独立 Mesh + 独立 GameObject（共用材质和图集），
/// 层间前后关系由 Renderer.sortingOrder 固定，互不干扰。
/// </summary>
public static class CloudLayerStack
{
    public const string PresetPath = "Assets/Scene/Models/CloudSkyPreset.asset";

    [MenuItem("Tools/Skybox Clouds/Layers/Generate All Layers From Preset", false, 10)]
    public static void GenerateFromPreset()
    {
        var preset = LoadOrCreate();
        int made = 0, skipped = 0;

        foreach (var spec in preset.layers)
        {
            if (spec == null) continue;
            if (!spec.enabled) { skipped++; continue; }
            GenerateLayer(preset, spec);
            made++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[CloudLayerStack] 按预设 {PresetPath} 生成 {made} 层，跳过（未勾选）{skipped} 层。\n" +
                  "未勾选的层完全没被读写，手调结果不受影响。");
    }

    [MenuItem("Tools/Skybox Clouds/Layers/Select-or-Create Preset", false, 11)]
    public static void SelectPreset()
    {
        var preset = LoadOrCreate();
        Selection.activeObject = preset;
        EditorGUIUtility.PingObject(preset);
    }

    [MenuItem("Tools/Skybox Clouds/Layers/Reset Preset To Defaults", false, 12)]
    public static void ResetPreset()
    {
        if (!EditorUtility.DisplayDialog("重置云层预设",
                $"会把 {PresetPath} 里的层参数恢复成默认三层（地平线层仍然默认不重建）。\n资产本身不会被删。", "重置", "取消"))
            return;

        var def = CloudSkyPreset.CreateDefault();
        var existing = AssetDatabase.LoadAssetAtPath<CloudSkyPreset>(PresetPath);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(def, PresetPath);
        }
        else
        {
            EditorUtility.CopySerialized(def, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(def);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[CloudLayerStack] 预设已重置");
    }

    [MenuItem("Tools/Skybox Clouds/Layers/Report Layer Stack", false, 30)]
    public static void Report()
    {
        var preset = AssetDatabase.LoadAssetAtPath<CloudSkyPreset>(PresetPath);
        if (preset != null)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in preset.layers)
                sb.AppendLine($"  {(s.enabled ? "[生成]" : "[跳过]")} {s.label}  {s.objectName}  {s.elevationMinDeg}°~{s.elevationMaxDeg}°  x{s.cloudCount}  sortingOrder={s.sortingOrder}");
            Debug.Log($"[CloudLayerStack] 预设 {PresetPath}:\n{sb}");
        }

        foreach (var name in new[] { "SkyCloudsHigh", "SkyCloudsMid", "SkyClouds" })
        {
            var go = GameObject.Find(name);
            if (go == null) { Debug.Log($"[CloudLayerStack] {name}: 不存在"); continue; }
            var mr = go.GetComponent<MeshRenderer>();
            var mf = go.GetComponent<MeshFilter>();
            int quads = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.vertexCount / 4 : 0;
            Debug.Log($"[CloudLayerStack] {name}: {quads} 面片, sortingOrder={(mr != null ? mr.sortingOrder : 0)}, " +
                      $"跟随相机={(go.GetComponent<SkyCloudsRig>() != null)}");
        }
    }

    // ==================== 内部 ====================

    public static CloudSkyPreset LoadOrCreate()
    {
        var preset = AssetDatabase.LoadAssetAtPath<CloudSkyPreset>(PresetPath);
        if (preset != null) return preset;

        preset = CloudSkyPreset.CreateDefault();
        string dir = System.IO.Path.GetDirectoryName(PresetPath);
        if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder("Assets/Scene", "Models");
        AssetDatabase.CreateAsset(preset, PresetPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[CloudLayerStack] 已创建默认预设: {PresetPath}");
        return preset;
    }

    private static void GenerateLayer(CloudSkyPreset preset, CloudLayerSpec spec)
    {
        var w = ScriptableObject.CreateInstance<CloudDomeGenerator>();

        w.objectName = spec.objectName;
        w.layoutPath = spec.layoutPath;
        w.meshPath = spec.meshPath;
        w.materialPath = preset.materialPath;
        w.atlasPath = preset.atlasPath;
        w.atlasCols = preset.atlasCols;
        w.atlasRows = preset.atlasRows;
        w.sortingOrder = spec.sortingOrder;
        w.coverageMaskPath = preset.coverageMaskPath;
        w.coverageMaskStrength = preset.coverageMaskStrength;

        w.useShells = preset.useShells;
        w.shellRadiusNear = preset.shellRadiusNear;
        w.shellRadiusMid = preset.shellRadiusMid;
        w.shellRadiusFar = preset.shellRadiusFar;

        w.puffCountMin = preset.puffCountMin;
        w.puffCountMax = preset.puffCountMax;
        w.puffSpread = preset.puffSpread;
        w.puffVariance = preset.puffVariance;

        w.elevationMinDeg = spec.elevationMinDeg;
        w.elevationMaxDeg = spec.elevationMaxDeg;
        w.elevationShiftDeg = 0f;              // 仰角带已经在 spec 里定好，不要再整体抬高
        w.horizonBias = spec.horizonBias;

        w.cloudCount = spec.cloudCount;
        w.useClusters = spec.useClusters;
        w.clusterCount = spec.clusterCount;
        w.clusterSpreadDeg = spec.clusterSpreadDeg;
        w.seed = spec.seed;

        w.sizeMin = spec.sizeMin;
        w.sizeMax = spec.sizeMax;
        w.minSizeFloor = spec.minSizeFloor;
        w.elevationSizeFalloff = spec.elevationSizeFalloff;

        w.Generate();
        Object.DestroyImmediate(w);
    }
}
