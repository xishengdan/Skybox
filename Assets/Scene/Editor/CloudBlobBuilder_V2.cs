using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// v2 网格云几何生成器（P1a 原型）
///
/// 流程：
///   1. 在水平圆盘内布球（中间高、边缘小）-> 一朵"棉花堆"
///   2. 隐式场 f(x) = smin_i(|x-ci| - ri)，多项式平滑并集让球"融"在一起
///   3. f = max(f, basePlane - y) 与半空间求交 -> 平底
///   4. Surface Nets 提取等值面（不需要 Marching Cubes 的查找表，拓扑天然封闭）
///   5. 法线由场的中心差分梯度解析求出
///   6. 顶点色 R 存一个便宜的 AO（被越多球包住越暗），用于让瓣间缝隙读得出来
///
/// 只用 v2 新文件，不碰 v1 任何代码。
/// </summary>
public static class CloudBlobBuilder_V2
{
    public struct Blob
    {
        public Vector3 c;
        public float r;
    }

    public class Params
    {
        [Tooltip("随机种子")] public int seed = 20260915;

        [Header("球体布局")]
        public int blobCount = 8;
        public float radiusMin = 0.34f;
        public float radiusMax = 0.55f;
        [Tooltip("穹顶半径（整体大小）")] public float spread = 1.00f;
        [Tooltip("纵深压缩（Z / X）")] public float depthRatio = 0.72f;
        [Tooltip("穹顶高度：越高整体越'方'，越小越扁")] public float heightBias = 0.88f;
        [Tooltip("球心往内收多少：0=球心贴在壳面上(鼓包最明显)，越大越平滑")] public float blobInset = 0.03f;

        [Header("场的形状")]
        [Tooltip("smin 平滑系数：越大越融合（糊成一团），越小越能看出球（想让瓣清楚就调小）")]
        public float smoothK = 0.25f;
        [Tooltip("平底裁切平面（球体局部空间 Y）")] public float basePlane = 0f;

        [Header("提取")]
        [Tooltip("长边体素分辨率，越高越细越慢（要显示噪声至少要 60+）")] public int resolution = 64;
        [Tooltip("包围盒外扩")] public float padding = 0.18f;

        [Header("输出")]
        [Tooltip("生成后缩放到这个高度（世界单位）")] public float targetHeight = 5f;

        [Header("AO")]
        [Tooltip("半球 AO 的探测距离（球体单位）")] public float aoInset = 0.40f;
        [Tooltip("保留字段（当前算法不用）")] public float aoScale = 3f;

        [Header("噪声位移（P1b：把光滑球打碎成菜花）")]
        [Tooltip("FBM 基础频率，越大细节越小（低频=让瓣本身不规则，推荐 2 左右）")] public float noiseFreq = 2.0f;
        [Tooltip("FBM 层数")] public int noiseOctaves = 3;
        [Tooltip("沿法线的位移幅度（球体单位）。0 = 关掉噪声")] public float noiseAmp = 0.12f;
        [Tooltip("额外沿 Y 的位移比例（让顶部更毛糙）")] public float noiseVertical = 0.30f;
        [Tooltip("平底往上多少距离内逐渐关掉位移（保住平底）")] public float noiseBaseFade = 0.22f;
    }

    public class Result
    {
        public Mesh mesh;
        public int vertexCount;
        public int triangleCount;
        public double ms;
        public Vector3 size;        // 缩放后的世界尺寸
        public float aspectXY;      // 侧视宽高比（和参考图的 1.84 / 2.26 / 1.11 对比）
        public float fillXY;        // 侧视投影填充率（参考图是 62~69%）
        public int gridX, gridY, gridZ;
        public int blobCount;
    }

    private static readonly int[] CornerOffset =
    {
        0,0,0,  1,0,0,  0,1,0,  1,1,0,
        0,0,1,  1,0,1,  0,1,1,  1,1,1
    };

    private static readonly int[,] EdgeVerts =
    {
        {0,1},{1,3},{3,2},{2,0},   // z = 0 面
        {4,5},{5,7},{7,6},{6,4},   // z = 1 面
        {0,4},{1,5},{2,6},{3,7}    // 竖边
    };

    // ============================================================

    public static Result Build(Params p)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var blobs = LayoutBlobs(p);

        // ---------- 包围盒 ----------
        Vector3 bmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 bmax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var b in blobs)
        {
            bmin = Vector3.Min(bmin, b.c - Vector3.one * b.r);
            bmax = Vector3.Max(bmax, b.c + Vector3.one * b.r);
        }
        bmin.y = Mathf.Max(bmin.y, p.basePlane - 0.25f);   // 裁切面以下没必要采样太多
        Vector3 pad = (bmax - bmin) * p.padding + Vector3.one * 1e-4f;
        bmin -= pad; bmax += pad;

        Vector3 span = bmax - bmin;
        float maxDim = Mathf.Max(span.x, Mathf.Max(span.y, span.z));
        int nx = Mathf.Max(6, Mathf.RoundToInt(p.resolution * span.x / maxDim));
        int ny = Mathf.Max(6, Mathf.RoundToInt(p.resolution * span.y / maxDim));
        int nz = Mathf.Max(6, Mathf.RoundToInt(p.resolution * span.z / maxDim));

        int sx = nx + 1, sy = ny + 1, sz = nz + 1;
        float vx = span.x / nx, vy = span.y / ny, vz = span.z / nz;

        // ---------- 采样隐式场 ----------
        var grid = new float[sx * sy * sz];
        for (int k = 0; k < sz; k++)
        {
            float wz = bmin.z + k * vz;
            for (int j = 0; j < sy; j++)
            {
                float wy = bmin.y + j * vy;
                int rowBase = j * sx + k * sx * sy;
                for (int i = 0; i < sx; i++)
                {
                    Vector3 q = new Vector3(bmin.x + i * vx, wy, wz);
                    grid[i + rowBase] = Field(q, blobs, p);
                }
            }
        }

        // ---------- Surface Nets：每个表面立方体放一个顶点 ----------
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var cubeVert = new int[nx * ny * nz];
        for (int i = 0; i < cubeVert.Length; i++) cubeVert[i] = -1;

        var cornerVal = new float[8];
        var cornerPos = new Vector3[8];

        for (int k = 0; k < nz; k++)
        for (int j = 0; j < ny; j++)
        for (int i = 0; i < nx; i++)
        {
            bool anyIn = false, anyOut = false;
            for (int c = 0; c < 8; c++)
            {
                int ci = i + CornerOffset[c * 3];
                int cj = j + CornerOffset[c * 3 + 1];
                int ck = k + CornerOffset[c * 3 + 2];
                cornerVal[c] = grid[ci + cj * sx + ck * sx * sy];
                cornerPos[c] = bmin + new Vector3(ci * vx, cj * vy, ck * vz);
                if (cornerVal[c] < 0f) anyIn = true; else anyOut = true;
            }
            if (!(anyIn && anyOut)) continue;

            Vector3 sum = Vector3.zero;
            int cnt = 0;
            for (int e = 0; e < 12; e++)
            {
                int a = EdgeVerts[e, 0], b = EdgeVerts[e, 1];
                float va = cornerVal[a], vb = cornerVal[b];
                if ((va < 0f) == (vb < 0f)) continue;
                float t = va / (va - vb);
                sum += Vector3.Lerp(cornerPos[a], cornerPos[b], t);
                cnt++;
            }
            if (cnt == 0) continue;

            Vector3 vp = sum / cnt;
            cubeVert[i + j * nx + k * nx * ny] = verts.Count;
            verts.Add(vp);
            normals.Add(Gradient(vp, blobs, p, Mathf.Min(vx, Mathf.Min(vy, vz)) * 0.5f));
            colors.Add(new Color(Ao(vp, normals[normals.Count - 1], blobs, p), 1f, 1f, 1f));
        }

        // ---------- 连接：每条跨面网格边生成一个四边形 ----------
        var tris = new List<int>();

        // X 方向的边
        for (int k = 1; k < nz; k++)
        for (int j = 1; j < ny; j++)
        for (int i = 0; i < nx; i++)
        {
            float s0 = grid[i + j * sx + k * sx * sy];
            float s1 = grid[(i + 1) + j * sx + k * sx * sy];
            if ((s0 < 0f) == (s1 < 0f)) continue;
            EmitQuad(tris, verts, normals, blobs, p,
                cubeVert[i + j * nx + k * nx * ny],
                cubeVert[i + (j - 1) * nx + k * nx * ny],
                cubeVert[i + (j - 1) * nx + (k - 1) * nx * ny],
                cubeVert[i + j * nx + (k - 1) * nx * ny]);
        }
        // Y 方向的边（相邻立方体沿 X 与 Z）
        for (int k = 1; k < nz; k++)
        for (int j = 0; j < ny; j++)
        for (int i = 1; i < nx; i++)
        {
            float s0 = grid[i + j * sx + k * sx * sy];
            float s1 = grid[i + (j + 1) * sx + k * sx * sy];
            if ((s0 < 0f) == (s1 < 0f)) continue;
            EmitQuad(tris, verts, normals, blobs, p,
                cubeVert[i + j * nx + k * nx * ny],
                cubeVert[i + j * nx + (k - 1) * nx * ny],
                cubeVert[(i - 1) + j * nx + (k - 1) * nx * ny],
                cubeVert[(i - 1) + j * nx + k * nx * ny]);
        }
        // Z 方向的边
        for (int k = 0; k < nz; k++)
        for (int j = 1; j < ny; j++)
        for (int i = 1; i < nx; i++)
        {
            float s0 = grid[i + j * sx + k * sx * sy];
            float s1 = grid[i + j * sx + (k + 1) * sx * sy];
            if ((s0 < 0f) == (s1 < 0f)) continue;
            EmitQuad(tris, verts, normals, blobs, p,
                cubeVert[i + j * nx + k * nx * ny],
                cubeVert[(i - 1) + j * nx + k * nx * ny],
                cubeVert[(i - 1) + (j - 1) * nx + k * nx * ny],
                cubeVert[i + (j - 1) * nx + k * nx * ny]);
        }

        if (verts.Count == 0)
        {
            sw.Stop();
            return new Result { mesh = null, ms = sw.Elapsed.TotalMilliseconds, blobCount = blobs.Count };
        }

        // ---------- P1b：顶点噪声位移（把光滑球打碎成菜花） ----------
        bool useNoise = p.noiseAmp > 1e-5f;
        if (useNoise)
        {
            // 用种子生成噪声偏移，换个种子就是另一朵云的细节
            var nr = new System.Random(p.seed * 7919 + 13);
            Vector3 nOff = new Vector3((float)nr.NextDouble() * 100f, (float)nr.NextDouble() * 100f, (float)nr.NextDouble() * 100f);

            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 vp = verts[i];
                Vector3 n = normals[i];

                float f = Fbm3(vp * p.noiseFreq + nOff, p.noiseOctaves);   // 0..1
                float d = (f - 0.5f) * 2f * p.noiseAmp;                     // -amp..+amp

                // 平底附近逐渐关掉位移，保住平底不被弄皱
                float baseMask = p.noiseBaseFade <= 1e-4f
                    ? 1f
                    : Mathf.Clamp01((vp.y - p.basePlane) / p.noiseBaseFade);

                Vector3 disp = n * (d * baseMask);
                if (Mathf.Abs(p.noiseVertical) > 1e-4f)
                    disp.y += d * p.noiseVertical * baseMask;

                verts[i] = vp + disp;
            }
        }

        // ---------- 缩放到目标高度 ----------
        Vector3 lo = verts[0], hi = verts[0];
        foreach (var v in verts) { lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
        float scale = p.targetHeight / Mathf.Max(1e-4f, hi.y - lo.y);
        for (int i = 0; i < verts.Count; i++) verts[i] *= scale;

        // ---------- Mesh ----------
        var mesh = new Mesh { name = "CloudBlob_V2" };
        if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        if (useNoise) mesh.RecalculateNormals();   // 位移后原解析法线不再成立，重算
        mesh.RecalculateBounds();

        var size = mesh.bounds.size;
        sw.Stop();

        return new Result
        {
            mesh = mesh,
            vertexCount = verts.Count,
            triangleCount = tris.Count / 3,
            ms = sw.Elapsed.TotalMilliseconds,
            size = size,
            aspectXY = size.x / Mathf.Max(1e-4f, size.y),
            fillXY = ComputeFill(mesh),
            gridX = nx, gridY = ny, gridZ = nz,
            blobCount = blobs.Count
        };
    }

    // ============================================================

    /// <summary>
    /// 球体布局：在压扁的穹顶壳上用 Fibonacci 球面分布铺球（天然均匀、左右对称），
    /// 球心略往内收 -> 每个球从表面鼓出来，轮廓就是一圈圆瓣（菜花状）。
    /// 用随机取方向会成团、左右不对称，所以特意用确定性的黄金角分布 + 轻微抖动。
    /// </summary>
    public static List<Blob> LayoutBlobs(Params p)
    {
        var rand = new System.Random(p.seed);
        var list = new List<Blob>();
        float R() => (float)rand.NextDouble();

        int n = Mathf.Max(3, p.blobCount);
        const float goldenAngle = 2.39996323f;

        for (int i = 0; i < n; i++)
        {
            // 上半球面均匀分布：cosTheta 均匀 == 球面面积均匀
            float t = (i + 0.5f) / n;
            float cosTheta = Mathf.Clamp01(1f - t);
            float theta = Mathf.Acos(cosTheta) + (R() - 0.5f) * 0.20f;
            float phi = i * goldenAngle + (R() - 0.5f) * 0.55f;
            theta = Mathf.Clamp(theta, 0f, Mathf.PI * 0.5f);

            float sx = Mathf.Sin(theta) * Mathf.Cos(phi);
            float sz = Mathf.Sin(theta) * Mathf.Sin(phi);
            float sy = Mathf.Cos(theta);

            Vector3 shell = new Vector3(sx * p.spread, sy * p.heightBias, sz * p.spread * p.depthRatio);
            Vector3 c = shell * (1f - p.blobInset);
            c.y += p.radiusMin * 0.45f;                 // 抬离平底裁切面

            // 越靠边缘的球稍微小一点，顶部大一点
            float sizeT = Mathf.Lerp(1f, 0.82f, Mathf.Clamp01(sy < 0f ? 1f : 1f - sy));
            float r = Mathf.Lerp(p.radiusMin, p.radiusMax, R()) * sizeT;
            list.Add(new Blob { c = c, r = r });
        }

        // 中央补一个，避免顶部塌陷
        list.Add(new Blob
        {
            c = new Vector3(0f, p.heightBias * 0.45f, 0f),
            r = Mathf.Lerp(p.radiusMin, p.radiusMax, 0.85f)
        });
        return list;
    }

    public static float Field(Vector3 q, List<Blob> blobs, Params p)
    {
        float d = (q - blobs[0].c).magnitude - blobs[0].r;
        for (int i = 1; i < blobs.Count; i++)
        {
            float di = (q - blobs[i].c).magnitude - blobs[i].r;
            d = SmoothMin(d, di, p.smoothK);
        }
        return Mathf.Max(d, p.basePlane - q.y);       // 平底：与半空间 y >= basePlane 求交
    }

    private static float SmoothMin(float a, float b, float k)
    {
        if (k <= 1e-5f) return Mathf.Min(a, b);
        float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
        return Mathf.Lerp(b, a, h) - k * h * (1f - h);
    }

    // ==================== 3D 值噪声 + FBM（P1b 用） ====================

    private static float Hash13(int x, int y, int z)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + z * 1274126177;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x7fffffff) / (float)0x7fffffff;
        }
    }

    private static float ValueNoise3(Vector3 q)
    {
        int xi = Mathf.FloorToInt(q.x), yi = Mathf.FloorToInt(q.y), zi = Mathf.FloorToInt(q.z);
        float xf = q.x - xi, yf = q.y - yi, zf = q.z - zi;
        float u = xf * xf * (3f - 2f * xf);
        float v = yf * yf * (3f - 2f * yf);
        float w = zf * zf * (3f - 2f * zf);

        float n000 = Hash13(xi, yi, zi), n100 = Hash13(xi + 1, yi, zi);
        float n010 = Hash13(xi, yi + 1, zi), n110 = Hash13(xi + 1, yi + 1, zi);
        float n001 = Hash13(xi, yi, zi + 1), n101 = Hash13(xi + 1, yi, zi + 1);
        float n011 = Hash13(xi, yi + 1, zi + 1), n111 = Hash13(xi + 1, yi + 1, zi + 1);

        float x00 = Mathf.Lerp(n000, n100, u), x10 = Mathf.Lerp(n010, n110, u);
        float x01 = Mathf.Lerp(n001, n101, u), x11 = Mathf.Lerp(n011, n111, u);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, v), Mathf.Lerp(x01, x11, v), w);
    }

    /// <summary>分形噪声，返回 0..1。</summary>
    private static float Fbm3(Vector3 q, int octaves)
    {
        float sum = 0f, amp = 0.5f, norm = 0f, freq = 1f;
        int n = Mathf.Clamp(octaves, 1, 6);
        for (int i = 0; i < n; i++)
        {
            sum += amp * ValueNoise3(q * freq);
            norm += amp;
            amp *= 0.5f;
            freq *= 2.03f;      // 非整数倍，避免格点重合产生网格感
        }
        return norm > 0f ? sum / norm : 0.5f;
    }

    /// <summary>场的中心差分梯度 = 解析法线（比 RecalculateNormals 干净）。</summary>
    private static Vector3 Gradient(Vector3 q, List<Blob> blobs, Params p, float e)
    {
        float dx = Field(q + new Vector3(e, 0, 0), blobs, p) - Field(q - new Vector3(e, 0, 0), blobs, p);
        float dy = Field(q + new Vector3(0, e, 0), blobs, p) - Field(q - new Vector3(0, e, 0), blobs, p);
        float dz = Field(q + new Vector3(0, 0, e), blobs, p) - Field(q - new Vector3(0, 0, e), blobs, p);
        var g = new Vector3(dx, dy, dz);
        return g.sqrMagnitude < 1e-12f ? Vector3.up : g.normalized;
    }

    /// <summary>
    /// 半球采样 AO：在法线方向的半球上取 8 个方向，各前进一小步，
    /// 统计有多少方向撞进了云里。缝隙/底部会被邻球挡得多 -> 变暗。
    /// （之前"数包含它的球"那种写法检测不到浅缝隙，已验证无效。）
    /// </summary>
    private static float Ao(Vector3 vp, Vector3 n, List<Blob> blobs, Params p)
    {
        if (p.aoInset <= 1e-4f) return 1f;

        Vector3 t1 = Vector3.Cross(n, Vector3.up);
        if (t1.sqrMagnitude < 1e-4f) t1 = Vector3.right;
        t1.Normalize();
        Vector3 t2 = Vector3.Cross(n, t1);

        const int K = 8;
        int occluded = 0;
        for (int i = 0; i < K; i++)
        {
            float a = (i + 0.5f) / K * Mathf.PI * 2f;
            // 与法线约 55° 的方向（半球采样）
            Vector3 d = (n * 0.57f + (t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a)) * 0.82f).normalized;
            if (Field(vp + d * p.aoInset, blobs, p) < 0f) occluded++;
        }
        return 1f - (float)occluded / K;
    }

    /// <summary>用一个四边形的 4 个角点生成两个三角形，并按场的梯度自动纠正绕序。</summary>
    private static void EmitQuad(List<int> tris, List<Vector3> verts, List<Vector3> normals,
                                 List<Blob> blobs, Params p, int a, int b, int c, int d)
    {
        if (a < 0 || b < 0 || c < 0 || d < 0) return;

        Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
        // 用中点的场梯度判断"外"侧；不一致就翻面
        Vector3 mid = (verts[a] + verts[b] + verts[c] + verts[d]) * 0.25f;
        Vector3 g = Gradient(mid, blobs, p, 0.02f);
        if (Vector3.Dot(n, g) < 0f) { int t = b; b = d; d = t; }

        tris.Add(a); tris.Add(b); tris.Add(c);
        tris.Add(a); tris.Add(c); tris.Add(d);
    }

    /// <summary>侧视（XY）投影填充率，和参考剪影的 62~69% 直接对比。</summary>
    private static float ComputeFill(Mesh mesh)
    {
        const int GW = 96, GH = 96;
        var cov = new bool[GW * GH];
        var v = mesh.vertices;
        var idx = mesh.triangles;
        if (v.Length == 0 || idx.Length == 0) return 0f;

        Vector2 lo = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 hi = new Vector2(float.MinValue, float.MinValue);
        foreach (var q in v) { lo = Vector2.Min(lo, q); hi = Vector2.Max(hi, q); }
        Vector2 s = hi - lo;
        if (s.x <= 0f || s.y <= 0f) return 0f;

        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            Vector2 p0 = ((Vector2)v[idx[t]] - lo) / s * new Vector2(GW - 1, GH - 1);
            Vector2 p1 = ((Vector2)v[idx[t + 1]] - lo) / s * new Vector2(GW - 1, GH - 1);
            Vector2 p2 = ((Vector2)v[idx[t + 2]] - lo) / s * new Vector2(GW - 1, GH - 1);

            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))));
            int x1 = Mathf.Min(GW - 1, Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))));
            int y1 = Mathf.Min(GH - 1, Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))));

            float d = (p1.y - p2.y) * (p0.x - p2.x) + (p2.x - p1.x) * (p0.y - p2.y);
            if (Mathf.Abs(d) < 1e-6f) continue;

            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (cov[y * GW + x]) continue;
                float px = x, py = y;
                float l0 = ((p1.y - p2.y) * (px - p2.x) + (p2.x - p1.x) * (py - p2.y)) / d;
                float l1 = ((p2.y - p0.y) * (px - p2.x) + (p0.x - p2.x) * (py - p2.y)) / d;
                float l2 = 1f - l0 - l1;
                if (l0 >= 0f && l1 >= 0f && l2 >= 0f) cov[y * GW + x] = true;
            }
        }

        int hit = 0;
        foreach (var b in cov) if (b) hit++;
        return 100f * hit / (GW * GH);
    }
}
