using UnityEngine;

[ExecuteInEditMode]
public class SkyboxLightSpaceSetter : MonoBehaviour
{
    public Light directionalLight;  // 你的方向光

    void Update()
    {
        if (directionalLight == null)
            return;

        // 构建世界到光源空间的转换矩阵
        // 对于方向光，这个矩阵会将光源方向映射到-Z轴
        Matrix4x4 worldToLight = Matrix4x4.TRS(
            directionalLight.transform.position,
            directionalLight.transform.rotation,
            Vector3.one
        ).inverse;

        // 或者更简单的方式：
        // worldToLight = directionalLight.transform.worldToLocalMatrix;

        // 写全局属性而非材质属性：
        // SetGlobalMatrix 只写内存，不把 Skybox.mat 标记为 dirty，
        // 因此不会每帧改写资产文件、也不会把过期的矩阵值烤进 .mat。
        Shader.SetGlobalMatrix("_SunLocalToWorld", worldToLight);
    }
}