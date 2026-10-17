using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Section = NorthWestRoomFramework.Section;

public static partial class NorthWestRoomLighting
{
    const string PatchPrefix = "Lighting_Patch_037_";
    static bool IsSplittable(Transform t)
    {
        var scene = SceneManager.GetActiveScene();
        return t.IsChildOf(NorthWestRoomFramework.FindSection(scene, Section.Rendering)) ||
            t.IsChildOf(NorthWestRoomFramework.FindSection(scene, Section.FloorDetail));
    }

    // Six broad env_036/042 surface renderers can intersect five or six culling frusta even where
    // an actor sees at most four. Partition their triangles without moving a surface or changing UVs.
    // The source mesh asset is never edited. First patch stays on the original renderer; additional
    // patches are identifiable children. This also lets an isolated lighting rebuild restore its input.
    static void SplitSurfaces(NorthWestRoomFramework.Layout layout)
    {
        var scene = SceneManager.GetActiveScene();
        var room = NorthWestRoomFramework.FindSection(scene, Section.Lighting).parent;
        var lights = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>())
            .Where(l => l.enabled && l.intensity > 0 && l.type != LightType.Directional && l.lightmapBakeType != LightmapBakeType.Baked).ToArray();
        foreach (var r in room.GetComponentsInChildren<MeshRenderer>().Where(r => IsSplittable(r.transform) &&
            !r.name.StartsWith(PatchPrefix, StringComparison.Ordinal)).ToArray())
        {
            var filter = r.GetComponent<MeshFilter>();
            var old = r.transform.Cast<Transform>().Where(t => t.name.StartsWith(PatchPrefix, StringComparison.Ordinal)).ToArray();
            if (old.Length > 0)
            {
                string guid = old[0].name.Substring(PatchPrefix.Length).Split('_')[0];
                Undo.RecordObject(filter, UndoName);
                filter.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guid))
                    ?? throw new InvalidOperationException("Missing original surface mesh " + guid);
                foreach (var t in old) Undo.DestroyObjectImmediate(t.gameObject);
            }
            if (lights.Count(l => Touches(l, r.bounds)) <= 4) continue;
            var original = filter.sharedMesh;
            if (original.subMeshCount != 1) throw new InvalidOperationException("Cannot split a multi-material surface " + r.name);
            string sourceGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original));
            var patches = new List<Mesh>();
            Partition(original, r.transform, lights, 0, patches);
            bool skin = r.transform.IsChildOf(NorthWestRoomFramework.FindSection(scene, Section.Rendering));
            Section owner = skin ? Section.Rendering : Section.FloorDetail;
            string meshFolder = NorthWestRoomFramework.AssetFolder(owner) + (skin ? "/Shell/Lighting037" : "/Lighting037");
            EnsureFolder(meshFolder);
            for (int i = 0; i < patches.Count; i++)
            {
                var mesh = Save(patches[i], meshFolder + "/" + sourceGuid + "_" + GlobalObjectId.GetGlobalObjectIdSlow(r).targetObjectId + "_" + i + ".asset", owner);
                if (i == 0) { Undo.RecordObject(filter, UndoName); filter.sharedMesh = mesh; continue; }
                var part = Group(r.transform, PatchPrefix + sourceGuid + "_" + i);
                part.gameObject.layer = r.gameObject.layer;
                GameObjectUtility.SetStaticEditorFlags(part.gameObject, GameObjectUtility.GetStaticEditorFlags(r.gameObject));
                part.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var copy = part.gameObject.AddComponent<MeshRenderer>();
                EditorUtility.CopySerialized(r, copy);
            }
            Debug.Log($"Lighting surface partition: {r.name}, {patches.Count} patches, unchanged source mesh/UVs/area.");
        }
        // These two folders contain only this pass's derived patches. Keep the source meshes and
        // delete obsolete derived assets only after no scene MeshFilter references them.
        var used = new HashSet<string>(room.GetComponentsInChildren<MeshFilter>(true)
            .Where(f => f.sharedMesh).Select(f => AssetDatabase.GetAssetPath(f.sharedMesh)));
        foreach (string folder in new[] { NorthWestRoomFramework.AssetRoot + "/Rendering/Shell/Lighting037",
            NorthWestRoomFramework.AssetRoot + "/Floor_Detail/Lighting037" })
            if (AssetDatabase.IsValidFolder(folder))
                foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!used.Contains(path)) AssetDatabase.DeleteAsset(path);
                }
    }

    static string OriginalMeshIdentity(Transform t)
    {
        var mf = t.GetComponent<MeshFilter>();
        if (!mf || !IsSplittable(t)) return "";
        var patch = t.Cast<Transform>().FirstOrDefault(c => c.name.StartsWith(PatchPrefix, StringComparison.Ordinal));
        return patch ? patch.name.Substring(PatchPrefix.Length).Split('_')[0] : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mf.sharedMesh));
    }

    static void ValidateSurfaceAreas(Transform room)
    {
        foreach (var mf in room.GetComponentsInChildren<MeshFilter>(true).Where(f => IsSplittable(f.transform)))
        {
            var children = mf.transform.Cast<Transform>().Where(t => t.name.StartsWith(PatchPrefix, StringComparison.Ordinal)).ToArray();
            if (children.Length == 0) continue;
            string guid = OriginalMeshIdentity(mf.transform);
            var original = AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guid));
            if (!original) throw new InvalidOperationException("Lost original mesh " + guid);
            float expected = Area(original), actual = Area(mf.sharedMesh) + children.Sum(t => Area(t.GetComponent<MeshFilter>().sharedMesh));
            if (Mathf.Abs(expected - actual) > Mathf.Max(.0001f, expected * .0001f))
                throw new InvalidOperationException("Surface partitions changed area: " + mf.name);
            foreach (var child in children)
                if (child.localPosition != Vector3.zero || child.localRotation != Quaternion.identity || child.localScale != Vector3.one)
                    throw new InvalidOperationException("Surface partition transform changed: " + child.name);
        }
    }

    static void Partition(Mesh mesh, Transform t, Light[] lights, int depth, List<Mesh> result)
    {
        var vertices = mesh.vertices;
        var bounds = new Bounds(t.TransformPoint(vertices[0]), Vector3.zero);
        foreach (var v in vertices) bounds.Encapsulate(t.TransformPoint(v));
        if (lights.Count(l => Touches(l, bounds)) <= 4) { result.Add(Object.Instantiate(mesh)); return; }
        if (depth >= 8) throw new InvalidOperationException("Surface still exceeds four lights after eight splits at " + bounds);
        int axis = bounds.size.x >= bounds.size.y && bounds.size.x >= bounds.size.z ? 0 : bounds.size.y >= bounds.size.z ? 1 : 2;
        float cut = bounds.center[axis];
        float originalArea = Area(mesh);
        var a = Clip(mesh, t, axis, cut, false); var b = Clip(mesh, t, axis, cut, true);
        if (a.vertexCount == 0 || b.vertexCount == 0 || Mathf.Abs(Area(a) + Area(b) - originalArea) > Mathf.Max(.0001f, originalArea * .0001f))
            throw new InvalidOperationException("Lighting surface partition lost geometry.");
        try { Partition(a, t, lights, depth + 1, result); Partition(b, t, lights, depth + 1, result); }
        finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
    }

    struct Vertex
    {
        public Vector3 p, normal; public Vector2 uv; public Vector4 tangent; public Color color;
        public static Vertex Lerp(Vertex a, Vertex b, float f) => new Vertex {
            p = Vector3.Lerp(a.p,b.p,f), normal = Vector3.Lerp(a.normal,b.normal,f), uv = Vector2.Lerp(a.uv,b.uv,f),
            tangent = Vector4.Lerp(a.tangent,b.tangent,f), color = Color.Lerp(a.color,b.color,f) };
    }
    static Mesh Clip(Mesh mesh, Transform t, int axis, float cut, bool greater)
    {
        var p=mesh.vertices; var n=mesh.normals; var uv=mesh.uv; var tangents=mesh.tangents; var colors=mesh.colors;
        var v = p.Select((point,i)=>new Vertex {p=point,normal=n.Length==p.Length?n[i]:Vector3.up,
            uv=uv.Length==p.Length?uv[i]:Vector2.zero,tangent=tangents.Length==p.Length?tangents[i]:Vector4.zero,
            color=colors.Length==p.Length?colors[i]:Color.white}).ToArray();
        var output=new List<Vertex>(); var triangles=new List<int>(); var input=mesh.triangles;
        float Distance(Vertex a) => (t.TransformPoint(a.p)[axis]-cut)*(greater?1:-1);
        for(int i=0;i<input.Length;i+=3)
        {
            var polygon=new List<Vertex>();
            var tri=new[]{v[input[i]],v[input[i+1]],v[input[i+2]]};
            for(int j=0;j<3;j++)
            {
                var a=tri[j];var b=tri[(j+1)%3];float da=Distance(a),db=Distance(b);
                if(da>=0)polygon.Add(a);
                if((da<0)!=(db<0))polygon.Add(Vertex.Lerp(a,b,da/(da-db)));
            }
            int start=output.Count;output.AddRange(polygon);
            for(int j=1;j+1<polygon.Count;j++)triangles.AddRange(new[]{start,start+j,start+j+1});
        }
        var result=new Mesh();result.SetVertices(output.Select(a=>a.p).ToList());result.SetTriangles(triangles,0);
        result.SetNormals(output.Select(a=>a.normal).ToList());result.SetUVs(0,output.Select(a=>a.uv).ToList());
        if(tangents.Length==p.Length)result.SetTangents(output.Select(a=>a.tangent).ToList());
        if(colors.Length==p.Length)result.SetColors(output.Select(a=>a.color).ToList());
        result.RecalculateBounds();return result;
    }
    static float Area(Mesh mesh)
    {
        var v=mesh.vertices;var t=mesh.triangles;float result=0;
        for(int i=0;i<t.Length;i+=3)result+=Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]).magnitude*.5f;
        return result;
    }
}
