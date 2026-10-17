using System;
using System.Collections.Generic;
using System.Linq;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static StartWorkshopDresser;

/// <summary>
/// Rebuildable corebuster_art_003 signage pass. Hangs the project-owned stencil graphics from
/// Tools/Art/generate_start_workshop_signage.py in <c>Signage_Art003</c>:
/// <list type="bullet">
/// <item>the three wall slogans, painted high on the west, east and north-east walls above the pipe run and the overhead grid;</item>
/// <item>the robot-arm blueprint poster above the corebuster_env_010 bench, over the arm;</item>
/// <item>a pair of hazard chevron strips converging on the exit above the portal;</item>
/// <item>a P-3 identification plate wrapped round each pressure tank, between its lower bands.</item>
/// </list>
/// Every wall graphic is placed in measured free wall space: a sampled patch of the real wall surface that is flat and
/// clear of every other pass's geometry, sliding along the wall from its preferred spot. Nothing has a collider, nothing
/// hangs below head height except the tank plates on the tanks, and the materials are URP/Lit so the paint sits in the
/// room's light instead of glowing.
/// </summary>
public static class StartWorkshopSignage
{
    public const string GroupName = "Signage_Art003";
    const string UndoName = "Build Start Workshop Signage";
    const string Textures = "Assets/FPS/Art/Textures/StartWorkshop/";
    const string Materials = "Assets/FPS/Art/Materials/Props/";
    const string MeshFolder = "Assets/FPS/Art/StartWorkshop/Signage";
    const string TankMeshPath = MeshFolder + "/SM_Tank_ID_Wrap.asset";
    const string BackingMaterial = Materials + "M_Workstation_PaintedSteel.mat";
    const string ArmCellPath = "Workstation_010/Robot_Arm_Cell";
    const string CaptureFolder = StartWorkshopReviewCapture.OutputFolder + "Signage_art003/";

    public const string ToolsSlogan = "Slogan_Tools_People_Progress";
    public const string MaintainSlogan = "Slogan_Maintain_Build_Improve_Survive";
    public const string RobotsSlogan = "Slogan_Small_Robots_Big_Future";
    public const string Blueprint = "Blueprint_Robot_Arm";
    public const string ChevronWest = "Chevron_Exit_West", ChevronEast = "Chevron_Exit_East";
    public const string TankWest = "Tank_ID_P3_West", TankEast = "Tank_ID_P3_East";
    static readonly string[] WallItems = { ToolsSlogan, MaintainSlogan, RobotsSlogan, Blueprint, ChevronWest, ChevronEast };
    static readonly (string item, string tank)[] TankItems =
        { (TankWest, "Pipework_009/Tank_P3_West/Tank"), (TankEast, "Pipework_009/Tank_P3_East/Tank") };

    // Painted paint sits this far off the wall surface; the mounted panel's backing plate is this deep.
    const float PaintGap = .012f, PanelDepth = .04f;
    // The mounted panel's backing plate overhangs its printed face by half this on every side.
    const float PanelMargin = .06f;
    // Wall-surface sampling: how far in front of the frame face the rays start, and how uneven a patch may be before it
    // is judged to straddle a pilaster or a recess.
    const float RayStart = 1.2f, MaxRelief = .05f;
    // The volume in front of a graphic that must be clear of other geometry.
    const float ClearDepth = .8f;
    const float SlideStep = .1f;
    const float CeilingMargin = .2f;

    // The tank plate wraps the P_PressureTank_P3 shell (radius .60 m) between its bands at .58 m and 1.5 m and below the
    // corebuster_art_001 block-built P-3 lettering (from 1.30 m). 800 x 512 px texture.
    const float TankRadius = .612f, TankPlateBottom = .68f, TankPlateTop = 1.24f;
    const int TankSegments = 16;
    const float TankPlateAspect = 800f / 512f;

    enum Paint { Painted, Poster, Panel, Plate, Strip }

    /// <summary>One wall graphic: its wall, preferred centre along the wall, bottom height above the floor and size.</summary>
    struct Spec
    {
        public string Name;
        public Wall Wall;
        public float Along, Bottom;
        public Vector2 Size;
        public Paint Paint;
        public float MaxSlide, MaxRise;
        public bool Mirror;
        public string Texture;
    }

    [MenuItem("Tools/CoreBuster/Build Start Workshop Signage")]
    public static void Rebuild()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var level = scene.path == ScenePath ? FindLevel(scene) : null;
        var root = level != null ? level.Find(ContainerName) : null;
        if (root == null)
        {
            Debug.LogError($"Open {ScenePath} and run Tools/CoreBuster/Dress Start Workshop first.");
            return;
        }
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        RebuildIn(scene, root);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeTransform = root.Find(GroupName);
        StartWorkshopChecks.Run();
    }

    /// <summary>Batch: -executeMethod StartWorkshopSignage.BuildBatch. Rebuilds, saves MainScene and runs the validator.</summary>
    public static void BuildBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var level = FindLevel(scene);
        var root = level != null ? level.Find(ContainerName) : null;
        if (root == null)
        {
            Debug.LogError($"No {LevelName}/{ContainerName} in {ScenePath}; run Tools/CoreBuster/Dress Start Workshop first.");
            EditorApplication.Exit(1);
            return;
        }
        RebuildIn(scene, root);
        EditorSceneManager.SaveScene(scene);
        StartWorkshopChecks.Run();
    }

    static void RebuildIn(Scene scene, Transform root)
    {
        var existing = root.Find(GroupName);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
        var f = StartWorkshopFrame.Measure(scene, root);
        Build(root, f);
        ApplyStaticConvention(root.Find(GroupName), GameObjectUtility.GetStaticEditorFlags(root.parent.gameObject));
        StartWorkshopLitMaterials.ApplyTo(scene, root);
    }

    public static bool IsGroup(Transform group) => group != null && group.name == GroupName;

    static List<Spec> Specs(Transform root, StartWorkshopFrame f)
    {
        var cell = root.Find(ArmCellPath) ?? throw new InvalidOperationException(
            $"Missing {ArmCellPath}; run Tools/CoreBuster/Build Start Workshop Workstation first.");
        float midZ = (f.South + f.North) * .5f;
        float top = f.Ceiling - f.Floor - CeilingMargin;
        // Slogans hang beside and above the overhead grid (5.8-7.2 m) where the upper walls are bare, read from across the
        // room like painted supergraphics. The north-east panel stays under the shell's ledge trim at about 10.2 m. The
        // chevrons sit just above the north pipe run, over the doorway.
        return new List<Spec>
        {
            new Spec { Name = ToolsSlogan, Texture = ToolsSlogan, Wall = Wall.West, Along = midZ - 1f, Bottom = top - 4.6f,
                Size = new Vector2(4.6f, 4.6f), Paint = Paint.Painted, MaxSlide = 5f, MaxRise = 0f },
            new Spec { Name = MaintainSlogan, Texture = MaintainSlogan, Wall = Wall.East, Along = midZ + 6.5f, Bottom = top - 4.6f,
                Size = new Vector2(3.68f, 4.6f), Paint = Paint.Painted, MaxSlide = 5f, MaxRise = 0f },
            new Spec { Name = RobotsSlogan, Texture = RobotsSlogan, Wall = Wall.North, Along = f.OpeningMaxX + 5.4f, Bottom = 6.1f,
                Size = new Vector2(2.6f, 3.9f), Paint = Paint.Panel, MaxSlide = 1.5f, MaxRise = .2f },
            new Spec { Name = Blueprint, Texture = Blueprint, Wall = Wall.East, Along = B(cell.gameObject).center.z, Bottom = 2.95f,
                Size = new Vector2(1.8f, 1.2f), Paint = Paint.Poster, MaxSlide = 3f, MaxRise = .3f },
            new Spec { Name = ChevronWest, Texture = "Hazard_Chevron", Wall = Wall.North, Along = f.OpeningCenter - 1.45f, Bottom = 5.8f,
                Size = new Vector2(2.4f, .42f), Paint = Paint.Strip, MaxSlide = 0f, MaxRise = .6f },
            new Spec { Name = ChevronEast, Texture = "Hazard_Chevron", Wall = Wall.North, Along = f.OpeningCenter + 1.45f, Bottom = 5.8f,
                Size = new Vector2(2.4f, .42f), Paint = Paint.Strip, MaxSlide = 0f, MaxRise = .6f, Mirror = true },
        };
    }

    public static void Build(Transform root, StartWorkshopFrame f)
    {
        if (root.Find(GroupName) != null) throw new InvalidOperationException(GroupName + " already exists.");
        var obstacles = Obstacles(root.gameObject.scene, root, f, null);
        var group = new GameObject(GroupName).transform;
        group.SetParent(root, false);
        Undo.RegisterCreatedObjectUndo(group.gameObject, UndoName);
        try
        {
            foreach (var spec in Specs(root, f))
            {
                if (!TryPlace(root, f, spec, obstacles, out Vector3 surface, out float along, out float bottom, out string why))
                    throw new InvalidOperationException($"No clear, flat wall space for {spec.Name} on the {spec.Wall} wall near {spec.Along:F2} ({why}).");
                Hang(group, f, spec, surface);
                Debug.Log(FormattableString.Invariant(
                    $"{spec.Name}: {spec.Wall} wall at {along:F2} (preferred {spec.Along:F2}), {bottom:F2}-{bottom + spec.Size.y:F2} m above the floor."));
            }
            var mesh = TankPlateMesh();
            var plate = LitMaterial("Tank_ID_P3", Paint.Plate, Vector2.one);
            foreach (var (item, path) in TankItems)
            {
                var tank = root.Find(path) ?? throw new InvalidOperationException(
                    $"Missing {path}; run Tools/CoreBuster/Build Start Workshop Pipework first.");
                var go = new GameObject(item);
                go.transform.SetParent(group, false);
                go.transform.SetPositionAndRotation(tank.position, tank.rotation);
                go.transform.localScale = tank.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = plate;
            }
        }
        catch
        {
            UnityEngine.Object.DestroyImmediate(group.gameObject);
            throw;
        }
    }

    // --- Placement ---------------------------------------------------------------------------------------------------

    static Vector3 Inward(Wall wall) => wall switch
    {
        Wall.West => Vector3.right, Wall.East => Vector3.left, Wall.South => Vector3.forward, _ => Vector3.back,
    };

    static float Face(StartWorkshopFrame f, Wall wall) => wall switch
    {
        Wall.West => f.West, Wall.East => f.East, Wall.South => f.South, _ => f.North,
    };

    static (float min, float max) Span(StartWorkshopFrame f, Wall wall) =>
        wall == Wall.West || wall == Wall.East ? (f.South, f.North) : (f.West, f.East);

    /// <summary>A point on the wall's frame face plane, <paramref name="depth"/> metres into the room.</summary>
    static Vector3 WallPoint(StartWorkshopFrame f, Wall wall, float along, float height, float depth)
    {
        Vector3 inward = Inward(wall);
        float face = Face(f, wall) + depth * (inward.x + inward.z);
        return wall == Wall.West || wall == Wall.East
            ? new Vector3(face, f.Floor + height, along)
            : new Vector3(along, f.Floor + height, face);
    }

    /// <summary>
    /// Slides from the preferred spot in <see cref="SlideStep"/> steps, then rises, until the patch is flat real wall
    /// with nothing in front of it. <paramref name="surface"/> is the patch centre on the wall's front-most surface.
    /// </summary>
    static bool TryPlace(Transform root, StartWorkshopFrame f, Spec spec, List<(string name, Bounds b, bool surface)> obstacles,
        out Vector3 surface, out float along, out float bottom, out string why)
    {
        surface = default;
        along = spec.Along;
        bottom = spec.Bottom;
        why = "no candidate";
        var (min, max) = Span(f, spec.Wall);
        int slides = Mathf.RoundToInt(spec.MaxSlide / SlideStep), rises = Mathf.RoundToInt(spec.MaxRise / SlideStep);
        float top = f.Ceiling - f.Floor - CeilingMargin;
        for (int s = 0; s <= 2 * slides; s++)
        {
            float a = spec.Along + (s % 2 == 1 ? 1 : -1) * ((s + 1) / 2) * SlideStep;
            if (a - Extent(spec).x * .5f < min + .3f || a + Extent(spec).x * .5f > max - .3f) continue;
            for (int r = 0; r <= rises; r++)
            {
                float y = spec.Bottom + r * SlideStep;
                if (y + (spec.Size.y + Extent(spec).y) * .5f > top) break;
                if (!Probe(root, f, spec, a, y, obstacles, out float depth, out why)) continue;
                along = a;
                bottom = y;
                surface = WallPoint(f, spec.Wall, a, y + spec.Size.y * .5f, depth);
                return true;
            }
        }
        return false;
    }

    /// <summary>Samples the patch with rays onto the shell and checks the volume in front of it for other geometry.</summary>
    static bool Probe(Transform root, StartWorkshopFrame f, Spec spec, float along, float bottom,
        List<(string name, Bounds b, bool surface)> obstacles, out float depth, out string why)
    {
        depth = 0;
        why = null;
        Vector3 inward = Inward(spec.Wall);
        Vector2 size = Extent(spec);
        bottom -= (size.y - spec.Size.y) * .5f;
        // The front-most sample (largest depth into the room) is the surface; the spread is how uneven the patch is.
        float front = float.MinValue, back = float.MaxValue;
        const int columns = 7, rows = 5;
        for (int i = 0; i < columns; i++)
            for (int j = 0; j < rows; j++)
            {
                float a = along + size.x * ((i + .5f) / columns - .5f);
                float y = bottom + size.y * (j + .5f) / rows;
                if (!FirstHit(root, WallPoint(f, spec.Wall, a, y, RayStart), -inward, RayStart + 1f, out RaycastHit hit))
                {
                    why = "no wall surface behind it";
                    return false;
                }
                if (!IsShell(hit.transform, root))
                {
                    why = "blocked by " + hit.transform.name;
                    return false;
                }
                float d = RayStart - hit.distance;
                front = Mathf.Max(front, d);
                back = Mathf.Min(back, d);
            }
        if (front - back > MaxRelief)
        {
            why = FormattableString.Invariant($"uneven wall ({front - back:F2} m relief)");
            return false;
        }
        depth = front;

        var footprint = Footprint(f, spec.Wall, along, bottom, size, depth - .05f, depth + ClearDepth);
        foreach (var (name, b, isSurface) in obstacles)
        {
            if (!b.Intersects(footprint)) continue;
            if (isSurface)
            {
                // A liner is a room-side copy of the wall: paint goes on its face, as long as the patch stays flat.
                float face = Vector3.Dot(inward, b.center) + Vector3.Dot(Abs(inward), b.extents) - Face(f, spec.Wall) * (inward.x + inward.z);
                if (face - back > MaxRelief) { why = "uneven liner " + name; return false; }
                depth = Mathf.Max(depth, face);
                continue;
            }
            why = "overlaps " + name;
            return false;
        }
        return true;
    }

    /// <summary>The graphic's full wall footprint, including a mounted panel's backing plate.</summary>
    static Vector2 Extent(Spec spec) => spec.Paint == Paint.Panel ? spec.Size + Vector2.one * PanelMargin : spec.Size;

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    static Bounds Footprint(StartWorkshopFrame f, Wall wall, float along, float bottom, Vector2 size, float fromDepth, float toDepth)
    {
        var b = new Bounds();
        Vector3 p0 = WallPoint(f, wall, along - size.x * .5f, bottom, fromDepth);
        Vector3 p1 = WallPoint(f, wall, along + size.x * .5f, bottom + size.y, toDepth);
        b.SetMinMax(Vector3.Min(p0, p1), Vector3.Max(p0, p1));
        return b;
    }

    /// <summary>Nearest non-trigger hit, ignoring this pass's own group.</summary>
    static bool FirstHit(Transform root, Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
    {
        nearest = default;
        float best = float.MaxValue;
        var own = root.Find(GroupName);
        foreach (var hit in Physics.RaycastAll(origin, direction, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            if ((own != null && hit.transform.IsChildOf(own)) || hit.distance >= best) continue;
            best = hit.distance;
            nearest = hit;
        }
        return best < float.MaxValue;
    }

    /// <summary>The fixed room shell: a wall piece outside the dressing container.</summary>
    static bool IsShell(Transform t, Transform root)
    {
        if (t.IsChildOf(root)) return false;
        for (; t != null && !t.GetComponent<HierarchyFolder>(); t = t.parent) if (t.name.Contains("Wall")) return true;
        return false;
    }

    /// <summary>
    /// Every renderer a graphic must not cover: the other passes' dressing and any non-shell scene geometry in the room.
    /// The render_007 liners count as wall surface. Actors move, so they are not obstacles.
    /// </summary>
    static List<(string name, Bounds b, bool surface)> Obstacles(Scene scene, Transform root, StartWorkshopFrame f, Transform own)
    {
        var room = new Bounds();
        room.SetMinMax(new Vector3(f.West, f.Floor, f.South) - Vector3.one, new Vector3(f.East, f.Ceiling, f.North) + Vector3.one);
        var list = new List<(string, Bounds, bool)>();
        foreach (var go in scene.GetRootGameObjects())
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer) || !room.Intersects(r.bounds)) continue;
                var t = r.transform;
                if (own != null && t.IsChildOf(own)) continue;
                if (t.GetComponentInParent<Health>(true) != null || t.GetComponentInParent<PlayerCharacterController>(true) != null) continue;
                if (t.IsChildOf(root))
                {
                    var group = t;
                    while (group.parent != root) group = group.parent;
                    if (IsGroup(group) || StartWorkshopRendering.IsGroup(group)) continue;
                    list.Add((group.name + "/" + t.name, r.bounds, StartWorkshopLiners.IsGroup(group)));
                    continue;
                }
                bool shell = false;
                for (var p = t; p != null && !shell && !p.GetComponent<HierarchyFolder>(); p = p.parent) shell = p.name.Contains("Wall") || p.name.Contains("Floor Tile");
                if (!shell) list.Add((t.name, r.bounds, false));
            }
        return list;
    }

    // --- Construction ------------------------------------------------------------------------------------------------

    static void Hang(Transform group, StartWorkshopFrame f, Spec spec, Vector3 surface)
    {
        Vector3 inward = Inward(spec.Wall);
        // Unity's Quad faces local -Z: looking into the wall puts the printed side towards the room, reading left to right.
        var rotation = Quaternion.LookRotation(-inward, Vector3.up);
        if (spec.Mirror) rotation *= Quaternion.AngleAxis(180, Vector3.forward);
        Vector2 tiling = spec.Paint == Paint.Strip ? new Vector2(spec.Size.x / spec.Size.y / 4f, 1) : Vector2.one;
        var material = LitMaterial(spec.Texture, spec.Paint, tiling);
        if (spec.Paint == Paint.Panel)
        {
            var panel = new GameObject(spec.Name).transform;
            panel.SetParent(group, false);
            var backing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backing.name = "Backing";
            UnityEngine.Object.DestroyImmediate(backing.GetComponent<Collider>());
            backing.transform.SetParent(panel, false);
            backing.transform.SetPositionAndRotation(surface + inward * (PanelDepth * .5f + .002f), rotation);
            Vector2 extent = Extent(spec);
            backing.transform.localScale = new Vector3(extent.x, extent.y, PanelDepth);
            backing.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(BackingMaterial) ??
                throw new InvalidOperationException($"Missing {BackingMaterial}; run Tools/CoreBuster/Build Start Workshop Workstation first.");
            Quad(panel, "Face", surface + inward * (PanelDepth + .004f), rotation, spec.Size, material);
            return;
        }
        Quad(group, spec.Name, surface + inward * PaintGap, rotation, spec.Size, material);
    }

    static void Quad(Transform parent, string name, Vector3 position, Quaternion rotation, Vector2 size, Material material)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, false);
        quad.transform.SetPositionAndRotation(position, rotation);
        quad.transform.localScale = new Vector3(size.x, size.y, 1);
        quad.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    /// <summary>
    /// A cylindrical strip in tank space, just outside the shell and centred on the tank's room-facing +Z, with UVs that
    /// read left to right from the front.
    /// </summary>
    static Mesh TankPlateMesh()
    {
        float height = TankPlateTop - TankPlateBottom;
        float span = height * TankPlateAspect / TankRadius;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        for (int i = 0; i <= TankSegments; i++)
        {
            float u = (float)i / TankSegments;
            // Seen from the front, the viewer's right is the tank's local -X.
            float angle = span * (.5f - u);
            var normal = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
            foreach (var (y, v) in new[] { (TankPlateBottom, 0f), (TankPlateTop, 1f) })
            {
                vertices.Add(normal * TankRadius + Vector3.up * y);
                normals.Add(normal);
                uvs.Add(new Vector2(u, v));
            }
            if (i == TankSegments) continue;
            int b = 2 * i;
            triangles.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
        }
        var mesh = new Mesh { name = "SM_Tank_ID_Wrap" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        EnsureFolder(MeshFolder);
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(TankMeshPath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(mesh, existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, TankMeshPath);
        return mesh;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }

    /// <summary>Project-owned URP/Lit material for one signage texture; wall paint is alpha-blended over the wall.</summary>
    static Material LitMaterial(string textureName, Paint paint, Vector2 tiling)
    {
        string texturePath = Textures + textureName + ".png";
        var texture = ConfigureTexture(texturePath, paint == Paint.Painted, paint == Paint.Strip);
        string path = Materials + "M_" + textureName + ".mat";
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? throw new InvalidOperationException("URP Lit shader is unavailable.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = "M_" + textureName };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetTexture("_BaseMap", texture);
        material.SetTextureScale("_BaseMap", tiling);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Metallic", 0);
        material.SetFloat("_Smoothness", paint switch { Paint.Poster => .12f, Paint.Painted => .25f, _ => .32f });
        material.SetFloat("_Surface", paint == Paint.Painted ? 1 : 0);
        material.SetFloat("_Blend", 0);
        material.SetFloat("_AlphaClip", 0);
        material.SetFloat("_Cull", 2);
        material.SetFloat("_ReceiveShadows", 1);
        BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Texture2D ConfigureTexture(string path, bool alpha, bool repeat)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter ?? throw new InvalidOperationException(
            $"Missing project-owned texture {path}; run python Tools/Art/generate_start_workshop_signage.py.");
        var wrap = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        if (importer.textureType != TextureImporterType.Default || !importer.sRGBTexture || importer.alphaIsTransparency != alpha ||
            !importer.mipmapEnabled || importer.wrapMode != wrap || importer.filterMode != FilterMode.Trilinear ||
            importer.anisoLevel != 8 || importer.maxTextureSize != 2048 || importer.textureCompression != TextureImporterCompression.CompressedHQ)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = alpha;
            importer.mipmapEnabled = true;
            importer.wrapMode = wrap;
            importer.filterMode = FilterMode.Trilinear;
            // Graphics are read at grazing angles from across the room; anisotropic filtering keeps the letters sharp.
            importer.anisoLevel = 8;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // --- Validation --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The signage rule: every graphic is present and collider-free on a project-owned URP/Lit material; wall graphics
    /// are above head height, inside the room, flush with (within <see cref="FlushTolerance"/> of) the real wall and
    /// clear of every other pass's geometry; each tank plate sits on its tank below the P-3 lettering.
    /// </summary>
    public static void Validate(Transform root, StartWorkshopFrame f, List<string> errors)
    {
        var group = root.Find(GroupName);
        if (group == null)
        {
            errors.Add(GroupName + " is missing; run Tools/CoreBuster/Build Start Workshop Signage.");
            return;
        }
        if (group.GetComponentsInChildren<Collider>(true).Length > 0) errors.Add(GroupName + " must have no colliders.");
        foreach (var r in group.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m == null || m.shader == null || m.shader.name != "Universal Render Pipeline/Lit" ||
                    !AssetDatabase.GetAssetPath(m).StartsWith("Assets/FPS/Art/", StringComparison.Ordinal))
                    errors.Add($"{GroupName}/{r.name} must use a project-owned URP/Lit material.");

        var obstacles = Obstacles(root.gameObject.scene, root, f, group);
        foreach (var name in WallItems)
        {
            var item = group.Find(name);
            if (item == null) { errors.Add($"{GroupName}/{name} is missing."); continue; }
            var face = item.Find("Face") ?? item;
            Bounds b = B(item.gameObject);
            if (b.min.x < f.West - .25f || b.max.x > f.East + .25f || b.min.z < f.South - .25f || b.max.z > f.North + .25f ||
                b.max.y > f.Ceiling)
                errors.Add($"{GroupName}/{name} extends outside the room.");
            if (b.min.y < f.Floor + StartWorkshopFrame.HeadHeight)
                errors.Add($"{GroupName}/{name} hangs below head height ({b.min.y - f.Floor:F2} m).");
            Vector3 inward = -face.forward;
            float lead = .5f + (face == item ? 0 : PanelDepth);
            if (!FirstHit(root, face.position + inward * .5f, -inward, 1.5f, out RaycastHit hit) || !IsShell(hit.transform, root))
                errors.Add($"{GroupName}/{name} is not in front of the room's wall.");
            else if (hit.distance < .5f - .002f)
                errors.Add($"{GroupName}/{name} is behind the wall surface.");
            else if (hit.distance > lead + FlushTolerance)
                errors.Add(FormattableString.Invariant($"{GroupName}/{name} stands {hit.distance - .5f:F3} m off the wall."));
            foreach (var (other, ob, surface) in obstacles)
                if (!surface && ob.Intersects(b))
                {
                    errors.Add($"{GroupName}/{name} overlaps {other}.");
                    break;
                }
        }
        foreach (var (item, path) in TankItems)
        {
            var plate = group.Find(item);
            var tank = root.Find(path);
            if (plate == null || tank == null) { errors.Add($"{GroupName}/{item} or its tank {path} is missing."); continue; }
            Bounds pb = B(plate.gameObject), tb = B(tank.gameObject);
            tb.Expand(.06f);
            if (!tb.Contains(pb.min) || !tb.Contains(pb.max)) errors.Add($"{GroupName}/{item} is not on its tank.");
            var id = tank.Find("P3_ID");
            if (id != null && B(id.gameObject).Intersects(pb)) errors.Add($"{GroupName}/{item} covers the tank's P-3 lettering.");
        }
    }

    const float FlushTolerance = .03f;

    // --- Legibility captures -----------------------------------------------------------------------------------------

    /// <summary>
    /// Batch, without -nographics: -executeMethod StartWorkshopSignage.CaptureLegibilityBatch. Renders every graphic
    /// from 5, 10 and 15 m (horizontal distance, eye height, looking at its centre) into ReviewCaptures.
    /// </summary>
    public static void CaptureLegibilityBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        CaptureLegibility(scene);
    }

    [MenuItem("Tools/CoreBuster/Capture Start Workshop Signage Legibility")]
    public static void CaptureLegibilityFromMenu() => CaptureLegibility(EditorSceneManager.GetActiveScene());

    static void CaptureLegibility(Scene scene)
    {
        var root = FindLevel(scene)?.Find(ContainerName);
        var group = root != null ? root.Find(GroupName) : null;
        if (group == null)
        {
            Debug.LogError($"No {GroupName} in {ScenePath}; run Tools/CoreBuster/Build Start Workshop Signage first.");
            return;
        }
        var f = StartWorkshopFrame.Measure(scene, root);
        Physics.SyncTransforms();
        var views = new List<(string, Vector3, Quaternion)>();
        var targets = WallItems.Select(n => group.Find(n)).Where(t => t != null)
            .Select(t => (t.name, B(t.gameObject).center, -(t.Find("Face") ?? t).forward))
            .Concat(TankItems.Select(p => group.Find(p.item)).Where(t => t != null)
                .Select(t => (t.name, B(t.gameObject).center, t.forward)));
        foreach (var (name, centre, outward) in targets)
        {
            Vector3 flat = new Vector3(outward.x, 0, outward.z).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, flat);
            foreach (float distance in new[] { 5f, 10f, 15f })
            {
                Vector3 eye = new Vector3(centre.x, f.Floor + 1.6f, centre.z) + flat * distance;
                // Stand where a player could: inside the room and out of any prop.
                foreach (float shift in new[] { 0f, 1f, -1f, 2f, -2f, 3f, -3f })
                {
                    Vector3 p = eye + side * shift;
                    if (p.x < f.West + .5f || p.x > f.East - .5f || p.z < f.South + .5f || p.z > f.North - .5f) continue;
                    if (Physics.CheckSphere(p, .35f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    views.Add(($"{name}_{distance:00}m", p, Quaternion.LookRotation(centre - p, Vector3.up)));
                    break;
                }
            }
        }
        StartWorkshopReviewCapture.CaptureViews(CaptureFolder, views, warmUpAll: true);
    }
}
