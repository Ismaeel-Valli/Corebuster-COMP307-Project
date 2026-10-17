using System.Collections.Generic;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEngine;
using K = CoreExecutionBuilder;

/// <summary>
/// Builds and installs the Disassembler: the left-hip cyber blunderbuss on the player, its laser bolts, the
/// electrocution effect bolted enemies get, and the iris hatch on each armoured enemy's chest that opens
/// onto the core while they are shocked. Also synthesizes all of its sounds. Safe to re-run.
/// The HUD charge gauge is added by Tools/CoreBuster/Reskin HUD and Pause Menu.
/// </summary>
public static class DisassemblerBuilder
{
    const string PlayerPath = "Assets/FPS/Prefabs/Player.prefab";
    const string GunPath = "Assets/FPS/Prefabs/Weapons/Weapon_Disassembler.prefab";
    const string BoltPath = "Assets/FPS/Prefabs/Projectiles/Projectile_DisassemblerBolt.prefab";
    const string ImpactVfxPath = "Assets/FPS/Prefabs/VFX/VFX_DisassemblerImpact.prefab";
    const string MuzzleVfxPath = "Assets/FPS/Prefabs/VFX/VFX_DisassemblerMuzzle.prefab";
    // Loaded by DisassemblerShock through Resources, as the shock is added to enemies at runtime.
    const string ElectrocutionPath = "Assets/FPS/Resources/CoreBuster/VFX_Electrocution.prefab";
    const string BellMeshPath = "Assets/FPS/Art/Models/Disassembler_Bell.asset";
    const string MaterialFolder = "Assets/FPS/Art/Materials/CoreBuster";
    const string ArcShader = "CoreBuster/Electric Arc";
    const string WindowShader = "CoreBuster/Core Window";
    const string EnemyFolder = "Assets/FPS/Prefabs/Enemies/";
    const int SampleRate = K.SampleRate;

    static readonly string[] k_CoredEnemies =
    {
        EnemyFolder + "Enemy_CyberSoldier_PistonFist.prefab",
        EnemyFolder + "Enemy_CyberSoldier_RivetCannon.prefab",
        EnemyFolder + "Enemy_Elite_PistonFist.prefab",
        EnemyFolder + "Enemy_Elite_Rifleman.prefab",
    };

    // The model is authored at pistol size; this scales it to sit chunky in the left hand.
    const float GunScale = 1.3f;

    // Width of the chest hatch, and how far it stands proud of the armour.
    const float WindowSize = .2f;
    const float WindowStandOff = .014f;

    static readonly Color k_Electric = new Color(.25f, .6f, 1f);
    static readonly Color k_Ice = new Color(.78f, .93f, 1f);

    [MenuItem("Tools/CoreBuster/Install Disassembler")]
    public static void InstallAll()
    {
        K.EnsureFolder(MaterialFolder);
        K.EnsureFolder("Assets/FPS/Resources/CoreBuster");
        var impact = BuildImpactVfx();
        var muzzle = BuildMuzzleVfx();
        var bolt = BuildBolt(impact);
        var gun = BuildGun();
        BuildElectrocutionVfx();

        foreach (string path in k_CoredEnemies)
        {
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(path))
            {
                Debug.LogWarning("Disassembler: missing " + path);
                continue;
            }
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (InstallWindow(contents)) PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
        InstallOnPlayer(gun, bolt, muzzle);
        AssetDatabase.SaveAssets();
        Debug.Log("Disassembler installed. Run Tools/CoreBuster/Reskin HUD and Pause Menu for its charge gauge.");
    }

    // ------------------------------------------------------------------ player

    static void InstallOnPlayer(GameObject gun, GameObject bolt, GameObject muzzle)
    {
        var contents = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            var disassembler = contents.GetComponent<Disassembler>();
            if (!disassembler) disassembler = contents.AddComponent<Disassembler>();
            disassembler.GunPrefab = gun;
            disassembler.BoltPrefab = bolt.GetComponent<DisassemblerBolt>();
            disassembler.MuzzleFlashPrefab = muzzle;
            disassembler.ExecutionsForFullCharge = 3;
            disassembler.ShockDuration = 3f;
            // Held low on the left and canted in, so the gun shows its side rather than just its back.
            disassembler.HipPosition = new Vector3(-.36f, -.8f, .1f);
            disassembler.HipEuler = new Vector3(75f, 10f, 25f);
            disassembler.FirePosition = new Vector3(-.25f, -.24f, .34f);
            disassembler.FireCant = -28f;
            disassembler.AimSkew = new Vector2(6f, 16f);
            disassembler.ChargedSfx = K.SynthClip("DisassemblerCharged", SynthCharged);
            disassembler.DrawSfx = K.SynthClip("DisassemblerDraw", SynthDraw);
            disassembler.FireSfx = K.SynthClip("DisassemblerFire", SynthFire);
            disassembler.TwirlSfx = K.SynthClip("DisassemblerTwirl", SynthTwirl);
            disassembler.HolsterSfx = K.SynthClip("DisassemblerHolster", SynthHolster);
            disassembler.NotChargedSfx = K.SynthClip("DisassemblerDenied", SynthDenied);
            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // ------------------------------------------------------------------ chest window

    /// <summary>Puts the iris hatch on the chest, over the enemy's core.</summary>
    public static bool InstallWindow(GameObject root)
    {
        var target = root.GetComponent<CoreExecutionTarget>();
        UnstableCore core = null;
        if (target) core = target.Core ? target.Core : root.GetComponentInChildren<UnstableCore>(true);
        if (!core)
        {
            Debug.LogWarning("Disassembler: " + root.name + " has no core (run Install Core Execution first), skipped.");
            return false;
        }

        foreach (var old in root.GetComponentsInChildren<CoreWindow>(true))
            Object.DestroyImmediate(old.gameObject);

        Transform socket = core.transform.parent ? core.transform.parent : root.transform;
        // The core sits OrbRadius + OrbInset behind the front of the chest; the hatch goes just in front of that.
        Vector3 front = core.transform.position + root.transform.forward * (K.OrbRadius + K.OrbInset + WindowStandOff);

        var window = new GameObject("CoreWindow");
        window.transform.SetParent(socket, false);
        // The quad's visible side faces -Z, so +Z runs back into the chest.
        window.transform.SetPositionAndRotation(front, Quaternion.LookRotation(-root.transform.forward, root.transform.up));
        Vector3 parentScale = socket.lossyScale;
        window.transform.localScale = new Vector3(WindowSize / parentScale.x, WindowSize / parentScale.y, WindowSize / parentScale.z);

        window.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var renderer = window.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = WindowMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.enabled = false;

        var lightObject = new GameObject("Spill Light");
        lightObject.transform.SetParent(window.transform, false);
        lightObject.transform.localPosition = new Vector3(0f, 0f, -.6f);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(.35f, .65f, 1f);
        light.range = 1.6f;
        light.intensity = 0f;
        light.shadows = LightShadows.None;
        light.enabled = false;

        var source = window.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1f;
        source.maxDistance = 18f;

        var hatch = window.AddComponent<CoreWindow>();
        hatch.WindowRenderer = renderer;
        hatch.SpillLight = light;
        hatch.Source = source;
        hatch.OpenSfx = K.SynthClip("CoreWindowOpen", SynthWindowOpen);
        hatch.CloseSfx = K.SynthClip("CoreWindowClose", SynthWindowClose);
        target.Window = hatch;

        Debug.Log($"Disassembler: chest window placed on {root.name} under '{socket.name}'.");
        return true;
    }

    static Material WindowMaterial()
    {
        var material = K.LoadOrCreateMaterial("M_CoreWindow", WindowShader);
        // The cavity in quad units (the quad spans -0.5..0.5): the orb is scaled up a little from the real
        // one so it reads through the small opening.
        material.SetFloat("_Aperture", .74f);
        material.SetFloat("_CavityDepth", .9f);
        material.SetFloat("_OrbDepth", .5f);
        material.SetFloat("_OrbRadius", .27f);
        material.SetFloat("_BladeCount", 7f);
        material.SetColor("_FrameColor", new Color(.55f, .42f, .24f));
        material.SetColor("_BladeColor", new Color(.14f, .16f, .19f));
        material.SetColor("_GlowColor", new Color(.3f, .75f, 1f));
        EditorUtility.SetDirty(material);
        return material;
    }

    // ------------------------------------------------------------------ gun model

    // A stubby cyber blunderbuss: angled grip, a glowing energy drum, coil-wrapped barrel and a flared brass bell.
    // Origin at the trigger, barrel along +Z. The Muzzle child marks the mouth of the bell.
    static GameObject BuildGun()
    {
        // Only part metallic: the weapon layer has little to reflect, and fully metallic parts render black.
        Material gunmetal = LitMaterial("M_DisassemblerGunmetal", new Color(.24f, .26f, .3f), .5f, .55f, Color.black);
        Material brass = LitMaterial("M_DisassemblerBrass", new Color(.85f, .6f, .26f), .55f, .6f, Color.black);
        Material grip = LitMaterial("M_DisassemblerGrip", new Color(.07f, .05f, .045f), 0f, .3f, Color.black);
        Material energy = LitMaterial("M_DisassemblerEnergy", new Color(.1f, .25f, .45f), 0f, .9f, k_Electric * 5f);

        var root = new GameObject("Weapon_Disassembler");
        try
        {
            var model = new GameObject("Model").transform;
            model.SetParent(root.transform, false);
            model.localScale = Vector3.one * GunScale;

            var gripPart = Part(model, "Grip", PrimitiveType.Cube, new Vector3(0f, -.06f, -.035f), new Vector3(.034f, .11f, .045f), grip);
            gripPart.transform.localRotation = Quaternion.Euler(-18f, 0f, 0f);
            Part(model, "Pommel", PrimitiveType.Cube, new Vector3(0f, -.115f, -.055f), new Vector3(.04f, .018f, .052f), brass)
                .transform.localRotation = Quaternion.Euler(-18f, 0f, 0f);
            Part(model, "Trigger Guard", PrimitiveType.Cube, new Vector3(0f, -.03f, .02f), new Vector3(.012f, .008f, .05f), brass);
            Part(model, "Frame", PrimitiveType.Cube, new Vector3(0f, .005f, .02f), new Vector3(.042f, .045f, .13f), gunmetal);
            Part(model, "Hammer", PrimitiveType.Cube, new Vector3(0f, .036f, -.04f), new Vector3(.014f, .025f, .02f), brass)
                .transform.localRotation = Quaternion.Euler(-30f, 0f, 0f);

            // The energy drum, where a revolver's cylinder would be, banded with glowing cells.
            Cylinder(model, "Drum", new Vector3(0f, .016f, .035f), .058f, .06f, gunmetal);
            for (int i = 0; i < 3; i++)
                Cylinder(model, "Drum Cell " + (i + 1), new Vector3(0f, .016f, .015f + i * .02f), .061f, .006f, energy);

            // Barrel wrapped in coils, then the flared bell.
            Cylinder(model, "Barrel", new Vector3(0f, .02f, .15f), .026f, .15f, gunmetal);
            for (int i = 0; i < 4; i++)
                Cylinder(model, "Coil " + (i + 1), new Vector3(0f, .02f, .09f + i * .03f), .036f, .007f, i % 2 == 0 ? energy : brass);
            Part(model, "Top Rib", PrimitiveType.Cube, new Vector3(0f, .038f, .13f), new Vector3(.008f, .01f, .15f), brass);

            var bell = new GameObject("Bell");
            bell.transform.SetParent(model, false);
            bell.transform.localPosition = new Vector3(0f, .02f, .215f);
            bell.AddComponent<MeshFilter>().sharedMesh = BellMesh();
            bell.AddComponent<MeshRenderer>().sharedMaterial = brass;
            // The emitter glowing at the throat of the bell, and a glowing lip round its mouth.
            Cylinder(model, "Emitter", new Vector3(0f, .02f, .235f), .03f, .004f, energy);
            Cylinder(model, "Bell Lip", new Vector3(0f, .02f, .297f), .086f, .006f, energy);

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root.transform, false);
            muzzle.localPosition = new Vector3(0f, .02f, .3f) * GunScale;

            foreach (var collider in root.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, GunPath);
            Debug.Log("Disassembler gun saved: " + GunPath);
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
        return part;
    }

    // A cylinder along +Z with the given diameter and length.
    static GameObject Cylinder(Transform parent, string name, Vector3 position, float diameter, float length, Material material)
    {
        var part = Part(parent, name, PrimitiveType.Cylinder, position, new Vector3(diameter, length * .5f, diameter), material);
        part.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        return part;
    }

    // The flared bell, lathed from a closed profile (outside, lip, inside) so it has real wall thickness.
    static Mesh BellMesh()
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(BellMeshPath);
        if (!mesh)
        {
            mesh = new Mesh { name = "Disassembler_Bell" };
            K.EnsureFolder("Assets/FPS/Art/Models");
            AssetDatabase.CreateAsset(mesh, BellMeshPath);
        }

        // (radius, z) from the back of the outside, round the lip, and back down the inside.
        var profile = new List<Vector2>();
        const int flareSteps = 10;
        for (int i = 0; i <= flareSteps; i++)
        {
            float t = i / (float)flareSteps;
            profile.Add(new Vector2(.015f + .024f * Mathf.Pow(t, 2.2f), t * .08f));
        }
        profile.Add(new Vector2(.042f, .082f));
        profile.Add(new Vector2(.037f, .084f));
        for (int i = flareSteps; i >= 0; i--)
        {
            float t = i / (float)flareSteps;
            profile.Add(new Vector2(.011f + .023f * Mathf.Pow(t, 2.2f), t * .08f + .002f));
        }

        const int sides = 28;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int s = 0; s <= sides; s++)
        {
            float angle = s / (float)sides * Mathf.PI * 2f;
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            foreach (var point in profile) vertices.Add(new Vector3(cos * point.x, sin * point.x, point.y));
        }
        int rings = profile.Count;
        for (int s = 0; s < sides; s++)
        {
            for (int p = 0; p < rings - 1; p++)
            {
                int a = s * rings + p, b = a + 1, c = a + rings, d = c + 1;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
        }
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    static Material LitMaterial(string name, Color color, float metallic, float smoothness, Color emission)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        if (emission.maxColorComponent > 0f)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    // ------------------------------------------------------------------ bolt and effects

    static Material ArcMaterial(string name, Color color, float intensity, float coreWidth, float flicker)
    {
        var material = K.LoadOrCreateMaterial(name, ArcShader);
        material.SetColor("_Color", color);
        material.SetFloat("_Intensity", intensity);
        material.SetFloat("_CoreWidth", coreWidth);
        material.SetFloat("_Flicker", flicker);
        EditorUtility.SetDirty(material);
        return material;
    }

    static LineRenderer NewLine(GameObject go, Material material, float width)
    {
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.widthMultiplier = width;
        line.numCapVertices = 2;
        line.positionCount = 0;
        line.useWorldSpace = true;
        line.startColor = line.endColor = Color.white;
        K.NoShadows(line);
        return line;
    }

    static GameObject BuildBolt(GameObject impact)
    {
        Material beam = ArcMaterial("M_DisassemblerBolt", k_Electric, 6f, .16f, .35f);
        var root = new GameObject("Projectile_DisassemblerBolt");
        try
        {
            var bolt = root.AddComponent<DisassemblerBolt>();
            bolt.Beam = NewLine(root, beam, .07f);
            // Thin at the tail, full width at the head.
            bolt.Beam.widthCurve = AnimationCurve.Linear(0f, .25f, 1f, 1f);
            bolt.ImpactVfx = impact;
            bolt.ImpactSfx = K.SynthClip("DisassemblerBoltImpact", SynthBoltImpact);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, BoltPath);
            Debug.Log("Disassembler bolt saved: " + BoltPath);
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // Sparks spraying off the struck surface and a quick blue flash ring.
    static GameObject BuildImpactVfx()
    {
        Material spark = K.EnergyMaterial("M_DisassemblerSpark", 0f, 5f, 1.2f, Color.white);
        Material ring = K.EnergyMaterial("M_DisassemblerRing", 1f, 4f, 0f, Color.white);
        var root = new GameObject("VFX_DisassemblerImpact");
        try
        {
            var sparks = K.NewParticles("Sparks", root.transform, spark, 60);
            var main = sparks.main;
            main.playOnAwake = true;
            main.loop = false;
            main.duration = .3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.15f, .4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(.015f, .035f);
            main.startColor = new ParticleSystem.MinMaxGradient(k_Electric, k_Ice);
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            K.Burst(sparks, 22);
            var shape = sparks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 55f;
            shape.radius = .02f;
            K.FadeOut(sparks, k_Electric);
            K.Stretch(sparks, .05f, 1.5f);

            var flash = K.NewParticles("Flash", root.transform, ring, 2);
            main = flash.main;
            main.playOnAwake = true;
            main.loop = false;
            main.duration = .2f;
            main.startLifetime = .16f;
            main.startSpeed = 0f;
            main.startSize = .15f;
            main.startColor = k_Electric;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            K.Burst(flash, 1);
            K.GrowOut(flash, .5f, 4f);
            K.FadeOut(flash, k_Electric);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, ImpactVfxPath);
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // A blue blast out of the bell: a cone of sparks and a flash ring.
    static GameObject BuildMuzzleVfx()
    {
        Material spark = K.EnergyMaterial("M_DisassemblerSpark", 0f, 5f, 1.2f, Color.white);
        Material ring = K.EnergyMaterial("M_DisassemblerRing", 1f, 4f, 0f, Color.white);
        var root = new GameObject("VFX_DisassemblerMuzzle");
        try
        {
            var sparks = K.NewParticles("Blast", root.transform, spark, 80);
            var main = sparks.main;
            main.playOnAwake = true;
            main.loop = false;
            main.duration = .2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.06f, .16f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(.01f, .025f);
            main.startColor = new ParticleSystem.MinMaxGradient(k_Electric, k_Ice);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            K.Burst(sparks, 40);
            var shape = sparks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = .02f;
            K.FadeOut(sparks, k_Electric);
            K.Stretch(sparks, .03f, 2f);

            var flash = K.NewParticles("Flash", root.transform, ring, 2);
            main = flash.main;
            main.playOnAwake = true;
            main.loop = false;
            main.duration = .15f;
            main.startLifetime = .1f;
            main.startSpeed = 0f;
            main.startSize = .08f;
            main.startColor = k_Ice;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            K.Burst(flash, 1);
            K.GrowOut(flash, .6f, 3f);
            K.FadeOut(flash, k_Electric);
            // The ring faces down the barrel.
            var renderer = flash.GetComponent<ParticleSystemRenderer>();
            renderer.alignment = ParticleSystemRenderSpace.Local;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, MuzzleVfxPath);
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // The electrocution: lightning lines, sparks off the body mesh and ground, a strobing light and a buzz.
    static void BuildElectrocutionVfx()
    {
        Material lightning = ArcMaterial("M_Lightning", new Color(.45f, .7f, 1f), 7f, .1f, .7f);
        Material spark = K.EnergyMaterial("M_DisassemblerSpark", 0f, 5f, 1.2f, Color.white);
        var root = new GameObject("VFX_Electrocution");
        try
        {
            var vfx = root.AddComponent<ElectrocutionVfx>();
            var arcs = new LineRenderer[5];
            for (int i = 0; i < arcs.Length; i++)
            {
                var arc = new GameObject("Arc " + (i + 1));
                arc.transform.SetParent(root.transform, false);
                arcs[i] = NewLine(arc, lightning, .04f);
                arcs[i].numCapVertices = 0;
                arcs[i].numCornerVertices = 1;
            }
            vfx.Arcs = arcs;

            // Emits from the body's mesh; ElectrocutionVfx points the shape at the body when it plays.
            var body = K.NewParticles("Body Sparks", root.transform, spark, 200);
            var main = body.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.12f, .35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.8f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(.012f, .03f);
            main.startColor = new ParticleSystem.MinMaxGradient(k_Electric, k_Ice);
            main.gravityModifier = .8f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = body.emission;
            emission.rateOverTime = 70f;
            var shape = body.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = .4f;
            K.FadeOut(body, k_Electric);
            K.Stretch(body, .04f, 1.4f);
            vfx.BodySparks = body;

            var ground = K.NewParticles("Ground Sparks", root.transform, spark, 200);
            main = ground.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.2f, .5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(.012f, .03f);
            main.startColor = new ParticleSystem.MinMaxGradient(k_Electric, k_Ice);
            main.gravityModifier = 1.4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            emission = ground.emission;
            emission.rateOverTime = 0f;
            // EmitParams.position is used as is; the cone only gives each spark an upward-splashing velocity.
            K.UpwardCone(ground, 60f, .01f);
            K.FadeOut(ground, k_Electric);
            K.Stretch(ground, .04f, 1.4f);
            vfx.GroundSparks = ground;

            var lightObject = new GameObject("Shock Light");
            lightObject.transform.SetParent(root.transform, false);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(.4f, .65f, 1f);
            light.range = 3.5f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            vfx.Glow = light;
            vfx.GlowIntensity = 2.2f;

            var crackle = root.AddComponent<AudioSource>();
            crackle.clip = K.SynthClip("ElectrocuteLoop", SynthElectrocuteLoop);
            crackle.loop = true;
            crackle.playOnAwake = false;
            crackle.spatialBlend = 1f;
            crackle.dopplerLevel = 0f;
            crackle.rolloffMode = AudioRolloffMode.Linear;
            crackle.minDistance = 1.5f;
            crackle.maxDistance = 25f;
            vfx.CrackleLoop = crackle;
            vfx.ZapSfx = K.SynthClip("ElectrocuteZap", SynthZap);

            PrefabUtility.SaveAsPrefabAsset(root, ElectrocutionPath);
            Debug.Log("Electrocution effect saved: " + ElectrocutionPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // ------------------------------------------------------------------ sound synthesis

    static float Noise(System.Random random) => K.Noise(random);

    static float Square(float phase) => Mathf.Sin(phase) >= 0f ? 1f : -1f;

    // Crushes a sample to a few bits, for a digital edge.
    static float Crush(float sample, float levels) => Mathf.Round(sample * levels) / levels;

    // Charged: a bit-crushed rising arpeggio into a shimmering, tremoloing chord with a digital sparkle on top.
    static float[] SynthCharged(System.Random random)
    {
        const float length = 1.2f;
        var samples = new float[(int)(length * SampleRate)];
        float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
        const float noteLength = .065f;
        float[] phases = new float[6];
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float s = 0f;
            int note = Mathf.FloorToInt(t / noteLength);
            if (note < notes.Length)
            {
                float local = t - note * noteLength;
                phases[0] += 2f * Mathf.PI * notes[note] / SampleRate;
                phases[1] += 2f * Mathf.PI * notes[note] * 2.005f / SampleRate;
                s += (Square(phases[0]) * .5f + Mathf.Sin(phases[1]) * .3f) * Mathf.Exp(-local * 18f);
            }
            float chordStart = notes.Length * noteLength;
            if (t >= chordStart)
            {
                float local = t - chordStart;
                float tremolo = .65f + .35f * Mathf.Sin(2f * Mathf.PI * 14f * local);
                float envelope = Mathf.Clamp01(local / .02f) * Mathf.Exp(-local * 2.6f);
                phases[2] += 2f * Mathf.PI * 1046.5f / SampleRate;
                phases[3] += 2f * Mathf.PI * 1568f * 1.003f / SampleRate;
                phases[4] += 2f * Mathf.PI * 2093f / SampleRate;
                phases[5] += 2f * Mathf.PI * Mathf.Lerp(3000f, 6000f, Mathf.Clamp01(local / .5f)) / SampleRate;
                float chord = Mathf.Sin(phases[2]) * .45f + Mathf.Sin(phases[3]) * .3f + Square(phases[4]) * .12f;
                float sparkle = Mathf.Sin(phases[5]) * .12f * Mathf.Exp(-local * 6f) *
                                (random.NextDouble() < .5 ? 1f : 0f);
                s += (chord * tremolo + sparkle) * envelope;
            }
            samples[i] = Crush(s, 24f) * Mathf.Clamp01((length - t) / .08f);
        }
        return K.Normalize(samples, .8f);
    }

    // The blunderbuss blast: a cluster of detuned laser sweeps, a crack of noise and a low thump, fizzing out.
    static float[] SynthFire(System.Random random)
    {
        const float length = .75f;
        var samples = new float[(int)(length * SampleRate)];
        var crack = new K.BandPass();
        float[] detune = { 1f, 1.07f, .93f, 1.16f };
        float[] phases = new float[detune.Length];
        float thumpPhase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float sweep = Mathf.Lerp(2600f, 240f, Mathf.Pow(Mathf.Clamp01(t / .22f), .6f));
            float lasers = 0f;
            for (int d = 0; d < detune.Length; d++)
            {
                phases[d] += 2f * Mathf.PI * sweep * detune[d] / SampleRate;
                lasers += Mathf.Sin(phases[d] + Mathf.Sin(phases[d] * .5f) * 1.5f);
            }
            lasers *= .3f * Mathf.Exp(-t * 7f);
            float blast = crack.Process(Noise(random), Mathf.Lerp(4200f, 700f, Mathf.Clamp01(t / .15f)), 1.5f) *
                          2.2f * Mathf.Exp(-t * 16f);
            thumpPhase += 2f * Mathf.PI * Mathf.Lerp(120f, 45f, Mathf.Clamp01(t / .2f)) / SampleRate;
            float thump = Mathf.Sin(thumpPhase) * Mathf.Exp(-t * 10f);
            float fizz = random.NextDouble() < .02 * Mathf.Exp(-t * 4f) ? Noise(random) * .8f : 0f;
            samples[i] = (lasers + blast + thump + fizz) * Mathf.Clamp01(t / .001f) * Mathf.Clamp01((length - t) / .05f);
        }
        return K.Normalize(samples, .95f);
    }

    // Leather-and-steel quick draw: a rising swish ending in a click.
    static float[] SynthDraw(System.Random random)
    {
        const float length = .2f;
        var samples = new float[(int)(length * SampleRate)];
        var swish = new K.BandPass();
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float air = swish.Process(Noise(random), Mathf.Lerp(600f, 3200f, t / length), 2f) *
                        Mathf.Sin(Mathf.Clamp01(t / .14f) * Mathf.PI);
            float click = t > .14f ? Mathf.Sin(2f * Mathf.PI * 3400f * t) * Mathf.Exp(-(t - .14f) * 250f) : 0f;
            samples[i] = air * 1.5f + click * .7f;
        }
        return K.Normalize(samples, .6f);
    }

    // The twirl: a whirring whoosh pulsing with each turn.
    static float[] SynthTwirl(System.Random random)
    {
        const float length = .34f;
        var samples = new float[(int)(length * SampleRate)];
        var whirr = new K.BandPass();
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float spin = .45f + .55f * Mathf.Abs(Mathf.Sin(2f * Mathf.PI * 9f * t));
            float air = whirr.Process(Noise(random), 1400f + 500f * Mathf.Sin(2f * Mathf.PI * 9f * t), 3f);
            samples[i] = air * spin * Mathf.Sin(t / length * Mathf.PI);
        }
        return K.Normalize(samples, .45f);
    }

    // Holstered: a metallic click and a soft leather thunk.
    static float[] SynthHolster(System.Random random)
    {
        const float length = .16f;
        var samples = new float[(int)(length * SampleRate)];
        float low = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float click = (Mathf.Sin(2f * Mathf.PI * 2900f * t) + .5f * Mathf.Sin(2f * Mathf.PI * 4700f * t)) * Mathf.Exp(-t * 180f);
            low += (Noise(random) - low) * .08f;
            float thunk = (low * 3f + Mathf.Sin(2f * Mathf.PI * 140f * t)) * Mathf.Exp(-t * 35f);
            samples[i] = click * .6f + thunk * .5f;
        }
        return K.Normalize(samples, .55f);
    }

    // Not charged: a short low double buzz.
    static float[] SynthDenied(System.Random random)
    {
        const float length = .24f;
        var samples = new float[(int)(length * SampleRate)];
        float phase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            phase += 2f * Mathf.PI * 110f / SampleRate;
            float gate = t < .09f || (t > .12f && t < .21f) ? 1f : 0f;
            samples[i] = Crush(Square(phase) * .6f + Square(phase * 1.5f) * .3f, 8f) * gate;
        }
        return K.Normalize(samples, .45f);
    }

    // A bolt striking: a short, bright electric snap.
    static float[] SynthBoltImpact(System.Random random)
    {
        const float length = .3f;
        var samples = new float[(int)(length * SampleRate)];
        var filter = new K.BandPass();
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float zap = filter.Process(Noise(random), Mathf.Lerp(5000f, 1200f, t / length), 2.5f);
            float chirp = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(1800f, 400f, t / length) * t) * .4f;
            samples[i] = (zap * 2f + chirp) * Mathf.Clamp01(t / .002f) * Mathf.Exp(-t * 16f);
        }
        return K.Normalize(samples, .7f);
    }

    // The strike that starts an electrocution: a harsh crack with a buzzing tail.
    static float[] SynthZap(System.Random random)
    {
        const float length = .6f;
        var samples = new float[(int)(length * SampleRate)];
        var filter = new K.BandPass();
        float phase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float crack = filter.Process(Noise(random), Mathf.Lerp(6000f, 1500f, Mathf.Clamp01(t / .1f)), 1.2f) * 2.5f * Mathf.Exp(-t * 20f);
            phase += 2f * Mathf.PI * 120f / SampleRate;
            float saw = (phase / Mathf.PI % 2f) - 1f;
            float buzz = saw * .5f * Mathf.Exp(-t * 5f) * (.6f + .4f * Mathf.Sin(2f * Mathf.PI * 30f * t));
            samples[i] = (crack + buzz) * Mathf.Clamp01(t / .001f) * Mathf.Clamp01((length - t) / .05f);
        }
        return K.Normalize(samples, .9f);
    }

    // The current running through a body: a 120 Hz buzz, hiss and random crackles. Loops seamlessly.
    static float[] SynthElectrocuteLoop(System.Random random)
    {
        const float length = 1.6f, crossfade = .12f;
        int loop = (int)(length * SampleRate), fade = (int)(crossfade * SampleRate);
        var raw = new float[loop + fade];
        var hiss = new K.BandPass();
        float phase = 0f;
        float crackle = 0f;
        for (int i = 0; i < raw.Length; i++)
        {
            float t = i / (float)SampleRate;
            phase += 2f * Mathf.PI * (120f + 6f * Mathf.Sin(2f * Mathf.PI * 7f * t)) / SampleRate;
            float saw = (phase / Mathf.PI % 2f) - 1f;
            float buzz = Crush(saw, 6f) * (.35f + .25f * Mathf.PerlinNoise(t * 20f, .5f));
            float air = hiss.Process(Noise(random), 3500f, 1.5f) * .5f;
            if (random.NextDouble() < .0012) crackle = 1f;
            crackle *= .9985f;
            float snap = Noise(random) * crackle * crackle * 1.4f;
            raw[i] = buzz + air + snap;
        }
        var clip = new float[loop];
        for (int i = 0; i < loop; i++)
        {
            float k = i < fade ? i / (float)fade : 1f;
            clip[i] = i < fade ? raw[i] * k + raw[loop + i] * (1f - k) : raw[i];
        }
        return K.Normalize(clip, .6f);
    }

    // The chest hatch opening: a rising servo whine, the iris clacking back and a hiss of escaping pressure.
    static float[] SynthWindowOpen(System.Random random) => SynthServo(random, true);

    static float[] SynthWindowClose(System.Random random) => SynthServo(random, false);

    static float[] SynthServo(System.Random random, bool opening)
    {
        const float length = .45f;
        var samples = new float[(int)(length * SampleRate)];
        var hiss = new K.BandPass();
        float phase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float p = Mathf.Clamp01(t / .3f);
            float pitch = opening ? Mathf.Lerp(500f, 1400f, p) : Mathf.Lerp(1300f, 450f, p);
            phase += 2f * Mathf.PI * pitch / SampleRate;
            float servo = (Mathf.Sin(phase) + .3f * Square(phase * .5f)) * .35f * Mathf.Sin(p * Mathf.PI);
            float clackTime = opening ? .05f : .3f;
            float clack = t > clackTime ? Noise(random) * Mathf.Exp(-(t - clackTime) * 120f) : 0f;
            float gas = opening ? hiss.Process(Noise(random), 5000f, 1f) * Mathf.Exp(-Mathf.Max(0f, t - .06f) * 7f) * (t > .06f ? .8f : 0f) : 0f;
            samples[i] = servo + clack + gas;
        }
        return K.Normalize(samples, .6f);
    }
}
