using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class F117DamageAvatarGenerator
{
    private const string BuiltPrefabPath = "Assets/F117/Generated/F117A_Nighthawk.prefab";
    private const string SilhouettePath = "Assets/F117/UI/F117_Damage.png";
    private const string OutputRoot = "Assets/F117/UI/DamageSections";
    private const int OutputSize = 256;
    private const int Supersample = 4;
    private const float MinimumProjectedTriangleArea = 0.0000001f;
    private static readonly string[] PartNames =
    {
        "F117_CentralBody", "F117_Nose", "F117_RearBody",
        "F117_Wing_Left_Root", "F117_Wing_Left_Inner", "F117_Wing_Left_Outer",
        "F117_Wing_Right_Root", "F117_Wing_Right_Inner", "F117_Wing_Right_Outer",
        "F117_Elevon_L_Inner", "F117_Elevon_L_Outer",
        "F117_Elevon_R_Inner", "F117_Elevon_R_Outer",
        "F117_Rudder_L", "F117_Rudder_R",
        "F117_Engine_Left", "F117_Engine_Right"
    };

    private readonly struct PlanformTriangle
    {
        internal readonly Vector2 A;
        internal readonly Vector2 B;
        internal readonly Vector2 C;

        internal PlanformTriangle(Vector2 a, Vector2 b, Vector2 c)
        {
            A = a;
            B = b;
            C = c;
        }
    }

    private sealed class DamageSection
    {
        internal readonly string Name;
        internal readonly bool Internal;
        internal readonly List<PlanformTriangle> Triangles = new List<PlanformTriangle>();
        internal Vector2 Min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        internal Vector2 Max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        internal DamageSection(string name)
        {
            Name = name;
            Internal = name.StartsWith("F117_Engine_", StringComparison.Ordinal);
        }

        internal void Add(Vector2 a, Vector2 b, Vector2 c)
        {
            if (Mathf.Abs(Cross(b - a, c - a)) <= MinimumProjectedTriangleArea)
                return;
            Triangles.Add(new PlanformTriangle(a, b, c));
            Min = Vector2.Min(Min, Vector2.Min(a, Vector2.Min(b, c)));
            Max = Vector2.Max(Max, Vector2.Max(a, Vector2.Max(b, c)));
        }
    }

    [MenuItem("F-117A Nighthawk/Generate Exact Damage Avatar")]
    public static void GenerateFromBuiltPrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuiltPrefabPath);
        if (prefab == null)
            throw new FileNotFoundException("Build the F-117 prefab before generating its damage avatar.", BuiltPrefabPath);
        GameObject instance = PrefabUtility.LoadPrefabContents(BuiltPrefabPath);
        try
        {
            Generate(instance);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(instance);
        }
    }

    public static void Generate(GameObject aircraft)
    {
        if (aircraft == null)
            throw new ArgumentNullException(nameof(aircraft));

        Texture2D silhouette = LoadPng(SilhouettePath);
        try
        {
            Color32[] sourcePixels = silhouette.GetPixels32();
            FindOpaqueBounds(sourcePixels, silhouette.width, silhouette.height,
                out int alphaMinX, out int alphaMinY, out int alphaMaxX, out int alphaMaxY);

            DamageSection[] sections = PartNames.Select(name => BuildSection(aircraft.transform, name)).ToArray();
            Vector2 aircraftMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 aircraftMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (DamageSection section in sections.Where(section => !section.Internal))
            {
                aircraftMin = Vector2.Min(aircraftMin, section.Min);
                aircraftMax = Vector2.Max(aircraftMax, section.Max);
            }
            if (!Finite(aircraftMin.x) || !Finite(aircraftMax.x) ||
                aircraftMax.x - aircraftMin.x < 1f || aircraftMax.y - aircraftMin.y < 1f)
                throw new InvalidOperationException("Exact damage geometry produced an invalid aircraft planform.");
            Debug.Log("F-117 damage-avatar planform " + aircraftMin.ToString("F3") + ".." +
                aircraftMax.ToString("F3") + ": " + string.Join(", ", sections.Select(section =>
                    section.Name + "=" + section.Min.ToString("F3") + ".." + section.Max.ToString("F3") +
                    "/" + section.Triangles.Count + "t")));

            Directory.CreateDirectory(OutputRoot);
            int[] visiblePixels = new int[sections.Length];
            bool[] exteriorUnion = new bool[OutputSize * OutputSize];
            for (int index = 0; index < sections.Length; index++)
            {
                DamageSection section = sections[index];
                Color32[] mask = Rasterize(section, aircraftMin, aircraftMax, sourcePixels,
                    silhouette.width, silhouette.height, alphaMinX, alphaMinY, alphaMaxX, alphaMaxY);
                visiblePixels[index] = mask.Count(pixel => pixel.a > 0);
                if (visiblePixels[index] == 0)
                    throw new InvalidOperationException(section.Name + " exact damage mask is empty.");
                if (!section.Internal)
                    for (int pixel = 0; pixel < mask.Length; pixel++)
                        exteriorUnion[pixel] |= mask[pixel].a > 0;
                WriteMask(section.Name, mask);
            }

            int silhouettePixels = 0;
            int coveredSilhouettePixels = 0;
            for (int y = 0; y < OutputSize; y++)
            for (int x = 0; x < OutputSize; x++)
            {
                Color32 source = SourcePixel(sourcePixels, silhouette.width, silhouette.height, x, y);
                if (source.a == 0)
                    continue;
                silhouettePixels++;
                if (exteriorUnion[y * OutputSize + x])
                    coveredSilhouettePixels++;
            }
            float exteriorCoverage = silhouettePixels == 0 ? 0f :
                coveredSilhouettePixels / (float)silhouettePixels;
            if (exteriorCoverage < 0.90f)
                throw new InvalidOperationException("Exact damage masks cover only " +
                    exteriorCoverage.ToString("P1") + " of the authored silhouette.");

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string partName in PartNames)
                ConfigureMaskImporter(OutputRoot + "/" + partName + ".png");
            AssetDatabase.SaveAssets();
            Debug.Log("F-117 exact damage avatar generated: " + sections.Length +
                " parts, " + exteriorCoverage.ToString("P1") + " exterior coverage. " +
                string.Join(", ", sections.Select((section, index) =>
                    section.Name + "=" + section.Triangles.Count + "t/" + visiblePixels[index] + "px")));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(silhouette);
        }
    }

    private static DamageSection BuildSection(Transform aircraftRoot, string name)
    {
        Transform part = aircraftRoot.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.name == name);
        if (part == null)
            throw new InvalidOperationException("Cannot generate exact damage mask; missing part " + name + ".");

        var section = new DamageSection(name);
        Renderer[] renderers = DamageRenderers(part).ToArray();
        foreach (Renderer renderer in renderers)
            AddRenderer(section, renderer, aircraftRoot);
        if (section.Triangles.Count == 0 && section.Internal)
            AddColliderPlanform(section, part, aircraftRoot);
        if (section.Triangles.Count == 0)
            throw new InvalidOperationException(name + " has no projectable owned damage geometry.");
        return section;
    }

    private static IEnumerable<Renderer> DamageRenderers(Transform part)
    {
        foreach (Component component in part.GetComponents<Component>())
        {
            if (component == null)
                continue;
            SerializedObject data = new SerializedObject(component);
            SerializedProperty damageMaterial = data.FindProperty("damageMaterial");
            SerializedProperty renderers = damageMaterial?.FindPropertyRelative("renderers");
            if (renderers == null || !renderers.isArray)
                continue;
            for (int index = 0; index < renderers.arraySize; index++)
                if (renderers.GetArrayElementAtIndex(index).objectReferenceValue is Renderer renderer && renderer != null)
                    yield return renderer;
            yield break;
        }
    }

    private static void AddRenderer(DamageSection section, Renderer renderer, Transform aircraftRoot)
    {
        Mesh mesh = null;
        bool temporary = false;
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        if (filter != null)
            mesh = filter.sharedMesh;
        else if (renderer is SkinnedMeshRenderer skinned)
        {
            mesh = new Mesh { name = renderer.name + "_DamageAvatarBake" };
            skinned.BakeMesh(mesh);
            temporary = true;
        }
        if (mesh == null)
            throw new InvalidOperationException(section.Name + " owns renderer " + renderer.name +
                " without mesh geometry.");

        try
        {
            Vector3[] vertices = mesh.vertices;
            Matrix4x4 toAircraft = aircraftRoot.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                int[] triangles = mesh.GetTriangles(subMesh);
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 a = toAircraft.MultiplyPoint3x4(vertices[triangles[index]]);
                    Vector3 b = toAircraft.MultiplyPoint3x4(vertices[triangles[index + 1]]);
                    Vector3 c = toAircraft.MultiplyPoint3x4(vertices[triangles[index + 2]]);
                    section.Add(new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z));
                }
            }
        }
        finally
        {
            if (temporary)
                UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private static void AddColliderPlanform(DamageSection section, Transform part, Transform aircraftRoot)
    {
        BoxCollider collider = part.GetComponent<BoxCollider>();
        if (collider == null)
            throw new InvalidOperationException(section.Name + " has neither exterior geometry nor a box damage collider.");
        Vector3 half = collider.size * 0.5f;
        var points = new List<Vector2>();
        for (int mask = 0; mask < 8; mask++)
        {
            Vector3 corner = collider.center + new Vector3(
                (mask & 1) == 0 ? -half.x : half.x,
                (mask & 2) == 0 ? -half.y : half.y,
                (mask & 4) == 0 ? -half.z : half.z);
            Vector3 local = aircraftRoot.InverseTransformPoint(part.TransformPoint(corner));
            points.Add(new Vector2(local.x, local.z));
        }
        List<Vector2> hull = ConvexHull(points);
        for (int index = 1; index < hull.Count - 1; index++)
            section.Add(hull[0], hull[index], hull[index + 1]);
    }

    private static Color32[] Rasterize(DamageSection section, Vector2 aircraftMin, Vector2 aircraftMax,
        Color32[] sourcePixels, int sourceWidth, int sourceHeight,
        int alphaMinX, int alphaMinY, int alphaMaxX, int alphaMaxY)
    {
        int highSize = OutputSize * Supersample;
        var coverage = new byte[highSize * highSize];
        float xScale = (highSize - 1f) / (sourceWidth - 1f);
        float yScale = (highSize - 1f) / (sourceHeight - 1f);
        foreach (PlanformTriangle triangle in section.Triangles)
        {
            Vector2 a = ToMask(triangle.A);
            Vector2 b = ToMask(triangle.B);
            Vector2 c = ToMask(triangle.C);
            RasterizeTriangle(coverage, highSize, a, b, c);
        }

        var result = new Color32[OutputSize * OutputSize];
        int sampleCount = Supersample * Supersample;
        for (int y = 0; y < OutputSize; y++)
        for (int x = 0; x < OutputSize; x++)
        {
            int covered = 0;
            for (int sampleY = 0; sampleY < Supersample; sampleY++)
            for (int sampleX = 0; sampleX < Supersample; sampleX++)
                if (coverage[(y * Supersample + sampleY) * highSize + x * Supersample + sampleX] != 0)
                    covered++;
            if (covered == 0)
                continue;
            Color32 source = SourcePixel(sourcePixels, sourceWidth, sourceHeight, x, y);
            if (source.a == 0)
                continue;
            result[y * OutputSize + x] = new Color32(source.r, source.g, source.b,
                (byte)Mathf.RoundToInt(source.a * covered / (float)sampleCount));
        }
        return result;

        Vector2 ToMask(Vector2 point)
        {
            float across = Mathf.InverseLerp(aircraftMin.x, aircraftMax.x, point.x);
            // Texture2D pixel Y is bottom-up. The authored silhouette's nose is at
            // high pixel Y, matching positive aircraft-local Z.
            float foreAft = Mathf.InverseLerp(aircraftMin.y, aircraftMax.y, point.y);
            return new Vector2(Mathf.Lerp(alphaMinX, alphaMaxX, across) * xScale,
                Mathf.Lerp(alphaMinY, alphaMaxY, foreAft) * yScale);
        }
    }

    private static void RasterizeTriangle(byte[] target, int size, Vector2 a, Vector2 b, Vector2 c)
    {
        float area = Cross(b - a, c - a);
        if (Mathf.Abs(area) < 0.0001f)
            return;
        int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), 0, size - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))), 0, size - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), 0, size - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))), 0, size - 1);
        bool positive = area > 0f;
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
            float first = Cross(b - a, point - a);
            float second = Cross(c - b, point - b);
            float third = Cross(a - c, point - c);
            if (positive ? first >= -0.0001f && second >= -0.0001f && third >= -0.0001f
                         : first <= 0.0001f && second <= 0.0001f && third <= 0.0001f)
                target[y * size + x] = 255;
        }
    }

    private static Color32 SourcePixel(Color32[] source, int width, int height, int outputX, int outputY)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt((outputX + 0.5f) * width / OutputSize - 0.5f), 0, width - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt((outputY + 0.5f) * height / OutputSize - 0.5f), 0, height - 1);
        return source[y * width + x];
    }

    private static void WriteMask(string partName, Color32[] pixels)
    {
        Texture2D texture = new Texture2D(OutputSize, OutputSize, TextureFormat.RGBA32, false, false)
        {
            name = partName,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        try
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            byte[] png = texture.EncodeToPNG();
            string path = OutputRoot + "/" + partName + ".png";
            byte[] existing = File.Exists(path) ? File.ReadAllBytes(path) : null;
            if (existing == null || !existing.SequenceEqual(png))
                File.WriteAllBytes(path, png);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void ConfigureMaskImporter(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException(path + " has no TextureImporter.");
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.isReadable = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = OutputSize;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    private static Texture2D LoadPng(string assetPath)
    {
        byte[] bytes = File.ReadAllBytes(assetPath);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
        if (!ImageConversion.LoadImage(texture, bytes, false))
        {
            UnityEngine.Object.DestroyImmediate(texture);
            throw new InvalidOperationException("Unity could not decode " + assetPath + ".");
        }
        return texture;
    }

    private static void FindOpaqueBounds(Color32[] pixels, int width, int height,
        out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = width;
        minY = height;
        maxX = -1;
        maxY = -1;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            if (pixels[y * width + x].a > 0)
            {
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
            }
        if (maxX < minX || maxY < minY)
            throw new InvalidOperationException("The F-117 damage silhouette has no opaque pixels.");
    }

    private static List<Vector2> ConvexHull(IEnumerable<Vector2> source)
    {
        List<Vector2> points = source.OrderBy(point => point.x).ThenBy(point => point.y).ToList();
        var unique = new List<Vector2>();
        foreach (Vector2 point in points)
            if (unique.Count == 0 || (point - unique[unique.Count - 1]).sqrMagnitude > 0.00000001f)
                unique.Add(point);
        if (unique.Count <= 3)
            return unique;
        var lower = new List<Vector2>();
        foreach (Vector2 point in unique)
        {
            while (lower.Count >= 2 && Cross(lower[lower.Count - 1] - lower[lower.Count - 2],
                       point - lower[lower.Count - 1]) <= 0f)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(point);
        }
        var upper = new List<Vector2>();
        for (int index = unique.Count - 1; index >= 0; index--)
        {
            Vector2 point = unique[index];
            while (upper.Count >= 2 && Cross(upper[upper.Count - 1] - upper[upper.Count - 2],
                       point - upper[upper.Count - 1]) <= 0f)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(point);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static float Cross(Vector2 first, Vector2 second) =>
        first.x * second.y - first.y * second.x;

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
