using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 一次性工程整理工具（P0 解耦 + P1 清理）。
///
/// P0：把默认渲染管线 / High Fidelity 的 Renderer 从 Beautify 的 Demo 目录里解出来，
///     否则 Demo 目录永远删不掉（删了默认管线变空、后处理全丢）。
/// P1：删除确认无外部引用的冗余资产，并把散落的原画归到 Tex/Sources。
///
/// 删除前会把「整份待删清单」当成一个整体做 guid 反查：
/// 清单内部的互相引用不算外部引用，只有清单外的资产引用了才会拦住。
/// </summary>
public static class SkyProjectCleanup
{
    private const string SettingsRenderer = "Assets/Settings/URP-HighFidelity-Renderer.asset";
    private const string DemoRenderer =
        "Assets/Beautify/URP/Demo/DemoSources/URP Settings/UniversalRenderPipelineAsset_Renderer.asset";
    private const string HighFidelity = "Assets/Settings/URP-HighFidelity.asset";

    // ==================== P0 ====================

    [MenuItem("Tools/Skybox Clouds/Project/1. Migrate Render Pipeline Off Beautify Demo", false, 60)]
    public static void MigratePipeline()
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(DemoRenderer) == null)
        {
            Debug.Log("[SkyProjectCleanup] 已经迁移过了（Demo renderer 不存在）");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<Object>(SettingsRenderer) != null)
        {
            string t = AssetDatabase.MoveAsset(SettingsRenderer, SettingsRenderer + ".orphan");
            if (!string.IsNullOrEmpty(t)) { Debug.LogError("[SkyProjectCleanup] 让开旧 Renderer 失败: " + t); return; }
        }

        // Move 而不是 Copy：GUID 不变，URP-HighFidelity.asset 的 renderer 引用自动继续有效
        string guidBefore = AssetDatabase.AssetPathToGUID(DemoRenderer);
        string err = AssetDatabase.MoveAsset(DemoRenderer, SettingsRenderer);
        if (!string.IsNullOrEmpty(err)) { Debug.LogError("[SkyProjectCleanup] 迁移 Renderer 失败: " + err); return; }

        AssetDatabase.Refresh();
        string guidAfter = AssetDatabase.AssetPathToGUID(SettingsRenderer);

        var hf = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>(HighFidelity);
        UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = hf;
        AssetDatabase.SaveAssets();

        if (AssetDatabase.LoadAssetAtPath<Object>(SettingsRenderer + ".orphan") != null)
            AssetDatabase.DeleteAsset(SettingsRenderer + ".orphan");

        Debug.Log($"[SkyProjectCleanup] P0 完成\n" +
                  $"  Renderer 迁移: guid 不变 = {guidBefore == guidAfter}\n" +
                  $"  新路径: {SettingsRenderer}\n" +
                  $"  GraphicsSettings 默认管线: {(hf != null ? hf.name : "null")}");
    }

    // ==================== P1 ====================

    private static readonly string[] Redundant =
    {
        "Assets/Beautify/URP/Beautify_URP.unitypackage",
        "Assets/Beautify/URP/Demo",
        "Assets/Materials",
        "Assets/SDF",
        "Assets/TutorialInfo",
        "Assets/Readme.asset",
        "Assets/Scene/Tex/Noise_095.png",
        "Assets/Scene/Tex/泊松噪声采样点图.png",
        "Assets/Settings/SampleSceneProfile.asset",
        "Assets/Git",
        "Assets/Scene/Pre",
        "Assets/Screenshots",
    };

    private static readonly string[] Sources =
    {
        "Assets/云.jpg",
        "Assets/云1.png",
        "Assets/云1_0000s_0000_图层 0.tga",
        "Assets/Scene/Tex/Gemini_Generated_Image_jm9fz4jm9fz4jm9f.jpg",
    };

    [MenuItem("Tools/Skybox Clouds/Project/2. Report Redundant Assets", false, 61)]
    public static void ReportRedundant()
    {
        var map = ScanExternalRefs(Redundant);
        var lines = new List<string>();
        long total = 0;

        foreach (string p in Redundant)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(p) == null) { lines.Add($"  [缺失] {p}"); continue; }
            long mb = SizeOf(p);
            var blockers = Blockers(p, map);
            if (blockers.Count == 0) total += mb;
            lines.Add($"  [{(blockers.Count == 0 ? "可删" : "被拦")}] {p}  ({mb / 1024f / 1024f:F2} MB)");
            foreach (string b in blockers) lines.Add($"        <- {b}");
        }

        Debug.Log($"[SkyProjectCleanup] 冗余清单（可删合计 {total / 1024f / 1024f:F1} MB）:\n" + string.Join("\n", lines));
    }

    [MenuItem("Tools/Skybox Clouds/Project/3. Delete Redundant Assets", false, 62)]
    public static void DeleteRedundant()
    {
        var map = ScanExternalRefs(Redundant);
        var deleted = new List<string>();
        var skipped = new List<string>();
        long freed = 0;

        foreach (string p in Redundant)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(p) == null) continue;

            var blockers = Blockers(p, map);
            if (blockers.Count > 0) { skipped.Add($"{p}  <-  {string.Join(", ", blockers)}"); continue; }

            long mb = SizeOf(p);
            if (AssetDatabase.DeleteAsset(p)) { deleted.Add($"{p}  ({mb / 1024f / 1024f:F2} MB)"); freed += mb; }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[SkyProjectCleanup] P1 释放 {freed / 1024f / 1024f:F1} MB，删除 {deleted.Count} 项:\n  " +
                  string.Join("\n  ", deleted) +
                  (skipped.Count > 0 ? "\n跳过（仍有外部引用）:\n  " + string.Join("\n  ", skipped) : ""));
    }

    [MenuItem("Tools/Skybox Clouds/Project/4. Move Source Art Into Tex/Sources", false, 63)]
    public static void MoveSourceArt()
    {
        const string dir = "Assets/Scene/Tex/Sources";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Scene/Tex", "Sources");

        var moved = new List<string>();
        foreach (string src in Sources)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(src) == null) continue;
            string dst = dir + "/" + Path.GetFileName(src);
            string err = AssetDatabase.MoveAsset(src, dst);
            moved.Add(string.IsNullOrEmpty(err) ? $"{src}  ->  {dst}" : $"失败 {src}: {err}");
        }
        AssetDatabase.Refresh();
        Debug.Log("[SkyProjectCleanup] 原画归档:\n  " + string.Join("\n  ", moved));
    }

    // ==================== 工具 ====================

    private static List<string> Blockers(string item, Dictionary<string, HashSet<string>> map)
    {
        var own = GuidsUnder(new[] { item });
        return map.Where(kv => kv.Value.Overlaps(own)).Select(kv => kv.Key).OrderBy(x => x).ToList();
    }

    /// <summary>整份清单的 guid 集合。</summary>
    private static HashSet<string> GuidsUnder(string[] paths)
    {
        var set = new HashSet<string>();
        foreach (string p in paths)
        {
            string g = AssetDatabase.AssetPathToGUID(p);
            if (!string.IsNullOrEmpty(g)) set.Add(g);
            foreach (string sub in AssetDatabase.FindAssets("", new[] { p })) set.Add(sub);
        }
        return set;
    }

    /// <summary>扫描清单外的文本资产，返回 文件 -> 它引用到的清单内 guid 集合。</summary>
    private static Dictionary<string, HashSet<string>> ScanExternalRefs(string[] doomed)
    {
        var doomedGuids = GuidsUnder(doomed);
        var result = new Dictionary<string, HashSet<string>>();

        string root = Directory.GetParent(Application.dataPath).FullName;
        string rootNorm = root.Replace('\\', '/');

        var textExts = new HashSet<string>
        {
            ".asset", ".unity", ".mat", ".prefab", ".lighting", ".json", ".txt",
            ".asmdef", ".asmref", ".cs", ".controller", ".anim", ".shader", ".hlsl", ".uxml", ".uss"
        };

        foreach (string d in new[] { "Assets", "ProjectSettings", "Packages" })
        {
            string abs = Path.Combine(root, d);
            if (!Directory.Exists(abs)) continue;

            foreach (string f in Directory.GetFiles(abs, "*", SearchOption.AllDirectories))
            {
                if (!textExts.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;

                string norm = f.Replace('\\', '/');
                string rel = norm.StartsWith(rootNorm + "/") ? norm.Substring(rootNorm.Length + 1) : norm;

                bool isDoomed = doomed.Any(p => rel == p || rel.StartsWith(p + "/"));
                if (isDoomed) continue;

                string text;
                try { text = File.ReadAllText(f); } catch { continue; }

                foreach (string g in doomedGuids)
                {
                    if (text.Contains(g))
                    {
                        if (!result.TryGetValue(rel, out var set)) { set = new HashSet<string>(); result[rel] = set; }
                        set.Add(g);
                    }
                }
            }
        }
        return result;
    }

    private static long SizeOf(string path)
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        string abs = Path.Combine(root, path);
        try
        {
            if (File.Exists(abs)) return new FileInfo(abs).Length;
            if (Directory.Exists(abs))
            {
                long sum = 0;
                foreach (string f in Directory.GetFiles(abs, "*", SearchOption.AllDirectories))
                    if (!f.EndsWith(".meta")) sum += new FileInfo(f).Length;
                return sum;
            }
        }
        catch { }
        return 0;
    }
}
