using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 清理材质上“shader 里已经不存在的属性”。
/// 典型来源：shader 迭代后旧属性残留在 .mat 里（例如 Skybox.mat 上那 35 个
/// 早期版本的 _Cloud*/_Stars*/_Halo* 属性），既看不见又让材质文件难以维护。
/// </summary>
public static class SkyMaterialCleaner
{
    [MenuItem("Tools/Skybox Clouds/Utilities/Clean Dead Material Properties", false, 40)]
    public static void CleanAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Scene" });
        var touched = new List<string>();

        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            int removed = Clean(path);
            if (removed > 0) touched.Add($"{path} (-{removed})");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (touched.Count == 0) Debug.Log("[SkyMaterialCleaner] 没有发现死属性");
        else Debug.Log("[SkyMaterialCleaner] 清理完成：\n  " + string.Join("\n  ", touched));
    }

    [MenuItem("Tools/Skybox Clouds/Utilities/Report Material Properties", false, 41)]
    public static void Report()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Scene" });
        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;

            var valid = new HashSet<string>();
            for (int i = 0; i < mat.shader.GetPropertyCount(); i++) valid.Add(mat.shader.GetPropertyName(i));

            var so = new SerializedObject(mat);
            var dead = new List<string>();
            Collect(so, "m_SavedProperties.m_TexEnvs", valid, dead);
            Collect(so, "m_SavedProperties.m_Floats", valid, dead);
            Collect(so, "m_SavedProperties.m_Colors", valid, dead);
            Collect(so, "m_SavedProperties.m_Ints", valid, dead);

            Debug.Log($"[SkyMaterialCleaner] {path}: shader={mat.shader.name}, 死属性 {dead.Count} 个" +
                      (dead.Count > 0 ? " -> " + string.Join(", ", dead) : ""));
        }
    }

    private static void Collect(SerializedObject so, string path, HashSet<string> valid, List<string> dead)
    {
        var arr = so.FindProperty(path);
        if (arr == null || !arr.isArray) return;
        for (int i = 0; i < arr.arraySize; i++)
        {
            var key = arr.GetArrayElementAtIndex(i).FindPropertyRelative("first");
            if (key != null && !valid.Contains(key.stringValue)) dead.Add(key.stringValue);
        }
    }

    private static int Clean(string path)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null || mat.shader == null) return 0;

        var valid = new HashSet<string>();
        for (int i = 0; i < mat.shader.GetPropertyCount(); i++) valid.Add(mat.shader.GetPropertyName(i));

        var so = new SerializedObject(mat);
        int removed = 0;
        removed += Prune(so, "m_SavedProperties.m_TexEnvs", valid);
        removed += Prune(so, "m_SavedProperties.m_Floats", valid);
        removed += Prune(so, "m_SavedProperties.m_Colors", valid);
        removed += Prune(so, "m_SavedProperties.m_Ints", valid);

        if (removed == 0) return 0;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(mat);
        return removed;
    }

    private static int Prune(SerializedObject so, string path, HashSet<string> valid)
    {
        var arr = so.FindProperty(path);
        if (arr == null || !arr.isArray) return 0;
        int removed = 0;
        for (int i = arr.arraySize - 1; i >= 0; i--)
        {
            var key = arr.GetArrayElementAtIndex(i).FindPropertyRelative("first");
            if (key != null && !valid.Contains(key.stringValue))
            {
                arr.DeleteArrayElementAtIndex(i);
                removed++;
            }
        }
        return removed;
    }
}
