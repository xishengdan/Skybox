using UnityEngine;

/// <summary>
/// 昼夜循环 + 云层自动旋转：
/// - 按“现实秒 -> 游戏小时”推进 timeOfDay，驱动方向光沿太阳弧线旋转（天空盒的日/月/散射会自动跟随）。
/// - 可选按太阳高度调节方向光颜色/强度。
/// - 可选让云穹顶绕世界 Y 轴匀速横向旋转。
/// 挂在 Directional Light 上使用。
/// </summary>
[ExecuteInEditMode]
public class SkyDayNightController : MonoBehaviour
{
    [Header("时间")]
    public bool autoAdvance = true;                 // 自动推进时间
    [Range(0f, 24f)] public float timeOfDay = 6f;   // 当前时间（小时，0=午夜,6=日出,12=正午,18=日落）
    public float dayLengthSeconds = 120f;           // 一整天的现实秒数
    public float timeSpeed = 1f;                    // 时间倍率
    public bool previewInEditMode = true;           // 编辑器里也自动推进（方便预览）

    [Header("太阳弧线")]
    public Light sun;                               // 目标方向光
    public float sunYaw = 25f;                      // 弧线朝向（绕 Y）
    public float sunTilt = 15f;                     // 弧线倾斜（绕 Z）

    [Header("光照")]
    public bool driveLight = true;
    public float maxIntensity = 2f;
    public float nightIntensity = 0.05f;
    public Color dayColor = Color.white;
    public Color horizonColor = new Color(1f, 0.6f, 0.35f);

    [Header("曲线模式（按小时 0..24 采样，勾上就覆盖上面的四项）")]
    public bool useCurves = false;
    public AnimationCurve intensityCurve = new AnimationCurve(
        new Keyframe(0f, 0.00f), new Keyframe(5f, 0.00f), new Keyframe(6.5f, 0.6f),
        new Keyframe(12f, 1.0f), new Keyframe(17.5f, 0.6f), new Keyframe(19f, 0.00f), new Keyframe(24f, 0.00f));
    public Gradient colorGradient = new Gradient
    {
        colorKeys = new[]
        {
            new GradientColorKey(new Color(0.20f, 0.26f, 0.45f), 0f),
            new GradientColorKey(new Color(1.00f, 0.55f, 0.28f), 6f / 24f),
            new GradientColorKey(new Color(1.00f, 0.93f, 0.82f), 9f / 24f),
            new GradientColorKey(Color.white,                 12f / 24f),
            new GradientColorKey(new Color(1.00f, 0.85f, 0.65f), 16f / 24f),
            new GradientColorKey(new Color(1.00f, 0.45f, 0.20f), 18.5f / 24f),
            new GradientColorKey(new Color(0.20f, 0.26f, 0.45f), 24f / 24f),
        },
        alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) },
    };
    [Tooltip("曲线模式下的整体强度倍率")] public float curveIntensityScale = 2f;

    [Header("云层旋转")]
    public Transform cloudRoot;                     // 云穹顶
    public bool rotateClouds = true;
    public float cloudSpinDegPerSec = 1.5f;         // 横向旋转速度（度/秒）

    [Header("环境光联动")]
    [Tooltip("天空/太阳变了就重算环境光探针（不勾环境光会停在烘焙那一刻）")]
    public bool updateEnvironment = true;
    public float envUpdateStepHours = 1f;           // 至少相差这么多小时才重算，避免每帧都算

    private float lastRealtime;
    private float cloudAngle;
    private float lastEnvUpdateHour = -999f;

    private void OnEnable()
    {
        lastRealtime = Time.realtimeSinceStartup;
        if (cloudRoot != null)
            cloudAngle = cloudRoot.eulerAngles.y;
    }

    private void Update()
    {
        float now = Time.realtimeSinceStartup;
        float dt = Mathf.Clamp(now - lastRealtime, 0f, 0.1f);
        lastRealtime = now;

        bool advance = autoAdvance && (Application.isPlaying || previewInEditMode);
        if (advance)
        {
            timeOfDay += dt * timeSpeed / Mathf.Max(dayLengthSeconds, 0.01f) * 24f;
            timeOfDay = Mathf.Repeat(timeOfDay, 24f);
        }

        ApplyTime(advance ? dt : 0f);
    }

    /// <summary>把当前 timeOfDay 应用到光照与云层。</summary>
    public void ApplyTime(float dt)
    {
        // 太阳方向（指向太阳）：0h=-90°(地下), 6h=0°(东), 12h=90°(头顶), 18h=180°(西)
        float angle = (timeOfDay / 24f) * 360f - 90f;
        Vector3 dirLocal = new Vector3(
            Mathf.Cos(angle * Mathf.Deg2Rad),
            Mathf.Sin(angle * Mathf.Deg2Rad),
            0f);
        Vector3 sunDir = Quaternion.Euler(0f, sunYaw, sunTilt) * dirLocal;

        if (sun != null)
        {
            // URP 的 mainLight.direction = -transform.forward，所以 forward 取 -sunDir
            sun.transform.rotation = Quaternion.LookRotation(-sunDir);

            if (driveLight)
            {
                if (useCurves)
                {
                    // 按一天内的位置采样曲线：0h / 24h 在两端，12h 在中间
                    float t01 = Mathf.Repeat(timeOfDay, 24f) / 24f;
                    sun.color = colorGradient.Evaluate(t01);
                    sun.intensity = Mathf.Max(0f, intensityCurve.Evaluate(timeOfDay)) * curveIntensityScale;
                }
                else
                {
                    float up = sunDir.y;
                    float day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.1f, 0.3f, up));
                    float horizonAmt = Mathf.Clamp01(1f - Mathf.Abs(up) / 0.35f);
                    sun.color = Color.Lerp(dayColor, horizonColor, horizonAmt * day);
                    sun.intensity = Mathf.Lerp(nightIntensity, maxIntensity, day);
                }
            }
        }

        if (rotateClouds && cloudRoot != null)
        {
            cloudAngle += cloudSpinDegPerSec * dt;
            cloudRoot.rotation = Quaternion.Euler(0f, cloudAngle, 0f);
        }

        // 光照闭环：天空变了就把环境光探针重算一次，否则环境光会停在烘焙那一刻
        if (updateEnvironment && Mathf.Abs(timeOfDay - lastEnvUpdateHour) >= envUpdateStepHours)
        {
            lastEnvUpdateHour = timeOfDay;
            DynamicGI.UpdateEnvironment();
        }
    }
}
