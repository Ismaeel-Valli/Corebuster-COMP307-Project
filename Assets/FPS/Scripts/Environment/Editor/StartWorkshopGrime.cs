using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using static StartWorkshopDresser;

/// <summary>
/// Rebuildable corebuster_render_002 pass: surface grime for the Start Workshop as URP decal projectors, in
/// <c>Grime_002</c>. Six kinds, each drawn from Tools/Art/generate_start_workshop_grime.py:
/// <list type="bullet">
/// <item><b>Rust streaks</b> hang down the walls from below the pipe runs.</item>
/// <item><b>Leak stains</b> spread on the floor round each pipe drop and tank base, and as damp patches on the walls.</item>
/// <item><b>Bolt plates</b> are riveted repair plates on the floor under each drop and tank, and on the walls.</item>
/// <item><b>Oil puddles</b> lie on free floor beside the centre islands and the workstation.</item>
/// <item><b>Scuffs</b> follow the spawn-to-exit route and ring the spawn.</item>
/// <item><b>Edge grime</b> collects in patches along the foot of every wall.</item>
/// </list>
/// Nothing here has a renderer or a collider. A decal paints only renderers that carry <see cref="LayerIndex"/> in their
/// rendering-layer mask, and only the room's own surfaces do: the shell pieces the lit materials use, the boundary liners
/// and the wall cladding. Characters, weapons, props and everything outside the room stay on the default layer, so they are
/// never painted, and every projector's box lies inside the room. The feature that draws them is
/// <see cref="UrpDecalFeatureSetup"/>. Positions come from the measured room, the pipework, the centre islands and the
/// workstation, with a fixed seed, so a rebuild of an unchanged room is identical.
/// </summary>
public static class StartWorkshopGrime
{
    public const string GroupName = "Grime_002";
    /// <summary>The rendering layer ("Light Layer 7" in URP's global settings; light layers are off in this project) that receives grime.</summary>
    public const int LayerIndex = 7;
    public static uint LayerBit => 1u << LayerIndex;
    /// <summary>Frame-time budget: DBuffer decals cost per projector, so the count is capped.</summary>
    public const int MaxDecals = 120;

    const string UndoName = "Build Start Workshop Grime";
    const string Textures = "Assets/FPS/Art/Textures/StartWorkshop/Grime/";
    public const string MaterialFolder = "Assets/FPS/Art/Materials/Grime/";
    const int Seed = 3020002;
    const float DrawDistance = 70f;

    // Projector depth. A wall decal's box reaches WallBack behind the measured wall face (the panels stand up to 0.2 m behind
    // the pilasters that define the face, and the walls are 0.33 m thick) and WallFront in front of it, so it paints the panel,
    // the pilasters and thin cladding but not the props standing out in the room. A floor decal reaches only a hand's breadth
    // above the tile top, so it paints the floor and not the props standing on it.
    const float WallBack = .26f, WallFront = .09f, WallDepth = WallBack + WallFront;
    const float FloorBelow = .055f, FloorAbove = .105f, FloorDepth = FloorBelow + FloorAbove;
    // A box may cross the measured wall face by this much (validator), always less than the 0.33 m wall thickness.
    const float WallTolerance = .3f;
    // The builder keeps floor decals this far inside the wall line, stricter than the validator.
    const float FloorInset = .1f;

    enum Kind { RustStreak, LeakStain, Scuff, OilPuddle, EdgeGrime, BoltPlate }

    sealed class Spec
    {
        public string Prefix, Texture, Normal;
        public float NormalBlend;
    }

    static readonly Dictionary<Kind, Spec> Specs = new Dictionary<Kind, Spec>
    {
        { Kind.RustStreak, new Spec { Prefix = "Rust_Streak", Texture = "Grime_Rust_Streak" } },
        { Kind.LeakStain, new Spec { Prefix = "Leak_Stain", Texture = "Grime_Leak_Stain" } },
        { Kind.Scuff, new Spec { Prefix = "Scuff", Texture = "Grime_Scuff", Normal = "Grime_Scuff_N", NormalBlend = .6f } },
        { Kind.OilPuddle, new Spec { Prefix = "Oil_Puddle", Texture = "Grime_Oil_Puddle" } },
        { Kind.EdgeGrime, new Spec { Prefix = "Edge_Grime", Texture = "Grime_Edge_Strip" } },
        { Kind.BoltPlate, new Spec { Prefix = "Bolt_Plate", Texture = "Grime_Bolt_Plate", Normal = "Grime_Bolt_Plate_N", NormalBlend = 1f } },
    };

    struct Decal
    {
        public Kind Kind;
        public string Name;
        public Vector3 Centre;
        public Quaternion Rotation;
        public Vector3 Size;
        public Vector2 UvScale, UvBias;
    }

    static readonly Wall[] Walls = { Wall.West, Wall.East, Wall.South, Wall.North };

    [MenuItem("Tools/CoreBuster/Build Start Workshop Grime")]
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

    /// <summary>Batch: -executeMethod StartWorkshopGrime.BuildBatch. Rebuilds, saves MainScene and runs the validator.</summary>
    public static void BuildBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var root = FindRoom(scene);
        if (root == null) { EditorApplication.Exit(1); return; }
        RebuildIn(scene, root);
        EditorSceneManager.SaveScene(scene);
        if (!StartWorkshopChecks.Run()) EditorApplication.Exit(1);
    }

    internal static Transform FindRoom(Scene scene)
    {
        var level = scene.path == ScenePath ? FindLevel(scene) : null;
        var root = level != null ? level.Find(ContainerName) : null;
        if (root == null) Debug.LogError($"Open {ScenePath} and run Tools/CoreBuster/Dress Start Workshop first.");
        return root;
    }

    static void RebuildIn(Scene scene, Transform root)
    {
        var existing = root.Find(GroupName);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
        var f = StartWorkshopFrame.Measure(scene, root);
        Build(root, f);
        ApplyStaticConvention(root.Find(GroupName), GameObjectUtility.GetStaticEditorFlags(root.parent.gameObject));
        ApplyReceivers(scene, root, f);
    }

    public static bool IsGroup(Transform group) => group != null && group.name == GroupName;

    /// <summary>The kind prefix of a projector's name ("Scuff_03" is "Scuff", "Rust_Streak_West_01" is "Rust_Streak").</summary>
    internal static string KindOf(string decalName) =>
        Specs.Values.Select(spec => spec.Prefix).FirstOrDefault(prefix => decalName.StartsWith(prefix + "_", StringComparison.Ordinal)) ?? decalName;

    // --- Building ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds <see cref="GroupName"/>. Needs the pipework, the workstation and the centre islands, which it anchors to, and
    /// fails with the missing anchor's name if one is absent. Does not touch rendering layers; <see cref="ApplyReceivers"/> does.
    /// </summary>
    public static void Build(Transform root, StartWorkshopFrame f)
    {
        if (root.Find(GroupName) != null) throw new InvalidOperationException(GroupName + " already exists.");
        var plan = Plan(root, f);
        if (plan.Count > MaxDecals) throw new InvalidOperationException($"{plan.Count} decals planned; the budget is {MaxDecals}.");

        var group = Group(root, GroupName);
        Undo.RegisterCreatedObjectUndo(group.gameObject, UndoName);
        try
        {
            var materials = Specs.Keys.ToDictionary(k => k, DecalMaterial);
            foreach (var d in plan) Create(group, d, materials[d.Kind]);
            AssetDatabase.SaveAssets();
        }
        catch
        {
            UnityEngine.Object.DestroyImmediate(group.gameObject);
            throw;
        }
        var counts = new StringBuilder();
        foreach (var kind in Specs.Keys) counts.Append($"{plan.Count(d => d.Kind == kind)} {Specs[kind].Prefix.Replace('_', ' ').ToLowerInvariant()}, ");
        Debug.Log($"Start Workshop grime: {plan.Count} decal projectors in {GroupName} ({counts.ToString().TrimEnd(',', ' ')}); budget {MaxDecals}. " +
                  (UrpDecalFeatureSetup.Find(UrpDecalFeatureSetup.LoadRenderer()) == null
                      ? "WARNING: the URP decal feature is not on the forward renderer; run Tools/CoreBuster/Enable URP Decal Renderer Feature."
                      : "The URP decal feature is present."));
    }

    static void Create(Transform group, Decal d, Material material)
    {
        var go = new GameObject(d.Name);
        go.transform.SetParent(group, false);
        go.transform.SetPositionAndRotation(d.Centre, d.Rotation);
        var projector = go.AddComponent<DecalProjector>();
        projector.material = material;
        projector.scaleMode = DecalScaleMode.ScaleInvariant;
        projector.size = d.Size;
        // The box is centred on the transform, so the placement maths and the validator can treat position as the centre.
        projector.pivot = Vector3.zero;
        projector.drawDistance = DrawDistance;
        projector.fadeScale = .9f;
        // No angle fade: the URP Decal shader graph does not enable it. The boxes are thin instead.
        projector.startAngleFade = 180f;
        projector.endAngleFade = 180f;
        projector.uvScale = d.UvScale;
        projector.uvBias = d.UvBias;
        projector.renderingLayerMask = LayerBit;
    }

    // --- Layout --------------------------------------------------------------------------------------------------------

    sealed class Planner
    {
        public StartWorkshopFrame F;
        public System.Random Rng;
        public List<Decal> Decals = new List<Decal>();
        public List<(string name, Bounds b)> Props;
        readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

        public float R(float a, float b) => a + (float)Rng.NextDouble() * (b - a);
        public string NextName(Kind kind, string suffix = null)
        {
            string key = Specs[kind].Prefix + suffix;
            _counts.TryGetValue(key, out int n);
            _counts[key] = ++n;
            return $"{Specs[kind].Prefix}_{(suffix != null ? suffix + "_" : "")}{n:00}";
        }
    }

    static List<Decal> Plan(Transform root, StartWorkshopFrame f)
    {
        var pipework = root.Find(StartWorkshopPipework.GroupName) ?? throw new InvalidOperationException(
            $"Missing {StartWorkshopPipework.GroupName}; run Tools/CoreBuster/Build Start Workshop Pipework first.");
        var workstation = root.Find(StartWorkshopWorkstation.GroupName) ?? throw new InvalidOperationException(
            $"Missing {StartWorkshopWorkstation.GroupName}; run Tools/CoreBuster/Build Start Workshop Workstation first.");
        var islands = root.Cast<Transform>().Where(StartWorkshopCentre.IsGroup).ToList();
        if (islands.Count == 0)
            throw new InvalidOperationException($"No centre islands (groups ending {StartWorkshopCentre.GroupSuffix}); run Tools/CoreBuster/Build Start Workshop Centre first.");

        var p = new Planner { F = f, Rng = new System.Random(Seed), Props = StartWorkshopCentre.FloorProps(root, f, null) };

        PlanRustStreaks(p, pipework);
        PlanWallLeaks(p, pipework);
        PlanDropBases(p, pipework);
        PlanWallPlates(p);
        PlanPuddles(p, islands, workstation);
        PlanScuffs(p);
        PlanEdgeGrime(p);
        return p.Decals;
    }

    // Wall geometry.

    static Vector3 Inward(Wall wall) => wall switch
    {
        Wall.West => Vector3.right, Wall.East => Vector3.left, Wall.South => Vector3.forward, _ => Vector3.back,
    };

    static (float min, float max) AlongRange(StartWorkshopFrame f, Wall wall) =>
        wall == Wall.West || wall == Wall.East ? (f.South, f.North) : (f.West, f.East);

    static Vector3 WallPoint(StartWorkshopFrame f, Wall wall, float along, float y) => wall switch
    {
        Wall.West => new Vector3(f.West, y, along), Wall.East => new Vector3(f.East, y, along),
        Wall.South => new Vector3(along, y, f.South), _ => new Vector3(along, y, f.North),
    };

    /// <summary>The doorway keeps a clear stretch of its wall: the exit portal, its liner and the P-3 tanks stand there.</summary>
    static bool InDoorway(StartWorkshopFrame f, Wall wall, float from, float to) =>
        wall == Wall.North && to > f.OpeningMinX - .8f && from < f.OpeningMaxX + .8f;

    /// <summary>A decal on a wall, projected into it, reading upright (v up), rolled about the projection axis.</summary>
    static Decal WallDecal(Planner p, Kind kind, Wall wall, float along, float centreY, float width, float height, float roll = 0)
    {
        Vector3 n = Inward(wall);
        return new Decal
        {
            Kind = kind, Name = p.NextName(kind, wall.ToString()),
            Centre = WallPoint(p.F, wall, along, centreY) - n * (WallBack - WallDepth * .5f),
            Rotation = Quaternion.LookRotation(-n, Vector3.up) * Quaternion.AngleAxis(roll, Vector3.forward),
            Size = new Vector3(width, height, WallDepth), UvScale = Vector2.one,
        };
    }

    /// <summary>
    /// A decal on the floor, projected down. <paramref name="up"/> is the horizontal direction the picture's top points; the
    /// centre is pulled in so the whole rotated rectangle stays inside the room.
    /// </summary>
    static Decal FloorDecal(Planner p, Kind kind, Vector2 centre, Vector3 up, float width, float height)
    {
        up = new Vector3(up.x, 0, up.z).normalized;
        float hx = Mathf.Abs(up.x) * height * .5f + Mathf.Abs(up.z) * width * .5f;
        float hz = Mathf.Abs(up.z) * height * .5f + Mathf.Abs(up.x) * width * .5f;
        var f = p.F;
        centre.x = Mathf.Clamp(centre.x, f.West + hx + FloorInset, f.East - hx - FloorInset);
        centre.y = Mathf.Clamp(centre.y, f.South + hz + FloorInset, f.North - hz - FloorInset);
        return new Decal
        {
            Kind = kind, Name = p.NextName(kind),
            Centre = new Vector3(centre.x, f.Floor + (FloorAbove - FloorBelow) * .5f, centre.y),
            Rotation = Quaternion.LookRotation(Vector3.down, up),
            Size = new Vector3(width, height, FloorDepth), UvScale = Vector2.one,
        };
    }

    static Vector3 Yaw(float degrees) => new Vector3(Mathf.Sin(degrees * Mathf.Deg2Rad), 0, Mathf.Cos(degrees * Mathf.Deg2Rad));

    static Vector2 Xz(Vector3 v) => new Vector2(v.x, v.z);

    // Rust streaks: hang from below each pipe run.

    static void PlanRustStreaks(Planner p, Transform pipework)
    {
        var f = p.F;
        foreach (var wall in Walls)
        {
            var run = pipework.Find("Run_" + wall);
            if (run == null) continue;
            float top = B(run.gameObject).min.y - .2f;
            var (lo, hi) = AlongRange(f, wall);
            for (float cursor = lo + p.R(1f, 2.6f); cursor < hi - 1f; cursor += p.R(4.2f, 6.8f))
            {
                float width = p.R(.85f, 1.4f), height = Mathf.Min(p.R(2.2f, 3.9f), top - (f.Floor + 1f));
                if (height < 1.2f || InDoorway(f, wall, cursor - width * .5f, cursor + width * .5f)) continue;
                p.Decals.Add(WallDecal(p, Kind.RustStreak, wall, cursor, top - height * .5f, width, height));
            }
        }
    }

    // Leak stains and bolt plates: damp patches on the walls under the runs.

    static void PlanWallLeaks(Planner p, Transform pipework)
    {
        var f = p.F;
        foreach (var wall in Walls)
        {
            var run = pipework.Find("Run_" + wall);
            if (run == null) continue;
            float top = B(run.gameObject).min.y;
            var (lo, hi) = AlongRange(f, wall);
            for (int i = 0; i < 2; i++)
            {
                float along = Mathf.Lerp(lo + 4f, hi - 4f, p.R(.08f, .92f)), size = p.R(1.3f, 2f);
                if (InDoorway(f, wall, along - size * .5f, along + size * .5f)) continue;
                p.Decals.Add(WallDecal(p, Kind.LeakStain, wall, along, top - p.R(1.6f, 2.6f), size, size, p.R(-180f, 180f)));
            }
        }
    }

    static void PlanWallPlates(Planner p)
    {
        var f = p.F;
        foreach (var wall in Walls)
        {
            var (lo, hi) = AlongRange(f, wall);
            for (int i = 0; i < 2; i++)
            {
                float along = Mathf.Lerp(lo + 3f, hi - 3f, p.R(.05f, .95f)), size = p.R(.9f, 1.25f);
                if (InDoorway(f, wall, along - size * .5f, along + size * .5f)) continue;
                p.Decals.Add(WallDecal(p, Kind.BoltPlate, wall, along, f.Floor + p.R(1.5f, 2.9f), size, size, 90f * p.Rng.Next(4)));
            }
        }
    }

    // Floor stain and repair plate under each pipe drop and tank.

    static void PlanDropBases(Planner p, Transform pipework)
    {
        foreach (Transform child in pipework)
        {
            bool tank = child.name.StartsWith("Tank_P3_", StringComparison.Ordinal);
            if (!tank && !child.name.StartsWith("Drop_", StringComparison.Ordinal)) continue;
            var (centre, radius) = BaseCentre(child, p.F);
            float stain = Mathf.Max(radius * 2f + p.R(.9f, 1.5f), 1.7f);
            var offset = new Vector2(p.R(-.25f, .25f), p.R(-.25f, .25f));
            p.Decals.Add(FloorDecal(p, Kind.LeakStain, centre + offset, Yaw(p.R(0f, 360f)), stain, stain));
            float plate = tank ? radius * 2f + .5f : Mathf.Max(radius * 2f + .45f, .95f);
            p.Decals.Add(FloorDecal(p, Kind.BoltPlate, centre, Yaw(90f * p.Rng.Next(4)), plate, plate));
        }
    }

    /// <summary>The XZ centre and radius of a prop's parts that touch the floor (a drop's arms and a tank's dome stand higher).</summary>
    static (Vector2 centre, float radius) BaseCentre(Transform t, StartWorkshopFrame f)
    {
        Bounds b = default;
        bool any = false;
        foreach (var r in t.GetComponentsInChildren<MeshRenderer>())
        {
            if (!r.enabled || r.bounds.min.y > f.Floor + .6f) continue;
            if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
        }
        if (!any) b = B(t.gameObject);
        return (new Vector2(b.center.x, b.center.z), Mathf.Max(b.extents.x, b.extents.z));
    }

    // Oil puddles beside the work zones.

    static void PlanPuddles(Planner p, List<Transform> islands, Transform workstation)
    {
        var anchors = new List<(Bounds b, int count)>();
        foreach (var island in islands)
        {
            var plate = island.Find(StartWorkshopCentre.PlateName) ?? throw new InvalidOperationException(
                $"{island.name} has no {StartWorkshopCentre.PlateName}; run Tools/CoreBuster/Build Start Workshop Centre.");
            anchors.Add((B(plate.gameObject), 2));
        }
        foreach (var name in new[] { "Tool_Chest", "Robot_Arm_Cell" })
        {
            var t = workstation.Find(name) ?? throw new InvalidOperationException(
                $"{StartWorkshopWorkstation.GroupName} has no {name}; run Tools/CoreBuster/Build Start Workshop Workstation.");
            anchors.Add((B(t.gameObject), 1));
        }
        foreach (var (b, count) in anchors)
            for (int i = 0; i < count; i++)
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    float size = p.R(1f, 1.7f);
                    Vector3 dir = Yaw(p.R(0f, 360f));
                    Vector3 at = b.center + dir * (Mathf.Max(b.extents.x, b.extents.z) + p.R(.7f, 1.7f));
                    if (!FreeFloor(p, new Vector2(at.x, at.z), size * .55f + .1f)) continue;
                    p.Decals.Add(FloorDecal(p, Kind.OilPuddle, new Vector2(at.x, at.z), Yaw(p.R(0f, 360f)), size, size));
                    break;
                }
    }

    /// <summary>True if no body-height prop's footprint comes within <paramref name="radius"/> of the point and it is well inside the room.</summary>
    static bool FreeFloor(Planner p, Vector2 at, float radius)
    {
        var f = p.F;
        const float margin = 1f;
        if (at.x < f.West + margin + radius || at.x > f.East - margin - radius || at.y < f.South + margin + radius || at.y > f.North - margin - radius)
            return false;
        var point = new Vector3(at.x, f.Floor, at.y);
        foreach (var (_, b) in p.Props)
            if (StartWorkshopFrame.FlatDistance(point, b) < radius) return false;
        return true;
    }

    // Scuffs along the route and around the spawn.

    static void PlanScuffs(Planner p)
    {
        var f = p.F;
        Vector3 start = f.Spawn, end = f.RouteEnd;
        Vector3 route = new Vector3(end.x - start.x, 0, end.z - start.z).normalized;
        const int alongRoute = 9;
        for (int i = 0; i < alongRoute; i++)
        {
            float t = Mathf.Lerp(.06f, .94f, (i + p.R(.1f, .9f)) / alongRoute);
            Vector3 centre = Vector3.Lerp(start, end, t);
            Vector3 side = new Vector3(-route.z, 0, route.x);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector3 at = centre + side * p.R(-1.2f, 1.2f);
                var along = Quaternion.Euler(0, p.R(-20f, 20f), 0) * route;
                if (!FreeFloor(p, Xz(at), .7f)) continue;
                // The picture's U runs along the drag, so its V axis is the drag's perpendicular.
                p.Decals.Add(FloorDecal(p, Kind.Scuff, Xz(at), new Vector3(-along.z, 0, along.x), p.R(2.2f, 3.2f), p.R(1.1f, 1.6f)));
                break;
            }
        }
        for (int i = 0; i < 4; i++)
        {
            Vector3 dir = Yaw(p.R(0f, 360f));
            Vector3 at = start + dir * p.R(1.3f, 3.2f);
            if (!FreeFloor(p, Xz(at), .7f)) continue;
            p.Decals.Add(FloorDecal(p, Kind.Scuff, Xz(at), Yaw(p.R(0f, 360f)), p.R(2f, 2.8f), p.R(1f, 1.5f)));
        }
    }

    // Edge grime: patches along the foot of each wall.

    static void PlanEdgeGrime(Planner p)
    {
        var f = p.F;
        foreach (var wall in Walls)
        {
            var (lo, hi) = AlongRange(f, wall);
            Vector3 inward = Inward(wall);
            for (float cursor = lo + p.R(.3f, 1.2f); cursor < hi - 1.5f; )
            {
                float length = Mathf.Min(p.R(3.6f, 6f), hi - .3f - cursor), width = p.R(.6f, .95f);
                if (!InDoorway(f, wall, cursor - .5f, cursor + length + .5f))
                {
                    Vector3 face = WallPoint(f, wall, cursor + length * .5f, f.Floor);
                    Vector3 centre = face + inward * (width * .5f - .03f);
                    bool flip = p.Rng.Next(2) == 0;
                    p.Decals.Add(new Decal
                    {
                        Kind = Kind.EdgeGrime, Name = p.NextName(Kind.EdgeGrime, wall.ToString()),
                        Centre = new Vector3(centre.x, f.Floor + (FloorAbove - FloorBelow) * .5f, centre.z),
                        // Picture up (v) points away from the wall; the strip's bottom rows, its heaviest grime, lie against it.
                        Rotation = Quaternion.LookRotation(Vector3.down, inward),
                        Size = new Vector3(length, width, FloorDepth),
                        UvScale = flip ? new Vector2(-1, 1) : Vector2.one, UvBias = flip ? new Vector2(1, 0) : Vector2.zero,
                    });
                }
                cursor += length + p.R(.2f, 1.2f);
            }
        }
    }

    // --- Receivers -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The room's own surfaces, the only renderers a decal may paint: the shell pieces the lit materials cover (floor, wall and
    /// ceiling tiles) plus the boundary liners and the wall cladding. The liners' shadows-only casters are not surfaces.
    /// </summary>
    public static List<Renderer> Receivers(Scene scene, Transform root, StartWorkshopFrame f)
    {
        var receivers = new List<Renderer>(StartWorkshopLitMaterials.ShellRenderers(scene, root, f));
        foreach (Transform group in root)
            if (StartWorkshopLiners.IsGroup(group) || StartWorkshopCladding.IsGroup(group))
                foreach (var r in group.GetComponentsInChildren<MeshRenderer>(true))
                    if (r.enabled && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) receivers.Add(r);
        return receivers.Distinct().ToList();
    }

    /// <summary>
    /// Puts <see cref="LayerBit"/> on every receiver and takes it off every renderer that a decal could otherwise paint: any
    /// other renderer near the room, and every actor's renderers (enemies and the player), wherever they are. Many kit and model
    /// renderers ship with a mask of "everything", which includes the grime layer, so leaving them alone would let decals paint
    /// them. Renderers far outside the room are left as they are, because every projector's box is inside the room. Runs inside
    /// the caller's undo group and returns how many renderers changed. Rerunning changes nothing.
    /// </summary>
    public static int ApplyReceivers(Scene scene, Transform root, StartWorkshopFrame f)
    {
        var receivers = new HashSet<Renderer>(Receivers(scene, root, f));
        var region = RoomRegion(f);
        int set = 0, cleared = 0;
        foreach (var r in AllRenderers(scene))
        {
            bool has = (r.renderingLayerMask & LayerBit) != 0, want = receivers.Contains(r);
            if (has == want || (has && !want && !MustLackLayer(r, region))) continue;
            Undo.RecordObject(r, UndoName);
            r.renderingLayerMask = want ? r.renderingLayerMask | LayerBit : r.renderingLayerMask & ~LayerBit;
            if (PrefabUtility.IsPartOfPrefabInstance(r)) PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            if (want) set++; else cleared++;
        }
        Debug.Log($"Start Workshop grime receivers: {receivers.Count} room renderers carry rendering layer {LayerIndex}; layer set on {set}, cleared from {cleared} other renderer(s) near the room or on actors.");
        return set + cleared;
    }

    /// <summary>The room's box plus a margin: every renderer inside it can be reached by a projector, whose boxes all lie in the room.</summary>
    static Bounds RoomRegion(StartWorkshopFrame f)
    {
        var region = new Bounds();
        region.SetMinMax(new Vector3(f.West - .5f, f.Floor - .5f, f.South - .5f), new Vector3(f.East + .5f, f.Ceiling + .5f, f.North + .5f));
        return region;
    }

    /// <summary>A non-receiver that a decal could paint: it is an actor, or it lies within the room's region.</summary>
    static bool MustLackLayer(Renderer r, Bounds region) =>
        r.GetComponentInParent<Health>(true) != null || r.GetComponentInParent<PlayerCharacterController>(true) != null ||
        region.Intersects(r.bounds);

    static IEnumerable<Renderer> AllRenderers(Scene scene) =>
        scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Renderer>(true));

    // --- Materials -----------------------------------------------------------------------------------------------------

    static Shader DecalShader()
    {
        // Use the shader URP's own decal material is built on, so a package upgrade that renames it is followed.
        var template = AssetDatabase.LoadAssetAtPath<Material>(UrpDecalFeatureSetup.DecalTemplateMaterial);
        var shader = template != null ? template.shader : Shader.Find("Shader Graphs/Decal");
        if (shader == null || shader.name == "Hidden/InternalErrorShader")
            throw new InvalidOperationException("URP's Decal shader is unavailable (Shader Graphs/Decal).");
        return shader;
    }

    static Material DecalMaterial(Kind kind)
    {
        var spec = Specs[kind];
        var shader = DecalShader();
        string path = MaterialFolder + "M_Grime_" + spec.Prefix.Replace("_", "") + ".mat";
        EnsureFolder(MaterialFolder.TrimEnd('/'));
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        foreach (var property in new[] { "Base_Map", "Normal_Map", "Normal_Blend" })
            if (!material.HasProperty(property))
                throw new InvalidOperationException($"The URP Decal shader has no '{property}' property; update StartWorkshopGrime for this URP version.");
        material.SetTexture("Base_Map", LoadTexture(spec.Texture, false));
        material.SetTexture("Normal_Map", spec.Normal != null ? LoadTexture(spec.Normal, true) : null);
        material.SetFloat("Normal_Blend", spec.NormalBlend);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Texture2D LoadTexture(string name, bool normal)
    {
        string path = Textures + name + ".png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter ?? throw new InvalidOperationException(
            $"Missing project-owned texture {path}; run python Tools/Art/generate_start_workshop_grime.py, then refresh the Asset Database.");
        var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (importer.textureType != type || importer.sRGBTexture == normal || importer.alphaIsTransparency ||
            importer.wrapMode != TextureWrapMode.Clamp || importer.filterMode != FilterMode.Trilinear || importer.anisoLevel != 4 ||
            !importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.CompressedHQ)
        {
            importer.textureType = type;
            importer.sRGBTexture = !normal;
            // The alpha is the decal's coverage, data rather than transparency, so the importer must not dilate colour into it.
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        int slash = folder.LastIndexOf('/');
        EnsureFolder(folder.Substring(0, slash));
        AssetDatabase.CreateFolder(folder.Substring(0, slash), folder.Substring(slash + 1));
    }

    // --- Validation ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The grime rules:
    /// <list type="bullet">
    /// <item>the URP decal feature is installed and configured;</item>
    /// <item>the group holds 1 to <see cref="MaxDecals"/> projectors and nothing else: no renderer, collider or other component;</item>
    /// <item>each projector uses a project-owned URP decal material, paints only <see cref="LayerIndex"/> and lies inside the room;</item>
    /// <item>each projector reaches at least one receiver;</item>
    /// <item>exactly the receivers carry the layer, so no character, weapon, prop or outside renderer can be painted.</item>
    /// </list>
    /// </summary>
    public static void Validate(Transform root, StartWorkshopFrame f, List<string> errors)
    {
        UrpDecalFeatureSetup.Validate(errors);
        var group = root.Find(GroupName);
        if (group == null)
        {
            errors.Add(GroupName + " is missing; run Tools/CoreBuster/Build Start Workshop Grime.");
            return;
        }
        var scene = root.gameObject.scene;
        var receivers = Receivers(scene, root, f);
        var receiverSet = new HashSet<Renderer>(receivers);

        if (group.GetComponentsInChildren<Renderer>(true).Length > 0) errors.Add(GroupName + " must have no renderers.");
        if (group.GetComponentsInChildren<Collider>(true).Length > 0) errors.Add(GroupName + " must have no colliders.");
        int count = group.childCount;
        if (count == 0 || count > MaxDecals) errors.Add($"{GroupName} holds {count} decals; the budget is 1-{MaxDecals}.");

        foreach (Transform child in group)
        {
            string name = $"{GroupName}/{child.name}";
            var projector = child.GetComponent<DecalProjector>();
            if (projector == null) { errors.Add($"{name} has no DecalProjector."); continue; }
            if (child.GetComponents<Component>().Length != 2 || child.childCount != 0)
                errors.Add($"{name} must be a bare projector (no other component or child).");
            var material = projector.material;
            if (material == null || material.shader == null || material.shader.name.IndexOf("Decal", StringComparison.Ordinal) < 0 ||
                !AssetDatabase.GetAssetPath(material).StartsWith(MaterialFolder, StringComparison.Ordinal))
                errors.Add($"{name} must use a project-owned URP decal material under {MaterialFolder}.");
            if (projector.renderingLayerMask.value != LayerBit)
                errors.Add($"{name} must paint only rendering layer {LayerIndex} (mask {LayerBit}); it has {projector.renderingLayerMask.value}.");
            if (projector.pivot != Vector3.zero) errors.Add($"{name} must be centred on its transform (pivot zero).");

            Bounds box = BoxBounds(projector);
            if (box.min.x < f.West - WallTolerance || box.max.x > f.East + WallTolerance || box.min.z < f.South - WallTolerance ||
                box.max.z > f.North + WallTolerance || box.min.y < f.Floor - FloorBelow - .01f || box.max.y > f.Ceiling)
                errors.Add($"{name} reaches outside the room ({box.min:F2} to {box.max:F2}).");
            if (!receivers.Any(r => r.bounds.Intersects(box))) errors.Add($"{name} touches no receiving surface, so it paints nothing.");
        }

        int missing = 0;
        foreach (var r in receivers)
            if ((r.renderingLayerMask & LayerBit) == 0) missing++;
        if (missing > 0)
            errors.Add($"{missing} room surface renderer(s) lack rendering layer {LayerIndex}, so decals skip them; run Tools/CoreBuster/Build Start Workshop Grime.");
        var region = RoomRegion(f);
        var stray = AllRenderers(scene).Where(r => (r.renderingLayerMask & LayerBit) != 0 && !receiverSet.Contains(r) && MustLackLayer(r, region)).Take(5).ToList();
        foreach (var r in stray)
            errors.Add($"{r.name} carries rendering layer {LayerIndex} but is not a room surface; decals would paint it. Run Tools/CoreBuster/Build Start Workshop Grime.");
        ValidateActorPrefabs(errors);
    }

    /// <summary>
    /// Characters and weapons are spawned from prefabs at run time, after the scene's own renderers have been checked, so
    /// every prefab under Assets/FPS that holds a <see cref="Health"/>, <see cref="PlayerCharacterController"/> or
    /// <see cref="WeaponController"/> must have no renderer on the grime layer.
    /// </summary>
    static void ValidateActorPrefabs(List<string> errors)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/FPS" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || (prefab.GetComponentInChildren<Health>(true) == null &&
                                   prefab.GetComponentInChildren<PlayerCharacterController>(true) == null &&
                                   prefab.GetComponentInChildren<WeaponController>(true) == null)) continue;
            int painted = prefab.GetComponentsInChildren<Renderer>(true).Count(r => (r.renderingLayerMask & LayerBit) != 0);
            if (painted > 0)
                errors.Add($"{path} has {painted} renderer(s) on rendering layer {LayerIndex}; grime decals would paint this character or weapon.");
        }
    }

    /// <summary>The world-space box a projector paints, as bounds (a rotated box gives its enclosing axis-aligned box).</summary>
    public static Bounds BoxBounds(DecalProjector projector)
    {
        var t = projector.transform;
        Vector3 h = projector.size * .5f;
        Bounds b = default;
        bool first = true;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
            Vector3 world = t.TransformPoint(projector.pivot + corner);
            if (first) { b = new Bounds(world, Vector3.zero); first = false; } else b.Encapsulate(world);
        }
        return b;
    }
}
