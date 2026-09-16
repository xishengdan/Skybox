using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 主层 SkyClouds 的布局：只负责地平线附近，从 主参考_天空.png 直接提取。
///
/// 重点是"巨型云"而不是碎块：参考图里成片的云各自是一个连通域，
/// 每个连通域只放 1~2 张面片，每张按**整个云块**的高度定尺寸 —— 不是按小网格切碎。
/// 上层天空交给 SkyCloudsMid / SkyCloudsHigh，这里不做。
/// </summary>
public static class CloudLayoutFromReference
{
    private const string MainLayout = "Assets/Scene/Models/CloudLayout.asset";

    [MenuItem("Tools/Skybox Clouds/Build Layout From Reference Images", false, 21)]
    public static void BuildFromReferences()
    {
        var lay = AssetDatabase.LoadAssetAtPath<CloudLayout>(MainLayout);
        if (lay == null) { Debug.LogError("[LayoutFromRef] 找不到 " + MainLayout); return; }

        float[] aspects = CloudShapeBaker.GetShapeAspects();
        if (aspects.Length == 0) { Debug.LogError("[LayoutFromRef] 云素材图集切不出云块"); return; }
        lay.atlasCols = 4;
        lay.atlasRows = 4;

        var report = new System.Text.StringBuilder();
        report.AppendLine("变体 " + aspects.Length + " 种，图集 " + lay.atlasCols + "x" + lay.atlasRows);

        var list = new List<CloudEntry>();
        int n = AddHorizonMasses(list, lay, report, aspects, "主参考_天空.png",
            yTopFrac: 0.02f, yBotFrac: 0.78f, elevTop: 26f, elevBot: 0f,
            gridDiv: 0.95f, sizeMul: 1.15f,
            minLum: 0.62f, maxSat: 0.30f, minAreaFrac: 0.00010f);

        lay.clouds = list;
        EditorUtility.SetDirty(lay);
        CloudMeshBuilder.Build(lay);
        AssetDatabase.SaveAssets();

        report.Insert(0, "主层 " + list.Count + " 朵（只地平线附近）\n");
        Debug.Log("[LayoutFromRef]\n" + report);
    }

    /// <summary>多瓣/多层网格用的是同一个图集，格数要跟着图集走，否则会采到错误的格子。</summary>
    [MenuItem("Tools/Skybox Clouds/Repair Mid High Atlas Grid", false, 22)]
    public static void RepairMidHighGrid()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var p in new[] { "Assets/Scene/Models/CloudLayoutMid.asset", "Assets/Scene/Models/CloudLayoutHigh.asset" })
        {
            var lay = AssetDatabase.LoadAssetAtPath<CloudLayout>(p);
            if (lay == null) { sb.AppendLine("缺 " + p); continue; }
            int oldCols = Mathf.Max(1, lay.atlasCols), oldRows = Mathf.Max(1, lay.atlasRows);
            foreach (var e in lay.clouds)
            {
                int oldIdx = Mathf.Clamp(e.cellX + e.cellY * oldCols, 0, oldCols * oldRows - 1);
                e.cellX = oldIdx % 4;
                e.cellY = oldIdx / 4;
            }
            lay.atlasCols = 4; lay.atlasRows = 4;
            EditorUtility.SetDirty(lay);
            CloudMeshBuilder.Build(lay);
            sb.AppendLine(System.IO.Path.GetFileName(p) + " : " + oldCols + "x" + oldRows + " -> 4x4，格号已重映射");
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[LayoutFromRef] " + sb.ToString().Replace("\n", " | "));
    }

    // ---------------------------------------------------------------

    private static int AddHorizonMasses(List<CloudEntry> outList, CloudLayout lay, System.Text.StringBuilder report,
        float[] aspects, string file, float yTopFrac, float yBotFrac, float elevTop, float elevBot,
        float gridDiv, float sizeMul, float minLum, float maxSat, float minAreaFrac)
    {
        var tex = Load(file);
        if (tex == null) { report.AppendLine("  缺 " + file); return 0; }

        int W = tex.width, H = tex.height;
        var px = tex.GetPixels32();

        int y0 = Mathf.Clamp(Mathf.RoundToInt(yTopFrac * H), 0, H - 1);
        int y1 = Mathf.Clamp(Mathf.RoundToInt(yBotFrac * H), y0, H - 1);

        // 云像素：够亮 + 够不饱和。这张图的天空是淡青色，和奶油云数值很接近，
        // 所以还要加色相判据（云 r>=b 偏暖，青天空 b>r 偏冷）。
        var mask = new bool[W * H];
        int cloudPx = 0;
        for (int y = y0; y <= y1; y++)
        for (int x = 0; x < W; x++)
        {
            var c = px[y * W + x];
            float r = c.r / 255f, g = c.g / 255f, bl = c.b / 255f;
            float lum = 0.299f * r + 0.587f * g + 0.114f * bl;
            if (r >= bl - 0.01f && lum >= minLum && lum <= minLum + 0.38f) { mask[y * W + x] = true; cloudPx++; }
        }
        mask = Erode(Dilate(mask, W, H, 3), W, H, 3);      // 闭运算：把云内部的暗部接起来

        int minArea = Mathf.Max(60, Mathf.RoundToInt(minAreaFrac * W * H));
        var blobs = FindBlobs(mask, W, H, minArea);
        // 丢掉又细又长的条状连通域（参考图里地平线上的细缝），它们会变成怪异的扁片
        blobs.RemoveAll(b => (b.y1 - b.y0 + 1) < 34 || (b.x1 - b.x0 + 1) / (float)(b.y1 - b.y0 + 1) > 4.5f);
        report.AppendLine(file + " : " + W + "x" + H + "  云覆盖 " + (100f * cloudPx / (W * H)).ToString("F1")
            + "%  云块 " + blobs.Count + " 个");

        // 等角映射：横竖同一个"度/像素"，否则方位被拉长、云块包围盒会变竖直
        float degPerPx = (elevTop - elevBot) / Mathf.Max(1, y1 - y0 + 1);
        float fovH = W * degPerPx;
        // 相邻两次平铺要重叠一半左右，否则绕天一圈会有明显空档
        int tileN = Mathf.Max(1, Mathf.RoundToInt(360f / (fovH * 0.55f)));
        float tileSpan = 360f / tileN;
        report.AppendLine("   等角 " + degPerPx.ToString("F4") + "°/px  水平覆盖 " + fovH.ToString("F0") + "°  绕天 " + tileN + " 次");

        int added = 0;
        int cols = Mathf.Max(1, lay.atlasCols);
        var cells = new List<Vector3>();
        foreach (var b in blobs)
        {
            float bh = b.y1 - b.y0 + 1;
            float bw = b.x1 - b.x0 + 1;
            float rawAspect = bw / Mathf.Max(1f, bh);
            // 大云：每张面片按**整个云块**的高度定尺寸，网格步长≈云块高度 -> 一块只出 1~2 张
            int step = Mathf.Max(24, Mathf.RoundToInt(bh * gridDiv));
            float angH = Mathf.Clamp(bh * degPerPx * sizeMul, 2.5f, 17f);

            var sb2 = new System.Text.StringBuilder();
            cells.Clear();
            for (int gy = b.y0 + step / 2; gy <= b.y1; gy += step)
            for (int gx = b.x0 + step / 2; gx <= b.x1; gx += step)
            {
                int gi = (gx / Mathf.Max(1, step)) * 131 + (gy / Mathf.Max(1, step)) * 17;
                int px2 = gx + Mathf.RoundToInt((Hash01(gi, 11) - 0.5f) * step * 0.55f);
                int py2 = gy + Mathf.RoundToInt((Hash01(gi, 23) - 0.5f) * step * 0.55f);

                int cov = 0, tot = 0, q = Mathf.Max(1, step / 6);
                for (int yy = py2 - step / 4; yy <= py2 + step / 4; yy += q)
                for (int xx = px2 - step / 4; xx <= px2 + step / 4; xx += q)
                {
                    if (xx < 0 || xx >= W || yy < 0 || yy >= H) continue;
                    tot++; if (mask[yy * W + xx]) cov++;
                }
                if (tot == 0 || cov < tot * 0.45f) continue;
                cells.Add(new Vector3(px2, py2, angH));
            }
            sb2.Append("   云块 " + bw + "x" + bh + " -> " + cells.Count + " 张\n");
            report.Append(sb2);

            foreach (var cell in cells)
            {
                float elev = Mathf.Lerp(elevTop, elevBot, Mathf.InverseLerp(yTopFrac, yBotFrac, cell.y / (float)H));
                int shape = PickShape(rawAspect, cells.Count >= 4, aspects, added);

                for (int t2 = 0; t2 < tileN; t2++)
                {
                    float az = Mathf.Repeat(cell.x / (float)W * fovH + t2 * tileSpan, 360f);
                    float el = Mathf.Clamp(elev, 0.6f, 84f);
                    float d = Mathf.Lerp(0.06f, 0.94f, Mathf.Clamp01(Mathf.InverseLerp(78f, 1f, el)));
                    float radius = d < 0.5f ? Mathf.Lerp(lay.shellRadiusNear, lay.shellRadiusMid, d * 2f)
                                            : Mathf.Lerp(lay.shellRadiusMid, lay.shellRadiusFar, (d - 0.5f) * 2f);
                    float size = 2f * radius * Mathf.Tan(cell.z * 0.5f * Mathf.Deg2Rad);

                    outList.Add(new CloudEntry
                    {
                        enabled = true,
                        azimuthDeg = az,
                        elevationDeg = el,
                        size = size,
                        widthAspect = Mathf.Clamp(rawAspect, 1.4f, 2.6f),
                        rollDeg = (Hash01(added, 71) - 0.5f) * 6f,
                        mirror = ((t2 + added) & 1) == 1,
                        cellX = shape % cols,
                        cellY = shape / cols,
                        depth01 = d,
                        brightHash = Hash01(added, t2),
                        dissolveSeed = Hash01(added + 977, t2),
                        puffCount = 1,
                        puffSpread = 1f,
                        puffVariance = 0.3f,
                    });
                    added++;
                }
            }
        }
        report.AppendLine("   装入 " + added + " 朵（绕天 x" + tileN + "）");
        return added;
    }

    // ---------------------------------------------------------------

    private static int PickShape(float aspect, bool mass, float[] aspects, int seed)
    {
        float bestD = float.MaxValue;
        var cand = new List<int>();
        for (int i = 0; i < aspects.Length; i++)
        {
            if (mass && aspects[i] > 2.15f) continue;      // 成片的云团用圆润的变体填
            float d = Mathf.Abs(aspects[i] - aspect);
            if (d < bestD - 0.001f) { bestD = d; cand.Clear(); cand.Add(i); }
            else if (d <= bestD + 0.45f) cand.Add(i);
        }
        if (cand.Count == 0) return 0;
        return cand[Mathf.Abs(seed) % cand.Count];
    }

    private struct Blob { public int x0, y0, x1, y1; public int area; }

    private static List<Blob> FindBlobs(bool[] m, int w, int h, int minArea)
    {
        var seen = new bool[m.Length];
        var res = new List<Blob>();
        var st = new Stack<int>();
        for (int i = 0; i < m.Length; i++)
        {
            if (!m[i] || seen[i]) continue;
            seen[i] = true; st.Push(i);
            int x0 = w, y0 = h, x1 = -1, y1 = -1, area = 0;
            while (st.Count > 0)
            {
                int c = st.Pop(); int cy = c / w, cx = c - cy * w;
                area++;
                if (cx < x0) x0 = cx; if (cx > x1) x1 = cx;
                if (cy < y0) y0 = cy; if (cy > y1) y1 = cy;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + (d == 0 ? 1 : d == 1 ? -1 : 0);
                    int ny = cy + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    int ni = ny * w + nx;
                    if (!m[ni] || seen[ni]) continue;
                    seen[ni] = true; st.Push(ni);
                }
            }
            if (area >= minArea) res.Add(new Blob { x0 = x0, y0 = y0, x1 = x1, y1 = y1, area = area });
        }
        // 大的在前，方便看日志
        res.Sort((a, b) => b.area.CompareTo(a.area));
        return res;
    }

    private static bool[] Dilate(bool[] m, int w, int h, int r)
    {
        var t = new bool[m.Length];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            bool v = false;
            for (int d = -r; d <= r && !v; d++) { int nx = x + d; if (nx < 0 || nx >= w) continue; if (m[y * w + nx]) v = true; }
            t[y * w + x] = v;
        }
        var o = new bool[m.Length];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            bool v = false;
            for (int d = -r; d <= r && !v; d++) { int ny = y + d; if (ny < 0 || ny >= h) continue; if (t[ny * w + x]) v = true; }
            o[y * w + x] = v;
        }
        return o;
    }

    private static bool[] Erode(bool[] m, int w, int h, int r)
    {
        var t = new bool[m.Length];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            bool v = true;
            for (int d = -r; d <= r && v; d++) { int nx = x + d; if (nx < 0 || nx >= w) continue; if (!m[y * w + nx]) v = false; }
            t[y * w + x] = v;
        }
        var o = new bool[m.Length];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            bool v = true;
            for (int d = -r; d <= r && v; d++) { int ny = y + d; if (ny < 0 || ny >= h) continue; if (!t[ny * w + x]) v = false; }
            o[y * w + x] = v;
        }
        return o;
    }

    private static float Hash01(int a, int b)
    {
        unchecked
        {
            int hh = a * 374761393 + b * 668265263;
            hh = (hh ^ (hh >> 13)) * 1274126177;
            hh ^= hh >> 16;
            return (hh & 0x7fffffff) / (float)0x7fffffff;
        }
    }

    private static Texture2D Load(string name)
    {
        string p = System.IO.Path.Combine(Application.dataPath, "Scene/Tex/Sources_V2", name);
        if (!System.IO.File.Exists(p)) return null;
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(System.IO.File.ReadAllBytes(p));
        return t;
    }
}
