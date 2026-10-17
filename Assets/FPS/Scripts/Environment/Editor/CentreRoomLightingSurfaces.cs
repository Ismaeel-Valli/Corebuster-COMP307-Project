using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Section = CentreRoomFramework.Section;

public static partial class CentreRoomLighting
{
    const string CopyFolder = Folder + "/Materials/Copies/";

    static Material LitCopy(Material source)
    {
        if (!source || !source.shader) throw new InvalidOperationException("Missing source material.");
        string sourcePath = AssetDatabase.GetAssetPath(source);
        if (sourcePath.StartsWith(CopyFolder, StringComparison.Ordinal))
        {
            string guid = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
            source = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (!source) throw new InvalidOperationException("Cannot resolve original material " + guid);
            sourcePath = AssetDatabase.GetAssetPath(source);
        }
        if (source.shader.name.StartsWith("CoreBuster/", StringComparison.Ordinal)) return source;
        EnsureFolder(CopyFolder.TrimEnd('/'));
        var copy = new Material(source);
        if (copy.parent) copy.parent = null;
        if (copy.shader.name != "Universal Render Pipeline/Lit")
        {
            // These imported materials retain Standard's serialized maps even though their shader was changed to 2D.
            copy.shader = Shader.Find("Standard");
            if (copy.GetFloat("_Mode") > 3) throw new InvalidOperationException("Unsupported source blend mode: " + sourcePath);
            new StandardUpgrader("Standard").Upgrade(copy, MaterialUpgrader.UpgradeFlags.None);
        }
        if (!copy.GetTexture("_MetallicGlossMap")) copy.SetFloat("_Metallic", Mathf.Min(.28f, copy.GetFloat("_Metallic")));
        copy.SetFloat("_Smoothness", Mathf.Min(.45f, copy.GetFloat("_Smoothness")));
        BaseShaderGUI.SetMaterialKeywords(copy, LitGUI.SetMaterialKeywords);
        return Save(copy, CopyFolder + AssetDatabase.AssetPathToGUID(sourcePath) + ".mat");
    }

    static void ApplyMaterials(Scene scene)
    {
        var root = CentreRoomFramework.FindSection(scene, Section.Lighting).parent;
        var lighting = CentreRoomFramework.FindSection(scene, Section.Lighting);
        var copies = new Dictionary<Material, Material>();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true).Where(r => !r.transform.IsChildOf(lighting)))
        {
            var materials = r.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (!source) throw new InvalidOperationException(r.name + " has a missing material.");
                if (!copies.TryGetValue(source, out var copy)) copies[source] = copy = LitCopy(source);
                materials[i] = copy;
            }
            Undo.RecordObject(r, UndoName); r.sharedMaterials = materials;
            if (PrefabUtility.IsPartOfPrefabInstance(r)) PrefabUtility.RecordPrefabInstancePropertyModifications(r);
        }
    }

    struct Vertex
    {
        public Vector3 p, n;
        public Vector2 uv;
        public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex {
            p = Vector3.Lerp(a.p, b.p, t), n = Vector3.Lerp(a.n, b.n, t).normalized, uv = Vector2.Lerp(a.uv, b.uv, t) };
    }

    static void BuildSurfaces(Transform parent, CentreRoomFramework.Layout layout)
    {
        Transform group = Group(parent, "Room_Surfaces");
        group.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var seen = new HashSet<string>();
        var generated = new HashSet<string>();
        var copies = new Dictionary<Material, Material>();
        foreach (var shell in layout.report.shell)
        {
            if (!GlobalObjectId.TryParse(shell.id, out var id)) throw new InvalidOperationException("Invalid measured shell ID.");
            var r = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as MeshRenderer;
            var filter = r ? r.GetComponent<MeshFilter>() : null;
            if (!filter || !filter.sharedMesh) throw new InvalidOperationException("Missing measured shell mesh " + shell.name);
            Mesh src = filter.sharedMesh;
            Vector3[] positions = src.vertices, normals = src.normals;
            Vector2[] uv = src.uv;
            Vector3 inward;
            Bounds b = r.bounds;
            if (b.center.y < layout.Floor) inward = Vector3.up;
            else if (b.center.y > layout.Ceiling) inward = Vector3.down;
            else
            {
                var distances = new[] { Mathf.Abs(b.center.x - layout.interior.min.x), Mathf.Abs(b.center.x - layout.interior.max.x),
                    Mathf.Abs(b.center.z - layout.interior.min.z), Mathf.Abs(b.center.z - layout.interior.max.z) };
                inward = new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back }[Array.IndexOf(distances, distances.Min())];
            }
            // Suppress the known duplicate floor tile without editing either original shell object.
            string key = $"{b.center:F3}|{b.size:F3}|{src.GetEntityId()}";
            if (!seen.Add(key)) continue;
            for (int sub = 0; sub < src.subMeshCount; sub++)
            {
              // Eight-metre floor meshes would receive too many forward lights even though
              // their individual visible regions do not. Split only the finish, preserving the shell.
              int patches = inward == Vector3.up ? 4 : 1;
              for (int patch = 0; patch < patches; patch++)
              {
                var vertices = new List<Vector3>(); var outNormals = new List<Vector3>(); var outUV = new List<Vector2>(); var indices = new List<int>();
                var triangles = src.GetTriangles(sub);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var poly = new List<Vertex>();
                    for (int j = 0; j < 3; j++)
                    {
                        int index = triangles[i + j];
                        poly.Add(new Vertex { p = filter.transform.TransformPoint(positions[index]),
                            n = filter.transform.TransformDirection(normals[index]).normalized,
                            uv = uv.Length == positions.Length ? uv[index] : Vector2.zero });
                    }
                    Vector3 normal = (poly[0].n + poly[1].n + poly[2].n).normalized;
                    if (Vector3.Dot(normal, inward) < .55f) continue;
                    // Preserve existing openings and UVs. Recessed room faces move to the interior plane;
                    // only the room-facing surface is copied, never the outer face or floor side edge.
                    for (int j = 0; j < poly.Count; j++)
                    {
                        var v = poly[j];
                        for (int axis = 0; axis < 3; axis++)
                            if (Mathf.Abs(inward[axis]) > .5f)
                                v.p[axis] = Mathf.Clamp(v.p[axis], layout.interior.min[axis], layout.interior.max[axis]);
                        v.p += inward * .008f; poly[j] = v;
                    }
                    for (int axis = 0; axis < 3 && poly.Count >= 3; axis++)
                    {
                        poly = Clip(poly, axis, layout.interior.min[axis] + .003f, true);
                        poly = Clip(poly, axis, layout.interior.max[axis] - .003f, false);
                    }
                    if (patches > 1)
                    {
                        poly = Clip(poly, 0, b.center.x, (patch & 1) != 0);
                        poly = Clip(poly, 2, b.center.z, (patch & 2) != 0);
                    }
                    for (int j = 1; j + 1 < poly.Count; j++)
                    {
                        if (Vector3.Cross(poly[j].p - poly[0].p, poly[j + 1].p - poly[0].p).sqrMagnitude < 1e-12f) continue;
                        foreach (var v in new[] { poly[0], poly[j], poly[j + 1] })
                        { indices.Add(vertices.Count); vertices.Add(v.p); outNormals.Add(v.n); outUV.Add(v.uv); }
                    }
                }
                if (vertices.Count == 0) continue;
                string name = "Surface_" + id.targetPrefabId + "_" + id.targetObjectId + "_" + sub + "_" + patch;
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices); mesh.SetNormals(outNormals); mesh.SetUVs(0, outUV); mesh.SetTriangles(indices, 0);
                mesh.RecalculateBounds(); mesh.RecalculateTangents();
                mesh = Save(mesh, Folder + "/Shell/" + name + ".asset");
                generated.Add(AssetDatabase.GetAssetPath(mesh));
                var t = Group(group, name);
                t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = t.gameObject.AddComponent<MeshRenderer>();
                Material source = r.sharedMaterials[Mathf.Min(sub, r.sharedMaterials.Length - 1)];
                if (!copies.TryGetValue(source, out var material)) copies[source] = material = LitCopy(source);
                renderer.sharedMaterial = material;
              }
            }
        }
        foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { Folder + "/Shell" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!generated.Contains(path)) AssetDatabase.DeleteAsset(path);
        }
        Debug.Log("Centre Room room-facing surface copies: " + group.childCount);
    }

    static List<Vertex> Clip(List<Vertex> poly, int axis, float plane, bool keepGreater)
    {
        var result = new List<Vertex>();
        if (poly.Count == 0) return result;
        Vertex previous = poly[poly.Count - 1];
        float pd = (previous.p[axis] - plane) * (keepGreater ? 1 : -1);
        foreach (var current in poly)
        {
            float cd = (current.p[axis] - plane) * (keepGreater ? 1 : -1);
            if ((cd >= 0) != (pd >= 0)) result.Add(Vertex.Lerp(previous, current, pd / (pd - cd)));
            if (cd >= 0) result.Add(current);
            previous = current; pd = cd;
        }
        return result;
    }

    /// <summary>Only asset-backed, collider-free boundary skins may cross floor gameplay reservations.</summary>
    public static bool IsBoundarySurface(Renderer r, CentreRoomFramework.Layout layout)
    {
        var lighting = CentreRoomFramework.FindSection(r.gameObject.scene, Section.Lighting);
        if (!lighting || r.transform.parent != lighting.Find("Room_Surfaces") || r.GetComponent<Collider>()) return false;
        var mf = r.GetComponent<MeshFilter>();
        if (!mf || !mf.sharedMesh || !AssetDatabase.GetAssetPath(mf.sharedMesh).StartsWith(Folder + "/Shell/", StringComparison.Ordinal)) return false;
        Bounds b = layout.interior;
        foreach (var vertex in mf.sharedMesh.vertices)
        {
            Vector3 p = r.transform.TransformPoint(vertex);
            if (!b.Contains(p)) return false;
            float edge = Mathf.Min(p.x - b.min.x, b.max.x - p.x, p.y - b.min.y, b.max.y - p.y, p.z - b.min.z, b.max.z - p.z);
            if (edge > .5f) return false;
        }
        return true;
    }
}
