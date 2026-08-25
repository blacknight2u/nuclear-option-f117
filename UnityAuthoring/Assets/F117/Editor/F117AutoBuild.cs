using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class F117AutoBuild
{
    private const string RequestPath = "Assets/F117/Editor/REQUEST_BUILD.txt";

    static F117AutoBuild()
    {
        EditorApplication.delayCall += TryBuild;
    }

    private static void TryBuild()
    {
        if (!File.Exists(RequestPath))
            return;
        File.Delete(RequestPath);
        AssetDatabase.Refresh();
        Debug.Log("F-117 auto-build request found; assembling prefab and .nobp.");
        try
        {
            F117Builder.Build();
            Debug.Log("F-117 auto-build finished.");
        }
        catch (System.Exception exception)
        {
            Debug.LogError("F-117 auto-build failed: " + exception);
        }
    }
}
