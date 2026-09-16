using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 从 Sources_V2 的参考图烘"天空球云层"用的云场贴图。
///
/// 为什么不用程序化 FBM：
///   FBM 长不出"设计过"的轮廓（多瓣、平底、有性格），质量天花板很低。
///   参考图是美术画好的云，直接拿来用才是正解。
///
/// 做法：
///   1. 载入三朵参考云（主参考 / 宽扁 / 高大）+ 它们已有的剪影（当 alpha）
///   2. 从参考图的亮度里抽出「形体高度」：亮的在顶、灰的在底，
///      映射成 0..1，再模糊一次 -> 平滑的高度场
///   3. 把三朵云按随机位置/缩放/±20°旋转撒进一张可平铺的场里
///      （旋转限制在 ±20°，才能保持光照方向一致，不然云底会朝天）
///   4. 输出 CloudLayerField.png： R = 密度(覆盖率)，G = 形体高度
///
/// shader 端：R 走覆盖率阈值，G 直接当伪高度做 ramp（不再靠启发式猜）
/// </summary>
public static class CloudShapeBaker
{
    private const int FieldSize = 1024;
    private const int Placements = 10;
    private const string SrcDir = "Assets/Scene/Tex/Sources_V2";
    private const string OutPath = "Assets/Scene/Tex/CloudLayerField.png";

    private class Shape
    {
        public int w, h;
        public float[] density;   // 0..1
        public float[] height;    // 0..1
    }

    [MenuItem("Tools/Skybox Clouds/Bake Cloud Layer From References", false, 41)]
    public static void Bake()
    {
        string[] srcNames = { "主参考图.jpg", "宽扁.jpg", "高大.jpg" };
        string[] silNames = { "剪影_主参考_V2.png", "剪影_宽扁_V2.png", "剪影_高大_V2.png" };

        var shapes = new List<Shape>();
        for (int i = 0; i < srcNames.Length; i++)
        {
            var sh = BuildShape(Load(srcNames[i]), Load(silNames[i]));
            if (sh != null)
            {
                shapes.Add(sh);
                Debug.Log("[CloudShapeBaker] " + srcNames[i] + " -> 精灵 " + sh.w + "x" + sh.h);
            }
        }
        if (shapes.Count == 0) { Debug.LogError("[CloudShapeBaker] 没有可用的参考图"); return; }

        var fieldD = new float[FieldSize * FieldSize];
        var fieldH = new float[FieldSize * FieldSize];
        var rnd = new System.Random(20260916);

        for (int n = 0; n < Placements; n++)
        {
            var sh = shapes[rnd.Next(shapes.Count)];
            float targetH = FieldSize * (0.12f + (float)rnd.NextDouble() * 0.14f);   // 精灵在场里的高度
            float scale = targetH / sh.h;
            // 每朵云给一个随机强度：这样 shader 抬阈值就能"筛掉弱云"，
            // 才能做到「地平线密、天顶疏」而不是整片一起变淡
            float strength = 0.55f + (float)rnd.NextDouble() * 0.45f;
            float sw = sh.w * scale, shh = targetH;
            float halfW = sw * 0.5f, halfH = shh * 0.5f;

            float ang = (((float)rnd.NextDouble() * 2f - 1f) * 5f) * Mathf.Deg2Rad;  // ±5°：大了云会歪
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            float cx = (float)rnd.NextDouble() * FieldSize;
            float cy = (float)rnd.NextDouble() * FieldSize;

            int ext = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(halfW * ca) + Mathf.Abs(halfH * sa),
                                                Mathf.Abs(halfW * sa) + Mathf.Abs(halfH * ca))) + 2;

            for (int dy = -ext; dy <= ext; dy++)
            for (int dx = -ext; dx <= ext; dx++)
            {
                float lx = dx, ly = dy;
                float sx = lx * ca + ly * sa;
                float sy = -lx * sa + ly * ca;
                if (Mathf.Abs(sx) > halfW || Mathf.Abs(sy) > halfH) continue;

                float u = (sx / halfW) * 0.5f + 0.5f;
                float v = (sy / halfH) * 0.5f + 0.5f;
                int spx = Mathf.Clamp((int)(u * sh.w), 0, sh.w - 1);
                int spy = Mathf.Clamp((int)(v * sh.h), 0, sh.h - 1);
                int si = spy * sh.w + spx;

                float d = sh.density[si] * strength;
                if (d <= 0.001f) continue;

                int fx = ((int)(cx + dx) % FieldSize + FieldSize) % FieldSize;
                int fy = ((int)(cy + dy) % FieldSize + FieldSize) % FieldSize;
                int fi = fy * FieldSize + fx;

                if (d > fieldD[fi]) { fieldD[fi] = d; fieldH[fi] = sh.height[si]; }
            }
        }

        int hit = 0; foreach (var d in fieldD) if (d > 0.5f) hit++;
        Debug.Log("[CloudShapeBaker] 云端覆盖 " + (100f * hit / fieldD.Length).ToString("F1") + "%");

        var px = new Color32[FieldSize * FieldSize];
        for (int i = 0; i < px.Length; i++)
        {
            px[i] = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(fieldD[i] * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(fieldH[i] * 255f), 0, 255),
                0, 255);
        }

        var tex = new Texture2D(FieldSize, FieldSize, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        System.IO.File.WriteAllBytes(OutPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(OutPath, ImportAssetOptions.ForceUpdate);
        var imp = AssetImporter.GetAtPath(OutPath) as TextureImporter;
        if (imp != null)
        {
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.sRGBTexture = false;
            imp.mipmapEnabled = true;
            imp.filterMode = FilterMode.Bilinear;
            imp.alphaSource = TextureImporterAlphaSource.None;
            imp.maxTextureSize = FieldSize;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[CloudShapeBaker] 已输出 " + OutPath + " (R=密度 G=高度, " + FieldSize + "x" + FieldSize + ", 可平铺)");
    }

    // ==================== v1 billboard 图集（2x4 格）====================

    private const string AtlasPath = "Assets/Scene/Tex/CloudPacked.png";
    private const int AtlasW = 1024, AtlasH = 1024, AtlasCols = 2, AtlasRows = 4;

    private class AtlasSprite
    {
        public int w, h;
        public float[] alpha, d1, d2, hl;
    }

    /// <summary>
    /// 用同一套参考图重烘 v1 的 billboard 图集，把天空球云层和面片云的风格统一。
    /// 通道语义照 v1 CloudBillboard：R=暗部1  G=暗部2  B=高光（只给 rim）  A=密度，
    /// shader 里「亮部 = 1 - R - G」。
    /// </summary>
    [MenuItem("Tools/Skybox Clouds/Bake Cloud Billboard Atlas From References", false, 42)]
    public static void BakeBillboardAtlas()
    {
        string[] srcNames = { "主参考图.jpg", "宽扁.jpg", "高大.jpg" };
        string[] silNames = { "剪影_主参考_V2.png", "剪影_宽扁_V2.png", "剪影_高大_V2.png" };

        var sprites = new List<AtlasSprite>();
        for (int i = 0; i < srcNames.Length; i++)
        {
            var s = BuildAtlasSprite(Load(srcNames[i]), Load(silNames[i]));
            if (s != null) { sprites.Add(s); Debug.Log("[CloudAtlasBaker] " + srcNames[i] + " -> " + s.w + "x" + s.h); }
        }
        if (sprites.Count == 0) { Debug.LogError("[CloudAtlasBaker] 没有可用的参考图"); return; }

        int cellW = AtlasW / AtlasCols, cellH = AtlasH / AtlasRows;
        var buf = new Color32[AtlasW * AtlasH];

        int cells = AtlasCols * AtlasRows;
        for (int c = 0; c < cells; c++)
        {
            var s = sprites[c % sprites.Count];
            float k = 1f - 0.09f * ((c / sprites.Count) % 3);                      // 三档尺寸，避免重复感
            float scale = Mathf.Min(cellW * 0.94f / s.w, cellH * 0.86f / s.h) * k;
            int dw = Mathf.Max(2, Mathf.RoundToInt(s.w * scale));
            int dh = Mathf.Max(2, Mathf.RoundToInt(s.h * scale));

            int cx = c % AtlasCols, cy = c / AtlasCols;
            int ox = cx * cellW + (cellW - dw) / 2;
            int cellV0 = AtlasH - (cy + 1) * cellH;                                // 该格底边的像素 y（v 向上）
            int oy = cellV0 + Mathf.RoundToInt(cellH * 0.07f);                     // 底边对齐平底基线

            for (int y = 0; y < dh; y++)
            for (int x = 0; x < dw; x++)
            {
                int sx = Mathf.Clamp((int)(x / scale), 0, s.w - 1);
                int sy = Mathf.Clamp((int)(y / scale), 0, s.h - 1);
                int si = sy * s.w + sx;
                float a = s.alpha[si];
                if (a <= 0.002f) continue;
                int px = ox + x, py = oy + y;
                if (px < 0 || px >= AtlasW || py < 0 || py >= AtlasH) continue;
                buf[py * AtlasW + px] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(s.d1[si] * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(s.d2[si] * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(s.hl[si] * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
            }
        }

        var tex = new Texture2D(AtlasW, AtlasH, TextureFormat.RGBA32, false);
        tex.SetPixels32(buf);
        System.IO.File.WriteAllBytes(AtlasPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);
        var imp = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
        if (imp != null)
        {
            imp.wrapMode = TextureWrapMode.Clamp;      // 图集不能 repeat，否则跨格渗色
            imp.sRGBTexture = false;
            imp.mipmapEnabled = true;
            imp.filterMode = FilterMode.Bilinear;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = false;
            imp.maxTextureSize = 2048;
            imp.textureCompression = TextureImporterCompression.Compressed;
            imp.SaveAndReimport();
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[CloudAtlasBaker] 已输出 " + AtlasPath + " (" + AtlasCols + "x" + AtlasRows + " 格, 1024x1024)");
    }

    /// <summary>把参考云拆成 alpha + 三个色阶（暗1 / 暗2 / 高光）。</summary>
    private static AtlasSprite BuildAtlasSprite(Texture2D src, Texture2D sil)
    {
        if (src == null || sil == null) return null;
        int W = src.width, H = src.height;
        var sp = src.GetPixels32();
        var mp = sil.GetPixels32();
        if (sp.Length != mp.Length) return null;

        bool hasA = false;
        for (int i = 0; i < mp.Length; i++) if (mp[i].a < 250) { hasA = true; break; }
        Color32 bg = mp[0];
        var mask = new bool[W * H];
        for (int i = 0; i < mp.Length; i++)
            mask[i] = hasA ? mp[i].a > 127
                           : (Mathf.Abs(mp[i].r - bg.r) + Mathf.Abs(mp[i].g - bg.g) + Mathf.Abs(mp[i].b - bg.b)) > 60;

        int x0 = W, y0 = H, x1 = -1, y1 = -1;
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
            if (mask[y * W + x])
            {
                if (x < x0) x0 = x; if (x > x1) x1 = x;
                if (y < y0) y0 = y; if (y > y1) y1 = y;
            }
        if (x1 < 0) return null;
        int cw = x1 - x0 + 1, ch = y1 - y0 + 1;

        var s = new AtlasSprite { w = cw, h = ch, alpha = new float[cw * ch], d1 = new float[cw * ch], d2 = new float[cw * ch], hl = new float[cw * ch] };

        var dens = new float[cw * ch];
        var lum = new float[cw * ch];
        var inside = new List<float>();
        for (int y = 0; y < ch; y++)
        for (int x = 0; x < cw; x++)
        {
            int di = y * cw + x;
            int si = (y + y0) * W + (x + x0);
            dens[di] = mask[si] ? 1f : 0f;
            var c = sp[si];
            lum[di] = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
            if (dens[di] > 0.5f) inside.Add(lum[di]);
        }
        if (inside.Count < 10) return null;
        inside.Sort();
        float lo = inside[(int)(0.06f * inside.Count)];
        float hi = inside[(int)(0.94f * inside.Count)];
        float inv = 1f / Mathf.Max(1e-4f, hi - lo);

        dens = Blur(dens, cw, ch);
        dens = Blur(dens, cw, ch);
        for (int i = 0; i < dens.Length; i++)
        {
            float a = Mathf.Clamp01(dens[i]);
            float L = Mathf.Clamp01((lum[i] - lo) * inv);
            // 三色阶。注意 v1 材质的映射是  R -> _DarkColor(浅冷灰)  G -> _SecDarkColor(深冷蓝)，
            // 所以 R 放"中调"、G 放"暗部"，亮部 = 1-R-G（shader 里算）。
            // 阈值按参考图实测亮度分位定：暗 ~33% / 中 ~30% / 亮 ~37%
            s.alpha[i] = a;
            s.d1[i] = (Smooth(0.00f, 0.26f, L) * (1f - Smooth(0.38f, 0.62f, L))) * a;   // R = 中调
            s.d2[i] = (1f - Smooth(0.00f, 0.26f, L)) * a;                              // G = 暗部
            s.hl[i] = Smooth(0.72f, 0.92f, L) * a;                                     // B = 高光（只给 rim）
        }
        return s;
    }

    private static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / Mathf.Max(1e-5f, b - a));
        return t * t * (3f - 2f * t);
    }

    // ------------------------------------------------------------

    /// <summary>把一张参考云 + 它的剪影，拆成 密度场(0..1) 与 形体高度场(0..1)。</summary>
    private static Shape BuildShape(Texture2D src, Texture2D sil)
    {
        if (src == null || sil == null) return null;
        int W = src.width, H = src.height;
        var sp = src.GetPixels32();
        var mp = sil.GetPixels32();
        if (sp.Length != mp.Length) return null;

        // 1) 剪影 -> mask（有 alpha 用 alpha，否则用与角落背景的色差）
        bool hasA = false;
        for (int i = 0; i < mp.Length; i++) if (mp[i].a < 250) { hasA = true; break; }
        Color32 bg = mp[0];
        var mask = new bool[W * H];
        for (int i = 0; i < mp.Length; i++)
        {
            mask[i] = hasA
                ? mp[i].a > 127
                : (Mathf.Abs(mp[i].r - bg.r) + Mathf.Abs(mp[i].g - bg.g) + Mathf.Abs(mp[i].b - bg.b)) > 60;
        }

        // 2) bbox
        int x0 = W, y0 = H, x1 = -1, y1 = -1;
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
            if (mask[y * W + x])
            {
                if (x < x0) x0 = x; if (x > x1) x1 = x;
                if (y < y0) y0 = y; if (y > y1) y1 = y;
            }
        if (x1 < 0) return null;
        int cw = x1 - x0 + 1, ch = y1 - y0 + 1;

        var sh = new Shape { w = cw, h = ch, density = new float[cw * ch], height = new float[cw * ch] };

        // 3) 密度：二值 mask + 两次 3x3 模糊（软边）
        var dens = new float[cw * ch];
        for (int y = 0; y < ch; y++)
        for (int x = 0; x < cw; x++)
            dens[y * cw + x] = mask[(y + y0) * W + (x + x0)] ? 1f : 0f;
        dens = Blur(dens, cw, ch);
        dens = Blur(dens, cw, ch);
        for (int i = 0; i < dens.Length; i++) sh.density[i] = Mathf.Clamp01(dens[i]);

        // 4) 高度：参考图亮度（亮=顶，灰=底），按 mask 内的分位归一化
        var lum = new float[cw * ch];
        var inside = new List<float>();
        for (int y = 0; y < ch; y++)
        for (int x = 0; x < cw; x++)
        {
            var c = sp[(y + y0) * W + (x + x0)];
            float L = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
            lum[y * cw + x] = L;
            if (sh.density[y * cw + x] > 0.6f) inside.Add(L);
        }
        if (inside.Count < 10) return null;
        inside.Sort();
        float lo = inside[(int)(0.08f * inside.Count)];
        float hi = inside[(int)(0.92f * inside.Count)];
        float inv = 1f / Mathf.Max(1e-4f, hi - lo);

        var hgt = new float[cw * ch];
        for (int i = 0; i < hgt.Length; i++) hgt[i] = Mathf.Clamp01((lum[i] - lo) * inv);
        hgt = Blur(hgt, cw, ch);
        for (int i = 0; i < hgt.Length; i++) sh.height[i] = Mathf.Clamp01(hgt[i]) * sh.density[i];

        return sh;
    }

    private static float[] Blur(float[] a, int w, int h)
    {
        var o = new float[a.Length];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float s = 0f; int n = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                s += a[ny * w + nx]; n++;
            }
            o[y * w + x] = s / Mathf.Max(1, n);
        }
        return o;
    }

    private static Texture2D Load(string name)
    {
        string p = System.IO.Path.Combine(Application.dataPath, "Scene/Tex/Sources_V2", name);
        if (!System.IO.File.Exists(p)) { Debug.LogWarning("[CloudShapeBaker] 找不到 " + p); return null; }
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(System.IO.File.ReadAllBytes(p));
        return t;
    }
}
