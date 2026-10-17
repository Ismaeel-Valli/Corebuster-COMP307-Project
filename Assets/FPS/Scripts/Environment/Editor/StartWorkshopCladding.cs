using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static StartWorkshopDresser;

/// <summary>
/// Rebuildable corebuster_env_008 pass: a base hazard trim, a lower plating band, a top ledge trim and a vertical
/// column rhythm on the four Start Workshop walls. It scans the existing corebuster_env_002/004/007 groups for
/// near-wall prop footprints and only fills the open stretches between them, so it never displaces, hides or
/// overlaps another pass's dressing. The group name and every child name deliberately avoid the substring "Wall":
/// <see cref="StartWorkshopFrame.Measure"/> finds the real walls by that token on the renderer's transform chain,
/// so a cladding object named with it would be mistaken for room geometry and shift the measured frame.
/// </summary>
public static class StartWorkshopCladding
{
    public const string GroupName = "Cladding_008";
    const string UndoName = "Build Start Workshop Cladding";

    const string Kit = "Assets/SciFi Warehouse Kit/Prefabs/";
    // Probed 2026-09-28 (unscaled MeshRenderer bounds): size (0.40, 9.00, 0.20) - already authored upright, so the
    // column only needs a uniform Y stretch to reach floor-to-ceiling height, no corrective pitch rotation.
    const string ColumnPrefab = Kit + "Structures/Pillars and Beams/Wall Support.prefab";
    const string PlateMaterialPath = "Assets/FPS/Art/Materials/Level/Wall_DarkGrey.mat";
    const string Art = "Assets/FPS/Art/";
    const string Textures = Art + "Textures/StartWorkshop/";
    const string Materials = Art + "Materials/Props/";

    // How close an existing prop must be to a wall line to count as occupying it, and the clearance kept around it.
    const float NearWall = 1.6f;
    const float OccupantMargin = .45f;
    const float CornerMargin = .55f;
    const float MinSegment = 1.5f;
    const float SegmentInset = .12f;
    const float ColumnSpacing = 4f;

    const float TrimBottom = 0f, TrimTop = .28f;
    const float BandBottom = 1.2f, BandTop = 2.5f;
    const float LedgeDrop = .55f, LedgeHeight = .18f;
    const float PlateDepth = .12f;

    [MenuItem("Tools/CoreBuster/Build Start Workshop Cladding")]
    public static void Rebuild()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var level = scene.path == ScenePath ? FindLevel(scene) : null;
        var root = level != null ? level.Find(ContainerName) : null;
        if (root == null)
        {
            Debug.LogError($"Open {ScenePath} and run Tools/CoreBuster/Dress Start Workshop first; cladding needs {LevelName}/{ContainerName}.");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        var old = root.Find(GroupName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var frame = StartWorkshopFrame.Measure(scene, root);
        int firstChild = root.childCount;
        try
        {
            Build(root, frame);
            ApplyStaticConvention(root.GetChild(firstChild), GameObjectUtility.GetStaticEditorFlags(level.gameObject));
        }
        catch
        {
            if (root.childCount > firstChild) UnityEngine.Object.DestroyImmediate(root.GetChild(firstChild).gameObject);
            throw;
        }
        Undo.RegisterCreatedObjectUndo(root.GetChild(firstChild).gameObject, UndoName);
        StartWorkshopLitMaterials.ApplyTo(scene, root);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeTransform = root.Find(GroupName);
        Debug.Log($"Start Workshop cladding rebuilt. {frame}");
        StartWorkshopChecks.Run();
    }

    /// <summary>Called by the full workshop rebuild after the overhead pass, and by the isolated menu action.</summary>
    public static void Build(Transform root, StartWorkshopFrame frame)
    {
        if (root.Find(GroupName) != null)
            throw new InvalidOperationException($"{GroupName} already exists. Remove it before rebuilding the cladding pass.");

        var group = Group(root, GroupName);
        var trimMaterial = HazardMaterial();
        var plateMaterial = PlateMaterial();

        foreach (Wall wall in Enum.GetValues(typeof(Wall)))
            BuildWall(group, root, frame, wall, trimMaterial, plateMaterial);
    }

    static void BuildWall(Transform group, Transform root, StartWorkshopFrame f, Wall wall, Material trim, Material plate)
    {
        bool alongZ = wall == Wall.West || wall == Wall.East;
        float runStart = alongZ ? f.South : f.West;
        float runEnd = alongZ ? f.North : f.East;

        var occupied = OccupiedIntervals(root, f, wall, alongZ, OccupantMargin);
        if (wall == Wall.North)
        {
            // No wall collider spans the doorway, and the keep-out below buffers 1 m past its measured edges.
            InsertOccupied(occupied, f.OpeningMinX - 1f - OccupantMargin, f.OpeningMaxX + 1f + OccupantMargin);
        }
        var free = FreeIntervals(occupied, runStart + CornerMargin, runEnd - CornerMargin);

        int index = 0;
        foreach (var (lo, hi) in free)
        {
            if (hi - lo < MinSegment) continue;
            index++;
            BuildSegment(group, root, f, wall, index, lo + SegmentInset, hi - SegmentInset, trim, plate);
        }
    }

    /// <summary>One wrapper per open wall stretch: hazard trim, plating band, ledge trim and a column rhythm. Kept as
    /// a single wrapper (like <see cref="StartWorkshopDresser"/>'s Workbench) so it counts as one dressing prop.</summary>
    static void BuildSegment(Transform group, Transform root, StartWorkshopFrame f, Wall wall, int index,
        float segStart, float segEnd, Material trim, Material plate)
    {
        var wrapper = new GameObject($"Cladding_{wall}_{index}");
        wrapper.transform.SetParent(group, false);

        Plate(wrapper.transform, root, "Hazard_Trim", f, wall, segStart, segEnd, TrimBottom, TrimTop, trim, tile: true);
        Plate(wrapper.transform, root, "Plating_Band", f, wall, segStart, segEnd, BandBottom, BandTop, plate, tile: false);
        float ceilingHeight = f.Ceiling - f.Floor;
        Plate(wrapper.transform, root, "Ledge_Trim", f, wall, segStart, segEnd,
            ceilingHeight - LedgeDrop - LedgeHeight, ceilingHeight - LedgeDrop, plate, tile: false);

        float width = segEnd - segStart;
        int columns = width < 1.9f ? 1 : Mathf.Clamp(Mathf.FloorToInt(width / ColumnSpacing) + 1, 2, 6);
        for (int i = 0; i < columns; i++)
        {
            float pos = columns == 1 ? (segStart + segEnd) * .5f : Mathf.Lerp(segStart + .3f, segEnd - .3f, i / (columns - 1f));
            BuildColumn(wrapper.transform, root, f, wall, $"Column_{i + 1}", pos);
        }
    }

    /// <summary>A thin box hugging the wall between <paramref name="along0"/> and <paramref name="along1"/>, mounted
    /// onto the real wall collider like the first-pass wall props.</summary>
    static GameObject Plate(Transform parent, Transform root, string name, StartWorkshopFrame f, Wall wall,
        float along0, float along1, float y0, float y1, Material material, bool tile)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.rotation = Quaternion.identity;
        bool alongZ = wall == Wall.West || wall == Wall.East;
        float along = along1 - along0;
        go.transform.localScale = alongZ
            ? new Vector3(PlateDepth, y1 - y0, along)
            : new Vector3(along, y1 - y0, PlateDepth);
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = tile ? HazardStripeMaterial(material, along) : material;
        Bottom(go, f.Floor + y0);
        Face(go, f, wall, .02f);
        Along(go, f, wall, (along0 + along1) * .5f, Anchor.Center);
        Mount(go, root, wall);
        return go;
    }

    static void BuildColumn(Transform parent, Transform root, StartWorkshopFrame f, Wall wall, string name, float alongPos)
    {
        var col = Prop(parent, ColumnPrefab, name, f, wall);
        var scale = col.transform.localScale;
        float natural = B(col).size.y;
        scale.y *= (f.Ceiling - f.Floor - .02f) / natural;
        col.transform.localScale = scale;
        OnFloor(col, f);
        Face(col, f, wall, .04f);
        Along(col, f, wall, alongPos, Anchor.Center);
        Mount(col, root, wall);
    }

    /// <summary>Explicit corebuster_env_008 acceptance rule; cladding is counted separately from the density budgets.</summary>
    public static void Validate(Transform root, StartWorkshopFrame frame, List<string> errors)
    {
        var group = root.Find(GroupName);
        if (group == null)
        {
            errors.Add($"missing {GroupName}; run Tools/CoreBuster/Build Start Workshop Cladding.");
            return;
        }
        if (group.childCount == 0)
            errors.Add($"{GroupName} has no segments; every wall's dressing left no open stretch to clad, which should not happen.");
        foreach (Transform segment in group)
        {
            bool hasTrim = segment.Find("Hazard_Trim") != null;
            bool hasBand = segment.Find("Plating_Band") != null;
            bool hasLedge = segment.Find("Ledge_Trim") != null;
            if (!hasTrim || !hasBand || !hasLedge)
                errors.Add($"{GroupName}/{segment.name} is missing a hazard trim, plating band or ledge trim.");
        }
    }

    public static bool IsGroup(Transform group) => group != null && group.name == GroupName;

    static Material HazardMaterial()
    {
        string texturePath = Textures + "Hazard_Trim.png";
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null) throw new InvalidOperationException("Missing project-owned texture: " + texturePath);
        string materialPath = Materials + "M_Hazard_Trim.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is unavailable.");
            material = new Material(shader) { name = "M_Hazard_Trim" };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", .3f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static readonly Dictionary<float, Material> hazardStripeBands = new();

    /// <summary>
    /// One asset-backed material per stripe-repeat band, keyed by the segment length rounded to the nearest half
    /// repeat. A MaterialPropertyBlock is a runtime-only override (Unity does not serialize it with the renderer),
    /// so it read back at the material's 1x1 tiling after the scene was saved and reopened (`BUGS.md`). Banding the
    /// repeat keeps the asset count small while still varying the stripe count with each segment's length.
    /// </summary>
    static Material HazardStripeMaterial(Material baseMaterial, float along)
    {
        float band = Mathf.Round(Mathf.Max(1f, along * .5f) * 2f) / 2f;
        if (hazardStripeBands.TryGetValue(band, out var cached) && cached != null) return cached;

        string suffix = band.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', '_');
        string path = Materials + "M_Hazard_Trim_x" + suffix + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(baseMaterial.shader) { name = "M_Hazard_Trim_x" + suffix };
            AssetDatabase.CreateAsset(material, path);
        }
        material.CopyPropertiesFromMaterial(baseMaterial);
        material.shaderKeywords = baseMaterial.shaderKeywords;
        material.SetTextureScale("_BaseMap", new Vector2(band, 1));
        EditorUtility.SetDirty(material);

        hazardStripeBands[band] = material;
        return material;
    }

    static Material PlateMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(PlateMaterialPath);
        if (material == null) throw new InvalidOperationException("Missing plate material: " + PlateMaterialPath);
        return material;
    }

    /// <summary>Near-wall prop footprints (from every existing group) projected onto the wall's run axis and merged.</summary>
    static List<(float min, float max)> OccupiedIntervals(Transform root, StartWorkshopFrame f, Wall wall, bool alongZ, float margin)
    {
        var raw = new List<(float min, float max)>();
        foreach (Transform grp in root)
        {
            // The corebuster_env_009 pipework is built after this pass and stands clear of it; ignoring it keeps the
            // cladding layout, and so the pipework's placement, stable whichever of the two is rebuilt.
            // The corebuster_render_007 liners cover whole wall panels and are part of the wall surface, not dressing.
            // The corebuster_render_006 lamps are the room's last layer and fit themselves around this pass.
            // The corebuster_art_003 signage is placed after this pass, in wall space it leaves free.
            if (grp.name == StartWorkshopGraphics.GroupName || grp.name == GroupName || StartWorkshopPipework.IsGroup(grp) ||
                StartWorkshopLiners.IsGroup(grp) || StartWorkshopLighting.IsGroup(grp) || StartWorkshopSignage.IsGroup(grp) ||
                StartWorkshopFloor.IsGroup(grp)) continue;
            foreach (Transform prop in grp)
            {
                Bounds b;
                try { b = B(prop.gameObject); } catch { continue; }
                float dist = wall switch
                {
                    Wall.West => b.min.x - f.West,
                    Wall.East => f.East - b.max.x,
                    Wall.South => b.min.z - f.South,
                    _ => f.North - b.max.z,
                };
                if (dist > NearWall || dist < -NearWall) continue;
                float lo = alongZ ? b.min.z : b.min.x, hi = alongZ ? b.max.z : b.max.x;
                InsertOccupied(raw, lo - margin, hi + margin);
            }
        }
        return raw;
    }

    static void InsertOccupied(List<(float min, float max)> occupied, float lo, float hi)
    {
        occupied.Add((lo, hi));
        occupied.Sort((a, b) => a.min.CompareTo(b.min));
        for (int i = occupied.Count - 1; i > 0; i--)
        {
            if (occupied[i].min > occupied[i - 1].max) continue;
            occupied[i - 1] = (occupied[i - 1].min, Mathf.Max(occupied[i - 1].max, occupied[i].max));
            occupied.RemoveAt(i);
        }
    }

    static List<(float min, float max)> FreeIntervals(List<(float min, float max)> occupied, float start, float end)
    {
        var free = new List<(float min, float max)>();
        float cursor = start;
        foreach (var (lo, hi) in occupied)
        {
            if (cursor >= end) break;
            if (lo > cursor) free.Add((cursor, Mathf.Min(lo, end)));
            cursor = Mathf.Max(cursor, hi);
        }
        if (cursor < end) free.Add((cursor, end));
        return free;
    }
}
