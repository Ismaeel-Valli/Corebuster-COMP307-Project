using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Section = BossHallFramework.Section;

/// <summary>corebuster_env_054: visual-only meltdown effects in Meltdown_VFX. Script-, collider- and light-free; no gameplay state.</summary>
public static class BossHallMeltdown
{
    public const string Owner = "corebuster_env_054";
    public const string Folder = BossHallFramework.AssetRoot + "/Meltdown_VFX", Output = "ReviewCaptures/BossHall/054";
    const string SteamSource = "Assets/FPS/Art/Materials/Props/M_Env017_Steam.mat";
    const string ArcSource = "Assets/FPS/Art/NorthWestRoom/Electrical_Systems/Materials/M_Room3_Electric_Arc.mat";
    public static string ParticleMaterialPath => Folder + "/Materials/M_Meltdown_Particle.mat";
    public static string ArcMaterialPath => Folder + "/Materials/M_Meltdown_Arc.mat";
    public static string ScorchMeshPath => Folder + "/Meshes/SM_Meltdown_Scorch.asset";
    public static string PuffTexturePath => Folder + "/Textures/T_Meltdown_Puff.png";
    const string SteamTexture = "Assets/FPS/Art/Textures/StartWorkshop/Ambient/Steam.png";
    // Steam.png peaks at alpha 59/255, which disappears at hall scale; the owned copy scales only its alpha.
    const float PuffAlphaGain = 3.4f;
    public const int EmitterCap = 6;
    static readonly string[] Sides = { "South", "North" };

    public enum Kind { Steam, Sparks, Discharge, Arc, Scorch }
    public sealed class Effect
    {
        public string name, anchor;
        public Kind kind;
        public Vector3 position, scale = Vector3.one;
        public Quaternion rotation = Quaternion.identity;
        public Vector3[] points; // Arc only, local to position.
        public float alpha = 1;  // Arc only: seeds the shader's strobe phase.
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Recipes. Every number that drives rendering also drives the conservative bounds below.
    sealed class ParticleRecipe
    {
        public int max; public float duration, rate, gravity, cone, radius, growth, lengthScale, velocityScale;
        public Vector2 lifetime, speed, size; public float burstInterval, burstProbability; public short burstMin, burstMax;
        public Color startA, startB; public Gradient fade;
    }
    static Gradient Fade(Color a, Color b, float inEnd, float outStart)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 1) },
            new[] { new GradientAlphaKey(inEnd > 0 ? 0 : 1, 0), new GradientAlphaKey(1, Mathf.Max(inEnd, .001f)), new GradientAlphaKey(1, outStart), new GradientAlphaKey(0, 1) });
        return g;
    }
    // Overheated hydraulics: each upper ram clamp vents a soft plume outward along the wall, beside the ring and under the lintel.
    static readonly ParticleRecipe Steam = new ParticleRecipe
    {
        max = 20, duration = 5, rate = 8, gravity = -.006f, cone = 5, radius = .04f, growth = 1.8f,
        lifetime = new Vector2(1.6f, 2.4f), speed = new Vector2(.3f, .5f), size = new Vector2(.16f, .3f),
        startA = new Color(.8f, .84f, .88f, .55f), startB = new Color(.72f, .77f, .82f, .42f), fade = Fade(Color.white, new Color(.85f, .88f, .9f), .15f, .5f),
    };
    // Hot amber drips falling from the overloaded feeder conductor boxes over the assembly. Weapon hits are red, green or blue radial bursts.
    static readonly ParticleRecipe Sparks = new ParticleRecipe
    {
        max = 24, duration = 5.2f, rate = 2, gravity = 1, cone = 12, radius = .1f, growth = 1, lengthScale = 2, velocityScale = .05f,
        lifetime = new Vector2(.45f, .7f), speed = new Vector2(.4f, 1.4f), size = new Vector2(.04f, .06f),
        burstInterval = 2.6f, burstProbability = .55f, burstMin = 5, burstMax = 9,
        startA = new Color(1, .62f, .25f, 1), startB = new Color(1, .78f, .42f, 1), fade = Fade(Color.white, new Color(1, .5f, .18f), 0, .6f),
    };
    // Rare, brief violet bolts on the assembly ring. At most one burst per 1.9 s cycle slot and never sustained strobing.
    static readonly ParticleRecipe Discharge = new ParticleRecipe
    {
        max = 4, duration = 5.7f, rate = 0, radius = 3, lifetime = new Vector2(.08f, .14f), size = new Vector2(.65f, 1.1f),
        burstInterval = 1.9f, burstProbability = .5f, burstMin = 1, burstMax = 2,
        startA = new Color(.62f, .5f, 1, 1), startB = new Color(.5f, .62f, 1, 1), fade = Fade(Color.white, Color.white, 0, .35f),
    };
    const float DischargeThickness = .09f, ArcWidth = .07f;
    static readonly Color Soot = new Color(.05f, .042f, .036f, .9f), ArcTint = new Color(.42f, .36f, 1);
    static ParticleRecipe RecipeOf(Kind kind) => kind == Kind.Steam ? Steam : kind == Kind.Sparks ? Sparks : kind == Kind.Discharge ? Discharge : null;
    public static bool IsEmitter(Kind kind) => kind == Kind.Steam || kind == Kind.Sparks || kind == Kind.Discharge;

    // ---------------------------------------------------------------------------------------------------------------
    // Placement from the accepted contract frames and the saved parent machinery.
    static Renderer Named(Section section, string root, string name)
    {
        var r = BossHallFramework.FindSection(section).Find(root);
        return r ? r.GetComponentsInChildren<MeshRenderer>(true).SingleOrDefault(m => m.name == name) : null;
    }
    /// <summary>The runway conductor box is the third box (after the east intake and its wall plate) in the env_050 feeder recipe.</summary>
    static Bounds ConductorBounds(int s)
    {
        var body = Named(Section.Overhead, BossHallServices.Roots(Section.Overhead)[s], "Overhead_Feeder_" + Sides[s]);
        var verts = body.GetComponent<MeshFilter>().sharedMesh.vertices.Skip(48).Take(24).Select(v => body.transform.TransformPoint(v)).ToArray();
        var b = new Bounds(verts[0], Vector3.zero); foreach (var v in verts) b.Encapsulate(v);
        if (Mathf.Abs(b.size.x - .8f) > .01f || Mathf.Abs(b.size.z - .6f) > .01f || b.size.y < .6f || b.size.y > .8f)
            throw new InvalidOperationException("Feeder recipe changed; runway conductor box not found for " + Sides[s] + ": " + BossHallServices.B(b));
        return b;
    }
    /// <summary>Anchors that are absent because their parent section is legitimately cleared; build requires all present.</summary>
    public static bool AnchorsPresent() =>
        Named(Section.AssemblyBackdrop, "Assembly_Ring", "Ring_Pistons") &&
        Enumerable.Range(0, 2).All(s => Named(Section.Overhead, BossHallServices.Roots(Section.Overhead)[s], "Overhead_Feeder_" + Sides[s]));

    public static List<Effect> Plan(BossHallFramework.Layout layout) => Plan(layout, null);
    static List<Effect> Plan(BossHallFramework.Layout layout, List<string> issues)
    {
        if (!AnchorsPresent()) throw new InvalidOperationException("Meltdown anchors need the saved ring pistons and overhead feeders");
        var result = new List<Effect>(); var f = BossHallBackdrop.Measure(layout);
        // The ring's stator field sits at wallX + .54; spokes reach 2 cm in front of it. Arcs float 8 cm proud of the field;
        // discharge billboards sit 4 cm proud so their half-diagonal stays inside the measured west wall band.
        var ring = new Vector3(f.wallX + .62f, f.ringY, f.axisZ);
        result.Add(new Effect { name = "Ring_Discharge", kind = Kind.Discharge, anchor = "Assembly_Backdrop/Assembly_Ring", position = ring + Vector3.left * .04f });
        // Arcs bridge four spokes through the outer field band: the upper pair clears the turret silhouette from the entry,
        // the lower pair reads from the arena flanks and galleries.
        foreach (float spoke in new[] { 30f, -30f, 150f, -150f })
        {
            var points = new Vector3[7];
            for (int i = 0; i < points.Length; i++)
            {
                float t = i / (points.Length - 1f), a = (spoke - 13 + 26 * t) * Mathf.Deg2Rad;
                float r = 2.85f + (i == 0 || i == points.Length - 1 ? 0 : (i % 2 == 0 ? .11f : -.09f) * (Mathf.Abs(spoke) < 90 == spoke > 0 ? 1 : -1));
                points[i] = new Vector3(0, Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            }
            result.Add(new Effect { name = "Ring_Arc_" + (Mathf.Abs(spoke) < 90 ? "Upper_" : "Lower_") + (spoke > 0 ? "North" : "South"), kind = Kind.Arc,
                anchor = "Assembly_Backdrop/Assembly_Ring", position = ring, points = points, alpha = new[] { 1, .88f, .94f, .82f }[result.Count - 1] });
        }
        for (int s = 0; s < 2; s++)
        {
            string tag = Sides[s];
            // The env_049 ram clamp (ringOuter + .55 at 50 degrees either side of vertical); plume leaves its outer face along the wall.
            float side = s == 0 ? -1 : 1;
            var clamp = new Vector3(ring.x, f.ringY, f.axisZ) + Quaternion.AngleAxis(side * 50f, Vector3.right) * Vector3.up * (f.ringOuter + .55f);
            result.Add(new Effect { name = "Clamp_Steam_" + tag, kind = Kind.Steam, anchor = "Assembly_Backdrop/Assembly_Ring/Ring_Pistons",
                position = new Vector3(f.wallX + .6f, clamp.y, clamp.z + side * .25f),
                rotation = Quaternion.LookRotation(new Vector3(0, Mathf.Sin(15 * Mathf.Deg2Rad), side * Mathf.Cos(15 * Mathf.Deg2Rad))) });
            var box = ConductorBounds(s); string feeder = "Overhead/" + BossHallServices.Roots(Section.Overhead)[s];
            // The feeders deliver Room 3 power to the gantry runway over the assembly; their conductor boxes drip sparks.
            result.Add(new Effect { name = "Runway_Sparks_" + tag, kind = Kind.Sparks, anchor = feeder,
                position = new Vector3(box.center.x, box.min.y - .02f, box.center.z), rotation = Quaternion.LookRotation(Vector3.down, Vector3.left) });
            // Soot on the outer (room-facing) end of each conductor box, 12 mm proud of the face. The quad is visible from -Z.
            result.Add(new Effect { name = "Runway_Scorch_" + tag, kind = Kind.Scorch, anchor = feeder,
                position = new Vector3(box.center.x, box.min.y + .2f, s == 0 ? box.min.z - .012f : box.max.z + .012f),
                rotation = Quaternion.Euler(0, s == 0 ? 0 : 180, 0), scale = new Vector3(.62f, .34f, 1) });
        }
        foreach (var e in result)
        {
            string issue = layout.KeepOutViolation(EffectBounds(e)); if (issue == null) continue;
            if (issues == null) throw new InvalidOperationException(e.name + " effect envelope enters " + issue);
            issues.Add(e.name + " envelope " + BossHallServices.B(EffectBounds(e)) + " enters " + issue);
        }
        return result;
    }

    /// <summary>Conservative world envelope of everything the effect can ever draw, from its recipe (not from simulated state).</summary>
    public static Bounds EffectBounds(Effect e)
    {
        if (e.kind == Kind.Arc)
        {
            var b = new Bounds(e.position + e.rotation * e.points[0], Vector3.zero);
            foreach (var p in e.points) b.Encapsulate(e.position + e.rotation * p); b.Expand(ArcWidth); return b;
        }
        if (e.kind == Kind.Scorch)
        {
            var b = new Bounds(e.position, Vector3.zero);
            foreach (var c in new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, -.5f, 0) })
                b.Encapsulate(e.position + e.rotation * Vector3.Scale(c, e.scale));
            return b;
        }
        var recipe = RecipeOf(e.kind);
        if (e.kind == Kind.Discharge)
        {
            // Static billboards on the ring circle in the world YZ plane; a camera-facing quad may turn its half-diagonal any way.
            var b = new Bounds(e.position, new Vector3(0, 2 * recipe.radius, 2 * recipe.radius));
            b.Expand(2 * .5f * Mathf.Sqrt(recipe.size.y * recipe.size.y + DischargeThickness * DischargeThickness)); return b;
        }
        var g = Physics.gravity * recipe.gravity; var result = new Bounds(e.position, Vector3.zero);
        for (int k = 0; k < 16; k++)
            foreach (float polar in new[] { 0f, recipe.cone })
            {
                var dir = e.rotation * (Quaternion.AngleAxis(k * 22.5f, Vector3.forward) * Quaternion.AngleAxis(polar, Vector3.right) * Vector3.forward);
                for (float t = 0; t <= recipe.lifetime.y + .001f; t += .05f)
                    foreach (float s in new[] { 0f, recipe.speed.y })
                    {
                        Vector3 position = e.position + dir * s * t + .5f * g * t * t, v = dir * s + g * t;
                        result.Encapsulate(position);
                        // A stretched streak lies along its velocity; include it either side of the particle.
                        var streak = v * recipe.velocityScale + v.normalized * recipe.size.y * recipe.lengthScale;
                        result.Encapsulate(position + streak); result.Encapsulate(position - streak);
                    }
            }
        result.Expand(2 * (recipe.radius + .71f * recipe.size.y * recipe.growth)); return result;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Owned assets: flattened copies of the Workshop steam material and the Room 3 arc material; first-principles soot quad.
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return; int slash = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
    static Material ComposeParticleMaterial()
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(SteamSource) ?? throw new InvalidOperationException("Missing " + SteamSource);
        // URP Particles/Unlit, alpha blended; tinted per emitter/decal by vertex colour.
        var m = new Material(source) { name = "M_Meltdown_Particle" }; m.SetTexture("_BaseMap", Load<Texture2D>(PuffTexturePath)); return m;
    }
    static Material ComposeArcMaterial()
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(ArcSource) ?? throw new InvalidOperationException("Missing " + ArcSource);
        if (!source.shader || source.shader.name != "CoreBuster/Electric Arc" || ShaderUtil.ShaderHasError(source.shader)) throw new InvalidOperationException("Unavailable Electric Arc shader");
        var m = new Material(source) { name = "M_Meltdown_Arc" };
        // Neutral glow: arcs/discharges are tinted violet and sparks amber by vertex colour, over the same white-hot core.
        // Gentler strobe than Room 3 (0.38) for comfortable flicker.
        m.SetColor("_Color", Color.white); m.SetFloat("_Intensity", 4); m.SetFloat("_Flicker", .3f); m.SetFloat("_HotCore", 1.1f);
        return m;
    }
    static Mesh ComposeScorch()
    {
        var mesh = new Mesh { name = "SM_Meltdown_Scorch" };
        mesh.SetVertices(new List<Vector3> { new Vector3(-.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0), new Vector3(.5f, -.5f, 0) });
        mesh.SetUVs(0, new List<Vector2> { Vector2.zero, Vector2.up, Vector2.one, Vector2.right });
        mesh.SetColors(Enumerable.Repeat(Soot, 4).ToList()); mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0); // Visible from -Z.
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }
    /// <summary>Steam.png with only its alpha scaled; deterministic PNG bytes.</summary>
    static byte[] PuffBytes()
    {
        var source = new Texture2D(2, 2); var result = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!source.LoadImage(File.ReadAllBytes(SteamTexture))) throw new InvalidOperationException("Unreadable " + SteamTexture);
            var pixels = source.GetPixels32();
            for (int i = 0; i < pixels.Length; i++) pixels[i].a = (byte)Mathf.Min(255, Mathf.RoundToInt(pixels[i].a * PuffAlphaGain));
            result.Reinitialize(source.width, source.height); result.SetPixels32(pixels); result.Apply(); return result.EncodeToPNG();
        }
        finally { Object.DestroyImmediate(source); Object.DestroyImmediate(result); }
    }
    static void BuildTexture()
    {
        var bytes = PuffBytes();
        if (!File.Exists(PuffTexturePath) || !File.ReadAllBytes(PuffTexturePath).SequenceEqual(bytes))
        { File.WriteAllBytes(PuffTexturePath, bytes); AssetDatabase.ImportAsset(PuffTexturePath, ImportAssetOptions.ForceUpdate); }
        var source = (TextureImporter)AssetImporter.GetAtPath(SteamTexture); var importer = (TextureImporter)AssetImporter.GetAtPath(PuffTexturePath);
        if (importer.textureType != source.textureType || importer.alphaIsTransparency != source.alphaIsTransparency || importer.wrapMode != source.wrapMode ||
            importer.filterMode != source.filterMode || importer.mipmapEnabled != source.mipmapEnabled || importer.sRGBTexture != source.sRGBTexture ||
            importer.textureCompression != source.textureCompression || importer.maxTextureSize != source.maxTextureSize)
        {
            importer.textureType = source.textureType; importer.alphaIsTransparency = source.alphaIsTransparency; importer.wrapMode = source.wrapMode;
            importer.filterMode = source.filterMode; importer.mipmapEnabled = source.mipmapEnabled; importer.sRGBTexture = source.sRGBTexture;
            importer.textureCompression = source.textureCompression; importer.maxTextureSize = source.maxTextureSize; importer.SaveAndReimport();
        }
    }
    public static void BuildAssets()
    {
        EnsureFolder(Folder + "/Materials"); EnsureFolder(Folder + "/Meshes"); EnsureFolder(Folder + "/Textures"); BuildTexture();
        Store(ComposeParticleMaterial(), ParticleMaterialPath); Store(ComposeArcMaterial(), ArcMaterialPath); Store(ComposeScorch(), ScorchMeshPath);
        AssetDatabase.SaveAssets();
    }
    static void Store(Object generated, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Object>(path);
        if (existing) { EditorUtility.CopySerialized(generated, existing); Object.DestroyImmediate(generated); EditorUtility.SetDirty(existing); }
        else AssetDatabase.CreateAsset(generated, path);
    }
    static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Prepare meltdown assets first: " + path);

    // ---------------------------------------------------------------------------------------------------------------
    // Composition. The same function builds saved effects and in-memory validation references.
    static GameObject Compose(Effect e, Transform parent, bool undo)
    {
        var go = new GameObject(e.name);
        if (undo) Undo.RegisterCreatedObjectUndo(go, "Build Boss Hall meltdown VFX");
        else go.hideFlags = HideFlags.HideAndDontSave;
        if (parent) go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(e.position, e.rotation); go.transform.localScale = e.scale;
        if (IsEmitter(e.kind)) ComposeParticles(go, e);
        else if (e.kind == Kind.Arc)
        {
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false; line.loop = false; line.positionCount = e.points.Length; line.SetPositions(e.points);
            line.widthMultiplier = ArcWidth; line.numCapVertices = 0; line.numCornerVertices = 0; line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch; line.startColor = line.endColor = new Color(ArcTint.r, ArcTint.g, ArcTint.b, e.alpha);
            line.generateLightingData = false; Quiet(line, Load<Material>(ArcMaterialPath));
        }
        else
        {
            go.AddComponent<MeshFilter>().sharedMesh = Load<Mesh>(ScorchMeshPath);
            Quiet(go.AddComponent<MeshRenderer>(), Load<Material>(ParticleMaterialPath));
        }
        return go;
    }
    static void Quiet(Renderer r, Material material)
    {
        r.sharedMaterials = new[] { material }; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off; r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        r.allowOcclusionWhenDynamic = true; r.staticShadowCaster = false;
    }
    static void ComposeParticles(GameObject go, Effect e)
    {
        var recipe = RecipeOf(e.kind); var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = recipe.duration; main.loop = true; main.prewarm = false; main.playOnAwake = true; main.useUnscaledTime = false; // Pauses with Time.timeScale.
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Local; main.simulationSpeed = 1;
        main.maxParticles = recipe.max; main.startDelay = 0; main.startLifetime = new ParticleSystem.MinMaxCurve(recipe.lifetime.x, recipe.lifetime.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(recipe.speed.x, recipe.speed.y); main.gravityModifier = recipe.gravity;
        main.startColor = new ParticleSystem.MinMaxGradient(recipe.startA, recipe.startB); main.stopAction = ParticleSystemStopAction.None;
        main.cullingMode = ParticleSystemCullingMode.Automatic; main.ringBufferMode = ParticleSystemRingBufferMode.Disabled; main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
        main.startRotation = new ParticleSystem.MinMaxCurve(0, 2 * Mathf.PI); main.flipRotation = 0;
        if (e.kind == Kind.Discharge)
        {
            main.startSize3D = true; main.startSizeX = new ParticleSystem.MinMaxCurve(recipe.size.x, recipe.size.y);
            main.startSizeY = DischargeThickness; main.startSizeZ = 1;
        }
        else { main.startSize3D = false; main.startSize = new ParticleSystem.MinMaxCurve(recipe.size.x, recipe.size.y); }
        var emission = ps.emission; emission.enabled = true; emission.rateOverTime = recipe.rate; emission.rateOverDistance = 0;
        emission.SetBursts(recipe.burstMax == 0 ? new ParticleSystem.Burst[0] :
            new[] { new ParticleSystem.Burst(0, recipe.burstMin, recipe.burstMax, 0, recipe.burstInterval) { probability = recipe.burstProbability } });
        var shape = ps.shape; shape.enabled = true; shape.position = Vector3.zero; shape.scale = Vector3.one; shape.randomDirectionAmount = 0;
        shape.sphericalDirectionAmount = 0; shape.randomPositionAmount = 0; shape.alignToDirection = false; shape.arc = 360; shape.arcMode = ParticleSystemShapeMultiModeValue.Random;
        if (e.kind == Kind.Discharge)
        { shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = recipe.radius; shape.radiusThickness = 0; shape.rotation = new Vector3(0, 90, 0); } // XY circle → world YZ ring plane.
        else
        { shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = recipe.cone; shape.radius = recipe.radius; shape.radiusThickness = 1; shape.rotation = Vector3.zero; }
        var colour = ps.colorOverLifetime; colour.enabled = true; colour.color = new ParticleSystem.MinMaxGradient(recipe.fade);
        var size = ps.sizeOverLifetime; size.enabled = e.kind == Kind.Steam;
        if (size.enabled) size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, 1 / recipe.growth), new Keyframe(1, 1)));
        var spin = ps.rotationOverLifetime; spin.enabled = e.kind == Kind.Steam;
        if (spin.enabled) spin.z = new ParticleSystem.MinMaxCurve(-.25f, .25f);
        // Explicitly inert: no collision/trigger messages, light spawning, sub-emitters, force fields, trails or noise.
        var collision = ps.collision; collision.enabled = false; var trigger = ps.trigger; trigger.enabled = false;
        var lights = ps.lights; lights.enabled = false; var sub = ps.subEmitters; sub.enabled = false;
        var forces = ps.externalForces; forces.enabled = false; var trails = ps.trails; trails.enabled = false; var noise = ps.noise; noise.enabled = false;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = e.kind == Kind.Sparks ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        r.lengthScale = e.kind == Kind.Sparks ? recipe.lengthScale : 2; r.velocityScale = e.kind == Kind.Sparks ? recipe.velocityScale : 0; r.cameraVelocityScale = 0;
        r.alignment = ParticleSystemRenderSpace.View; r.sortMode = ParticleSystemSortMode.None; r.minParticleSize = 0; r.maxParticleSize = .5f;
        r.enableGPUInstancing = true; r.trailMaterial = null;
        Quiet(r, Load<Material>(e.kind == Kind.Steam ? ParticleMaterialPath : ArcMaterialPath));
    }

    // ---------------------------------------------------------------------------------------------------------------
    public static ParticleSystem[] Emitters() => BossHallFramework.FindSection(Section.MeltdownVfx).GetComponentsInChildren<ParticleSystem>(true);
    public static Renderer[] Renderers() => BossHallFramework.FindSection(Section.MeltdownVfx).GetComponentsInChildren<Renderer>(true);

    public static void Rebuild(BossHallFramework.Layout layout, string owner = Owner)
    {
        var plan = Plan(layout);
        BossHallFramework.RebuildSection(layout, Section.MeltdownVfx, owner, section => { foreach (var e in plan) Compose(e, section, true); });
    }

    static readonly HashSet<string> Volatile = new HashSet<string>(StringComparer.Ordinal)
        { "m_GameObject", "m_ObjectHideFlags", "randomSeed", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_EditorHideFlags", "m_Script" };
    static string State(Object o)
    {
        var lines = new List<string>(); var it = new SerializedObject(o).GetIterator(); bool enter = true;
        while (it.Next(enter))
        {
            enter = true;
            if (Volatile.Contains(it.name)) { enter = false; continue; }
            switch (it.propertyType)
            {
                case SerializedPropertyType.AnimationCurve:
                    lines.Add(it.propertyPath + "=" + string.Join(";", it.animationCurveValue.keys.Select(k => FormattableString.Invariant($"{k.time:R},{k.value:R},{k.inTangent:R},{k.outTangent:R}"))));
                    enter = false; break;
                case SerializedPropertyType.Gradient:
                    var g = it.gradientValue; enter = false;
                    lines.Add(it.propertyPath + "=" + g.mode + "|" + string.Join(";", g.colorKeys.Select(k => k.time.ToString("R") + "," + k.color.ToString("R"))) +
                        "|" + string.Join(";", g.alphaKeys.Select(k => k.time.ToString("R") + "," + k.alpha.ToString("R")))); break;
                case SerializedPropertyType.ObjectReference: lines.Add(it.propertyPath + "=" + (it.objectReferenceValue ? AssetDatabase.GetAssetPath(it.objectReferenceValue) + ":" + it.objectReferenceValue.name : "null")); break;
                case SerializedPropertyType.Float: lines.Add(it.propertyPath + "=" + it.floatValue.ToString("R", CultureInfo.InvariantCulture)); break;
                case SerializedPropertyType.Integer: case SerializedPropertyType.Enum: case SerializedPropertyType.LayerMask: case SerializedPropertyType.ArraySize:
                    lines.Add(it.propertyPath + "=" + it.longValue); break;
                case SerializedPropertyType.Boolean: lines.Add(it.propertyPath + "=" + it.boolValue); break;
                case SerializedPropertyType.String: lines.Add(it.propertyPath + "=" + it.stringValue); break;
                case SerializedPropertyType.Color: lines.Add(it.propertyPath + "=" + it.colorValue.ToString("R")); break;
                case SerializedPropertyType.Vector3: lines.Add(it.propertyPath + "=" + it.vector3Value.ToString("R")); break;
                case SerializedPropertyType.Vector4: lines.Add(it.propertyPath + "=" + it.vector4Value.ToString("R")); break;
                case SerializedPropertyType.Quaternion: lines.Add(it.propertyPath + "=" + it.quaternionValue.ToString("R")); break;
            }
        }
        return string.Join("\n", lines);
    }
    static string FirstDifference(string a, string b)
    {
        var x = a.Split('\n'); var y = b.Split('\n');
        for (int i = 0; i < Mathf.Max(x.Length, y.Length); i++)
            if (i >= x.Length || i >= y.Length || x[i] != y[i]) return (i < x.Length ? x[i] : "<end>") + " vs " + (i < y.Length ? y[i] : "<end>");
        return null;
    }

    /// <summary>Framework hook. Empty installation is allowed; a populated section must match the recipe exactly.</summary>
    public static void ValidateInstalled(BossHallFramework.Layout layout)
    {
        var section = BossHallFramework.FindSection(Section.MeltdownVfx); if (section.childCount == 0) return;
        // A parent machinery clear inside its own Undo transaction temporarily removes the anchors; installed recipes are still checked.
        bool anchored = AnchorsPresent(); var plan = anchored ? Plan(layout) : null;
        var emitters = section.GetComponentsInChildren<ParticleSystem>(true);
        if (emitters.Length > EmitterCap) throw new InvalidOperationException("Meltdown exceeds the initial emitter target");
        if (section.GetComponentsInChildren<Component>(true).Any(c => !(c is Transform || c is ParticleSystem || c is ParticleSystemRenderer || c is LineRenderer || c is MeshFilter || c is MeshRenderer)))
            throw new InvalidOperationException("Meltdown may contain only particle/line/mesh rendering components");
        if (anchored && (section.childCount != plan.Count || plan.Any(e => !section.Find(e.name)))) throw new InvalidOperationException("Missing, extra or renamed meltdown effect");
        foreach (Transform t in section)
        {
            if (t.childCount != 0 || !t.gameObject.activeSelf || t.gameObject.layer != 0 || t.gameObject.tag != "Untagged" || GameObjectUtility.GetStaticEditorFlags(t.gameObject) != 0)
                throw new InvalidOperationException("Meltdown effect must be a single active, Default, Untagged, non-static object: " + t.name);
            if (!anchored) continue;
            var e = plan.Single(p => p.name == t.name);
            if (Vector3.Distance(t.position, e.position) > .0001f || Quaternion.Angle(t.rotation, e.rotation) > .01f || Vector3.Distance(t.lossyScale, e.scale) > .0001f)
                throw new InvalidOperationException("Meltdown effect moved from its measured anchor: " + t.name);
            var reference = Compose(e, null, false);
            try
            {
                var actual = t.GetComponents<Component>().Where(c => !(c is Transform)).ToArray();
                var expected = reference.GetComponents<Component>().Where(c => !(c is Transform)).ToArray();
                if (actual.Length != expected.Length || actual.Zip(expected, (a, b) => a.GetType() != b.GetType()).Any(x => x))
                    throw new InvalidOperationException("Meltdown component set differs: " + t.name);
                for (int i = 0; i < actual.Length; i++)
                {
                    string diff = FirstDifference(State(actual[i]), State(expected[i]));
                    if (diff != null) throw new InvalidOperationException("Meltdown recipe differs at " + t.name + "/" + actual[i].GetType().Name + ": " + diff);
                }
            }
            finally { Object.DestroyImmediate(reference); }
        }
        foreach (var r in section.GetComponentsInChildren<Renderer>(true))
            if (!r.enabled || r.sharedMaterials.Length != 1 || r.sharedMaterial.parent || !new[] { ParticleMaterialPath, ArcMaterialPath }.Contains(AssetDatabase.GetAssetPath(r.sharedMaterial)))
                throw new InvalidOperationException("Meltdown renderer must use one owned material: " + r.name);
        foreach (var path in new[] { ParticleMaterialPath, ArcMaterialPath })
        {
            var saved = AssetDatabase.LoadAssetAtPath<Material>(path); var expected = path == ArcMaterialPath ? ComposeArcMaterial() : ComposeParticleMaterial();
            try
            {
                if (!saved || EditorUtility.IsDirty(saved)) throw new InvalidOperationException("Missing or unsaved meltdown material " + path);
                string diff = FirstDifference(State(saved).Replace("M_Meltdown", ""), State(expected).Replace("M_Meltdown", ""));
                if (diff != null) throw new InvalidOperationException("Meltdown material differs from recipe: " + diff);
            }
            finally { Object.DestroyImmediate(expected); }
        }
        if (!File.Exists(PuffTexturePath) || !File.ReadAllBytes(PuffTexturePath).SequenceEqual(PuffBytes())) throw new InvalidOperationException("Puff texture differs from its Steam.png derivation");
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ScorchMeshPath); var recipe = ComposeScorch();
        try
        {
            if (!mesh || EditorUtility.IsDirty(mesh) || !mesh.vertices.SequenceEqual(recipe.vertices) || !mesh.triangles.SequenceEqual(recipe.triangles) ||
                !mesh.uv.SequenceEqual(recipe.uv) || !mesh.colors.SequenceEqual(recipe.colors)) throw new InvalidOperationException("Scorch mesh differs from recipe");
        }
        finally { Object.DestroyImmediate(recipe); }
    }
    /// <summary>Framework hook: recipe envelope instead of transient simulated renderer bounds.</summary>
    public static Bounds RendererEnvelope(Renderer r, BossHallFramework.Layout layout)
    {
        if (!(r is ParticleSystemRenderer)) return r.bounds;
        if (!AnchorsPresent()) return new Bounds(r.transform.position, Vector3.zero);
        return EffectBounds(Plan(layout).SingleOrDefault(e => e.name == r.name) ?? throw new InvalidOperationException("Unplanned meltdown emitter " + r.name));
    }

    /// <summary>Reserved warning-flicker bindings for corebuster_env_055 (Lighting). No light or script is created here.</summary>
    public static IEnumerable<(string name, Vector3 position, Color colour, string binding)> FlickerReservations(BossHallFramework.Layout layout)
    {
        foreach (var e in Plan(layout).Where(e => e.kind == Kind.Discharge || e.kind == Kind.Sparks))
            yield return e.kind == Kind.Discharge
                ? ("Warning_Flicker_Ring", e.position + Vector3.right * 1.5f, new Color(.55f, .45f, 1),
                    "ReactorInstabilityLight on a Lighting-section light, field = Energy/M_Energy_Contained_Field (instability 0.3, <= 2 flashes/s)")
                : ("Warning_Flicker_" + e.name.Substring("Runway_Sparks_".Length), e.position + Vector3.down, new Color(1, .6f, .22f),
                    "WorkshopAmbientMotion lamp pattern (scaled time, 0.45-1.0 factor) on a Lighting-section amber practical");
    }

    public static string Validate(BossHallFramework.Layout layout)
    {
        BossHallEnergy.Validate(layout); // Includes the framework, every parent section and the contained energy.
        var plan = Plan(layout); var section = BossHallFramework.FindSection(Section.MeltdownVfx);
        if (section.childCount != plan.Count) throw new InvalidOperationException("Meltdown effects are not installed");
        ValidateInstalled(layout);
        var head = (layout.Report.turret.head.min + layout.Report.turret.head.max) * .5f; float d = Vector3.Distance(layout.Report.entryEye, head);
        var ray = new Ray(layout.Report.entryEye, (head - layout.Report.entryEye) / d);
        foreach (var e in plan) if (EffectBounds(e).IntersectRay(ray, out float hit) && hit >= 0 && hit < d) throw new InvalidOperationException(e.name + " crosses the entry-to-turret sight line");
        int mats = BossHallFramework.FindRoot().GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().Count();
        int emitters = Emitters().Length, cap = plan.Where(e => IsEmitter(e.kind)).Sum(e => RecipeOf(e.kind).max);
        return $"BOSS HALL MELTDOWN VFX: {plan.Count} effects ({emitters}/{EmitterCap} emitters, {plan.Count(e => e.kind == Kind.Arc)} script-free arcs, {plan.Count(e => e.kind == Kind.Scorch)} scorch decals), " +
            $"live-particle hard cap {cap}, 2 owned materials, {mats}/20 hall materials. Only Transform/ParticleSystem/renderers; collision/trigger/light/sub-emitter modules off; " +
            "scaled time; no script, collider, light, audio, damage or gameplay state. Entry-to-turret sight line clear.";
    }
    public static string PlanReport(BossHallFramework.Layout layout)
    {
        string V(Vector3 v) => FormattableString.Invariant($"({v.x:F3}, {v.y:F3}, {v.z:F3})");
        var lines = Plan(layout).Select(e =>
        {
            var r = RecipeOf(e.kind); string caps = r == null ? "" : FormattableString.Invariant($"; max {r.max}, life {r.lifetime.x:F2}-{r.lifetime.y:F2} s, rate {r.rate}/s") +
                (r.burstMax > 0 ? FormattableString.Invariant($", bursts {r.burstMin}-{r.burstMax} p{r.burstProbability:F2} every {r.burstInterval:F1} s") : "");
            return $"{e.kind} {e.name} on {e.anchor} at {V(e.position)}; envelope {BossHallServices.B(EffectBounds(e))}{caps}";
        }).ToList();
        lines.Add("Reserved env_055 warning-flicker bindings (no light created):");
        lines.AddRange(FlickerReservations(layout).Select(f => $"  {f.name} at {V(f.position)}: {f.binding}"));
        return string.Join("\n", lines);
    }

    [MenuItem("Tools/CoreBuster/Build Boss Hall Meltdown VFX")]
    public static void BuildMenu()
    { var layout = ReplaceOutdated(BossHallFramework.MeasureLayout()); BossHallEnergy.Validate(layout); BuildAssets(); Rebuild(layout); Debug.Log(Validate(layout)); }

    /// <summary>
    /// An installation from an earlier recipe fails framework validation by design. Remove only this task's own effects, save that
    /// cleared state (parent validators measure the original geometry on a clean saved scene) and prove nothing else changed.
    /// </summary>
    static BossHallFramework.Layout ReplaceOutdated(BossHallFramework.Layout layout)
    {
        var section = BossHallFramework.FindSection(Section.MeltdownVfx); if (section.childCount == 0) return layout;
        var before = CaptureProtected();
        foreach (Transform t in section.Cast<Transform>().ToArray()) Undo.DestroyObjectImmediate(t.gameObject);
        BossHallSnapshot.Compare(before, CaptureProtected(), "Scene outside Meltdown_VFX while clearing it");
        if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new IOException("MainScene save failed.");
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); layout = BossHallFramework.MeasureLayout();
        BossHallSnapshot.Compare(before, CaptureProtected(), "Scene outside Meltdown_VFX after saving the cleared section");
        Debug.Log("ENV054: replaced an outdated meltdown installation; cleared section saved, all other scene state unchanged");
        return layout;
    }
    [MenuItem("Tools/CoreBuster/Validate Boss Hall Meltdown VFX")]
    public static void ValidateMenu() => Debug.Log(Validate(BossHallFramework.MeasureLayout()));

    public static void PreflightBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); var layout = BossHallFramework.MeasureLayout();
        string installed = "";
        try { BossHallEnergy.Validate(layout); }
        catch (InvalidOperationException ex) when (BossHallFramework.FindSection(Section.MeltdownVfx).childCount != 0)
        { installed = "Installed meltdown differs from the current recipe; the build replaces it: " + ex.Message + "\n"; }
        var f = BossHallBackdrop.Measure(layout); var issues = new List<string>(); Plan(layout, issues);
        string anchors = "Ring_Pistons " + BossHallServices.B(Named(Section.AssemblyBackdrop, "Assembly_Ring", "Ring_Pistons").bounds) + "\n" +
            string.Join("\n", Enumerable.Range(0, 2).Select(s => "Runway conductor box " + Sides[s] + " " + BossHallServices.B(ConductorBounds(s))));
        string report = "READ-ONLY ENV054 PREFLIGHT\nScene: " + layout.sceneSha + "\n" + installed.TrimEnd('\n') +
            FormattableString.Invariant($"\nRing centre Y {f.ringY:F3}, lintel underside {f.lintelBottom:F3}, upper-deck volume from Y 16.68; room {BossHallServices.B(layout.interior)}\n") +
            anchors + "\n" + (issues.Count == 0 ? PlanReport(layout) : "KEEP-OUT ISSUES:\n" + string.Join("\n", issues));
        if (SceneManager.GetActiveScene().isDirty || layout.sceneSha != BossHallSurvey.Hash(BossHallSurvey.ScenePath)) throw new InvalidOperationException("Preflight changed scene");
        File.WriteAllText("Docs/BossHall/MeltdownPreflight_054.txt", report + "\n"); Debug.Log(report);
    }
    static bool Owned(string path) { path = path.Replace('\\', '/'); return path == Folder + ".meta" || path.StartsWith(Folder + "/", StringComparison.Ordinal); }
    static Dictionary<string, string> AssetHashes() => Directory.GetFiles(Folder, "*", SearchOption.AllDirectories).ToDictionary(p => p, BossHallSurvey.Hash);
    static Dictionary<string, string> CaptureProtected() =>
        BossHallSnapshot.Capture(SceneManager.GetActiveScene(), BossHallFramework.FindSection(Section.MeltdownVfx));
    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use exclusive batch access");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); var layout = BossHallFramework.MeasureLayout();
        layout = ReplaceOutdated(layout); BossHallEnergy.Validate(layout);
        var before = CaptureProtected(); var shared = BossHallSnapshot.CaptureAssets();
        var others = Directory.GetFiles(BossHallFramework.AssetRoot, "*", SearchOption.AllDirectories)
            .Where(p => !Owned(p)).ToDictionary(p => p, BossHallSurvey.Hash);
        BuildAssets(); var hashes = AssetHashes(); BuildAssets(); BossHallSnapshot.Compare(hashes, AssetHashes(), "Regenerated meltdown assets/meta");
        Rebuild(layout); Validate(layout); string simulated = SimulationProbe(layout); NegativeGuards(layout);
        // Parent machinery rebuilds leave the measured anchors, and therefore every effect, exactly in place.
        foreach (var s in new[] { Section.AssemblyBackdrop, Section.Overhead }) // The services builder rebuilds power, cooling and overhead together.
        {
            Undo.IncrementCurrentGroup(); int parentGroup = Undo.GetCurrentGroup();
            if (s == Section.AssemblyBackdrop) BossHallBackdrop.RebuildForEnergyVerification(layout); else BossHallServices.RebuildForEnergyVerification(layout);
            Validate(layout); Undo.RevertAllDownToGroup(parentGroup); Validate(layout);
        }
        BossHallSnapshot.Compare(before, CaptureProtected(), "Protected scene state after meltdown build/Undo");
        BossHallSnapshot.Compare(shared, BossHallSnapshot.CaptureAssets(), "Protected shared entries");
        BossHallSnapshot.Compare(others, Directory.GetFiles(BossHallFramework.AssetRoot, "*", SearchOption.AllDirectories)
            .Where(p => !Owned(p)).ToDictionary(p => p, BossHallSurvey.Hash), "Other Boss Hall assets/meta");
        layout = BossHallFramework.SaveAndReload(layout); string report = Validate(layout);
        BossHallSnapshot.Compare(before, CaptureProtected(), "Protected scene state after meltdown save/reload");
        File.WriteAllText("Docs/BossHall/Meltdown_054.txt", report + "\nSaved scene: " + layout.sceneSha + "\nProtected scene states: " + before.Count +
            "; shared entries: " + shared.Count + ". Byte-identical regenerated assets/meta; parent rebuild/Undo; negative guards; save/reload pass.\n" +
            simulated + "\n" + PlanReport(layout) + "\n" + BossHallFramework.BudgetReport() + "\n");
        Debug.Log(report);
    }

    /// <summary>Edit-mode simulation of every emitter for 30 s: live counts and every particle stay inside the recipe envelope.</summary>
    public static string SimulationProbe(BossHallFramework.Layout layout)
    {
        var plan = Plan(layout); var lines = new List<string>(); int peakTotal = 0; var totals = new int[301];
        foreach (var ps in Emitters())
        {
            var e = plan.Single(p => p.name == ps.name); var envelope = EffectBounds(e); var buffer = new ParticleSystem.Particle[ps.main.maxParticles];
            int peak = 0; ps.Simulate(0, false, true, true);
            for (int step = 1; step <= 300; step++)
            {
                ps.Simulate(.1f, false, false, true); int n = ps.GetParticles(buffer); peak = Mathf.Max(peak, n); totals[step] += n;
                for (int i = 0; i < n; i++)
                {
                    // Billboards use only X/Y size; Z is the unused third start-size axis.
                    var p = buffer[i]; var size = p.GetCurrentSize3D(ps); float half = .5f * Mathf.Max(size.x, size.y);
                    var b = new Bounds(p.position, Vector3.one * 2 * half);
                    if (!envelope.Contains(b.min) || !envelope.Contains(b.max))
                        throw new InvalidOperationException(ps.name + " particle left its envelope at " + p.position.ToString("F3") + " size " + half.ToString("F3") + "; envelope " + BossHallServices.B(envelope));
                }
            }
            ps.Clear(true); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            lines.Add($"{ps.name}: peak {peak}/{ps.main.maxParticles} live, all positions inside envelope over 30 s");
        }
        peakTotal = totals.Max();
        lines.Add($"Combined edit-mode simulated peak {peakTotal} live particles (hard cap {plan.Where(e => IsEmitter(e.kind)).Sum(e => RecipeOf(e.kind).max)}).");
        return string.Join("\n", lines);
    }

    static void NegativeGuards(BossHallFramework.Layout layout)
    {
        void Reject(Action action, string label)
        { try { action(); } catch (InvalidOperationException) { Debug.Log("PASS: meltdown guard " + label); return; } throw new InvalidOperationException("Accepted invalid meltdown: " + label); }
        Reject(() => Rebuild(layout, "corebuster_env_055"), "wrong owner");
        var steam = Emitters().First(p => p.name.StartsWith("Clamp_Steam", StringComparison.Ordinal)); var t = steam.transform; var position = t.position;
        try { t.position += Vector3.right * 2; Reject(() => BossHallFramework.Validate(layout), "moved emitter"); } finally { t.position = position; }
        var collider = t.gameObject.AddComponent<SphereCollider>();
        try { Reject(() => BossHallFramework.Validate(layout), "collider"); } finally { Object.DestroyImmediate(collider); }
        var health = t.gameObject.AddComponent<Unity.FPS.Game.Health>();
        try { Reject(() => BossHallFramework.Validate(layout), "gameplay health"); } finally { Object.DestroyImmediate(health); }
        var light = t.gameObject.AddComponent<Light>();
        try { Reject(() => BossHallFramework.Validate(layout), "light in Meltdown_VFX"); } finally { Object.DestroyImmediate(light); }
        void Module(string label, Action<ParticleSystem, bool> toggle)
        { try { toggle(steam, true); Reject(() => BossHallFramework.Validate(layout), label); } finally { toggle(steam, false); } }
        Module("collision module", (p, on) => { var m = p.collision; m.enabled = on; });
        Module("trigger module", (p, on) => { var m = p.trigger; m.enabled = on; });
        Module("lights module", (p, on) => { var m = p.lights; m.enabled = on; });
        Module("unscaled time", (p, on) => { var m = p.main; m.useUnscaledTime = on; });
        Module("raised particle cap", (p, on) => { var m = p.main; m.maxParticles = on ? 200 : Steam.max; });
        var r = steam.GetComponent<ParticleSystemRenderer>(); var material = r.sharedMaterial;
        try { r.sharedMaterials = new[] { AssetDatabase.LoadAssetAtPath<Material>(SteamSource) }; Reject(() => BossHallFramework.Validate(layout), "shared Workshop material"); }
        finally { r.sharedMaterials = new[] { material }; }
        var extras = new List<GameObject>();
        try
        {
            for (int i = 0; i < 2; i++) { var g = Object.Instantiate(steam.gameObject, BossHallFramework.FindSection(Section.MeltdownVfx)); g.name = "Extra_" + i; extras.Add(g); }
            Reject(() => BossHallFramework.Validate(layout), "seven emitters");
        }
        finally { foreach (var g in extras) Object.DestroyImmediate(g); }
        var stray = Object.Instantiate(steam.gameObject, BossHallFramework.FindSection(Section.CoolingServices).GetChild(0));
        try { Reject(() => BossHallFramework.Validate(layout), "emitter outside Meltdown_VFX"); } finally { Object.DestroyImmediate(stray); }
        Physics.SyncTransforms(); Validate(layout);
    }
    public static void ValidateBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); var layout = BossHallFramework.MeasureLayout();
        string report = Validate(layout); if (SceneManager.GetActiveScene().isDirty || layout.sceneSha != BossHallSurvey.Hash(BossHallSurvey.ScenePath)) throw new InvalidOperationException("Validation changed scene");
        Debug.Log(report);
    }
}
