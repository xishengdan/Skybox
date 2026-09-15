using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 从 CloudLayout 资产构建云穹顶网格（并接好材质与 GameObject）。
/// 生成器与布局编辑器共用这一份构建逻辑。
/// </summary>
public static class CloudMeshBuilder
{
    public static Mesh Build(CloudLayout layout)
    {
        if (layout == null) return null;

        var verts = new List<Vector3>();
        var uvs = new List<Vector4>();
        var uv1s = new List<Vector2>();
        var colors = new List<Color>();
        var quadDepths = new List<float>();
        var quadSubs = new List<float>();

        int cols = Mathf.Max(1, layout.atlasCols);
        int rows = Mathf.Max(1, layout.atlasRows);
        int cellCount = Mathf.Max(1, cols * rows);
        float farRadius = layout.useShells
            ? Mathf.Max(layout.radius, layout.shellRadiusFar)
            : layout.radius;
        float maxExtent = Mathf.Tan(Mathf.Deg2Rad * Mathf.Max(1f, layout.maxCloudAngleDeg)) * farRadius;

        // 空间分布遮罩：按 (方位角, 仰角) 采样，烘进顶点色 alpha 给 shader 做覆盖率
        Texture2D covMask = null;
        if (layout.coverageMaskStrength > 0.001f && !string.IsNullOrEmpty(layout.coverageMaskPath))
            covMask = AssetDatabase.LoadAssetAtPath<Texture2D>(layout.coverageMaskPath);
        if (covMask != null && !covMask.isReadable)
        {
            Debug.LogWarning($"[CloudMeshBuilder] 遮罩 {layout.coverageMaskPath} 不可读（Read/Write 未开），本次忽略遮罩");
            covMask = null;
        }

        for (int ei = 0; ei < layout.clouds.Count; ei++)
        {
            var e = layout.clouds[ei];
            if (e == null || !e.enabled) continue;

            float elev = Mathf.Deg2Rad * Mathf.Clamp(e.elevationDeg, 0.5f, 89f);
            float az = Mathf.Deg2Rad * e.azimuthDeg;
            Vector3 dir = new Vector3(
                Mathf.Cos(elev) * Mathf.Cos(az),
                Mathf.Sin(elev),
                Mathf.Cos(elev) * Mathf.Sin(az)).normalized;

            Vector3 center = dir * RadiusFor(layout, e.depth01);
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(dir, right).normalized;

            float roll = Mathf.Deg2Rad * e.rollDeg;
            Vector3 r2 = right * Mathf.Cos(roll) + up * Mathf.Sin(roll);
            Vector3 u2 = -right * Mathf.Sin(roll) + up * Mathf.Cos(roll);

            float fullH = Mathf.Max(1f, e.size);
            float fullW = fullH * Mathf.Max(0.1f, e.widthAspect);
            fullW = Mathf.Min(fullW, maxExtent * 2f);
            fullH = Mathf.Min(fullH, maxExtent * 2f);

            int puffCount = Mathf.Clamp(e.puffCount, 1, 9);
            float spread = Mathf.Max(0.1f, e.puffSpread);
            float baseLine = -fullH * 0.5f;      // 所有瓣共用这条平底基线

            // 这朵云的覆盖系数（整朵共用一个值 -> 云被整体收紧或吃没，不会半个瓣消失）
            float cloudCoverage = 1f;
            if (covMask != null)
            {
                float mu = Mathf.Repeat(e.azimuthDeg / 360f, 1f);
                float mv = Mathf.Clamp01(Mathf.InverseLerp(-5f, 90f, e.elevationDeg));
                float m = covMask.GetPixelBilinear(mu, mv).r;
                cloudCoverage = Mathf.Lerp(1f, m, Mathf.Clamp01(layout.coverageMaskStrength));
            }

            // 先算每瓣尺寸：横向包络（中间高两边矮）+ 每瓣随机起伏
            var puffs = new List<int>();
            var puffX = new List<float>();
            var puffW = new List<float>();
            var puffH = new List<float>();
            for (int p = 0; p < puffCount; p++)
            {
                float t = puffCount == 1 ? 0f : (p / (float)(puffCount - 1)) * 2f - 1f;   // -1..1
                float hash = Hash01(ei, p);

                float env = Mathf.Sqrt(Mathf.Max(0.05f, 1f - 0.7f * t * t));
                float hf = Mathf.Clamp(env * Mathf.Lerp(1f - e.puffVariance, 1f, hash), 0.25f, 1f);
                float wf = Mathf.Lerp(0.45f, 0.8f, Mathf.Lerp(1f, hash, 0.5f));

                puffs.Add(p);
                puffX.Add(t * fullW * 0.5f * spread);
                puffW.Add(Mathf.Clamp(fullW * wf, 1f, maxExtent * 2f));
                puffH.Add(Mathf.Clamp(fullH * hf, 1f, maxExtent * 2f));
            }

            // 同一朵云内：矮瓣先画、高瓣后画（高的盖在前面）
            puffs.Sort((a, b) => puffH[a].CompareTo(puffH[b]));

            int sub = 0;
            foreach (int p in puffs)
            {
                float pw = puffW[p];
                float ph = puffH[p];

                // 底边对齐平底基线（留一点随机抖动，避免多瓣底边叠出重复的横条）
                float baseJitter = (Hash01(ei, p + 701) - 0.5f) * ph * 0.22f;
                Vector3 pCenter = center + r2 * puffX[p] + u2 * (baseLine + ph * 0.5f + baseJitter);

                int cellIdx = ((e.cellX + e.cellY * cols) + p * 3) % cellCount;
                if (cellIdx < 0) cellIdx += cellCount;
                int cx = cellIdx % cols;
                int cy = cellIdx / cols;
                Vector2 cellOrigin = new Vector2(
                    (float)cx / cols,
                    1f - (float)(cy + 1) / rows);

                bool mirror = puffCount > 1
                    ? (e.mirror ^ (Hash01(ei, p + 97) > 0.5f))
                    : e.mirror;

                Vector3[] corner =
                {
                    pCenter - r2 * (pw * 0.5f) - u2 * (ph * 0.5f),
                    pCenter + r2 * (pw * 0.5f) - u2 * (ph * 0.5f),
                    pCenter + r2 * (pw * 0.5f) + u2 * (ph * 0.5f),
                    pCenter - r2 * (pw * 0.5f) + u2 * (ph * 0.5f),
                };
                Vector2[] luv = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };

                float brightHash = Mathf.Clamp01(e.brightHash + (Hash01(ei, p + 311) - 0.5f) * 0.3f);
                float dissolveSeed = Mathf.Clamp01(e.dissolveSeed + (Hash01(ei, p + 523) - 0.5f) * 0.2f);

                for (int k = 0; k < 4; k++)
                {
                    float ux = mirror ? 1f - luv[k].x : luv[k].x;
                    verts.Add(corner[k]);
                    uvs.Add(new Vector4(ux, luv[k].y, cellOrigin.x, cellOrigin.y));
                    uv1s.Add(new Vector2(Mathf.Clamp01(e.depth01), brightHash));
                    colors.Add(new Color(dissolveSeed, dissolveSeed, 0f, cloudCoverage));
                }
                quadDepths.Add(e.depth01);
                quadSubs.Add(sub++);
            }
        }

        // 画家排序：远的先画；同一朵云内矮瓣先画、高瓣后画
        int quadCount = quadDepths.Count;
        var order = new List<int>(quadCount);
        for (int q = 0; q < quadCount; q++) order.Add(q);
        order.Sort((a, b) =>
        {
            int c = quadDepths[b].CompareTo(quadDepths[a]);
            return c != 0 ? c : quadSubs[a].CompareTo(quadSubs[b]);
        });

        var tris = new List<int>(quadCount * 6);
        foreach (int q in order)
        {
            int b = q * 4;
            tris.Add(b + 0); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 3);
        }

        // ---- Mesh ----
        string meshDir = System.IO.Path.GetDirectoryName(layout.meshPath);
        if (!string.IsNullOrEmpty(meshDir) && !System.IO.Directory.Exists(meshDir))
            System.IO.Directory.CreateDirectory(meshDir);

        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(layout.meshPath);
        if (mesh == null)
        {
            mesh = new Mesh { name = "CloudDome" };
            AssetDatabase.CreateAsset(mesh, layout.meshPath);
        }
        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uv1s);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);

        // ---- Material ----
        string matDir = System.IO.Path.GetDirectoryName(layout.materialPath);
        if (!string.IsNullOrEmpty(matDir) && !System.IO.Directory.Exists(matDir))
            System.IO.Directory.CreateDirectory(matDir);

        var mat = AssetDatabase.LoadAssetAtPath<Material>(layout.materialPath);
        var shader = Shader.Find("Skybox/CloudBillboard");
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, layout.materialPath);
        }
        if (mat.shader != shader) mat.shader = shader;

        var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(layout.atlasPath);
        if (atlas != null) mat.SetTexture("_CloudTex", atlas);
        mat.SetFloat("_GridCols", cols);
        mat.SetFloat("_GridRows", rows);
        EditorUtility.SetDirty(mat);

        // ---- GameObject ----
        var go = GameObject.Find(layout.objectName);
        if (go == null) go = new GameObject(layout.objectName);
        var mf = go.GetComponent<MeshFilter>();
        if (mf == null) mf = go.AddComponent<MeshFilter>();
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) mr = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = mesh;
        mr.sharedMaterial = mat;
        mr.sortingOrder = layout.sortingOrder;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        // 新物体自动挂上跟随相机的支架（已存在则原样不动，避免影响现有层）
        if (go.GetComponent<SkyCloudsRig>() == null)
        {
            var rig = go.AddComponent<SkyCloudsRig>();
            rig.targetCamera = Camera.main;
            rig.cloudRenderer = mr;
        }

        EditorUtility.SetDirty(layout);
        AssetDatabase.SaveAssets();
        if (quadCount == 0)
            Debug.LogWarning("[CloudMeshBuilder] 布局里没有启用的云（列表为空，或条目 enabled 都没勾）");
        return mesh;
    }

    // depth01 -> 壳半径（近/中/远三层，产生纵深）
    private static float RadiusFor(CloudLayout layout, float depth01)
    {
        if (!layout.useShells) return layout.radius;

        float near = Mathf.Max(1f, layout.shellRadiusNear);
        float mid = Mathf.Max(near, layout.shellRadiusMid);
        float far = Mathf.Max(mid, layout.shellRadiusFar);

        depth01 = Mathf.Clamp01(depth01);
        return depth01 < 0.5f
            ? Mathf.Lerp(near, mid, depth01 * 2f)
            : Mathf.Lerp(mid, far, (depth01 - 0.5f) * 2f);
    }

    // 确定性 hash（同一 entry/瓣号 每帧结果一致）
    private static float Hash01(int a, int b)
    {
        unchecked
        {
            int h = a * 374761393 + b * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x7fffffff) / (float)0x7fffffff;
        }
    }
}
