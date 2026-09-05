using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

internal static class F117CanopyPaint
{
    internal static bool HasPairedFaces(Mesh mesh, int slot)
    {
        if (mesh == null || slot < 0) return false;
        int[] triangles = mesh.GetTriangles(slot);
        Vector3[] positions = mesh.vertices;
        Vector3[] normals = mesh.normals;
        if (triangles.Length == 0 || triangles.Length % 6 != 0 || normals.Length != positions.Length)
            return false;
        for (int i = 0; i < triangles.Length; i += 6)
            for (int corner = 0; corner < 3; corner++)
            {
                int a = triangles[i + corner];
                int b = triangles[i + 5 - corner];
                if (!positions[a].Equals(positions[b]) || (normals[a] + normals[b]).sqrMagnitude > 1e-10f)
                    return false;
            }
        return true;
    }

    internal static void Apply(GameObject visual, string outputDirectory)
    {
        var renderer = visual.GetComponentsInChildren<MeshRenderer>(true)
            .Single(r => r.name == "F117_Canopy_Mesh");
        var body = visual.GetComponentsInChildren<MeshRenderer>(true)
            .Single(r => r.name == "F117_Exterior_Mesh");
        Material paint = body.sharedMaterials.First(m => m.name.EndsWith("F117_EXTERNAL_1"));
        Material[] materials = renderer.sharedMaterials;
        int slot = Array.FindIndex(materials, m => m.name.EndsWith("INT_CockpitFrame"));
        if (slot < 0) throw new InvalidOperationException("Missing canopy frame material slot.");
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        Mesh source = filter.sharedMesh;
        Mesh mesh = UnityEngine.Object.Instantiate(source);
        mesh.name = "F117_Canopy_BodyPaint_TwoSided";
        var vertices = source.vertices.ToList();
        var normals = source.normals.ToList();
        var tangents = source.tangents.ToList();
        var colors = source.colors.ToList();
        var uv = new List<Vector4>[8];
        for (int channel = 0; channel < uv.Length; channel++)
        {
            uv[channel] = new List<Vector4>();
            source.GetUVs(channel, uv[channel]);
        }
        if (normals.Count != vertices.Count || uv[0].Count != vertices.Count)
            throw new InvalidOperationException("Canopy needs authored normals and UVs.");
        int[] frame = source.GetTriangles(slot);
        var glassVertices = new HashSet<int>(Enumerable.Range(0, source.subMeshCount)
            .Where(i => i != slot).SelectMany(source.GetTriangles));
        var front = new Dictionary<int, int>();
        var back = new Dictionary<int, int>();
        int CopyVertex(int original, bool reversed)
        {
            int index = vertices.Count;
            vertices.Add(vertices[original]);
            normals.Add(reversed ? -normals[original] : normals[original]);
            if (tangents.Count > 0)
            {
                Vector4 tangent = tangents[original];
                if (reversed) tangent.w = -tangent.w;
                tangents.Add(tangent);
            }
            if (colors.Count > 0) colors.Add(colors[original]);
            foreach (var channel in uv) if (channel.Count > 0) channel.Add(channel[original]);
            return index;
        }
        foreach (int original in frame.Distinct())
        {
            // Keep glass vertices and every non-paint UV channel untouched.
            int a = glassVertices.Contains(original) ? CopyVertex(original, false) : original;
            Vector4 coord = uv[0][original];
            // Actual clean paint patch beside the canopy, in the body's own atlas.
            coord.x = 0.783647418f + (Mathf.Repeat(coord.x, 1f) - .5f) * (8f / 1024f);
            coord.y = 0.758566737f + (Mathf.Repeat(coord.y, 1f) - .5f) * (8f / 1024f);
            uv[0][a] = coord;
            int b = CopyVertex(a, true);
            front[original] = a;
            back[original] = b;
        }
        var triangles = new List<int>(frame.Length * 2);
        for (int i = 0; i < frame.Length; i += 3)
        {
            triangles.Add(front[frame[i]]); triangles.Add(front[frame[i+1]]); triangles.Add(front[frame[i+2]]);
            triangles.Add(back[frame[i+2]]); triangles.Add(back[frame[i+1]]); triangles.Add(back[frame[i]]);
        }
        // Same physical surface with opposite face winding; no offset shells, no
        // added scene objects, and native backface culling draws only one side.
        if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        if (tangents.Count > 0) mesh.SetTangents(tangents);
        if (colors.Count > 0) mesh.SetColors(colors);
        for (int channel = 0; channel < uv.Length; channel++)
            if (uv[channel].Count > 0) mesh.SetUVs(channel, uv[channel]);
        mesh.SetTriangles(triangles, slot, false);
        mesh.bounds = source.bounds;
        for (int i = 0; i < source.subMeshCount; i++)
            if (i != slot && !mesh.GetTriangles(i).SequenceEqual(source.GetTriangles(i)))
                throw new InvalidOperationException("Canopy glass topology changed.");
        AssetDatabase.CreateAsset(mesh, outputDirectory + "/F117_Canopy_BodyPaint.asset");
        filter.sharedMesh = mesh;
        if (!HasPairedFaces(mesh, slot))
            throw new InvalidOperationException("Canopy paint faces are not opposite-wound pairs.");
        materials[slot] = paint;
        renderer.sharedMaterials = materials;
    }
}
