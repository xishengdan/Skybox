using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CloudLayout))]
public class CloudLayoutEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        var layout = (CloudLayout)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("操作", EditorStyles.boldLabel);

        if (GUILayout.Button("重建网格（按上面的列表生成）", GUILayout.Height(34)))
        {
            serializedObject.ApplyModifiedProperties();   // 确保列表改动先落地
            Rebuild(layout);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("全部大小 ×1.1"))
        {
            foreach (var c in layout.clouds) if (c != null) c.size *= 1.1f;
            Rebuild(layout);
        }
        if (GUILayout.Button("全部大小 ×0.9"))
        {
            foreach (var c in layout.clouds) if (c != null) c.size *= 0.9f;
            Rebuild(layout);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox(
            "云列表里每一条就是一朵云：改 elevationDeg/azimuthDeg 换位置，改 size 直接控制大小，\n" +
            "widthAspect 控制扁圆，depth01 控制远近（雾化/透明度），cellX/cellY 换图集造型。\n" +
            "取消勾选 enabled 可隐藏某朵云；改完点“重建网格”。", MessageType.Info);
    }

    private static void Rebuild(CloudLayout layout)
    {
        var mesh = CloudMeshBuilder.Build(layout);
        EditorUtility.SetDirty(layout);
        AssetDatabase.SaveAssets();
        EditorApplication.RepaintHierarchyWindow();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        var go = GameObject.Find(layout.objectName);
        if (go != null) EditorGUIUtility.PingObject(go);
        int quads = mesh != null ? mesh.vertexCount / 4 : 0;
        Debug.Log($"[CloudLayout] 重建完成：列表 {layout.clouds.Count} 朵 -> 网格 {quads} 面片（{layout.meshPath}）");
    }
}
