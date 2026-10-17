using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.FPS.Gameplay;
using Unity.FPS.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the game's finale into MainScene (corebuster_game_001): the choice at the Master terminal, the escape
/// lockdown in the North-West Room, and the Boss Hall fight against the giant core. corebuster_game_002 adds the
/// reactor under the core (a copy of the Centre Room's containment reactor) firing a beam up through an opening in
/// the bottom of the armour, and three consoles ringed around it with power cables into the reactor: the player
/// injects Disassembler cores into a console to overcharge the reactor and blow off one armour layer. The Boss Hall
/// turret moves west to make room. Everything lives under its own
/// root, rebuilt from scratch on each run. The giant core, its platform and every finale material and mesh are made
/// here from first principles and written to Assets/FPS/Art/Finale. Existing scene objects are only referenced,
/// except that the old kill-every-enemy objective is made optional: the finale now decides how the game ends.
/// </summary>
public static class FinaleBuilder
{
    const string ScenePath = "Assets/FPS/Scenes/MainScene.unity";
    const string RootName = "===== FINALE =====";
    const string KeycardRootName = "===== KEYCARD PROGRESSION =====";
    const string ArtRoot = "Assets/FPS/Art/Finale";
    const string MeshFolder = ArtRoot + "/Meshes";
    const string MaterialFolder = ArtRoot + "/Materials";
    const string EnemyFolder = "Assets/FPS/Prefabs/Enemies/";
    const string HeartMaterialPath = "Assets/FPS/Art/Materials/CoreBuster/M_UnstableOrb.mat";

    // The giant core hangs in the open middle of the Boss Hall's upper ring, under the skylight.
    static readonly Vector3 k_CoreCentre = new Vector3(-296f, 20f, 115.3f);
    const float HeartRadius = 2.2f;
    const float CeilingY = 26.17f;
    const float PoleLatitude = 80f;

    // The Boss Hall's interior (Docs/BossHall/Measurement_047.json), stopping short of the entry incline.
    static readonly Bounds k_Hall = new Bounds(new Vector3(-296.3f, 16.2f, 115f), new Vector3(63.5f, 20f, 65f));
    // The turret used to stand under the core; it now guards the west end, clear of the reactor ring.
    static readonly Vector3 k_Turret = new Vector3(-311.5f, 6.2f, 115.3f);
    const string TurretName = "Enemy_Turret";
    const string ReactorSource = "Level/Centre_Room/Reactor/Containment_Assembly";
    // Consoles stand on a ring around the reactor, the first facing the east entry.
    const float ConsoleRing = 8.5f;
    const float ReactorKeepClear = 12f;
    static readonly float[] k_ConsoleAngles = { 0f, 120f, 240f };
    static readonly Vector3 k_Entry = new Vector3(-263f, 6.2f, 115.3f);

    // The platform: a ramp up from the ring's east side, over the ring's inner rail, to a bridge facing the core.
    const float RingDeckY = 16.68f;
    const float BridgeDeckY = 18.25f;
    const float RampFootX = -283.9f, RampTopX = -286.7f, BridgeEndX = -291.2f;
    const float BridgeZMin = 114.1f, BridgeZMax = 116.5f;

    class LayerSpec
    {
        public string Name, Prefix;
        public ArmourStyle Style;
        public float OuterRadius, Thickness, InsetDegrees, MaxHealth, ShedBeforeBreak, Mass;
        public float[] Latitudes;
        public int PerBand, GridU, GridV;
        public string PieceSfx, BreakSfx;
    }

    static readonly LayerSpec[] k_Layers =
    {
        new LayerSpec { Name = "Steel plating", Prefix = "Steel Plate", Style = ArmourStyle.Steel, OuterRadius = 3.6f, Thickness = .28f,
            InsetDegrees = .7f, MaxHealth = 2400f, ShedBeforeBreak = .6f, Mass = 60f,
            Latitudes = new[] { -45f, -15f, 15f, 45f, PoleLatitude }, PerBand = 8, GridU = 8, GridV = 6,
            PieceSfx = "SecurityDoorClose", BreakSfx = "CoreSurge" },
        new LayerSpec { Name = "Ceramic composite", Prefix = "Ceramic Tile", Style = ArmourStyle.Ceramic, OuterRadius = 3.25f, Thickness = .24f,
            InsetDegrees = .45f, MaxHealth = 1800f, ShedBeforeBreak = .5f, Mass = 8f,
            Latitudes = new[] { -55f, -30f, -10f, 10f, 30f, 55f, PoleLatitude }, PerBand = 10, GridU = 4, GridV = 3,
            PieceSfx = "CoreWindowClose", BreakSfx = "CoreSurge" },
        new LayerSpec { Name = "Lead lining", Prefix = "Lead Panel", Style = ArmourStyle.Lead, OuterRadius = 2.92f, Thickness = .2f,
            InsetDegrees = .5f, MaxHealth = 1500f, ShedBeforeBreak = .55f, Mass = 50f,
            Latitudes = new[] { -40f, 0f, 40f, PoleLatitude }, PerBand = 8, GridU = 10, GridV = 8,
            PieceSfx = "CoreRip", BreakSfx = "CoreSurge" },
    };

    [MenuItem("Tools/CoreBuster/Build Finale")]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError("Finale builder: open " + ScenePath + " first.");
            return;
        }
        if (scene.isDirty)
        {
            Debug.LogError("Finale builder: save or discard the changes in MainScene first.");
            return;
        }

        var master = UnityEngine.Object.FindAnyObjectByType<MasterTerminal>();
        var doors = UnityEngine.Object.FindObjectsByType<SecurityDoor>(FindObjectsInactive.Include);
        var northWestDoor = doors.FirstOrDefault(d => d.name == "North-West Room Door (E)");
        var bossDoor = doors.FirstOrDefault(d => d.name == "Boss Hall Door");
        var redTerminal = UnityEngine.Object.FindObjectsByType<KeycardTerminal>(FindObjectsInactive.Include)
            .FirstOrDefault(t => t.name == "North-West Room Terminal (Red)");
        if (!master || !northWestDoor || !bossDoor || !redTerminal)
        {
            Debug.LogError("Finale builder: run Tools/CoreBuster/Install Keycard Progression first (missing the Master " +
                           "terminal, the North-West Room door, the Boss Hall door or the red terminal).");
            return;
        }

        EnsureFolder(MeshFolder);
        EnsureFolder(MaterialFolder);
        var materials = BuildMaterials();

        var old = GameObject.Find(RootName);
        if (old) UnityEngine.Object.DestroyImmediate(old);
        var root = new GameObject(RootName).transform;

        var core = BuildGiantCore(root, materials);
        BuildPlatform(root, materials);
        MoveTurret();
        Vector3 reactorBase = BuildReactor(root, materials, out ReactorBeam beam);

        var director = new GameObject("Finale Director").AddComponent<FinaleDirector>();
        director.transform.SetParent(root, false);
        director.Master = master;
        director.EscapeDoors = new[] { northWestDoor, bossDoor };
        director.Core = core;
        director.ArenaBounds = k_Hall;
        director.ChoiceSfx = Clip("CoreCriticalBeep");
        director.AlarmSfx = Clip("TerminalLockdown");
        director.OverloadSfx = Clip("Launcher_Charge_BuildUp");
        director.BurstSfx = Clip("Launcher_Explosion");

        GameObject E(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(EnemyFolder + name + ".prefab");
        director.EscapeSpawner = BuildSpawner(root, "Escape Spawner", redTerminal.SpawnPoints,
            new[] { E("Enemy_Elite_PistonFist"), E("Enemy_Elite_Rifleman") }, redTerminal.WarpVfx,
            startInterval: 7f, endInterval: 3.5f, startAlive: 2, endAlive: 5, ramp: 120f, minDistance: 7f);
        var arenaPoints = BuildArenaSpawnPoints(root);
        director.ArenaSpawner = BuildSpawner(root, "Arena Spawner", arenaPoints,
            new[]
            {
                E("Enemy_Robot1_PistonFist"), E("Enemy_Robot1_RivetCannon"), E("Enemy_CyberSoldier_PistonFist"),
                E("Enemy_CyberSoldier_RivetCannon"), E("Enemy_HoverBot"), E("Enemy_HoverBot_PistonFist"),
                E("Enemy_HoverBot_ScrapScattergun"), E("Enemy_Elite_PistonFist"), E("Enemy_Elite_Rifleman"),
            }.Where(p => p).ToArray(), redTerminal.WarpVfx,
            startInterval: 6f, endInterval: 1.2f, startAlive: 4, endAlive: 14, ramp: 240f, minDistance: 10f);

        director.EscapeObjective = BuildObjective(root, "Objective Survive", "Survive the lockdown",
            "Hold out until the escape pod is primed");
        director.CoreObjective = BuildObjective(root, "Objective Core", "Bust the ship's core",
            "Overcharge the reactor consoles to strip the core's armour");
        director.Beam = beam;
        director.Consoles = BuildConsoles(root, materials, reactorBase, director);

        var bust = new GameObject("Bust Point").AddComponent<CoreBustPoint>();
        bust.transform.SetParent(core.transform, false);
        bust.transform.localPosition = new Vector3(HeartRadius + .15f, 0f, 0f);
        bust.Core = core;
        bust.Director = director;
        bust.Range = 3.6f;

        BuildOverlay(root, director, materials.Meltdown);

        // The finale decides how the game ends now; the old kill-every-enemy goal stays on the HUD as optional.
        foreach (var objective in UnityEngine.Object.FindObjectsByType<Unity.FPS.Gameplay.ObjectiveKillEnemies>(FindObjectsInactive.Include))
        {
            Undo.RecordObject(objective, "Make kill objective optional");
            objective.IsOptional = true;
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        int pieces = core.Layers.Sum(l => l.Pieces.Length);
        Debug.Log($"FINALE BUILT: giant core with {pieces} armour pieces in {core.Layers.Length} layers, platform, " +
                  $"{arenaPoints.Length} arena spawn points, {redTerminal.SpawnPoints.Length} escape spawn points, " +
                  "director, objectives and overlay under " + RootName);
    }

    // ------------------------------------------------------------------ materials

    class Materials
    {
        public Material Steel, Ceramic, Lead, Gunmetal, Grating, Hazard, Heart, Meltdown, Beam, BeamHalo, Cable, Lamp, Port;
    }

    static Materials BuildMaterials()
    {
        Shader armour = Shader.Find("CoreBuster/Core Armour");
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        var m = new Materials
        {
            Steel = Armour("M_Core_Steel", armour, 0, new Color(.3f, .31f, .33f), .8f, .45f, new Color(4f, 1.1f, .25f), .08f),
            Ceramic = Armour("M_Core_Ceramic", armour, 1, new Color(.8f, .72f, .6f), 0f, .3f, new Color(5f, 1.6f, .4f), 0f),
            Lead = Armour("M_Core_Lead", armour, 2, new Color(.33f, .36f, .4f), .55f, .25f, new Color(4f, 1.2f, .3f), .09f),
            Gunmetal = Lit("M_Finale_Gunmetal", lit, new Color(.26f, .27f, .29f), .45f, .4f),
            Grating = Lit("M_Finale_Grating", lit, new Color(.38f, .39f, .4f), .2f, .3f),
            Hazard = Lit("M_Finale_Hazard", lit, new Color(.9f, .68f, .08f), .3f, .45f),
            Heart = AssetDatabase.LoadAssetAtPath<Material>(HeartMaterialPath),
        };
        m.Meltdown = LoadOrCreate("M_UICoreMeltdown", Shader.Find("CoreBuster/UI Core Meltdown"));

        Shader beam = Shader.Find("CoreBuster/Reactor Beam");
        m.Beam = LoadOrCreate("M_Finale_ReactorBeam", beam);
        m.Beam.SetFloat("_Intensity", 1.6f);
        m.Beam.SetFloat("_CoreSharpness", 5f);
        m.BeamHalo = LoadOrCreate("M_Finale_ReactorBeamHalo", beam);
        m.BeamHalo.SetFloat("_Intensity", .45f);
        m.BeamHalo.SetFloat("_CoreSharpness", 1.5f);
        m.BeamHalo.SetFloat("_FilamentScale", .7f);
        m.Cable = LoadOrCreate("M_Finale_PowerCable", Shader.Find("CoreBuster/Power Cable"));
        m.Lamp = Lit("M_Finale_ConsoleLamp", lit, Color.white, 0f, .8f);
        m.Lamp.EnableKeyword("_EMISSION");
        m.Lamp.SetColor("_EmissionColor", new Color(1f, .55f, .1f) * 2f);
        m.Lamp.globalIlluminationFlags = MaterialGlobalIlluminationFlags.AnyEmissive;
        m.Port = Lit("M_Finale_ConsolePort", lit, new Color(.02f, .02f, .025f), .6f, .7f);
        foreach (var material in new[] { m.Beam, m.BeamHalo, m.Cable, m.Lamp, m.Port }) EditorUtility.SetDirty(material);
        return m;
    }

    static Material Armour(string name, Shader shader, int style, Color baseColor, float metallic, float smoothness,
        Color glow, float dent)
    {
        var material = LoadOrCreate(name, shader);
        material.SetFloat("_Style", style);
        foreach (var keyword in new[] { "_STYLE_STEEL", "_STYLE_CERAMIC", "_STYLE_LEAD" }) material.DisableKeyword(keyword);
        material.EnableKeyword(new[] { "_STYLE_STEEL", "_STYLE_CERAMIC", "_STYLE_LEAD" }[style]);
        material.SetColor("_BaseColor", baseColor);
        material.SetColor("_DetailColor", new Color(.95f, .72f, .08f));
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        material.SetColor("_GlowColor", glow);
        material.SetFloat("_DentDepth", dent);
        material.SetFloat("_CrackScale", 6f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material Lit(string name, Shader shader, Color color, float metallic, float smoothness)
    {
        var material = LoadOrCreate(name, shader);
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material LoadOrCreate(string name, Shader shader)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        return material;
    }

    // ------------------------------------------------------------------ giant core

    static GiantCore BuildGiantCore(Transform root, Materials materials)
    {
        var coreObject = new GameObject("Giant Core");
        coreObject.transform.SetParent(root, false);
        coreObject.transform.position = k_CoreCentre;
        var core = coreObject.AddComponent<GiantCore>();

        // The unstable heart: the enemies' core orb at the scale of the ship.
        var heart = Part(coreObject.transform, "Heart", SaveMesh(Sphere(HeartRadius, 24, 36), "SM_Core_Heart"), materials.Heart);
        heart.gameObject.AddComponent<SphereCollider>().radius = HeartRadius;
        heart.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var lightObject = new GameObject("Heart Light");
        lightObject.transform.SetParent(heart.transform, false);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, .42f, .16f);
        light.range = 30f;
        light.intensity = 60f;
        light.shadows = LightShadows.None;
        core.Heart = heart;
        core.HeartLight = light;

        var layers = new List<GiantCore.ArmourLayer>();
        foreach (var spec in k_Layers)
            layers.Add(BuildLayer(coreObject.transform, spec, spec.Style == ArmourStyle.Steel ? materials.Steel :
                spec.Style == ArmourStyle.Ceramic ? materials.Ceramic : materials.Lead));
        core.Layers = layers.ToArray();
        core.ShardMeshes = Enumerable.Range(0, 4).Select(i => SaveMesh(Shard(i), "SM_Core_Shard_" + i)).ToArray();
        core.ShardMaterial = materials.Ceramic;
        core.DeflectSfx = Clip("DisassemblerBoltImpact");

        // The crown and base that cap the poles, and the cables it hangs from.
        float top = k_Layers[0].OuterRadius * Mathf.Sin(PoleLatitude * Mathf.Deg2Rad);
        var fittings = new Geo();
        fittings.Cylinder(new Vector3(0f, top - .15f, 0f), new Vector3(0f, top + .45f, 0f), 1.1f, 24);
        fittings.Cylinder(new Vector3(0f, top + .45f, 0f), new Vector3(0f, top + .8f, 0f), .55f, 16);
        // The bottom is open: an intake ring frames the opening where the reactor's beam enters the core.
        float rimLatitude = k_Layers[0].Latitudes[0] * Mathf.Deg2Rad;
        float rimRadius = k_Layers[0].OuterRadius * Mathf.Cos(rimLatitude) + .1f;
        float rimY = k_Layers[0].OuterRadius * Mathf.Sin(rimLatitude) - .05f;
        for (int i = 0; i < 32; i++)
        {
            float a0 = i * Mathf.PI * 2f / 32f, a1 = (i + 1) * Mathf.PI * 2f / 32f;
            var p0 = new Vector3(Mathf.Cos(a0) * rimRadius, rimY, Mathf.Sin(a0) * rimRadius);
            var p1 = new Vector3(Mathf.Cos(a1) * rimRadius, rimY, Mathf.Sin(a1) * rimRadius);
            fittings.Cylinder(p0, p1, .16f, 8);
        }
        Part(coreObject.transform, "Crown And Base", SaveMesh(fittings.ToMesh("SM_Core_Fittings"), "SM_Core_Fittings"), materials.Gunmetal)
            .gameObject.AddComponent<MeshCollider>();

        var cables = new Geo();
        float ceiling = CeilingY - k_CoreCentre.y;
        cables.Cylinder(new Vector3(0f, top + .8f, 0f), new Vector3(0f, ceiling, 0f), .22f, 12);
        for (int i = 0; i < 4; i++)
        {
            float angle = (45f + 90f * i) * Mathf.Deg2Rad;
            var foot = new Vector3(Mathf.Cos(angle) * .9f, top + .4f, Mathf.Sin(angle) * .9f);
            var anchor = new Vector3(Mathf.Cos(angle) * 2.6f, ceiling, Mathf.Sin(angle) * 2.6f);
            cables.Cylinder(foot, anchor, .07f, 8);
        }
        Part(coreObject.transform, "Suspension", SaveMesh(cables.ToMesh("SM_Core_Suspension"), "SM_Core_Suspension"), materials.Gunmetal);
        return core;
    }

    static GiantCore.ArmourLayer BuildLayer(Transform coreRoot, LayerSpec spec, Material material)
    {
        var container = new GameObject(spec.Name).transform;
        container.SetParent(coreRoot, false);
        var pieces = new List<ArmourPiece>();
        for (int band = 0; band < spec.Latitudes.Length - 1; band++)
        {
            float halfWidth = 180f / spec.PerBand;
            var patch = Patch(spec.Latitudes[band] + spec.InsetDegrees, spec.Latitudes[band + 1] - spec.InsetDegrees,
                halfWidth - spec.InsetDegrees, spec.OuterRadius, spec.Thickness, spec.GridU, spec.GridV, out Vector3 centroid);
            var mesh = SaveMesh(patch.ToMesh("SM_" + spec.Prefix.Replace(" ", "") + "_Band" + band),
                "SM_" + spec.Prefix.Replace(" ", "") + "_Band" + band);
            // The top edge's midpoint and direction, for lead panels peeling back.
            Vector3 hinge = Spherical(spec.Latitudes[band + 1] - spec.InsetDegrees, 0f, spec.OuterRadius) - centroid;
            // Alternate bands are offset by half a piece, like courses of brickwork.
            float stagger = band % 2 == 1 ? halfWidth : 0f;
            for (int i = 0; i < spec.PerBand; i++)
            {
                var rotation = Quaternion.Euler(0f, stagger + i * 2f * halfWidth, 0f);
                var renderer = Part(container, $"{spec.Prefix} {band + 1}-{i + 1}", mesh, material);
                renderer.transform.localPosition = rotation * centroid;
                renderer.transform.localRotation = rotation;
                var go = renderer.gameObject;
                go.AddComponent<MeshCollider>().convex = true;
                var health = go.AddComponent<Unity.FPS.Game.Health>();
                health.MaxHealth = 1000000f;
                go.AddComponent<Unity.FPS.Game.Damageable>();
                var piece = go.AddComponent<ArmourPiece>();
                piece.Style = spec.Style;
                piece.HingePoint = hinge;
                piece.HingeAxis = Vector3.right;
                piece.Outward = centroid.normalized;
                piece.Mass = spec.Mass;
                pieces.Add(piece);
            }
        }
        return new GiantCore.ArmourLayer
        {
            Name = spec.Name,
            Style = spec.Style,
            MaxHealth = spec.MaxHealth,
            Pieces = pieces.ToArray(),
            ShedBeforeBreak = spec.ShedBeforeBreak,
            PieceSfx = Clip(spec.PieceSfx),
            BreakSfx = Clip(spec.BreakSfx),
        };
    }

    static Vector3 Spherical(float latitude, float longitude, float radius)
    {
        float lat = latitude * Mathf.Deg2Rad, lon = longitude * Mathf.Deg2Rad;
        return radius * new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Cos(lon));
    }

    // A curved armour piece: a patch of a spherical shell centred on +Z, with its outer face (vertex colour red,
    // the only face the armour shader displaces), its inner face and four closed edges. Returned about its centroid.
    static Geo Patch(float lat0, float lat1, float lonHalf, float outer, float thickness, int nu, int nv, out Vector3 centroid)
    {
        float inner = outer - thickness;
        Vector3 Pt(int i, int j, float r) => Spherical(Mathf.Lerp(lat0, lat1, j / (float)nv), Mathf.Lerp(-lonHalf, lonHalf, i / (float)nu), r);

        var geo = new Geo();
        geo.Grid((i, j) => Pt(i, j, outer), nu, nv, Color.red, false);
        geo.Grid((i, j) => Pt(i, j, inner), nu, nv, Color.black, true);
        Vector3 mid = Spherical((lat0 + lat1) * .5f, 0f, outer);
        for (int i = 0; i < nu; i++)
        {
            geo.Quad(Pt(i, 0, outer), Pt(i + 1, 0, outer), Pt(i + 1, 0, inner), Pt(i, 0, inner), Color.black,
                Pt(i, 0, outer) - Spherical(lat0 + 1f, Mathf.Lerp(-lonHalf, lonHalf, i / (float)nu), outer));
            geo.Quad(Pt(i, nv, outer), Pt(i + 1, nv, outer), Pt(i + 1, nv, inner), Pt(i, nv, inner), Color.black,
                Pt(i, nv, outer) - Spherical(lat1 - 1f, Mathf.Lerp(-lonHalf, lonHalf, i / (float)nu), outer));
        }
        for (int j = 0; j < nv; j++)
        {
            geo.Quad(Pt(0, j, outer), Pt(0, j + 1, outer), Pt(0, j + 1, inner), Pt(0, j, inner), Color.black,
                Pt(0, j, outer) - mid);
            geo.Quad(Pt(nu, j, outer), Pt(nu, j + 1, outer), Pt(nu, j + 1, inner), Pt(nu, j, inner), Color.black,
                Pt(nu, j, outer) - mid);
        }
        centroid = geo.Centre();
        geo.Offset(-centroid);
        return geo;
    }

    static Mesh Sphere(float radius, int rings, int segments)
    {
        var geo = new Geo();
        geo.Grid((i, j) =>
        {
            float lat = Mathf.Lerp(-90f, 90f, j / (float)rings);
            float lon = i * 360f / segments;
            return Spherical(lat, lon, radius);
        }, segments, rings, Color.black, false);
        return geo.ToMesh("SM_Core_Heart");
    }

    // An irregular ceramic chip: a squashed, jittered octahedron.
    static Mesh Shard(int seed)
    {
        var random = new System.Random(1000 + seed);
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        var points = new[]
        {
            new Vector3(R(.6f, 1f), R(-.2f, .2f), R(-.2f, .2f)), new Vector3(-R(.6f, 1f), R(-.2f, .2f), R(-.2f, .2f)),
            new Vector3(R(-.2f, .2f), R(.15f, .3f), R(-.2f, .2f)), new Vector3(R(-.2f, .2f), -R(.15f, .3f), R(-.2f, .2f)),
            new Vector3(R(-.2f, .2f), R(-.1f, .1f), R(.4f, .8f)), new Vector3(R(-.2f, .2f), R(-.1f, .1f), -R(.4f, .8f)),
        };
        int[][] faces = { new[] { 0, 2, 4 }, new[] { 4, 2, 1 }, new[] { 1, 2, 5 }, new[] { 5, 2, 0 },
                          new[] { 0, 4, 3 }, new[] { 4, 1, 3 }, new[] { 1, 5, 3 }, new[] { 5, 0, 3 } };
        var geo = new Geo();
        foreach (var f in faces) geo.Triangle(points[f[0]], points[f[1]], points[f[2]], Color.black, (points[f[0]] + points[f[1]] + points[f[2]]) / 3f);
        return geo.ToMesh("SM_Core_Shard_" + seed);
    }

    // ------------------------------------------------------------------ reactor and consoles

    static void MoveTurret()
    {
        var turret = GameObject.Find(TurretName);
        if (!turret)
        {
            Debug.LogWarning("Finale builder: no " + TurretName + " to move off the reactor's spot.");
            return;
        }
        Undo.RecordObject(turret.transform, "Move turret");
        var p = turret.transform.position;
        turret.transform.position = new Vector3(k_Turret.x, p.y, k_Turret.z);
        Physics.SyncTransforms();
    }

    static float FloorAt(Vector3 point, float from = 12f)
    {
        var hits = Physics.RaycastAll(new Vector3(point.x, from, point.z), Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (var hit in hits)
            if (!hit.collider.GetComponentInParent<Unity.FPS.Game.Health>() && hit.point.y < from - .5f)
                best = Mathf.Max(best, hit.point.y);
        return float.IsNegativeInfinity(best) ? 6.2f : best;
    }

    // A copy of the Centre Room's containment reactor on the hall floor right under the core, and the beam it fires
    // up through the opening in the core's armour. Returns the reactor's base centre.
    static Vector3 BuildReactor(Transform root, Materials materials, out ReactorBeam beam)
    {
        beam = null;
        var source = GameObject.Find(ReactorSource);
        float floor = FloorAt(k_CoreCentre);
        var basePoint = new Vector3(k_CoreCentre.x, floor, k_CoreCentre.z);
        var reactorRoot = new GameObject("Overcharge Reactor").transform;
        reactorRoot.SetParent(root, false);
        reactorRoot.position = basePoint;
        float top = floor + 8f;
        if (source)
        {
            // Seat the copy by the source's lowest point (a probe from above would hit the reactor itself).
            float sourceFloor = source.GetComponentsInChildren<Renderer>().Min(r => r.bounds.min.y);
            var copy = UnityEngine.Object.Instantiate(source, reactorRoot);
            copy.name = "Containment Assembly";
            copy.transform.position = basePoint + Vector3.up * (source.transform.position.y - sourceFloor);
            copy.transform.rotation = source.transform.rotation;
            var bounds = new Bounds(copy.transform.position, Vector3.zero);
            foreach (var renderer in copy.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
            top = bounds.max.y;
            var obstacle = reactorRoot.gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Capsule;
            obstacle.radius = Mathf.Max(bounds.extents.x, bounds.extents.z) + .3f;
            obstacle.height = bounds.size.y;
            obstacle.center = Vector3.up * (bounds.size.y * .5f);
            obstacle.carving = true;
        }
        else Debug.LogWarning("Finale builder: no Centre Room reactor at " + ReactorSource + " to copy.");

        // The beam: a white-hot core tube inside a wide, faint sheath.
        float bottom = top - .5f;
        float tip = k_CoreCentre.y - HeartRadius + .5f;
        var beamRoot = new GameObject("Reactor Beam").transform;
        beamRoot.SetParent(root, false);
        beamRoot.position = new Vector3(k_CoreCentre.x, bottom, k_CoreCentre.z);
        float length = tip - bottom;
        var inner = Part(beamRoot, "Beam Core", SaveMesh(Tube(length, .32f, 20), "SM_Reactor_Beam"), materials.Beam);
        var halo = Part(beamRoot, "Beam Halo", SaveMesh(Tube(length, .85f, 24), "SM_Reactor_BeamHalo"), materials.BeamHalo);
        foreach (var r in new[] { inner, halo })
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        var lightObject = new GameObject("Beam Light");
        lightObject.transform.SetParent(beamRoot, false);
        lightObject.transform.localPosition = Vector3.up * (length * .5f);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(.45f, .65f, 1f);
        light.range = 16f;
        light.intensity = 10f;
        light.shadows = LightShadows.None;
        beam = beamRoot.gameObject.AddComponent<ReactorBeam>();
        beam.Renderers = new Renderer[] { inner, halo };
        beam.BeamLight = light;
        beam.LightIntensity = 10f;
        beam.Length = length;
        var hum = beamRoot.gameObject.AddComponent<AudioSource>();
        hum.spatialBlend = 1f;
        hum.minDistance = 4f;
        hum.maxDistance = 40f;
        hum.playOnAwake = false;
        beam.Hum = hum;
        beam.SurgeSfx = Clip("Launcher_Charge_BuildUp");
        return basePoint;
    }

    // An open tube along +Y from 0 to length: UV.x goes once around, UV.y runs 0 at the bottom to 1 at the top.
    static Mesh Tube(float length, float radius, int segments)
    {
        var geo = new Geo();
        int rings = Mathf.Max(2, Mathf.CeilToInt(length / .5f));
        geo.Grid((i, j) =>
        {
            float a = i * Mathf.PI * 2f / segments;
            return new Vector3(Mathf.Cos(a) * radius, length * j / rings, Mathf.Sin(a) * radius);
        }, segments, rings, Color.black, true);
        return geo.ToMesh("SM_Tube");
    }

    static ReactorConsole[] BuildConsoles(Transform root, Materials materials, Vector3 reactorBase, FinaleDirector director)
    {
        var group = new GameObject("Reactor Consoles").transform;
        group.SetParent(root, false);
        var consoleMesh = SaveMesh(ConsoleBody().ToMesh("SM_Console_Body"), "SM_Console_Body");
        var trimMesh = SaveMesh(ConsoleTrim().ToMesh("SM_Console_Trim"), "SM_Console_Trim");
        var portMesh = SaveMesh(ConsolePort().ToMesh("SM_Console_Port"), "SM_Console_Port");
        var lampMesh = SaveMesh(ConsoleLamps().ToMesh("SM_Console_Lamps"), "SM_Console_Lamps");
        var heart = AssetDatabase.LoadAssetAtPath<Material>(HeartMaterialPath);
        var consoles = new List<ReactorConsole>();
        for (int i = 0; i < k_ConsoleAngles.Length; i++)
        {
            float angle = k_ConsoleAngles[i] * Mathf.Deg2Rad;
            var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            var spot = reactorBase + outward * ConsoleRing;
            spot.y = FloorAt(spot);

            var console = new GameObject("Reactor Console " + (i + 1)).transform;
            console.SetParent(group, false);
            console.position = spot;
            // The port faces away from the reactor, toward the player coming round the ring.
            console.rotation = Quaternion.LookRotation(outward, Vector3.up);

            var body = Part(console, "Body", consoleMesh, materials.Gunmetal);
            Part(console, "Trim", trimMesh, materials.Hazard);
            Part(console, "Port", portMesh, materials.Port);
            var lamps = Part(console, "Lamps", lampMesh, materials.Lamp);
            var collider = console.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, .78f, -.05f);
            collider.size = new Vector3(1.1f, 1.56f, .9f);
            var obstacle = console.gameObject.AddComponent<NavMeshObstacle>();
            obstacle.center = collider.center;
            obstacle.size = collider.size + new Vector3(.2f, 0f, .2f);
            obstacle.carving = true;

            var slot = new GameObject("Gun Port").transform;
            slot.SetParent(console, false);
            slot.localPosition = new Vector3(0f, 1.22f, .42f);

            var lightObject = new GameObject("Status Light");
            lightObject.transform.SetParent(console, false);
            lightObject.transform.localPosition = new Vector3(0f, 1.5f, .7f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 4.5f;
            light.shadows = LightShadows.None;

            var anchors = new List<Transform> { slot };
            foreach (float x in new[] { -.62f, .62f })
            {
                var anchor = new GameObject("Electrode").transform;
                anchor.SetParent(console, false);
                anchor.localPosition = new Vector3(x, 2.28f, -.25f);
                anchors.Add(anchor);
            }

            var cable = BuildCable(console, i, spot, reactorBase, materials);

            var interactable = console.gameObject.AddComponent<ReactorConsole>();
            interactable.Director = director;
            interactable.Slot = slot;
            interactable.FocusPoint = slot;
            interactable.Range = 2.8f;
            interactable.Cable = cable;
            interactable.CoreOrbMaterial = heart;
            interactable.Body = body;
            interactable.Lamps = new Renderer[] { lamps };
            interactable.StatusLight = light;
            interactable.ArcAnchors = anchors.ToArray();
            interactable.DeniedSfx = Clip("CoreCriticalBeep");
            interactable.CoreInjectedSfx = Clip("CoreSurge");
            interactable.OverloadSfx = Clip("CoreCriticalWhine");
            interactable.SpentSfx = Clip("SecurityDoorClose");
            consoles.Add(interactable);
        }
        return consoles.ToArray();
    }

    // Console, in its own space (+Z out of the port, toward the player): a hazard-trimmed plinth, a body block holding
    // the gun port at chest height, a slanted status screen on top and two electrode posts behind.
    static Geo ConsoleBody()
    {
        var geo = new Geo();
        geo.Box(new Vector3(0f, .1f, -.05f), new Vector3(1.3f, .2f, 1.1f));
        geo.Box(new Vector3(0f, .83f, -.05f), new Vector3(1f, 1.26f, .8f));
        geo.Box(new Vector3(0f, 1.52f, -.2f), new Vector3(.95f, .12f, .5f));
        foreach (float x in new[] { -.62f, .62f })
        {
            geo.Cylinder(new Vector3(x, .2f, -.25f), new Vector3(x, 2.15f, -.25f), .06f, 10);
            geo.Cylinder(new Vector3(x, 1.95f, -.25f), new Vector3(x, 2.05f, -.25f), .11f, 10);
        }
        // Cable plug at the back.
        geo.Cylinder(new Vector3(0f, .32f, -.45f), new Vector3(0f, .32f, -.7f), .3f, 16);
        return geo;
    }

    static Geo ConsoleTrim()
    {
        var geo = new Geo();
        for (int i = 0; i < 6; i++)
            geo.Box(new Vector3(-.55f + i * .22f, .1f, .505f), new Vector3(.11f, .2f, .02f));
        // A collar around the port.
        geo.Box(new Vector3(0f, 1.42f, .48f), new Vector3(.5f, .06f, .1f));
        geo.Box(new Vector3(0f, 1.02f, .48f), new Vector3(.5f, .06f, .1f));
        geo.Box(new Vector3(-.22f, 1.22f, .48f), new Vector3(.06f, .46f, .1f));
        geo.Box(new Vector3(.22f, 1.22f, .48f), new Vector3(.06f, .46f, .1f));
        return geo;
    }

    static Geo ConsolePort()
    {
        var geo = new Geo();
        // The barrel socket, a raised dark panel on the body's face with a short ring the muzzle seats in.
        geo.Box(new Vector3(0f, 1.22f, .41f), new Vector3(.38f, .38f, .12f));
        geo.Cylinder(new Vector3(0f, 1.22f, .53f), new Vector3(0f, 1.22f, .47f), .1f, 16);
        return geo;
    }

    static Geo ConsoleLamps()
    {
        var geo = new Geo();
        // Status ring round the port, the screen and the electrode tips: all glow with the console's state.
        geo.Box(new Vector3(0f, 1.36f, .5f), new Vector3(.3f, .025f, .02f));
        geo.Box(new Vector3(0f, 1.08f, .5f), new Vector3(.3f, .025f, .02f));
        geo.Box(new Vector3(-.15f, 1.22f, .5f), new Vector3(.025f, .3f, .02f));
        geo.Box(new Vector3(.15f, 1.22f, .5f), new Vector3(.025f, .3f, .02f));
        var screenCentre = new Vector3(0f, 1.66f, -.12f);
        var tilt = Quaternion.Euler(-35f, 0f, 0f);
        var a = screenCentre + tilt * new Vector3(-.38f, -.16f, 0f);
        var b = screenCentre + tilt * new Vector3(.38f, -.16f, 0f);
        var c = screenCentre + tilt * new Vector3(.38f, .16f, 0f);
        var d = screenCentre + tilt * new Vector3(-.38f, .16f, 0f);
        geo.Quad(a, b, c, d, Color.black, tilt * Vector3.forward);
        geo.Quad(a, d, c, b, Color.black, tilt * Vector3.back);
        foreach (float x in new[] { -.62f, .62f })
            geo.Cylinder(new Vector3(x, 2.15f, -.25f), new Vector3(x, 2.32f, -.25f), .085f, 10);
        return geo;
    }

    // A heavy cable from the console's back plug, down across the floor, and up into the reactor's base.
    static PowerCable BuildCable(Transform console, int index, Vector3 consoleSpot, Vector3 reactorBase, Materials materials)
    {
        Vector3 inward = (reactorBase - consoleSpot);
        inward.y = 0f;
        inward.Normalize();
        float floor = consoleSpot.y;
        float radius = .2f;
        Vector3 plug = console.TransformPoint(new Vector3(0f, .32f, -.7f));
        Vector3 reactorEdge = reactorBase - inward * 3.1f;
        var control = new[]
        {
            plug,
            plug + inward * .6f + Vector3.down * .08f,
            new Vector3(0f, floor + radius, 0f) + Flat(plug + inward * 1.6f),
            new Vector3(0f, floor + radius, 0f) + Flat(Vector3.Lerp(plug, reactorEdge, .5f)) + Vector3.Cross(Vector3.up, inward) * (index % 2 == 0 ? .5f : -.5f),
            new Vector3(0f, floor + radius, 0f) + Flat(reactorEdge - inward * 1.2f),
            reactorEdge - inward * .35f + Vector3.up * (radius + .05f),
            reactorEdge + Vector3.up * .55f,
        };
        var path = CatmullRom(control, 10);
        var mesh = SaveMesh(CableMesh(path, radius, 16, out float length), "SM_PowerCable_" + (index + 1));

        var cableObject = new GameObject("Power Cable").transform;
        cableObject.SetParent(console.parent, false);
        cableObject.name = "Power Cable " + (index + 1);
        var renderer = Part(cableObject, "Cable", mesh, materials.Cable);
        var cable = cableObject.gameObject.AddComponent<PowerCable>();
        cable.Renderers = new Renderer[] { renderer };
        cable.Length = length;
        cable.SurgeDuration = 1.6f;
        return cable;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    static List<Vector3> CatmullRom(Vector3[] points, int steps)
    {
        var result = new List<Vector3>();
        for (int i = 0; i < points.Length - 1; i++)
        {
            Vector3 p0 = points[Mathf.Max(0, i - 1)], p1 = points[i], p2 = points[i + 1], p3 = points[Mathf.Min(points.Length - 1, i + 2)];
            for (int s = 0; s < steps; s++)
            {
                float t = s / (float)steps, t2 = t * t, t3 = t2 * t;
                result.Add(.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
            }
        }
        result.Add(points[points.Length - 1]);
        return result;
    }

    // A tube along a path, framed by parallel transport so it never twists. UV.x is metres along it, UV.y once round.
    static Mesh CableMesh(List<Vector3> path, float radius, int segments, out float length)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        Vector3 tangent = (path[1] - path[0]).normalized;
        Vector3 normal = Vector3.Cross(tangent, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(tangent, Vector3.up).normalized : Vector3.right;
        length = 0f;
        for (int i = 0; i < path.Count; i++)
        {
            if (i > 0)
            {
                length += Vector3.Distance(path[i - 1], path[i]);
                Vector3 next = (i < path.Count - 1 ? path[i + 1] - path[i] : path[i] - path[i - 1]).normalized;
                Vector3 previous = tangent;
                tangent = ((path[i] - path[i - 1]).normalized + next).normalized;
                normal = Quaternion.FromToRotation(previous, tangent) * normal;
            }
            Vector3 binormal = Vector3.Cross(tangent, normal).normalized;
            for (int s = 0; s <= segments; s++)
            {
                float a = s * Mathf.PI * 2f / segments;
                Vector3 n = normal * Mathf.Cos(a) + binormal * Mathf.Sin(a);
                vertices.Add(path[i] + n * radius);
                normals.Add(n);
                uvs.Add(new Vector2(length, s / (float)segments));
            }
        }
        int stride = segments + 1;
        for (int i = 0; i < path.Count - 1; i++)
            for (int s = 0; s < segments; s++)
            {
                int a = i * stride + s, b = a + 1, c = a + stride, d = c + 1;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
        var mesh = new Mesh { name = "SM_PowerCable" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    // ------------------------------------------------------------------ platform

    static void BuildPlatform(Transform root, Materials materials)
    {
        var platform = new GameObject("Core Platform").transform;
        platform.SetParent(root, false);

        // The ramp climbs from the ring deck over the ring's inner rail.
        var ramp = new Geo();
        Vector3 Foot(float z) => new Vector3(RampFootX, RingDeckY, z);
        Vector3 Top(float z) => new Vector3(RampTopX, BridgeDeckY, z);
        Vector3 Base(float z) => new Vector3(RampTopX, RingDeckY, z);
        ramp.Quad(Foot(BridgeZMin), Foot(BridgeZMax), Top(BridgeZMax), Top(BridgeZMin), Color.black, new Vector3(1f, 1.8f, 0f));
        ramp.Quad(Top(BridgeZMin), Top(BridgeZMax), Base(BridgeZMax), Base(BridgeZMin), Color.black, Vector3.left);
        ramp.Triangle(Foot(BridgeZMin), Top(BridgeZMin), Base(BridgeZMin), Color.black, Vector3.back);
        ramp.Triangle(Foot(BridgeZMax), Top(BridgeZMax), Base(BridgeZMax), Color.black, Vector3.forward);
        var rampRenderer = Part(platform, "Ramp", SaveMesh(ramp.ToMesh("SM_Platform_Ramp"), "SM_Platform_Ramp"), materials.Grating);
        rampRenderer.gameObject.AddComponent<MeshCollider>();

        // The bridge deck out over the pit, ending a stride from the outer armour.
        var deckCentre = new Vector3((RampTopX + BridgeEndX) * .5f, BridgeDeckY - .09f, (BridgeZMin + BridgeZMax) * .5f);
        var deckSize = new Vector3(RampTopX - BridgeEndX + .2f, .18f, BridgeZMax - BridgeZMin);
        var deck = new Geo();
        deck.Box(deckCentre, deckSize);
        Part(platform, "Bridge Deck", SaveMesh(deck.ToMesh("SM_Platform_Deck"), "SM_Platform_Deck"), materials.Grating);
        AddBoxCollider(platform, "Bridge Deck Collision", deckCentre, deckSize);

        // Rails along both sides and across the end, with posts every metre.
        var rails = new Geo();
        float railTop = BridgeDeckY + 1.05f;
        foreach (float z in new[] { BridgeZMin + .05f, BridgeZMax - .05f })
        {
            rails.Box(new Vector3(deckCentre.x, railTop, z), new Vector3(deckSize.x, .06f, .06f));
            rails.Box(new Vector3(deckCentre.x, railTop - .5f, z), new Vector3(deckSize.x, .04f, .04f));
            for (float x = RampTopX; x >= BridgeEndX - .01f; x -= 1f)
                rails.Box(new Vector3(x, BridgeDeckY + .52f, z), new Vector3(.07f, 1.05f, .07f));
            AddBoxCollider(platform, "Rail Collision", new Vector3(deckCentre.x, BridgeDeckY + .55f, z),
                new Vector3(deckSize.x, 1.1f, .1f));
        }
        rails.Box(new Vector3(BridgeEndX + .05f, railTop, deckCentre.z), new Vector3(.06f, .06f, deckSize.z));
        rails.Box(new Vector3(BridgeEndX + .05f, railTop - .5f, deckCentre.z), new Vector3(.04f, .04f, deckSize.z));
        AddBoxCollider(platform, "End Rail Collision", new Vector3(BridgeEndX + .05f, BridgeDeckY + .55f, deckCentre.z),
            new Vector3(.1f, 1.1f, deckSize.z));
        Part(platform, "Rails", SaveMesh(rails.ToMesh("SM_Platform_Rails"), "SM_Platform_Rails"), materials.Hazard);

        // Hangers from the skylight frame hold up the far end.
        var hangers = new Geo();
        foreach (float z in new[] { BridgeZMin + .1f, BridgeZMax - .1f })
            hangers.Cylinder(new Vector3(BridgeEndX + .15f, BridgeDeckY, z), new Vector3(BridgeEndX + .15f, CeilingY, z), .05f, 8);
        Part(platform, "Hangers", SaveMesh(hangers.ToMesh("SM_Platform_Hangers"), "SM_Platform_Hangers"), materials.Gunmetal);
    }

    static void AddBoxCollider(Transform parent, string name, Vector3 centre, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = centre;
        go.AddComponent<BoxCollider>().size = size;
    }

    // ------------------------------------------------------------------ spawning and objectives

    // Spawn points over the Boss Hall floor and galleries, clear of the turret and the entry, each joined to the
    // hall's floor on the NavMesh so every enemy can reach the fight.
    static Transform[] BuildArenaSpawnPoints(Transform root)
    {
        var group = new GameObject("Arena Spawn Points").transform;
        group.SetParent(root, false);
        var anchor = new Vector3(-280f, 6.2f, 115.3f);
        if (!NavMesh.SamplePosition(anchor, out NavMeshHit anchorHit, 3f, NavMesh.AllAreas))
        {
            Debug.LogWarning("Finale builder: no NavMesh on the Boss Hall floor; arena spawn points skipped.");
            return new Transform[0];
        }

        var candidates = new List<Vector3>();
        for (float x = -322f; x <= -270f; x += 8f)
            for (float z = 90f; z <= 140f; z += 8f)
                candidates.Add(new Vector3(x, 6.2f, z));
        for (float x = -320f; x <= -272f; x += 12f)
        {
            candidates.Add(new Vector3(x, 10.7f, 86f));
            candidates.Add(new Vector3(x, 10.7f, 144f));
        }

        var points = new List<Transform>();
        var path = new NavMeshPath();
        foreach (var guess in candidates)
        {
            if (Vector2.Distance(new Vector2(guess.x, guess.z), new Vector2(k_Turret.x, k_Turret.z)) < 7f) continue;
            if (Vector2.Distance(new Vector2(guess.x, guess.z), new Vector2(k_CoreCentre.x, k_CoreCentre.z)) < ReactorKeepClear) continue;
            if (Vector2.Distance(new Vector2(guess.x, guess.z), new Vector2(k_Entry.x, k_Entry.z)) < 9f) continue;
            if (!NavMesh.SamplePosition(guess, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) continue;
            if (Mathf.Abs(hit.position.y - guess.y) > 1f) continue;
            if (!NavMesh.CalculatePath(hit.position, anchorHit.position, NavMesh.AllAreas, path) ||
                path.status != NavMeshPathStatus.PathComplete) continue;
            var point = new GameObject("Arena Spawn Point " + (points.Count + 1)).transform;
            point.SetParent(group, false);
            point.position = hit.position;
            points.Add(point);
        }
        return points.ToArray();
    }

    static FinaleSpawner BuildSpawner(Transform root, string name, Transform[] points, GameObject[] prefabs, GameObject warp,
        float startInterval, float endInterval, int startAlive, int endAlive, float ramp, float minDistance)
    {
        var spawner = new GameObject(name).AddComponent<FinaleSpawner>();
        spawner.transform.SetParent(root, false);
        spawner.SpawnPoints = points;
        spawner.Prefabs = prefabs;
        spawner.WarpVfx = warp;
        spawner.WarpSfx = Clip("EnemyWarpIn");
        spawner.StartInterval = startInterval;
        spawner.EndInterval = endInterval;
        spawner.StartAlive = startAlive;
        spawner.EndAlive = endAlive;
        spawner.RampDuration = ramp;
        spawner.MinSpawnDistance = minDistance;
        return spawner;
    }

    static FinaleObjective BuildObjective(Transform root, string name, string title, string description)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        var objective = go.AddComponent<FinaleObjective>();
        objective.Title = title;
        objective.Description = description;
        // Shown only when its phase begins.
        go.SetActive(false);
        return objective;
    }

    // ------------------------------------------------------------------ overlay

    static void BuildOverlay(Transform root, FinaleDirector director, Material meltdownMaterial)
    {
        var kit = MenuSceneBuilder.LoadKit();
        var canvasObject = new GameObject("Finale Overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(root, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        var rect = (RectTransform)canvasObject.transform;
        var overlay = canvasObject.AddComponent<FinaleOverlay>();
        overlay.Director = director;

        // The choice.
        var choice = MenuSceneBuilder.Stretch(rect, "Choice Panel");
        overlay.ChoicePanel = choice.gameObject.AddComponent<CanvasGroup>();
        MenuSceneBuilder.FullScreen<Image>(choice, "Dim").color = new Color(0f, 0f, 0f, .7f);
        var frame = MenuSceneBuilder.Rect(choice, "Frame", Vector2.zero, new Vector2(1400, 660));
        frame.gameObject.AddComponent<Image>().color = new Color(.04f, .04f, .06f, 1f);
        MenuSceneBuilder.Text(frame, "Title", "THE SHIP'S CORE IS GOING CRITICAL", kit.Bebas, kit.BebasGold, 76, Color.white,
            new Vector2(0, 245), new Vector2(1300, 100)).characterSpacing = 6;
        MenuSceneBuilder.Text(frame, "Body",
            "The vault is unlocked. Bust the ship's core and everyone aboard is saved - or seal yourself in here " +
            "and hold out for the escape pod.", kit.Roboto, null, 30, MenuSceneBuilder.k_Parchment,
            new Vector2(0, 130), new Vector2(1200, 120));
        overlay.EscapeButton = MenuSceneBuilder.MenuButton(kit, frame, "Escape", "ESCAPE", new Vector2(-330, -40), new Vector2(520, 120));
        overlay.FightButton = MenuSceneBuilder.MenuButton(kit, frame, "Fight", "FIGHT", new Vector2(330, -40), new Vector2(520, 120));
        MenuSceneBuilder.Text(frame, "Escape Detail", "[1]  SAVE YOURSELF\nSeal the room and survive for 2 minutes",
            kit.Roboto, null, 26, MenuSceneBuilder.k_Cream, new Vector2(-330, -175), new Vector2(560, 100));
        MenuSceneBuilder.Text(frame, "Fight Detail", "[2]  SAVE EVERYONE\nOpen the vault and bust the ship's core",
            kit.Roboto, null, 26, MenuSceneBuilder.k_Cream, new Vector2(330, -175), new Vector2(560, 100));

        // The meltdown: the player's core rising up the screen.
        var meltdown = MenuSceneBuilder.Stretch(rect, "Meltdown");
        overlay.MeltdownGroup = meltdown.gameObject.AddComponent<CanvasGroup>();
        var darken = MenuSceneBuilder.FullScreen<Image>(meltdown, "Darken");
        darken.color = new Color(0f, 0f, 0f, .45f);
        darken.raycastTarget = false;
        overlay.ShakeRoot = MenuSceneBuilder.Stretch(meltdown, "Shake");
        overlay.MeltdownCore = MenuSceneBuilder.Rect(overlay.ShakeRoot, "Player Core", Vector2.zero, new Vector2(560, 560));
        var coreImage = overlay.MeltdownCore.gameObject.AddComponent<Image>();
        coreImage.material = meltdownMaterial;
        coreImage.raycastTarget = false;
        overlay.MeltdownGraphic = coreImage;
        overlay.RumbleSfx = Clip("CoreCriticalWhine");
        overlay.BurstSfx = Clip("CoreGasBlast");

        // The white flash, over everything.
        var flash = MenuSceneBuilder.FullScreen<Image>(rect, "White Flash");
        flash.color = Color.white;
        flash.raycastTarget = false;
        overlay.WhiteFlash = flash.gameObject.AddComponent<CanvasGroup>();
    }

    // ------------------------------------------------------------------ helpers

    static MeshRenderer Part(Transform parent, string name, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        return renderer;
    }

    static Mesh SaveMesh(Mesh mesh, string name)
    {
        string path = MeshFolder + "/" + name + ".asset";
        mesh.name = name;
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing)
        {
            Overwrite(existing, mesh);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // Rewrites an existing mesh asset's data in place (keeping its GUID). CopySerialized leaves the editor drawing the
    // old geometry; setting the buffers directly re-uploads them.
    static void Overwrite(Mesh target, Mesh source)
    {
        target.Clear();
        target.indexFormat = source.indexFormat;
        target.SetVertices(source.vertices);
        target.SetNormals(source.normals);
        if (source.tangents.Length == source.vertexCount) target.SetTangents(source.tangents);
        if (source.colors.Length == source.vertexCount) target.SetColors(source.colors);
        var uvs = new System.Collections.Generic.List<Vector2>();
        source.GetUVs(0, uvs);
        if (uvs.Count == source.vertexCount) target.SetUVs(0, uvs);
        target.subMeshCount = source.subMeshCount;
        for (int i = 0; i < source.subMeshCount; i++) target.SetTriangles(source.GetTriangles(i), i);
        target.RecalculateBounds();
    }

    static AudioClip Clip(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets(name + " t:AudioClip"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
        Debug.LogWarning("Finale builder: no audio clip named " + name);
        return null;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }

    // Minimal mesh assembly. A triangle (a, b, c) faces the side cross(b - a, c - a) points to; Quad and Triangle
    // flip their winding so the face points along "facing".
    sealed class Geo
    {
        readonly List<Vector3> m_Vertices = new List<Vector3>();
        readonly List<Vector2> m_Uvs = new List<Vector2>();
        readonly List<Color> m_Colors = new List<Color>();
        readonly List<int> m_Triangles = new List<int>();

        public void Grid(Func<int, int, Vector3> position, int nu, int nv, Color color, bool flip)
        {
            int start = m_Vertices.Count;
            for (int j = 0; j <= nv; j++)
                for (int i = 0; i <= nu; i++)
                    Add(position(i, j), new Vector2(i / (float)nu, j / (float)nv), color);
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    int a = start + j * (nu + 1) + i, b = a + 1, c = a + nu + 2, d = a + nu + 1;
                    if (flip) m_Triangles.AddRange(new[] { a, c, b, a, d, c });
                    else m_Triangles.AddRange(new[] { a, b, c, a, c, d });
                }
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, Vector3 facing)
        {
            int start = m_Vertices.Count;
            Add(a, Vector2.zero, color);
            Add(b, Vector2.right, color);
            Add(c, Vector2.one, color);
            Add(d, Vector2.up, color);
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f;
            m_Triangles.AddRange(flip
                ? new[] { start, start + 2, start + 1, start, start + 3, start + 2 }
                : new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color, Vector3 facing)
        {
            int start = m_Vertices.Count;
            Add(a, Vector2.zero, color);
            Add(b, Vector2.right, color);
            Add(c, Vector2.up, color);
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f;
            m_Triangles.AddRange(flip ? new[] { start, start + 2, start + 1 } : new[] { start, start + 1, start + 2 });
        }

        public void Box(Vector3 centre, Vector3 size)
        {
            Vector3 h = size * .5f;
            Vector3 P(float x, float y, float z) => centre + new Vector3(h.x * x, h.y * y, h.z * z);
            Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), Color.black, Vector3.forward);
            Quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), Color.black, Vector3.back);
            Quad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), Color.black, Vector3.right);
            Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), Color.black, Vector3.left);
            Quad(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), Color.black, Vector3.up);
            Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), Color.black, Vector3.down);
        }

        public void Cylinder(Vector3 from, Vector3 to, float radius, int segments)
        {
            Vector3 axis = (to - from).normalized;
            Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) > .9f ? Vector3.right : Vector3.up).normalized;
            Vector3 up = Vector3.Cross(axis, side);
            Vector3 Ring(int i) => (side * Mathf.Cos(i * 2f * Mathf.PI / segments) + up * Mathf.Sin(i * 2f * Mathf.PI / segments)) * radius;
            for (int i = 0; i < segments; i++)
            {
                Vector3 r0 = Ring(i), r1 = Ring(i + 1);
                Quad(from + r0, from + r1, to + r1, to + r0, Color.black, r0 + r1);
                Triangle(from, from + r1, from + r0, Color.black, -axis);
                Triangle(to, to + r0, to + r1, Color.black, axis);
            }
        }

        public Vector3 Centre()
        {
            Vector3 sum = Vector3.zero;
            foreach (var v in m_Vertices) sum += v;
            return m_Vertices.Count > 0 ? sum / m_Vertices.Count : Vector3.zero;
        }

        public void Offset(Vector3 offset)
        {
            for (int i = 0; i < m_Vertices.Count; i++) m_Vertices[i] += offset;
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (m_Vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(m_Vertices);
            mesh.SetUVs(0, m_Uvs);
            mesh.SetColors(m_Colors);
            mesh.SetTriangles(m_Triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        void Add(Vector3 position, Vector2 uv, Color color)
        {
            m_Vertices.Add(position);
            m_Uvs.Add(uv);
            m_Colors.Add(color);
        }
    }
}
