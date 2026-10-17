using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static StartWorkshopDresser;

/// <summary>
/// corebuster_render_007. Lines the room side of the boundary shell pieces that corebuster_render_005 keeps on their
/// shared material (<see cref="StartWorkshopLitMaterials.KeepShared"/>), whose outer faces close the exit corridor and
/// the corridor west of the room. Each liner is a single-sided copy of its piece's room-facing triangles, with the
/// piece's own transform moved <see cref="Offset"/> into the room, so its outline (including the doorway's cut-out)
/// and UVs match the piece exactly. The liners take the room's lit wall copy through
/// <see cref="StartWorkshopLitMaterials.ApplyTo"/>. Each has a <see cref="CasterName"/> child: the piece's own closed
/// mesh on the piece's exact transform, drawn only into shadow maps, so the kept piece blocks light like the room's lit
/// wall panels (a zero-thickness liner leaked a line of light along the floor through the shadow bias). Nothing has a
/// collider, so the exit gap, the walls' collision and the frame measurements are unchanged, and from outside the room
/// the liners are back faces and the casters are invisible, so the kept pieces' outer faces render as before. The kept
/// pieces themselves are never touched. Names avoid the "Wall" token that <see cref="StartWorkshopFrame.Measure"/> uses
/// to find the real walls.
/// </summary>
public static class StartWorkshopLiners
{
    public const string GroupName = "Liners_007";
    public const string CasterName = "Shadow_Caster";
    const string UndoName = "Build Start Workshop Liners";
    const string MeshParent = "Assets/FPS/Art/StartWorkshop", MeshFolder = MeshParent + "/BoundaryLiners";

    // How far each liner stands in front of its piece's room face: enough to win the depth test against the shared
    // material's room face across the room, and inside the 2-3 cm gap the wall-mounted passes leave.
    public const float Offset = .01f;
    // A triangle belongs to the room face when it faces the room within this cosine and lies within this distance of
    // the piece's room-most plane; the outer face, the ends and the top and bottom caps are left out.
    const float FacingCos = .99f, FaceTolerance = .02f;
    // Kept pieces are looked for within the interior plus this margin, like StartWorkshopLitMaterials' shell pieces.
    const float SearchMargin = 1.5f;
    // Validator: coverage sample spacing and inset from the piece's edges, and the inset kept from the edges of the
    // measured exit gap, which is only resolved to 5 cm.
    const float SampleStep = .25f, SampleInset = .05f, GapBand = .1f;
    const float PositionTolerance = .001f, AngleTolerance = .01f, ScaleTolerance = .001f;

    [MenuItem("Tools/CoreBuster/Build Start Workshop Liners")]
    public static void Rebuild()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var root = FindRoom(scene);
        if (root == null) return;
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        RebuildIn(scene, root);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeTransform = root.Find(GroupName);
        StartWorkshopChecks.Run();
    }

    /// <summary>
    /// Batch entry point: Unity.exe -batchmode -projectPath &lt;path&gt; -executeMethod StartWorkshopLiners.BuildBatch -quit.
    /// Opens MainScene, rebuilds the liners, applies the lit materials, saves the scene and runs the workshop validator.
    /// </summary>
    public static void BuildBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var root = FindRoom(scene);
        if (root == null) { EditorApplication.Exit(1); return; }
        RebuildIn(scene, root);
        EditorSceneManager.SaveScene(scene);
        StartWorkshopChecks.Run();
    }

    static Transform FindRoom(Scene scene)
    {
        var level = scene.path == ScenePath ? FindLevel(scene) : null;
        var root = level != null ? level.Find(ContainerName) : null;
        if (root == null)
            Debug.LogError($"Open {ScenePath} and run Tools/CoreBuster/Dress Start Workshop first; the liners belong under {LevelName}/{ContainerName}.");
        return root;
    }

    static void RebuildIn(Scene scene, Transform root)
    {
        var old = root.Find(GroupName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var f = StartWorkshopFrame.Measure(scene, root);
        int firstChild = root.childCount;
        try
        {
            Build(root, f);
            ApplyStaticConvention(root.GetChild(firstChild), GameObjectUtility.GetStaticEditorFlags(root.parent.gameObject));
        }
        catch
        {
            if (root.childCount > firstChild) UnityEngine.Object.DestroyImmediate(root.GetChild(firstChild).gameObject);
            throw;
        }
        Undo.RegisterCreatedObjectUndo(root.GetChild(firstChild).gameObject, UndoName);
        StartWorkshopLitMaterials.ApplyTo(scene, root);
        Debug.Log($"Start Workshop boundary liners rebuilt. {f}");
    }

    /// <summary>Called by the full workshop rebuild before the lit-materials step, and by the isolated menu action.</summary>
    public static void Build(Transform root, StartWorkshopFrame f)
    {
        if (root.Find(GroupName) != null)
            throw new InvalidOperationException($"{GroupName} already exists. Remove it before rebuilding the liners.");
        var kept = KeptPieces(root, f);
        var group = Group(root, GroupName);
        foreach (var name in StartWorkshopLitMaterials.KeepShared)
        {
            if (!kept.TryGetValue(name, out var piece))
                throw new InvalidOperationException($"Kept boundary piece '{name}' was not found at the Start Workshop's walls; " +
                                                    "the room shell is fixed, so check the scene before rebuilding the liners.");
            BuildLiner(group, piece, f);
        }
        AssetDatabase.SaveAssets();
    }

    static void BuildLiner(Transform group, MeshRenderer piece, StartWorkshopFrame f)
    {
        var filter = piece.GetComponent<MeshFilter>();
        var source = filter != null ? filter.sharedMesh : null;
        if (source == null) throw new InvalidOperationException($"Kept boundary piece '{piece.name}' has no mesh to line.");
        Vector3 inward = Inward(piece, f);
        string name = LinerName(piece.name);
        var mesh = SaveMesh(RoomFace(source, piece.transform, inward, name));

        var go = new GameObject(name);
        go.transform.SetParent(group, false);
        go.transform.SetPositionAndRotation(piece.transform.position + inward * Offset, piece.transform.rotation);
        Vector3 parentScale = group.lossyScale, scale = piece.transform.lossyScale;
        go.transform.localScale = new Vector3(scale.x / parentScale.x, scale.y / parentScale.y, scale.z / parentScale.z);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        // The shared material, as every pass instantiates; StartWorkshopLitMaterials.ApplyTo moves it onto the room's copy.
        r.sharedMaterials = piece.sharedMaterials;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = true;

        // The directional light reaches the south-west liners from outside the room. The piece's full thickness casts
        // the shadow, like every other wall panel, instead of the liner's single plane.
        var caster = new GameObject(CasterName);
        caster.transform.SetParent(go.transform, false);
        caster.transform.position = piece.transform.position;
        caster.AddComponent<MeshFilter>().sharedMesh = source;
        var cr = caster.AddComponent<MeshRenderer>();
        cr.sharedMaterials = piece.sharedMaterials;
        cr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        cr.receiveShadows = false;
    }

    /// <summary>The source mesh's room-facing triangles, with every vertex attribute, in the source's local space.</summary>
    static Mesh RoomFace(Mesh source, Transform t, Vector3 inward, string name)
    {
        var vertices = source.vertices;
        var normals = source.normals;
        var tangents = source.tangents;
        var uv0 = source.uv;
        var uv1 = source.uv2;
        var colors = source.colors;

        var remap = new Dictionary<int, int>();
        var used = new List<int>();
        var submeshes = RoomTriangles(source, t, inward);
        foreach (var tris in submeshes)
            for (int i = 0; i < tris.Count; i++)
            {
                if (!remap.TryGetValue(tris[i], out int index))
                {
                    index = used.Count;
                    remap[tris[i]] = index;
                    used.Add(tris[i]);
                }
                tris[i] = index;
            }
        if (used.Count == 0)
            throw new InvalidOperationException($"'{t.name}' has no triangles facing the room along {inward}; it cannot be lined.");

        var mesh = new Mesh { name = name };
        mesh.SetVertices(used.Select(i => vertices[i]).ToList());
        if (normals.Length == vertices.Length) mesh.SetNormals(used.Select(i => normals[i]).ToList());
        if (tangents.Length == vertices.Length) mesh.SetTangents(used.Select(i => tangents[i]).ToList());
        if (uv0.Length == vertices.Length) mesh.SetUVs(0, used.Select(i => uv0[i]).ToList());
        if (uv1.Length == vertices.Length) mesh.SetUVs(1, used.Select(i => uv1[i]).ToList());
        if (colors.Length == vertices.Length) mesh.SetColors(used.Select(i => colors[i]).ToList());
        mesh.subMeshCount = submeshes.Length;
        for (int s = 0; s < submeshes.Length; s++) mesh.SetTriangles(submeshes[s], s);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Per submesh, the source's vertex indices of the triangles on the room face: facing the room and on the room-most
    /// plane of the piece placed by <paramref name="t"/>.
    /// </summary>
    static List<int>[] RoomTriangles(Mesh source, Transform t, Vector3 inward)
    {
        var world = source.vertices.Select(v => t.TransformPoint(v)).ToArray();
        float face = world.Max(p => Vector3.Dot(p, inward));
        var submeshes = new List<int>[source.subMeshCount];
        for (int s = 0; s < source.subMeshCount; s++)
        {
            submeshes[s] = new List<int>();
            var tris = source.GetTriangles(s);
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = world[tris[i]], b = world[tris[i + 1]], c = world[tris[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a).normalized, inward) < FacingCos) continue;
                if (Mathf.Min(Vector3.Dot(a, inward), Vector3.Dot(b, inward), Vector3.Dot(c, inward)) < face - FaceTolerance) continue;
                submeshes[s].AddRange(new[] { tris[i], tris[i + 1], tris[i + 2] });
            }
        }
        return submeshes;
    }

    /// <summary>Writes the liner mesh to its asset in place, so a rebuild keeps the asset's GUID and the scene's reference.</summary>
    static Mesh SaveMesh(Mesh built)
    {
        if (!AssetDatabase.IsValidFolder(MeshFolder)) AssetDatabase.CreateFolder(MeshParent, MeshFolder.Substring(MeshParent.Length + 1));
        string path = $"{MeshFolder}/{built.name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(built, path);
            return built;
        }
        EditorUtility.CopySerialized(built, existing);
        UnityEngine.Object.DestroyImmediate(built);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    /// <summary>The kept pieces at the room's walls, by name. Kit pieces elsewhere in MainScene can share their names.</summary>
    static Dictionary<string, MeshRenderer> KeptPieces(Transform root, StartWorkshopFrame f)
    {
        var box = new Bounds();
        box.SetMinMax(new Vector3(f.West, f.Floor, f.South) - Vector3.one * SearchMargin,
            new Vector3(f.East, f.Ceiling, f.North) + Vector3.one * SearchMargin);
        var found = new Dictionary<string, MeshRenderer>();
        foreach (var go in root.gameObject.scene.GetRootGameObjects())
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.transform.IsChildOf(root) || !r.enabled || !StartWorkshopLitMaterials.KeepShared.Contains(r.name)) continue;
                if (!box.Contains(r.bounds.min) || !box.Contains(r.bounds.max)) continue;
                if (found.ContainsKey(r.name))
                    throw new InvalidOperationException($"More than one '{r.name}' stands at the Start Workshop's walls; the kept boundary piece is ambiguous.");
                found[r.name] = r;
            }
        return found;
    }

    /// <summary>The horizontal axis through the piece's thickness, pointing into the room.</summary>
    static Vector3 Inward(Renderer piece, StartWorkshopFrame f)
    {
        Bounds b = piece.bounds;
        Vector3 toRoom = f.Center - b.center;
        return b.size.x < b.size.z ? new Vector3(Mathf.Sign(toRoom.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(toRoom.z));
    }

    // "Alternate Wall Door (4)" -> "Liner_Alternate Door (4)", "Wall Plain (45)" -> "Liner_Plain (45)".
    static string LinerName(string piece)
    {
        string name = "Liner_" + piece.Replace("Wall ", "");
        if (name.Contains("Wall"))
            throw new InvalidOperationException($"Liner name '{name}' contains \"Wall\", which StartWorkshopFrame would measure as room geometry.");
        return name;
    }

    public static bool IsGroup(Transform group) => group != null && group.name == GroupName;

    /// <summary>
    /// corebuster_render_007 acceptance rules, separate from the prop rules (a liner is a surface of the shell, not a
    /// prop): one liner per kept piece, on its transform moved <see cref="Offset"/> into the room; only room-facing
    /// triangles; no collider; a shadows-only caster on the piece's own mesh and transform; covering exactly the piece's
    /// room face, so the doorway's opening stays as authored and the measured exit gap stays open.
    /// </summary>
    public static void Validate(Transform root, StartWorkshopFrame f, List<string> errors)
    {
        var group = root.Find(GroupName);
        if (group == null)
        {
            errors.Add($"missing {GroupName}; run Tools/CoreBuster/Build Start Workshop Liners.");
            return;
        }
        Dictionary<string, MeshRenderer> kept;
        try { kept = KeptPieces(root, f); }
        catch (InvalidOperationException e) { errors.Add(e.Message); return; }

        var expected = new HashSet<string>();
        foreach (var name in StartWorkshopLitMaterials.KeepShared)
        {
            string linerName = LinerName(name);
            expected.Add(linerName);
            var liner = group.Find(linerName);
            if (!kept.TryGetValue(name, out var piece)) errors.Add($"kept boundary piece {name} not found at the room's walls.");
            else if (liner == null) errors.Add($"{GroupName} has no {linerName} for {name}; run Tools/CoreBuster/Build Start Workshop Liners.");
            else ValidateLiner(f, liner, piece, errors);
        }
        foreach (Transform child in group)
            if (!expected.Contains(child.name)) errors.Add($"{GroupName}/{child.name} lines no kept boundary piece.");
    }

    static void ValidateLiner(StartWorkshopFrame f, Transform liner, MeshRenderer piece, List<string> errors)
    {
        string label = $"{GroupName}/{liner.name}";
        if (liner.GetComponentsInChildren<Collider>(true).Length > 0)
            errors.Add($"{label} has a collider; liners must leave the walls' collision and the exit gap as they are.");
        var filter = liner.GetComponent<MeshFilter>();
        var mesh = filter != null ? filter.sharedMesh : null;
        var r = liner.GetComponent<MeshRenderer>();
        if (mesh == null || r == null)
        {
            errors.Add($"{label} has no mesh or renderer.");
            return;
        }
        if (r.shadowCastingMode != ShadowCastingMode.Off)
            errors.Add($"{label} casts {r.shadowCastingMode} shadows; its {CasterName} casts them, and a single plane leaks light at the floor.");

        Vector3 inward = Inward(piece, f);
        Transform t = piece.transform;
        if (!OnTransform(liner, t, inward * Offset))
            errors.Add($"{label} is not on {piece.name}'s transform moved {Offset} m into the room; rebuild the liners.");

        var caster = liner.Find(CasterName);
        var casterFilter = caster != null ? caster.GetComponent<MeshFilter>() : null;
        var casterRenderer = caster != null ? caster.GetComponent<MeshRenderer>() : null;
        if (casterFilter == null || casterRenderer == null)
            errors.Add($"{label} has no {CasterName}; without it the directional light leaks through {piece.name} onto the floor.");
        else if (casterFilter.sharedMesh != piece.GetComponent<MeshFilter>().sharedMesh || !OnTransform(caster, t, Vector3.zero) ||
                 casterRenderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly)
            errors.Add($"{label}/{CasterName} must be {piece.name}'s own mesh on its exact transform, drawn only into shadow maps.");

        var world = mesh.vertices.Select(v => liner.TransformPoint(v)).ToArray();
        var tris = Enumerable.Range(0, mesh.subMeshCount).SelectMany(s => mesh.GetTriangles(s)).ToArray();
        int away = 0;
        for (int i = 0; i < tris.Length; i += 3)
            if (Vector3.Dot(Vector3.Cross(world[tris[i + 1]] - world[tris[i]], world[tris[i + 2]] - world[tris[i]]).normalized, inward) < FacingCos)
                away++;
        if (away > 0) errors.Add($"{label} has {away} triangle(s) not facing the room; they would show from outside or through the walls.");

        // Sample the piece's room face in wall coordinates (along the wall, height): the liner must cover exactly what
        // the piece's own room face covers, so the doorway's cut-out, chamfers and lintel included stay as authored.
        bool alongZ = inward.x != 0;
        var pieceWorld = piece.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v => t.TransformPoint(v)).ToArray();
        var pieceTris = RoomTriangles(piece.GetComponent<MeshFilter>().sharedMesh, t, inward).SelectMany(s => s).ToArray();
        Bounds b = piece.bounds;
        float along0 = alongZ ? b.min.z : b.min.x, along1 = alongZ ? b.max.z : b.max.x;
        int uncovered = 0, extra = 0, gap = 0;
        for (float u = along0 + SampleInset; u <= along1 - SampleInset; u += SampleStep)
        for (float v = b.min.y + SampleInset; v <= b.max.y - SampleInset; v += SampleStep)
        {
            bool covered = Covers(world, tris, alongZ, u, v), authored = Covers(pieceWorld, pieceTris, alongZ, u, v);
            if (authored && !covered) uncovered++;
            else if (covered && !authored) extra++;
            // The measured exit gap at walking height stays open whatever the mesh outline.
            if (covered && !alongZ && u > f.OpeningMinX + GapBand && u < f.OpeningMaxX - GapBand && v < f.Floor + StartWorkshopFrame.HeadHeight)
                gap++;
        }
        if (gap > 0) errors.Add($"{label} covers {gap} sample(s) of the measured exit gap below head height; the doorway must stay open.");
        if (extra > 0) errors.Add($"{label} covers {extra} sample(s) where {piece.name} is open; it must follow the piece's outline.");
        if (uncovered > 0) errors.Add($"{label} leaves {uncovered} sample(s) of {piece.name}'s room face uncovered, where it draws flat and leaks light.");
    }

    static bool OnTransform(Transform t, Transform piece, Vector3 offset) =>
        (t.position - (piece.position + offset)).magnitude <= PositionTolerance &&
        Quaternion.Angle(t.rotation, piece.rotation) <= AngleTolerance && (t.lossyScale - piece.lossyScale).magnitude <= ScaleTolerance;

    static bool Covers(Vector3[] world, int[] tris, bool alongZ, float u, float v)
    {
        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector2 a = Flat(world[tris[i]], alongZ), b = Flat(world[tris[i + 1]], alongZ), c = Flat(world[tris[i + 2]], alongZ);
            float d1 = Side(u, v, a, b), d2 = Side(u, v, b, c), d3 = Side(u, v, c, a);
            bool negative = d1 < -1e-6f || d2 < -1e-6f || d3 < -1e-6f, positive = d1 > 1e-6f || d2 > 1e-6f || d3 > 1e-6f;
            if (!(negative && positive)) return true;
        }
        return false;
    }

    static Vector2 Flat(Vector3 p, bool alongZ) => new Vector2(alongZ ? p.z : p.x, p.y);

    static float Side(float u, float v, Vector2 a, Vector2 b) => (u - b.x) * (a.y - b.y) - (a.x - b.x) * (v - b.y);
}
