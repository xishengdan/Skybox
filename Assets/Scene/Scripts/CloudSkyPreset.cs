using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一个云层的描述。之前这些参数写死在 CloudLayerStack.cs 里，
/// 和 CloudDomeGenerator 的字段各存了一份，改一处另一处就漂。现在统一放这里（资产里可改）。
/// </summary>
[System.Serializable]
public class CloudLayerSpec
{
    [Tooltip("只在工具里显示用")] public string label = "层";
    [Tooltip("取消勾选就不会被生成（用来保护手调过的层）")] public bool enabled = true;

    public string objectName = "SkyCloudsMid";
    public string layoutPath = "Assets/Scene/Models/CloudLayoutMid.asset";
    public string meshPath = "Assets/Scene/Models/CloudDomeMid.asset";
    [Tooltip("越小越先画（越靠后）。地平线层 0，越高层用更小的值")] public int sortingOrder = -1;

    [Header("仰角带")]
    public float elevationMinDeg = 30f;
    public float elevationMaxDeg = 55f;
    [Tooltip(">1 会往低仰角挤（地平线云带用）")] public float horizonBias = 1f;

    [Header("数量 / 分布")]
    public int cloudCount = 18;
    public bool useClusters = true;
    public int clusterCount = 7;
    public float clusterSpreadDeg = 9f;
    public int seed = 20260912;

    [Header("尺寸")]
    public float sizeMin = 110f;
    public float sizeMax = 240f;
    public float minSizeFloor = 130f;
    [Tooltip("层内是否做「越低越小」")] public bool elevationSizeFalloff = false;
}

/// <summary>
/// 整个天空的云层配置。CloudDomeGenerator / CloudLayerStack 都从这里取参数。
/// </summary>
[CreateAssetMenu(fileName = "CloudSkyPreset", menuName = "Skybox Clouds/Cloud Sky Preset")]
public class CloudSkyPreset : ScriptableObject
{
    [Header("共用资产")]
    public string materialPath = "Assets/Scene/Mat/CloudBillboard.mat";
    public string atlasPath = "Assets/Scene/Tex/CloudPacked.png";
    public int atlasCols = 2;
    public int atlasRows = 4;

    [Header("深度分层（视差壳）")]
    public bool useShells = true;
    public float shellRadiusNear = 380f;
    public float shellRadiusMid = 640f;
    public float shellRadiusFar = 900f;

    [Header("空间分布遮罩")]
    public string coverageMaskPath = "Assets/Scene/Tex/SkyCoverageMask.png";
    [Range(0f, 1f)] public float coverageMaskStrength = 1f;

    [Header("多瓣")]
    public int puffCountMin = 2;
    public int puffCountMax = 4;
    public float puffSpread = 0.65f;
    public float puffVariance = 0.4f;

    [Header("层")]
    public List<CloudLayerSpec> layers = new List<CloudLayerSpec>();

    /// <summary>首次使用时的默认三层：地平线层默认不参与生成，保护手调结果。</summary>
    public static CloudSkyPreset CreateDefault()
    {
        var p = CreateInstance<CloudSkyPreset>();
        p.layers = new List<CloudLayerSpec>
        {
            new CloudLayerSpec
            {
                label = "地平线层（现状，默认不重建）",
                enabled = false,
                objectName = "SkyClouds",
                layoutPath = "Assets/Scene/Models/CloudLayout.asset",
                meshPath = "Assets/Scene/Models/CloudDome.asset",
                sortingOrder = 0,
                elevationMinDeg = 5f, elevationMaxDeg = 32f, horizonBias = 1.3f,
                cloudCount = 46, clusterCount = 15, clusterSpreadDeg = 7f, seed = 20260911,
                sizeMin = 120f, sizeMax = 300f, minSizeFloor = 200f, elevationSizeFalloff = true,
            },
            new CloudLayerSpec
            {
                label = "中高层",
                enabled = true,
                objectName = "SkyCloudsMid",
                layoutPath = "Assets/Scene/Models/CloudLayoutMid.asset",
                meshPath = "Assets/Scene/Models/CloudDomeMid.asset",
                sortingOrder = -1,
                elevationMinDeg = 30f, elevationMaxDeg = 55f,
                cloudCount = 18, clusterCount = 7, clusterSpreadDeg = 9f, seed = 20260912,
                sizeMin = 110f, sizeMax = 240f, minSizeFloor = 130f,
            },
            new CloudLayerSpec
            {
                label = "高天顶",
                enabled = true,
                objectName = "SkyCloudsHigh",
                layoutPath = "Assets/Scene/Models/CloudLayoutHigh.asset",
                meshPath = "Assets/Scene/Models/CloudDomeHigh.asset",
                sortingOrder = -2,
                elevationMinDeg = 52f, elevationMaxDeg = 85f,
                cloudCount = 10, clusterCount = 4, clusterSpreadDeg = 11f, seed = 20260913,
                sizeMin = 90f, sizeMax = 200f, minSizeFloor = 110f,
            },
        };
        return p;
    }
}
