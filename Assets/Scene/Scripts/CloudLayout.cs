using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单朵云的数据（可逐朵手改）。
/// </summary>
[System.Serializable]
public class CloudEntry
{
    public bool enabled = true;
    [Range(0f, 360f)] public float azimuthDeg = 0f;      // 方位角
    [Range(0.5f, 89f)] public float elevationDeg = 20f;  // 仰角
    [Tooltip("云高度（世界单位），直接控制这朵云的大小")] public float size = 220f;
    [Tooltip("宽高比")] public float widthAspect = 1.6f;
    [Range(-45f, 45f)] public float rollDeg = 0f;        // 倾斜
    public bool mirror = false;                          // 水平镜像
    public int cellX = 0;                                // 图集格
    public int cellY = 0;
    [Range(0f, 1f)] public float depth01 = 0.5f;         // 远近（雾化/透明/饱和）
    [Range(0f, 1f)] public float brightHash = 0.5f;      // 亮度扰动
    [Range(0f, 1f)] public float dissolveSeed = 0.5f;    // 消散相位

    [Header("多瓣（一朵云 = 几个面片叠成）")]
    [Tooltip("1 = 单面片（老行为）；3~5 就是积云那种一团多瓣")] [Range(1, 9)] public int puffCount = 3;
    [Tooltip("多瓣横向铺开比例（相对云宽）")] [Range(0.1f, 1.5f)] public float puffSpread = 0.9f;
    [Tooltip("各瓣高度的随机起伏，越大越毛糙")] [Range(0f, 1f)] public float puffVariance = 0.35f;
}

/// <summary>
/// 云穹顶布局：保存每朵云的位置/大小等，可逐朵编辑后一键重建网格。
/// </summary>
[CreateAssetMenu(fileName = "CloudLayout", menuName = "Skybox Clouds/Cloud Layout")]
public class CloudLayout : ScriptableObject
{
    [Header("穹顶")]
    public float radius = 900f;
    [Tooltip("单朵云最大半张角（防超大面片出现刀片斜边）")] public float maxCloudAngleDeg = 15f;

    [Header("图集")]
    public int atlasCols = 2;
    public int atlasRows = 4;
    public string atlasPath = "Assets/Scene/Tex/CloudPacked.png";

    [Header("深度分层（视差壳）")]
    [Tooltip("按 depth01 把云分到 3 层不同半径，产生纵深")]
    public bool useShells = true;
    public float shellRadiusNear = 380f;
    public float shellRadiusMid = 640f;
    public float shellRadiusFar = 900f;

    [Header("空间分布遮罩（哪片天多云）")]
    [Tooltip("横向 = 方位角 0..360°，纵向 = 仰角。白 = 这里云更实，黑 = 这里云被吃掉。\n" +
             "用 Tools/Skybox Clouds/Sky Coverage Painter 手画。")]
    public string coverageMaskPath = "Assets/Scene/Tex/SkyCoverageMask.png";
    [Tooltip("0 = 忽略遮罩；1 = 遮罩完全生效")] [Range(0f, 1f)] public float coverageMaskStrength = 1f;

    [Header("输出")]
    public string objectName = "SkyClouds";
    public string meshPath = "Assets/Scene/Models/CloudDome.asset";
    public string materialPath = "Assets/Scene/Mat/CloudBillboard.mat";
    [Tooltip("透明排序层级：越小越先画（越靠后）。多层云时用它可以固定层与层之间的前后关系")]
    public int sortingOrder = 0;

    public List<CloudEntry> clouds = new List<CloudEntry>();
}
