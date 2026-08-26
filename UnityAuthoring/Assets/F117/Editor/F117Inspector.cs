using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class F117Inspector
{
    private const string SourcePrefab = "Assets/blueprinter/aryx/aryx_f16m/Aryx_F16M_KingViper.prefab";
    private const string ModelPrefab = "Assets/F117/Models/F117_Production.fbx";
    private const string BuiltPrefab = "Assets/F117/Generated/F117A_Nighthawk.prefab";
    private const string BuiltDefinition = "Assets/F117/Generated/F117A_Nighthawk_Definition.asset";
    private const string BuiltParameters = "Assets/F117/Generated/F117A_Nighthawk_Parameters.asset";
    private const string BuiltStatus = "Assets/F117/Generated/F117A_Nighthawk_StatusDisplay.prefab";

    private static string ReportPath(string fileName)
    {
        return Path.Combine(Application.dataPath, "F117", "Generated", "Reports", fileName);
    }

    public static void Dump()
    {
        var report = new StringBuilder(256 * 1024);
        DumpPrefab(SourcePrefab, report, IsRelevantSourceComponent);
        DumpPrefab(ModelPrefab, report, component => component is Transform || component is Renderer);
        DumpPrefab(BuiltPrefab, report, IsRelevantSourceComponent);
        DumpPrefab(BuiltStatus, report, component => component is Transform || component.GetType().Name == "Image" || component.GetType().Name == "StatusDisplay");
        DumpAsset(BuiltDefinition, report);
        DumpAsset(BuiltParameters, report);

        string path = ReportPath("Unity_Audit.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, report.ToString());
        Debug.Log("F-117 Unity audit written to " + path);
    }

    public static void DumpCockpitDisplayGeometry()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuiltPrefab);
        if (prefab == null)
            throw new InvalidOperationException("Missing asset: " + BuiltPrefab);

        var report = new StringBuilder(16 * 1024);
        Transform eye = prefab.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item => item.name == "F117_CockpitViewPoint");
        report.AppendLine("EYE position=" + (eye == null ? "missing" : Format(eye.position)) +
                          " forward=" + (eye == null ? "missing" : Format(eye.forward)));

        foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                continue;
            Material[] materials = renderer.sharedMaterials;
            Mesh mesh = filter.sharedMesh;
            for (int submesh = 0; submesh < Math.Min(mesh.subMeshCount, materials.Length); submesh++)
            {
                Material material = materials[submesh];
                string materialName = material == null ? string.Empty : material.name;
                if (materialName.IndexOf("HUD", StringComparison.OrdinalIgnoreCase) < 0 &&
                    materialName.IndexOf("MFD", StringComparison.OrdinalIgnoreCase) < 0 &&
                    renderer.name.IndexOf("Tacscreen", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                int[] triangles = mesh.GetTriangles(submesh);
                Vector3 low = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                Vector3 high = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                Vector3 normal = Vector3.zero;
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 a = renderer.transform.TransformPoint(mesh.vertices[triangles[index]]);
                    Vector3 b = renderer.transform.TransformPoint(mesh.vertices[triangles[index + 1]]);
                    Vector3 c = renderer.transform.TransformPoint(mesh.vertices[triangles[index + 2]]);
                    low = Vector3.Min(low, Vector3.Min(a, Vector3.Min(b, c)));
                    high = Vector3.Max(high, Vector3.Max(a, Vector3.Max(b, c)));
                    normal += Vector3.Cross(b - a, c - a);
                }
                normal.Normalize();
                Vector3 center = (low + high) * 0.5f;
                Vector3 toCenter = eye == null ? Vector3.zero : center - eye.position;
                report.AppendLine("RENDERER=" + renderer.name + " SUBMESH=" + submesh +
                                  " MATERIAL=" + materialName + " TRIANGLES=" + triangles.Length / 3 +
                                  " CENTER=" + Format(center) + " SIZE=" + Format(high - low) +
                                  " NORMAL=" + Format(normal) +
                                  " EYE_DELTA=" + Format(toCenter) +
                                  " VIEW_ALIGNMENT=" + (eye == null ? "n/a" :
                                      Vector3.Dot(normal, toCenter.normalized).ToString("0.######", CultureInfo.InvariantCulture)));
            }

            if (eye != null && renderer.name == "F117_Cockpit_Mesh")
                DumpProjectedCockpitIslands(renderer, filter.sharedMesh, eye, report);
        }

        string path = ReportPath("Unity_Cockpit_Display_Geometry.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, report.ToString());
        Debug.Log("F-117 cockpit display geometry written to " + path);
    }

    public static void DumpParadeOverlayGeometry()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuiltPrefab);
        if (prefab == null)
            throw new InvalidOperationException("Missing asset: " + BuiltPrefab);
        var report = new StringBuilder(32 * 1024);
        foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true)
            .Where(item => item.name.StartsWith(F117AircraftAssembler.ParadeFlagOverlayPrefix,
                StringComparison.Ordinal)).OrderBy(item => item.name, StringComparer.Ordinal))
        {
            Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null)
                continue;
            Bounds bounds = new Bounds();
            bool initialized = false;
            foreach (Vector3 vertex in mesh.vertices)
            {
                Vector3 point = prefab.transform.InverseTransformPoint(renderer.transform.TransformPoint(vertex));
                if (!initialized)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    initialized = true;
                }
                else
                    bounds.Encapsulate(point);
            }
            report.AppendLine(renderer.name + " parent=" + renderer.transform.parent.name +
                " triangles=" + mesh.triangles.Length / 3 + " center=" + Format(bounds.center) +
                " size=" + Format(bounds.size) + " min=" + Format(bounds.min) + " max=" + Format(bounds.max));
        }
        string path = ReportPath("F117_Parade_Overlay_Geometry.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, report.ToString());
        Debug.Log("F-117 parade overlay geometry written to " + path);
    }

    private static void DumpProjectedCockpitIslands(MeshRenderer renderer, Mesh mesh, Transform eye, StringBuilder report)
    {
        Material[] materials = renderer.sharedMaterials;
        Vector3[] vertices = mesh.vertices;
        for (int submesh = 0; submesh < Math.Min(mesh.subMeshCount, materials.Length); submesh++)
        {
            int[] triangles = mesh.GetTriangles(submesh);
            var triangleByVertex = new Dictionary<int, List<int>>();
            for (int triangle = 0; triangle < triangles.Length / 3; triangle++)
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertex = triangles[triangle * 3 + corner];
                    if (!triangleByVertex.TryGetValue(vertex, out List<int> owners))
                        triangleByVertex.Add(vertex, owners = new List<int>());
                    owners.Add(triangle);
                }

            var remaining = new HashSet<int>(Enumerable.Range(0, triangles.Length / 3));
            int island = 0;
            while (remaining.Count > 0)
            {
                int seed = remaining.First();
                remaining.Remove(seed);
                var group = new HashSet<int> { seed };
                var pending = new Queue<int>();
                pending.Enqueue(seed);
                while (pending.Count > 0)
                {
                    int triangle = pending.Dequeue();
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int vertex = triangles[triangle * 3 + corner];
                        foreach (int neighbor in triangleByVertex[vertex])
                            if (remaining.Remove(neighbor))
                            {
                                group.Add(neighbor);
                                pending.Enqueue(neighbor);
                            }
                    }
                }

                var componentVertices = new HashSet<int>();
                foreach (int triangle in group)
                    for (int corner = 0; corner < 3; corner++)
                        componentVertices.Add(triangles[triangle * 3 + corner]);

                float minX = float.PositiveInfinity;
                float maxX = float.NegativeInfinity;
                float minY = float.PositiveInfinity;
                float maxY = float.NegativeInfinity;
                float minDepth = float.PositiveInfinity;
                foreach (int vertex in componentVertices)
                {
                    Vector3 relative = renderer.transform.TransformPoint(vertices[vertex]) - eye.position;
                    float depth = Vector3.Dot(relative, eye.forward);
                    if (depth <= 0.01f)
                        continue;
                    float projectedX = Vector3.Dot(relative, eye.right) / depth;
                    float projectedY = Vector3.Dot(relative, eye.up) / depth;
                    minX = Math.Min(minX, projectedX);
                    maxX = Math.Max(maxX, projectedX);
                    minY = Math.Min(minY, projectedY);
                    maxY = Math.Max(maxY, projectedY);
                    minDepth = Math.Min(minDepth, depth);
                }
                float width = maxX - minX;
                float height = maxY - minY;
                bool central = minX < 0.12f && maxX > -0.12f && minY < 0.5f && maxY > -0.5f;
                if (central && width > 0.02f && height > 0.08f)
                {
                    string materialName = materials[submesh] == null ? "<null>" : materials[submesh].name;
                    report.AppendLine("PROJECTED MATERIAL=" + materialName + " SUBMESH=" + submesh +
                                      " ISLAND=" + island + " TRIANGLES=" + group.Count +
                                      " X=" + minX.ToString("0.###", CultureInfo.InvariantCulture) + ".." +
                                      maxX.ToString("0.###", CultureInfo.InvariantCulture) +
                                      " Y=" + minY.ToString("0.###", CultureInfo.InvariantCulture) + ".." +
                                      maxY.ToString("0.###", CultureInfo.InvariantCulture) +
                                      " ASPECT=" + (height / width).ToString("0.###", CultureInfo.InvariantCulture) +
                                      " DEPTH=" + minDepth.ToString("0.###", CultureInfo.InvariantCulture));
                }
                island++;
            }
        }
    }

    private static void DumpAsset(string path, StringBuilder report)
    {
        UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
        if (asset == null)
            throw new InvalidOperationException("Missing asset: " + path);
        report.AppendLine();
        report.AppendLine("=== " + path + " ===");
        DumpComponentLike(asset, report);
    }

    private static void DumpComponentLike(UnityEngine.Object target, StringBuilder report)
    {
        report.AppendLine("  OBJECT " + target.name + " <" + target.GetType().FullName + ">");
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.GetIterator();
        while (property.NextVisible(true))
        {
            if (property.name == "m_Script")
                continue;
            string value = PropertyValue(property);
            if (value != null)
                report.AppendLine("    " + property.propertyPath + " = " + value);
        }
    }

    private static void DumpPrefab(string path, StringBuilder report, Func<Component, bool> include)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            throw new InvalidOperationException("Missing prefab: " + path);

        report.AppendLine("=== " + path + " ===");
        foreach (Transform transform in prefab.GetComponentsInChildren<Transform>(true))
        {
            Component[] components = transform.GetComponents<Component>().Where(component => component != null && include(component)).ToArray();
            if (components.Length == 0)
                continue;

            report.AppendLine();
            report.AppendLine("OBJECT " + HierarchyPath(prefab.transform, transform));
            report.AppendLine("  activeSelf=" + transform.gameObject.activeSelf +
                              " activeInHierarchy=" + transform.gameObject.activeInHierarchy +
                              " localPosition=" + Format(transform.localPosition) +
                              " localRotation=" + Format(transform.localEulerAngles) +
                              " localScale=" + Format(transform.localScale));
            foreach (Component component in components)
                DumpComponent(component, report);
        }
    }

    private static bool IsRelevantSourceComponent(Component component)
    {
        // Intentionally exhaustive: the report is evidence that no serialized
        // donor tuning or donor reference survived unnoticed.
        return true;
    }

    private static void DumpComponent(Component component, StringBuilder report)
    {
        report.AppendLine("  COMPONENT " + component.GetType().FullName);
        if (component is Renderer renderer)
            report.AppendLine("    enabled(runtime)=" + renderer.enabled);
        SerializedObject serialized;
        try
        {
            serialized = new SerializedObject(component);
        }
        catch
        {
            return;
        }

        SerializedProperty property = serialized.GetIterator();
        while (property.NextVisible(true))
        {
            if (property.name == "m_Script")
                continue;
            string value = PropertyValue(property);
            if (value != null)
                report.AppendLine("    " + property.propertyPath + " = " + value);
        }
    }

    private static string PropertyValue(SerializedProperty property)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.Integer: return property.longValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Boolean: return property.boolValue.ToString();
            case SerializedPropertyType.Float: return property.doubleValue.ToString("0.######", CultureInfo.InvariantCulture);
            case SerializedPropertyType.String: return property.stringValue;
            case SerializedPropertyType.Color: return property.colorValue.ToString();
            case SerializedPropertyType.ObjectReference:
                return ObjectReferenceValue(property.objectReferenceValue);
            case SerializedPropertyType.LayerMask: return property.intValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Enum: return property.enumDisplayNames.ElementAtOrDefault(property.enumValueIndex) ?? property.enumValueIndex.ToString();
            case SerializedPropertyType.Vector2: return property.vector2Value.ToString("F4");
            case SerializedPropertyType.Vector3: return Format(property.vector3Value);
            case SerializedPropertyType.Vector4: return property.vector4Value.ToString("F4");
            case SerializedPropertyType.Rect: return property.rectValue.ToString();
            case SerializedPropertyType.Bounds: return property.boundsValue.ToString();
            case SerializedPropertyType.Quaternion: return property.quaternionValue.eulerAngles.ToString("F4");
            case SerializedPropertyType.ArraySize: return property.intValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.AnimationCurve:
                return "curve(keys=" + (property.animationCurveValue == null ? 0 : property.animationCurveValue.length) + ")";
            case SerializedPropertyType.Generic:
                return property.isArray ? "array(size=" + property.arraySize + ")" : null;
            default:
                return null;
        }
    }

    private static string ObjectReferenceValue(UnityEngine.Object value)
    {
        if (value == null)
            return "null";

        string assetPath = AssetDatabase.GetAssetPath(value);
        if (!string.IsNullOrEmpty(assetPath))
            return assetPath + " :: " + value.name + " <" + value.GetType().FullName + ">";

        Component component = value as Component;
        if (component != null)
            return FullHierarchyPath(component.transform) + " <" + component.GetType().FullName + ">";

        GameObject gameObject = value as GameObject;
        if (gameObject != null)
            return FullHierarchyPath(gameObject.transform) + " <UnityEngine.GameObject>";

        return value.name + " <" + value.GetType().FullName + ">";
    }

    private static string FullHierarchyPath(Transform current)
    {
        var parts = new Stack<string>();
        while (current != null)
        {
            parts.Push(current.name);
            current = current.parent;
        }
        return string.Join("/", parts.ToArray());
    }

    private static string HierarchyPath(Transform root, Transform current)
    {
        var parts = new Stack<string>();
        while (current != null)
        {
            parts.Push(current.name);
            if (current == root)
                break;
            current = current.parent;
        }
        return string.Join("/", parts.ToArray());
    }

    private static string Format(Vector3 value)
    {
        return string.Format(CultureInfo.InvariantCulture, "({0:0.####}, {1:0.####}, {2:0.####})", value.x, value.y, value.z);
    }
}
