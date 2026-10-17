using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// corebuster_render_005. Gives the Start Workshop its own lit copies of the third-party pack materials its renderers
/// use, and assigns them as material overrides on renderers inside the room only: everything under
/// Level/Start_Workshop, plus the scene-root shell pieces (walls, floor and ceiling tiles, the doorway) whose bounds lie
/// inside the measured interior, allowing for wall and tile thickness. Each copy lives under <see cref="CopyRoot"/> at
/// its source's path below Assets/, and is regenerated from that source on every run: converted with
/// <see cref="WorkshopMaterialUpgrade.Upgrade"/>, then calibrated for the room. Pack materials and prefabs are never
/// edited, so no other area changes. Tune a copy here, not in the Inspector; the next run overwrites hand edits.
/// </summary>
public static class StartWorkshopLitMaterials
{
    public const string CopyRoot = "Assets/FPS/Art/Materials/Workshop";
    const string UndoName = "Apply Start Workshop Lit Materials";
    const string LitShader = "Universal Render Pipeline/Lit";

    // How far a shell piece may extend past the measured interior faces: wall panels behind the pilasters, floor and
    // ceiling tile thickness. The nearest neighbouring-space walls stand about 3 m outside the west wall.
    const float ShellAllowance = 1.5f;

    // The kit's authored metallic (0.5-0.9; the floor is 0.888) leaves almost no diffuse term, so the floor rendered
    // black. It is remapped into the worn/painted-steel range; values at or below the range floor are left alone, so
    // non-metals stay non-metal. Smoothness stays as authored. corebuster_render_006 tried raising the floor's to 0.68
    // for a wet sheen: the floor tiles' outer edges show at the foot of the walls from the corridor west of the room and
    // outside the south wall, so the change reached those views. A sheen belongs on the tiles' top faces only.
    const float MetallicMin = .1f, MetallicMax = .4f, AuthoredMetallicMax = .9f;

    // Boundary shell pieces (GameObject names) that keep their shared material because an override would visibly change
    // a neighbouring space. Each is one single-material mesh, so its outer face would change with its inner face.
    // Found 2026-09-29 by raycasting from every NavMesh-reachable standing spot outside the room, then confirmed by
    // captures: lit, these outer faces turn from flat grey to near-black, and the room's own lights must stop at its
    // walls. The doorway piece and the north panel west of it close the end of the exit corridor; the two southern
    // west-wall panels close the open end of the corridor that runs west of the room. Keep in step with the
    // corebuster_render_005 record. StartWorkshopLiners (corebuster_render_007) lines their room side.
    internal static readonly string[] KeepShared = { "Alternate Wall Door (4)", "Wall Plain (45)", "Wall Plain (50)", "Wall Plain (58)" };

    [MenuItem("Tools/CoreBuster/Apply Start Workshop Lit Materials")]
    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var root = FindRoom(scene);
        if (root == null) return;
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        if (ApplyTo(scene, root) > 0) EditorSceneManager.MarkSceneDirty(scene);
        StartWorkshopChecks.Run();
    }

    /// <summary>
    /// Batch entry point: Unity.exe -batchmode -projectPath &lt;path&gt; -executeMethod StartWorkshopLitMaterials.ApplyBatch -quit.
    /// Opens MainScene, applies the overrides, saves the scene if anything changed, then runs the workshop validator.
    /// </summary>
    public static void ApplyBatch()
    {
        var scene = EditorSceneManager.OpenScene(StartWorkshopDresser.ScenePath, OpenSceneMode.Single);
        var root = FindRoom(scene);
        if (root == null) { EditorApplication.Exit(1); return; }
        if (ApplyTo(scene, root) > 0) EditorSceneManager.SaveScene(scene);
        StartWorkshopChecks.Run();
    }

    static Transform FindRoom(Scene scene)
    {
        var level = scene.path == StartWorkshopDresser.ScenePath ? StartWorkshopDresser.FindLevel(scene) : null;
        var root = level != null ? level.Find(StartWorkshopDresser.ContainerName) : null;
        if (root == null)
            Debug.LogError($"Open {StartWorkshopDresser.ScenePath} and run Tools/CoreBuster/Dress Start Workshop first; the lit " +
                           $"materials belong on {StartWorkshopDresser.LevelName}/{StartWorkshopDresser.ContainerName} and the room shell.");
        return root;
    }

    /// <summary>
    /// Regenerates the copies and moves every room renderer onto them. Called by the menu and batch actions and as the
    /// last step of every workshop pass's build menu, inside that pass's undo group, since the builders instantiate kit
    /// prefabs with their shared materials. Returns how many renderers changed.
    /// </summary>
    public static int ApplyTo(Scene scene, Transform root)
    {
        var f = StartWorkshopFrame.Measure(scene, root);
        var room = RoomRenderers(scene, root, f);
        var log = new StringBuilder();

        var copies = new Dictionary<Material, Material>();
        var convert = new List<(string path, Material material)>();
        // Sources of copies already assigned are regenerated too, so a calibration change reaches every copy.
        foreach (var source in room.SelectMany(r => r.sharedMaterials).Select(m => IsCopy(m) ? SourceOf(m) : m)
                     .Where(IsPackMaterial).Distinct().OrderBy(m => AssetDatabase.GetAssetPath(m), StringComparer.Ordinal))
        {
            string path = AssetDatabase.GetAssetPath(source);
            if (!Copyable(source))
            {
                log.AppendLine($"  left shared: {path} ({(source.shader == null ? "no shader" : source.shader.name)} cannot be converted to URP/Lit)");
                continue;
            }
            var copy = Regenerate(source);
            copies[source] = copy;
            if (source.shader.name != LitShader) convert.Add((AssetDatabase.GetAssetPath(copy), copy));
        }
        if (convert.Count > 0) WorkshopMaterialUpgrade.Upgrade(convert);
        foreach (var pair in copies) log.AppendLine($"  {AssetDatabase.GetAssetPath(pair.Value)}: {Calibrate(pair.Value)}");
        AssetDatabase.SaveAssets();

        int changed = 0;
        var inRoom = new HashSet<Renderer>(room);
        foreach (var r in room)
            if (Swap(r, m => copies.TryGetValue(m, out var copy) ? copy : m)) changed++;
        // A piece that has left the room (or joined KeepShared) goes back to the shared pack material.
        var restored = new List<string>();
        foreach (var r in AllRenderers(scene))
            if (!inRoom.Contains(r) && Swap(r, m => IsCopy(m) ? SourceOf(m) ?? m : m))
            {
                changed++;
                restored.Add(Describe(r));
            }

        var overridden = room.Where(r => r.sharedMaterials.Any(IsCopy)).Select(Describe).ToList();
        var report = new StringBuilder();
        report.AppendLine($"Start Workshop lit materials: {copies.Count} copies under {CopyRoot}, {overridden.Count} room renderers on them, " +
                          $"{changed} renderer(s) changed this run, {restored.Count} outside the room restored to shared materials.");
        report.AppendLine(f.ToString());
        report.Append(log);
        report.AppendLine("Overridden renderers (all inside the frame):");
        foreach (var line in overridden) report.AppendLine("  " + line);
        foreach (var line in restored) report.AppendLine("  RESTORED outside the room: " + line);
        Debug.Log(report.ToString());
        return changed;
    }

    /// <summary>The renderers the lit copies belong on: everything under the container, then the shell pieces.</summary>
    public static List<Renderer> RoomRenderers(Scene scene, Transform root, StartWorkshopFrame f)
    {
        var room = new List<Renderer>(root.GetComponentsInChildren<Renderer>(true));
        room.AddRange(ShellRenderers(scene, root, f));
        return room;
    }

    /// <summary>
    /// Kit-material mesh renderers outside the container whose bounds lie wholly inside the interior plus
    /// <see cref="ShellAllowance"/>. Only active renderers have placed bounds. The player, enemies and anything in
    /// <see cref="KeepShared"/> are left out.
    /// </summary>
    public static List<Renderer> ShellRenderers(Scene scene, Transform root, StartWorkshopFrame f)
    {
        var box = new Bounds();
        box.SetMinMax(new Vector3(f.West, f.Floor, f.South) - Vector3.one * ShellAllowance,
            new Vector3(f.East, f.Ceiling, f.North) + Vector3.one * ShellAllowance);
        var shell = new List<Renderer>();
        foreach (var go in scene.GetRootGameObjects())
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.transform.IsChildOf(root) || !r.enabled || Kept(r.transform) ||
                    r.GetComponentInParent<Health>(true) != null || r.GetComponentInParent<PlayerCharacterController>(true) != null)
                    continue;
                Bounds b = r.bounds;
                if (box.Contains(b.min) && box.Contains(b.max) && r.sharedMaterials.Any(m => IsPackMaterial(m) || IsCopy(m)))
                    shell.Add(r);
            }
        return shell;
    }

    /// <summary>
    /// Validator rules: no shell renderer draws flat (the container's renderers have the same rule in
    /// <see cref="StartWorkshopChecks"/>), no room renderer is still on a shared pack material that should have a lit
    /// copy, and no renderer outside the room references a copy.
    /// </summary>
    public static void Validate(Transform root, StartWorkshopFrame f, List<string> errors)
    {
        var scene = root.gameObject.scene;
        var shell = new HashSet<Renderer>(ShellRenderers(scene, root, f));
        var room = new HashSet<Renderer>(RoomRenderers(scene, root, f));
        foreach (var r in room)
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                bool flat = WorkshopMaterialUpgrade.RendersFlat(m.shader);
                if (flat && shell.Contains(r))
                    errors.Add($"Shell {Describe(r)} material {m.name} uses {m.shader.name}, which the forward renderer draws " +
                               "unlit or not at all; run Tools/CoreBuster/Apply Start Workshop Lit Materials.");
                else if (!flat && IsPackMaterial(m) && Copyable(m))
                    errors.Add($"{Describe(r)} still uses the shared pack material {AssetDatabase.GetAssetPath(m)}; run " +
                               "Tools/CoreBuster/Apply Start Workshop Lit Materials.");
            }
        foreach (var r in AllRenderers(scene))
            if (!room.Contains(r) && r.sharedMaterials.Any(IsCopy))
                errors.Add($"{Describe(r)} is outside the Start Workshop but references a {CopyRoot} copy; only the room may use them.");
    }

    static bool Swap(Renderer r, Func<Material, Material> map)
    {
        var materials = r.sharedMaterials;
        bool any = false;
        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i] == null) continue;
            var next = map(materials[i]);
            if (next == materials[i]) continue;
            materials[i] = next;
            any = true;
        }
        if (!any) return false;
        Undo.RecordObject(r, UndoName);
        r.sharedMaterials = materials;
        if (PrefabUtility.IsPartOfPrefabInstance(r)) PrefabUtility.RecordPrefabInstancePropertyModifications(r);
        return true;
    }

    /// <summary>
    /// Resets the copy to its source (creating it the first time), ready for conversion and calibration. A copy of a
    /// material variant (the kit's Sign Red is a variant of Shelf Trans Mat) is flattened, keeping its resolved values:
    /// a variant cannot change shader, and a copy must not inherit from a shared pack material.
    /// </summary>
    static Material Regenerate(Material source)
    {
        string sourcePath = AssetDatabase.GetAssetPath(source), copyPath = CopyPathOf(sourcePath);
        var copy = AssetDatabase.LoadAssetAtPath<Material>(copyPath);
        if (copy != null)
        {
            Undo.RecordObject(copy, UndoName);
            EditorUtility.CopySerialized(source, copy);
        }
        else
        {
            EnsureFolder(copyPath.Substring(0, copyPath.LastIndexOf('/')));
            if (!AssetDatabase.CopyAsset(sourcePath, copyPath))
                throw new InvalidOperationException($"Could not copy {sourcePath} to {copyPath}.");
            copy = AssetDatabase.LoadAssetAtPath<Material>(copyPath);
        }
        if (copy.parent != null) copy.parent = null;
        return copy;
    }

    static string Calibrate(Material copy)
    {
        if (copy.shader == null || copy.shader.name != LitShader)
            return $"on {(copy.shader == null ? "no shader" : copy.shader.name)}, not calibrated";
        if (copy.GetTexture("_MetallicGlossMap") != null) return "metallic comes from its map; left as authored";
        float authored = copy.GetFloat("_Metallic"), metallic = CalibratedMetallic(authored);
        copy.SetFloat("_Metallic", metallic);
        EditorUtility.SetDirty(copy);
        return FormattableString.Invariant($"metallic {authored:F3} -> {metallic:F3}, smoothness {copy.GetFloat("_Smoothness"):F3}");
    }

    static float CalibratedMetallic(float authored) => authored <= MetallicMin
        ? authored
        : Mathf.Min(MetallicMax, MetallicMin + (MetallicMax - MetallicMin) * (authored - MetallicMin) / (AuthoredMetallicMax - MetallicMin));

    /// <summary>A pack material the conversion routine can turn into URP/Lit, or one already on URP/Lit.</summary>
    static bool Copyable(Material m) => m.shader != null && m.shader.name != "Hidden/InternalErrorShader" &&
                                        (m.shader.name == LitShader || WorkshopMaterialUpgrade.RendersFlat(m.shader));

    public static bool IsPackMaterial(Material m) =>
        m != null && WorkshopMaterialUpgrade.PackRoots.Any(p => AssetDatabase.GetAssetPath(m).StartsWith(p + "/", StringComparison.Ordinal));

    public static bool IsCopy(Material m) => m != null && AssetDatabase.GetAssetPath(m).StartsWith(CopyRoot + "/", StringComparison.Ordinal);

    // "Assets/SciFi Warehouse Kit/Art/Materials/Floor Tile Mat.mat" <-> CopyRoot + "/SciFi Warehouse Kit/Art/Materials/Floor Tile Mat.mat"
    static string CopyPathOf(string sourcePath) => CopyRoot + sourcePath.Substring("Assets".Length);

    static Material SourceOf(Material copy) =>
        AssetDatabase.LoadAssetAtPath<Material>("Assets" + AssetDatabase.GetAssetPath(copy).Substring(CopyRoot.Length));

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        int slash = folder.LastIndexOf('/');
        EnsureFolder(folder.Substring(0, slash));
        AssetDatabase.CreateFolder(folder.Substring(0, slash), folder.Substring(slash + 1));
    }

    internal static bool Kept(Transform t)
    {
        for (; t != null; t = t.parent) if (KeepShared.Contains(t.name)) return true;
        return false;
    }

    static IEnumerable<Renderer> AllRenderers(Scene scene) =>
        scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Renderer>(true));

    static string Describe(Renderer r)
    {
        string path = r.name;
        for (var p = r.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
        Bounds b = r.bounds;
        return FormattableString.Invariant($"{path} ({b.min.x:F2}, {b.min.y:F2}, {b.min.z:F2}) to ({b.max.x:F2}, {b.max.y:F2}, {b.max.z:F2})");
    }
}
