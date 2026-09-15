using UnityEngine;

/// <summary>
/// 云穹顶支架：
/// - 跟随相机位移（不跟旋转），保证自由移动时云始终以相机为中心，无穿帮/视差。
/// - 提供覆盖率/色调的天气接口，用 MaterialPropertyBlock 驱动，不产生材质实例。
/// </summary>
[ExecuteInEditMode]
public class SkyCloudsRig : MonoBehaviour
{
    [Header("跟随相机（只跟位移，不跟旋转）")]
    public Camera targetCamera;
    public bool followCamera = true;

    [Header("天气驱动")]
    public MeshRenderer cloudRenderer;

    private MaterialPropertyBlock mpb;

    private void OnEnable()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (cloudRenderer == null) cloudRenderer = GetComponent<MeshRenderer>();
        mpb = new MaterialPropertyBlock();
    }

    private void LateUpdate()
    {
        if (!followCamera) return;
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam != null)
            transform.position = cam.transform.position;   // 只跟位移
    }

    // ===== 天气接口（0=无云, 1=默认, >1 更多） =====
    public void SetCoverage01(float v) => SetFloat("_CoverageGlobal", Mathf.Max(0f, v));

    // ===== 天气接口（云的全局染色） =====
    public void SetCloudTint(Color c) => SetColor("_CloudTintGlobal", c);

    private void SetFloat(string name, float v)
    {
        if (cloudRenderer == null) return;
        cloudRenderer.GetPropertyBlock(mpb);
        mpb.SetFloat(name, v);
        cloudRenderer.SetPropertyBlock(mpb);
    }

    private void SetColor(string name, Color c)
    {
        if (cloudRenderer == null) return;
        cloudRenderer.GetPropertyBlock(mpb);
        mpb.SetColor(name, c);
        cloudRenderer.SetPropertyBlock(mpb);
    }
}
