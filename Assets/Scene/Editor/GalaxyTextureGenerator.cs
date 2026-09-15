using UnityEngine;
using UnityEditor;
using System.IO;

public class GalaxyTextureGenerator : EditorWindow
{
    [MenuItem("Tools/Generate Galaxy Textures")]
    static void Init()
    {
        GalaxyTextureGenerator window = (GalaxyTextureGenerator)EditorWindow.GetWindow(typeof(GalaxyTextureGenerator));
        window.Show();
    }

    Vector2 gOffset = new Vector2(-0.01f, 0.02f);
    float rRadius = 0.15f;
    float gRadius = 0.08f;
    int textureSize = 2048;

    void OnGUI()
    {
        GUILayout.Label("银河纹理生成器", EditorStyles.boldLabel);

        textureSize = EditorGUILayout.IntField("纹理尺寸", textureSize);
        rRadius = EditorGUILayout.Slider("R通道(外围)宽度", rRadius, 0.05f, 0.4f);
        gRadius = EditorGUILayout.Slider("G通道(核心)宽度", gRadius, 0.02f, 0.25f);
        gOffset = EditorGUILayout.Vector2Field("G通道偏移", gOffset);

        EditorGUILayout.Space(10);

        if (GUILayout.Button("生成银河纹理", GUILayout.Height(30)))
        {
            GenerateGalaxyTex();
        }

        if (GUILayout.Button("生成噪声纹理", GUILayout.Height(30)))
        {
            GenerateNoiseTex();
        }

        if (GUILayout.Button("生成全部纹理", GUILayout.Height(30)))
        {
            GenerateGalaxyTex();
            GenerateNoiseTex();
        }
    }

    void GenerateGalaxyTex()
    {
        int size = textureSize;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBAFloat, true);

        Vector2 center = new Vector2(0.5f, 0.5f);
        // 旋转 45 度呈斜向弧带
        float angle = 45f * Mathf.Deg2Rad;
        float cosA = Mathf.Cos(angle);
        float sinA = Mathf.Sin(angle);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 uv = new Vector2((float)x / size, (float)y / size);
                Vector2 offset = uv - center;

                // 旋转 45 度
                Vector2 rotUV = new Vector2(
                    offset.x * cosA - offset.y * sinA,
                    offset.x * sinA + offset.y * cosA
                );

                // 稍微弯曲成弧形
                rotUV.x += rotUV.y * rotUV.y * 0.6f;

                // 计算与中心线距离，做二次方衰减保证边缘绝对为零
                // 宽 0.12，长 0.35
                float distR = (rotUV.x * rotUV.x) / (0.12f * 0.12f) + (rotUV.y * rotUV.y) / (0.35f * 0.35f);
                float rValue = Mathf.Clamp01(1.0f - Mathf.Sqrt(distR));
                rValue = Mathf.Pow(rValue, 1.5f); // 边缘平滑柔和

                // G通道：中心稍窄的高亮部分
                float distG = (rotUV.x * rotUV.x) / (0.05f * 0.05f) + (rotUV.y * rotUV.y) / (0.28f * 0.28f);
                float gValue = Mathf.Clamp01(1.0f - Mathf.Sqrt(distG));
                gValue = Mathf.Pow(gValue, 2.5f); // 中心亮带更集中

                tex.SetPixel(x, y, new Color(rValue, gValue, 0f, 1f));
            }
        }

        tex.Apply();
        SaveTexture(tex, "GalaxyTex");
        Debug.Log("极简银河贴图生成完成！");
    }

    void GenerateNoiseTex()
    {
        int size = 1024;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBAFloat, true);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;
                float v = (float)y / size;

                float value = 0;
                float amplitude = 1f;
                float frequency = 3f;
                float totalAmplitude = 0;

                for (int octave = 0; octave < 5; octave++)
                {
                    value += Mathf.PerlinNoise(u * frequency, v * frequency) * amplitude;
                    totalAmplitude += amplitude;
                    amplitude *= 0.5f;
                    frequency *= 2.0f;
                }

                value /= totalAmplitude;
                value = Mathf.Pow(value, 0.8f);

                tex.SetPixel(x, y, new Color(value, value, value, 1));
            }
        }

        tex.Apply();
        SaveTexture(tex, "GalaxyNoiseTex");
        Debug.Log("噪声纹理生成完成！");
    }

    void SaveTexture(Texture2D tex, string name)
    {
        string path = $"Assets/{name}.png";
        byte[] bytes = tex.EncodeToPNG();
        File.WriteAllBytes(path, bytes);

        AssetDatabase.Refresh();

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.sRGBTexture = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        Debug.Log($"纹理已保存到: {path}");
        DestroyImmediate(tex);
    }
}