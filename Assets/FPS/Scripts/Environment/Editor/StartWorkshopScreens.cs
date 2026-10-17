using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static StartWorkshopDresser;

/// <summary>
/// Rebuildable corebuster_env_016 pass. Lights the workshop's existing equipment with first-principles emissive content
/// from Tools/Art/generate_start_workshop_screens.py, in <c>Screens_016</c>:
/// <list type="bullet">
/// <item>the flat white <c>big_screen</c> face becomes the CORE STATUS graph and each console's screen surface a readout or schematic;</item>
/// <item>a small gauge screen sits on the front of each fusebox;</item>
/// <item>segmented LED strips run along the fuseboxes and consoles and up the power canister.</item>
/// </list>
/// Every overlay is a collider-free quad 4 mm proud of a measured surface of its anchor: the screen faces come from the
/// anchor's own "screen" submesh triangles, the panels and strips from rays onto the anchor's colliders. The Modular Pack
/// and kit materials and prefabs are never edited; the overlays cover them on project-owned URP/Lit emissive materials.
/// </summary>
public static class StartWorkshopScreens
{
    public const string GroupName = "Screens_016";
    const string UndoName = "Build Start Workshop Screens";
    const string Textures = "Assets/FPS/Art/Textures/StartWorkshop/";
    const string Materials = "Assets/FPS/Art/Materials/Props/";
    const string CaptureFolder = StartWorkshopReviewCapture.OutputFolder + "Screens_env016/";

    const string East = "Maintenance_EastWall_Detail/", South = "Services_RearWall/";
    // An overlay stands this far off its surface, and a screen leaves a bezel of this fraction of the face uncovered.
    const float Gap = .004f, ScreenFill = .92f;
    const float PanelSize = .30f, StripThickness = .035f;
    // A strip or panel must sit within this of the anchor's collider surface.
    const float SurfaceTolerance = .03f;

    static readonly Color Teal = new Color(.27f, .93f, .80f), Amber = new Color(.97f, .69f, .20f);
    const float StripIntensity = 1.6f, ScreenIntensity = 1.35f;

    enum Kind { Screen, Panel, Strip, VerticalStrip }

    struct Item
    {
        public string Name, Target, Texture;
        public Kind Kind;
        public Wall Wall;
        public Color Colour;
        // Height of a panel or strip centre as a fraction of its anchor's height.
        public float Height;
    }

    static readonly Item[] Items =
    {
        new Item { Name = "Core_Status_Screen", Target = East + "Control_Screen", Texture = "Screen_Core_Status", Kind = Kind.Screen, Wall = Wall.East },
        new Item { Name = "Console_A_Screen", Target = East + "Control_Console_A", Texture = "Screen_Diagnostics", Kind = Kind.Screen, Wall = Wall.East },
        new Item { Name = "Console_B_Screen", Target = East + "Control_Console_B", Texture = "Screen_Schematic", Kind = Kind.Screen, Wall = Wall.East },
        new Item { Name = "Fusebox_A_Panel", Target = South + "Fusebox_A", Texture = "Screen_Panel_Small", Kind = Kind.Panel, Wall = Wall.South, Height = .68f },
        new Item { Name = "Fusebox_B_Panel", Target = South + "Fusebox_B", Texture = "Screen_Panel_Small", Kind = Kind.Panel, Wall = Wall.South, Height = .68f },
        new Item { Name = "Fusebox_A_Led", Target = South + "Fusebox_A", Texture = "Led_Strip", Kind = Kind.Strip, Wall = Wall.South, Colour = Amber, Height = .22f },
        new Item { Name = "Fusebox_B_Led", Target = South + "Fusebox_B", Texture = "Led_Strip", Kind = Kind.Strip, Wall = Wall.South, Colour = Teal, Height = .22f },
        new Item { Name = "Console_A_Led", Target = East + "Control_Console_A", Texture = "Led_Strip", Kind = Kind.Strip, Wall = Wall.East, Colour = Teal, Height = .3f },
        new Item { Name = "Console_B_Led", Target = East + "Control_Console_B", Texture = "Led_Strip", Kind = Kind.Strip, Wall = Wall.East, Colour = Amber, Height = .3f },
        new Item { Name = "Canister_Led", Target = East + "Power_Canister", Texture = "Led_Strip", Kind = Kind.VerticalStrip, Wall = Wall.East, Colour = Teal, Height = .5f },
    };

    static Vector3 Inward(Wall wall) => wall switch
    {
        Wall.West => Vector3.right, Wall.East => Vector3.left, Wall.South => Vector3.forward, _ => Vector3.back,
    };

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    [MenuItem("Tools/CoreBuster/Build Start Workshop Screens")]
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

    /// <summary>Batch: -executeMethod StartWorkshopScreens.BuildBatch. Rebuilds, saves MainScene and runs the validator.</summary>
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
        if (root.Find(StartWorkshopAmbient.GroupName) != null) StartWorkshopAmbient.BindScreens(root);
        StartWorkshopLitMaterials.ApplyTo(scene, root);
    }

    public static bool IsGroup(Transform group) => group != null && group.name == GroupName;

    public static void Build(Transform root, StartWorkshopFrame f)
    {
        if (root.Find(GroupName) != null) throw new InvalidOperationException(GroupName + " already exists.");
        Physics.SyncTransforms();
        var group = new GameObject(GroupName).transform;
        group.SetParent(root, false);
        Undo.RegisterCreatedObjectUndo(group.gameObject, UndoName);
        try
        {
            foreach (var item in Items)
            {
                var target = root.Find(item.Target) ?? throw new InvalidOperationException(
                    $"Missing {item.Target}; run Tools/CoreBuster/Densify Start Workshop and Build Start Workshop Workstation first.");
                Place(group, target, item);
            }
        }
        catch
        {
            UnityEngine.Object.DestroyImmediate(group.gameObject);
            throw;
        }
    }

    // --- Placement ---------------------------------------------------------------------------------------------------

    /// <summary>A measured, oriented rectangle on an anchor's surface: the quad's centre, outward normal, up axis and size.</summary>
    struct Patch
    {
        public Vector3 Centre, Normal, Up;
        public Vector2 Size;
    }

    static void Place(Transform group, Transform target, Item item)
    {
        Vector3 inward = Inward(item.Wall);
        Patch patch;
        switch (item.Kind)
        {
            case Kind.Screen:
                patch = ScreenPatch(target, inward, item);
                break;
            case Kind.Panel:
                patch = SurfacePatch(target, inward, item);
                patch.Size = Vector2.one * Mathf.Min(PanelSize, patch.Size.x * .6f) * new Vector2(1, .66f);
                break;
            default:
                patch = SurfacePatch(target, inward, item);
                break;
        }
        var texture = LoadTexture(item.Texture, item.Kind == Kind.Strip || item.Kind == Kind.VerticalStrip);
        Vector2 tiling = Vector2.one, offset = Vector2.zero;
        Vector2 size = patch.Size;
        if (item.Kind == Kind.Screen || item.Kind == Kind.Panel)
        {
            // Cover the quad with the texture, cropping the excess rather than stretching the lettering.
            if (item.Kind == Kind.Screen) size *= ScreenFill;
            float quadAspect = size.x / size.y, texAspect = (float)texture.width / texture.height;
            if (quadAspect > texAspect) tiling.y = texAspect / quadAspect; else tiling.x = quadAspect / texAspect;
            offset = (Vector2.one - tiling) * .5f;
        }
        else
        {
            // Strips: 16 square segments per texture repeat; the quad's long side runs along U.
            tiling = new Vector2(size.x / size.y / 16f, 1);
        }
        Color colour = item.Kind == Kind.Screen || item.Kind == Kind.Panel ? Color.white * ScreenIntensity : item.Colour * StripIntensity;
        var material = EmissiveMaterial(item.Name, texture, tiling, offset, colour, item.Kind == Kind.Screen || item.Kind == Kind.Panel);

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = item.Name;
        UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(group, false);
        // Unity's Quad faces local -Z: looking into the surface puts the picture towards the room, reading left to right.
        var rotation = Quaternion.LookRotation(-patch.Normal, patch.Up);
        if (item.Kind == Kind.VerticalStrip) rotation *= Quaternion.AngleAxis(90, Vector3.forward);
        quad.transform.SetPositionAndRotation(patch.Centre + patch.Normal * Gap, rotation);
        quad.transform.localScale = new Vector3(size.x, size.y, 1);
        quad.GetComponent<MeshRenderer>().sharedMaterial = material;
        Debug.Log(FormattableString.Invariant(
            $"{item.Name}: {size.x:F2} x {size.y:F2} m on {item.Target}, normal {patch.Normal.x:F2},{patch.Normal.y:F2},{patch.Normal.z:F2}."));
    }

    /// <summary>The up axis of a patch: world up projected onto its plane, or (for a near-horizontal top) toward the wall.</summary>
    static Vector3 UpAxis(Vector3 normal, Vector3 inward)
    {
        Vector3 reference = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > .9f ? -inward : Vector3.up;
        return Vector3.ProjectOnPlane(reference, normal).normalized;
    }

    /// <summary>
    /// The largest flat face of the anchor's "screen" submesh that looks into the room. The submesh is found by material
    /// name (the pack's <c>screen</c> palette or the room's lit copy of it), so the triangles' own winding is not trusted.
    /// </summary>
    static Patch ScreenPatch(Transform target, Vector3 inward, Item item)
    {
        var triangles = new List<(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, float area)>();
        foreach (var r in target.GetComponentsInChildren<MeshRenderer>(true))
        {
            var filter = r.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            var mesh = filter.sharedMesh;
            var materials = r.sharedMaterials;
            var vertices = mesh.vertices;
            for (int s = 0; s < materials.Length && s < mesh.subMeshCount; s++)
            {
                if (materials[s] == null || materials[s].name.IndexOf("screen", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var indices = mesh.GetTriangles(s);
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    Vector3 a = r.transform.TransformPoint(vertices[indices[i]]), b = r.transform.TransformPoint(vertices[indices[i + 1]]),
                        c = r.transform.TransformPoint(vertices[indices[i + 2]]);
                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    float area = cross.magnitude * .5f;
                    if (area < 1e-7f) continue;
                    triangles.Add((a, b, c, cross.normalized, area));
                }
            }
        }
        if (triangles.Count == 0)
            throw new InvalidOperationException($"{item.Name}: {item.Target} has no mesh submesh on a material named 'screen'.");

        // The dominant face: the normal (either sign) carrying the most area, turned to look towards the room.
        Vector3 preferred = (inward + Vector3.up * .5f).normalized;
        Vector3 best = Vector3.zero;
        float bestArea = 0;
        foreach (var t in triangles)
        {
            Vector3 n = Vector3.Dot(t.normal, preferred) < 0 ? -t.normal : t.normal;
            float area = triangles.Where(o => Mathf.Abs(Vector3.Dot(o.normal, n)) > .98f).Sum(o => o.area);
            if (area > bestArea) { bestArea = area; best = n; }
        }
        if (Vector3.Dot(best, inward) < .2f)
            throw new InvalidOperationException($"{item.Name}: the largest screen face of {item.Target} does not look into the room.");
        var face = triangles.Where(o => Mathf.Abs(Vector3.Dot(o.normal, best)) > .98f).ToList();
        // Only the front layer: a thick monitor's back triangles share the normal but sit behind it.
        float front = face.Max(t => Mathf.Max(Vector3.Dot(t.a, best), Vector3.Dot(t.b, best), Vector3.Dot(t.c, best)));
        face = face.Where(t => Vector3.Dot(t.a, best) > front - .02f && Vector3.Dot(t.b, best) > front - .02f && Vector3.Dot(t.c, best) > front - .02f).ToList();
        Vector3 up = UpAxis(best, inward);
        Vector3 right = Vector3.Cross(-best, up);
        float r0 = float.MaxValue, r1 = float.MinValue, u0 = float.MaxValue, u1 = float.MinValue;
        foreach (var t in face)
            foreach (var p in new[] { t.a, t.b, t.c })
            {
                float pr = Vector3.Dot(p, right), pu = Vector3.Dot(p, up);
                r0 = Mathf.Min(r0, pr); r1 = Mathf.Max(r1, pr); u0 = Mathf.Min(u0, pu); u1 = Mathf.Max(u1, pu);
            }
        return new Patch
        {
            Normal = best, Up = up, Size = new Vector2(r1 - r0, u1 - u0),
            Centre = right * ((r0 + r1) * .5f) + up * ((u0 + u1) * .5f) + best * front,
        };
    }

    /// <summary>
    /// A point on the anchor's front surface at a fraction of its height, found by a ray onto its colliders, with the
    /// anchor's extent along the wall as the size (a caller narrows it).
    /// </summary>
    static Patch SurfacePatch(Transform target, Vector3 inward, Item item)
    {
        Bounds b = B(target.gameObject);
        float along = Mathf.Abs(inward.x) > .5f ? b.size.z : b.size.x;
        float reach = Vector3.Dot(b.extents, Abs(inward)) + .3f;
        Vector3 point = new Vector3(b.center.x, b.min.y + b.size.y * item.Height, b.center.z);
        Vector3 origin = point + inward * reach;
        if (!SurfaceHit(target, origin, -inward, reach * 2, out Vector3 hitPoint, out Vector3 normal))
            throw new InvalidOperationException($"{item.Name}: no collider surface found on {item.Target} at {item.Height:P0} of its height.");
        if (Vector3.Dot(normal, inward) < .7f)
            throw new InvalidOperationException($"{item.Name}: the surface found on {item.Target} does not face into the room.");
        Vector3 up = UpAxis(normal, inward);
        Vector2 size = item.Kind == Kind.Strip ? new Vector2(along * .7f, StripThickness) :
            item.Kind == Kind.VerticalStrip ? new Vector2(b.size.y * .6f, StripThickness) : new Vector2(along, along);
        return new Patch { Centre = hitPoint, Normal = normal, Up = up, Size = size };
    }

    /// <summary>
    /// Nearest collider hit under <paramref name="target"/>, triggers included. Some kit props carry no usable collider,
    /// so the renderer bounds' face is the fallback; its normal is the box face the ray enters.
    /// </summary>
    static bool SurfaceHit(Transform target, Vector3 origin, Vector3 direction, float distance, out Vector3 point, out Vector3 normal)
    {
        point = normal = default;
        float best = float.MaxValue;
        foreach (var hit in Physics.RaycastAll(origin, direction, distance, ~0, QueryTriggerInteraction.Collide))
        {
            if (!hit.transform.IsChildOf(target) || hit.distance >= best) continue;
            best = hit.distance;
            point = hit.point;
            normal = hit.normal;
        }
        if (best < float.MaxValue) return true;
        Bounds b = B(target.gameObject);
        if (!b.IntersectRay(new Ray(origin, direction), out float enter) || enter > distance) return false;
        point = origin + direction * enter;
        // The entered face is the axis along which the ray travels furthest.
        Vector3 d = Abs(direction);
        normal = d.x >= d.y && d.x >= d.z ? new Vector3(-Mathf.Sign(direction.x), 0, 0)
            : d.y >= d.z ? new Vector3(0, -Mathf.Sign(direction.y), 0) : new Vector3(0, 0, -Mathf.Sign(direction.z));
        return true;
    }

    // --- Materials ---------------------------------------------------------------------------------------------------

    static Texture2D LoadTexture(string name, bool repeat)
    {
        string path = Textures + name + ".png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter ?? throw new InvalidOperationException(
            $"Missing project-owned texture {path}; run python Tools/Art/generate_start_workshop_screens.py.");
        var wrap = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        if (importer.textureType != TextureImporterType.Default || !importer.sRGBTexture || importer.alphaIsTransparency ||
            importer.wrapMode != wrap || importer.filterMode != FilterMode.Trilinear || importer.anisoLevel != 8 ||
            importer.textureCompression != TextureImporterCompression.CompressedHQ)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = wrap;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>
    /// A project-owned URP/Lit material whose emission carries the picture. Screens keep a dim base map so the glass
    /// reads in room light; strips are black glass with the tinted emission only.
    /// </summary>
    static Material EmissiveMaterial(string itemName, Texture2D texture, Vector2 tiling, Vector2 offset, Color emission, bool dimBase)
    {
        string path = Materials + "M_Env016_" + itemName + ".mat";
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? throw new InvalidOperationException("URP Lit shader is unavailable.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = "M_Env016_" + itemName };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetTexture("_BaseMap", texture);
        material.SetTextureScale("_BaseMap", tiling);
        material.SetTextureOffset("_BaseMap", offset);
        material.SetColor("_BaseColor", dimBase ? new Color(.22f, .22f, .22f) : Color.black);
        material.SetFloat("_Metallic", 0);
        material.SetFloat("_Smoothness", dimBase ? .85f : .4f);
        material.SetFloat("_Surface", 0);
        material.SetFloat("_AlphaClip", 0);
        material.SetFloat("_Cull", 2);
        material.SetTexture("_EmissionMap", texture);
        material.SetColor("_EmissionColor", emission);
        BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
        material.EnableKeyword("_EMISSION");
        // Room lights, not baked emission, own this look; the pass must not feed any lightmap or probe.
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(material);
        return material;
    }

    // --- Validation --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The screens rule: every overlay is present and collider-free on a project-owned emissive URP/Lit material, lies on
    /// its anchor (inside its bounds, plus the overlay gap), and a strip or panel sits within
    /// <see cref="SurfaceTolerance"/> of the anchor's collider surface.
    /// </summary>
    public static void Validate(Transform root, StartWorkshopFrame f, List<string> errors)
    {
        var group = root.Find(GroupName);
        if (group == null)
        {
            errors.Add(GroupName + " is missing; run Tools/CoreBuster/Build Start Workshop Screens.");
            return;
        }
        if (group.GetComponentsInChildren<Collider>(true).Length > 0) errors.Add(GroupName + " must have no colliders.");
        foreach (var r in group.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m == null || m.shader == null || m.shader.name != "Universal Render Pipeline/Lit" ||
                    !AssetDatabase.GetAssetPath(m).StartsWith("Assets/FPS/Art/", StringComparison.Ordinal) || !m.IsKeywordEnabled("_EMISSION"))
                    errors.Add($"{GroupName}/{r.name} must use a project-owned emissive URP/Lit material.");
        Physics.SyncTransforms();
        foreach (var item in Items)
        {
            var quad = group.Find(item.Name);
            var target = root.Find(item.Target);
            if (quad == null || target == null) { errors.Add($"{GroupName}/{item.Name} or its anchor {item.Target} is missing."); continue; }
            Bounds ab = B(target.gameObject), qb = B(quad.gameObject);
            ab.Expand(.06f);
            if (!ab.Contains(qb.min) || !ab.Contains(qb.max)) errors.Add($"{GroupName}/{item.Name} is not on its anchor {item.Target}.");
            if (qb.min.x < f.West || qb.max.x > f.East || qb.min.z < f.South || qb.max.z > f.North || qb.min.y < f.Floor || qb.max.y > f.Ceiling)
                errors.Add($"{GroupName}/{item.Name} extends outside the room.");
            if (item.Kind == Kind.Screen) continue;
            Vector3 normal = -quad.forward;
            if (!SurfaceHit(target, quad.position + normal * .3f, -normal, .6f, out Vector3 hit, out _))
                errors.Add($"{GroupName}/{item.Name} has no {item.Target} collider surface behind it.");
            else if (Vector3.Distance(hit, quad.position) > Gap + SurfaceTolerance)
                errors.Add(FormattableString.Invariant($"{GroupName}/{item.Name} stands {Vector3.Distance(hit, quad.position):F3} m off its anchor."));
        }
    }

    // --- Review captures ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Batch, without -nographics: -executeMethod StartWorkshopScreens.CaptureBatch. Renders each overlay from 1.5 and
    /// 4 m, straight on and at eye height, into ReviewCaptures.
    /// </summary>
    public static void CaptureBatch() => Capture(EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single));

    [MenuItem("Tools/CoreBuster/Capture Start Workshop Screens")]
    public static void CaptureFromMenu() => Capture(EditorSceneManager.GetActiveScene());

    static void Capture(Scene scene)
    {
        var root = FindLevel(scene)?.Find(ContainerName);
        var group = root != null ? root.Find(GroupName) : null;
        if (group == null)
        {
            Debug.LogError($"No {GroupName} in {ScenePath}; run Tools/CoreBuster/Build Start Workshop Screens first.");
            return;
        }
        var f = StartWorkshopFrame.Measure(scene, root);
        Physics.SyncTransforms();
        var views = new List<(string, Vector3, Quaternion)>();
        foreach (var item in Items)
        {
            var quad = group.Find(item.Name);
            if (quad == null) continue;
            Vector3 centre = quad.position, normal = -quad.forward;
            foreach (float distance in new[] { 1.5f, 4f })
            {
                Vector3 eye = centre + normal * distance;
                eye.y = Mathf.Clamp(eye.y, f.Floor + 1.4f, f.Floor + 1.8f);
                if (Physics.CheckSphere(eye, .3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                views.Add(($"{item.Name}_{distance:0.0}m", eye, Quaternion.LookRotation(centre - eye, Vector3.up)));
            }
        }
        StartWorkshopReviewCapture.CaptureViews(CaptureFolder, views, warmUpAll: true);
    }
}
