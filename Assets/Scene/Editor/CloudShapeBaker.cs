using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把云素材图集切成单朵云，烘成 v1 billboard 用的通道图集。
///
/// 素材图集是黑底的整幅 sprite sheet，所以：
///   1. alpha 直接从亮度得到（黑底=0，云=1）
///   2. 边缘像素是"云 x 黑底"的混合，直接用亮度会偏暗 -> 按 alpha 反预乘
///   3. 连通域切成单朵云，挑最大的若干朵装进图集
///
/// 通道语义必须和 v1 的 CloudBillboard.shader 对齐：
///   R = 中调主体  G = 暗部云底  B = 高光（只给 rim）  A = 密度
///   shader 里「亮部 = 1 - R - G」，材质把 R 映到 _DarkColor、G 映到 _SecDarkColor。
///   所以三个权重必须恒和 = 1：R=wMid、G=wDark，亮部就自动等于 wBright。
/// </summary>
public static class CloudShapeBaker
{
    public const string SheetPath = "Assets/Scene/Tex/Sources_V2/云素材图集.jpg";
    private const string AtlasPath = "Assets/Scene/Tex/CloudPacked.png";
    private const int AtlasW = 2048, AtlasH = 2048, Cols = 4, Rows = 4, MaxShapes = Cols * Rows;
    private const int MinBlobArea = 2600, MinBlobH = 38;

    // ---------------------------------------------------------------
    // 切图
    // ---------------------------------------------------------------

    public struct Blob { public int x0, y0, x1, y1, area; }

    /// <summary>把素材图集切成单朵云。返回的数组顺序和烘图集时的格顺序一致。</summary>
    public static List<Blob> SliceSheet()
    {
        var res = new List<Blob>();
        var sheet = LoadSheet();
        if (sheet == null) return res;

        int W = sheet.width, H = sheet.height;
        var px = sheet.GetPixels32();
        var alpha = new float[W * H];
        for (int i = 0; i < px.Length; i++)
        {
            float l = (0.299f * px[i].r + 0.587f * px[i].g + 0.114f * px[i].b) / 255f;
            alpha[i] = Smooth(0.05f, 0.16f, l);
        }
        // 小幅闭运算：修掉 JPEG 压缩把云边缘打碎的小孔
        var m = Erode(Dilate(ToMask(alpha, 0.5f), W, H, 2), W, H, 2);

        var seen = new bool[m.Length];
        var st = new Stack<int>();
        for (int i = 0; i < m.Length; i++)
        {
            if (!m[i] || seen[i]) continue;
            seen[i] = true; st.Push(i);
            int x0 = W, y0 = H, x1 = -1, y1 = -1, a = 0;
            while (st.Count > 0)
            {
                int c = st.Pop(); int cy = c / W, cx = c - cy * W; a++;
                if (cx < x0) x0 = cx; if (cx > x1) x1 = cx;
                if (cy < y0) y0 = cy; if (cy > y1) y1 = cy;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + (d == 0 ? 1 : d == 1 ? -1 : 0);
                    int ny = cy + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (nx < 0 || nx >= W || ny < 0 || ny >= H) continue;
                    int ni = ny * W + nx;
                    if (!m[ni] || seen[ni]) continue;
                    seen[ni] = true; st.Push(ni);
                }
            }
            if (a >= MinBlobArea && (y1 - y0 + 1) >= MinBlobH)
                res.Add(new Blob { x0 = x0, y0 = y0, x1 = x1, y1 = y1, area = a });
        }

        // 取最大的若干朵（确定性排序，保证 SliceSheet 与烘图集顺序一致）
        res.Sort((a, b) =>
        {
            int c = b.area.CompareTo(a.area);
            if (c != 0) return c;
            c = a.y0.CompareTo(b.y0);
            return c != 0 ? c : a.x0.CompareTo(b.x0);
        });
        if (res.Count > MaxShapes) res.RemoveRange(MaxShapes, res.Count - MaxShapes);

        // 再按位置（左上 -> 右下）排，让图集里的顺序稳定可读
        res.Sort((a, b) =>
        {
            int c = (a.y0 / 128).CompareTo(b.y0 / 128);
            if (c != 0) return c;
            c = a.x0.CompareTo(b.x0);
            return c != 0 ? c : a.y0.CompareTo(b.y0);
        });
        return res;
    }

    /// <summary>各格的原始宽高比，给布局工具按参考图云块的形状挑变体用。</summary>
    public static float[] GetShapeAspects()
    {
        var b = SliceSheet();
        var r = new float[b.Count];
        for (int i = 0; i < b.Count; i++)
            r[i] = (b[i].x1 - b[i].x0 + 1) / (float)Mathf.Max(1, b[i].y1 - b[i].y0 + 1);
        return r;
    }

    // ---------------------------------------------------------------

    [MenuItem("Tools/Skybox Clouds/Bake Cloud Billboard Atlas From References", false, 41)]
    public static void BakeBillboardAtlas()
    {
        var sheet = LoadSheet();
        if (sheet == null) { Debug.LogError("[CloudShapeBaker] 找不到 " + SheetPath); return; }

        int W = sheet.width, H = sheet.height;
        var px = sheet.GetPixels32();
        var alpha = new float[W * H];
        var lum = new float[W * H];
        for (int i = 0; i < px.Length; i++)
        {
            float l = (0.299f * px[i].r + 0.587f * px[i].g + 0.114f * px[i].b) / 255f;
            float a = Smooth(0.05f, 0.16f, l);
            alpha[i] = a;
            lum[i] = l / Mathf.Max(a, 0.40f);      // 反预乘：去掉黑底的混合，边缘才不会发暗
        }

        var blobs = SliceSheet();
        if (blobs.Count == 0) { Debug.LogError("[CloudShapeBaker] 没切出云块"); return; }

        int cellW = AtlasW / Cols, cellH = AtlasH / Rows;
        var buf = new Color32[AtlasW * AtlasH];

        for (int i = 0; i < blobs.Count; i++)
        {
            var b = blobs[i];
            int bw = b.x1 - b.x0 + 1, bh = b.y1 - b.y0 + 1;
            float scale = Mathf.Min(cellW * 0.92f / bw, cellH * 0.82f / bh);
            int dw = Mathf.Max(2, Mathf.RoundToInt(bw * scale));
            int dh = Mathf.Max(2, Mathf.RoundToInt(bh * scale));

            int cx = i % Cols, cy = i / Cols;
            int ox = cx * cellW + (cellW - dw) / 2;
            int cellV0 = AtlasH - (cy + 1) * cellH;                 // 该格底边（自下而上的行号）
            int oy = cellV0 + Mathf.RoundToInt(cellH * 0.10f);      // 底边对齐，平底坐在同一条基线上

            var inside = new List<float>();
            for (int y = b.y0; y <= b.y1; y++)
            for (int x = b.x0; x <= b.x1; x++)
            {
                int si = y * W + x;
                if (alpha[si] > 0.5f) inside.Add(lum[si]);
            }
            if (inside.Count < 10) continue;
            inside.Sort();
            float lo = inside[(int)(0.06f * inside.Count)];
            float hi = inside[(int)(0.94f * inside.Count)];
            float inv = 1f / Mathf.Max(1e-4f, hi - lo);

            for (int y = 0; y < dh; y++)
            for (int x = 0; x < dw; x++)
            {
                int sx = Mathf.Clamp(b.x0 + (int)(x / scale), 0, W - 1);
                int sy = Mathf.Clamp(b.y0 + (int)(y / scale), 0, H - 1);
                int si = sy * W + sx;
                float a = alpha[si];
                if (a <= 0.004f) continue;

                float L = Mathf.Clamp01((lum[si] - lo) * inv);
                // 黑底素材的抗锯齿最外圈本身就是暗的（和黑底混合出来的），反预乘也救不回来，
                // 直接按亮度分档会在云周围描出一圈半透明深色。让边缘强制走"亮部"，
                // 奶油色和云体本身融在一起，看不出边。
                float edge = 1f - Smooth(0.05f, 0.45f, a);
                float wDark = (1f - Smooth(0.00f, 0.55f, L)) * (1f - edge);
                float wBright = Mathf.Max(Smooth(0.55f, 1.00f, L), edge);
                float wMid = Mathf.Clamp01(1f - wDark - wBright);

                int dx = ox + x, dy = oy + y;
                if (dx < 0 || dx >= AtlasW || dy < 0 || dy >= AtlasH) continue;
                buf[dy * AtlasW + dx] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(wMid * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(wDark * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(wBright * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
            }
        }

        WriteTexture(buf, AtlasW, AtlasH, AtlasPath, TextureWrapMode.Clamp);
        Debug.Log("[CloudShapeBaker] 已输出 " + AtlasPath + "  形状 " + blobs.Count + " 朵  格 "
            + Cols + "x" + Rows + "  " + AtlasW + "x" + AtlasH);
    }

    // ---------------------------------------------------------------
    // 工具
    // ---------------------------------------------------------------

    private static bool[] ToMask(float[] a, float t)
    {
        var m = new bool[a.Length];
        for (int i = 0; i < a.Length; i++) m[i] = a[i] > t;
        return m;
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

    private static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / Mathf.Max(1e-5f, b - a));
        return t * t * (3f - 2f * t);
    }

    private static Texture2D LoadSheet()
    {
        string p = System.IO.Path.Combine(Application.dataPath, "Scene/Tex/Sources_V2/云素材图集.jpg");
        if (!System.IO.File.Exists(p)) { Debug.LogWarning("[CloudShapeBaker] 找不到 " + p); return null; }
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(System.IO.File.ReadAllBytes(p));
        return t;
    }

    private static void WriteTexture(Color32[] px, int w, int h, string path, TextureWrapMode wrap)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null)
        {
            imp.wrapMode = wrap;
            imp.sRGBTexture = false;          // 当数据用，不要 sRGB 转换
            imp.mipmapEnabled = true;         // 缩小后需要 mipmap，否则会走样出细直横线
            imp.filterMode = FilterMode.Bilinear;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = false;
            imp.maxTextureSize = 2048;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }
        AssetDatabase.SaveAssets();
    }
}
