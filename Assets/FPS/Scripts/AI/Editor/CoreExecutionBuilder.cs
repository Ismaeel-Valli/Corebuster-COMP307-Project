using System.Collections.Generic;
using System.IO;
using Unity.FPS.AI;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the Unstable Core prefab (shader materials, particles, light and synthesized sounds) and installs
/// the core execution: a core in the chest of each armoured enemy and the CoreExecutioner on the player.
/// </summary>
public static class CoreExecutionBuilder
{
    const string CorePrefabPath = "Assets/FPS/Prefabs/VFX/VFX_UnstableCore.prefab";
    const string MuzzleVfxPath = "Assets/FPS/Prefabs/VFX/VFX_CoreMuzzleCharge.prefab";
    const string GasCloudPath = "Assets/FPS/Prefabs/VFX/VFX_CoreGasCloud.prefab";
    const string CriticalVfxPath = "Assets/FPS/Prefabs/VFX/VFX_CoreCritical.prefab";
    const string OverlayMaterialPath = "Assets/FPS/Resources/CoreBuster/M_ToxicGasOverlay.mat";
    const string MaterialFolder = "Assets/FPS/Art/Materials/CoreBuster";
    const string AudioFolder = "Assets/FPS/Audio/SFX/CoreBuster";
    const string PlayerPath = "Assets/FPS/Prefabs/Player.prefab";
    const string EnemyFolder = "Assets/FPS/Prefabs/Enemies/";
    const string OrbShader = "CoreBuster/Unstable Orb";
    const string HaloShader = "CoreBuster/Core Halo";
    // Orb radius in metres; it sits this far behind the chest surface (plus its radius), fully inside the body.
    internal const float OrbRadius = .075f;
    internal const float OrbInset = .03f;
    // The halo shader's orb radius as a fraction of the quad's half-size.
    const float HaloOrbFraction = .3f;
    const string EnergyShader = "CoreBuster/Core Energy Additive";
    const string GasShader = "CoreBuster/Galaxy Gas";
    const string OverlayShader = "CoreBuster/UI Toxic Gas Overlay";
    // Rings drawn by the energy shader sit at this fraction of the particle's half-size (0.85 - ring width).
    const float RingRadiusFraction = .78f;
    internal const int SampleRate = 44100;

    static readonly string[] k_ArmouredEnemies =
    {
        EnemyFolder + "Enemy_CyberSoldier_PistonFist.prefab",
        EnemyFolder + "Enemy_CyberSoldier_RivetCannon.prefab",
    };

    // Concept-art palette, used for the particles.
    static readonly Color k_Azure = new Color(.3f, .62f, 1f);
    static readonly Color k_Ice = new Color(.78f, .93f, 1f);
    static readonly Color k_Lavender = new Color(.55f, .52f, .95f);
    static readonly Color k_Blood = new Color(1f, .14f, .09f);
    static readonly Color k_Ember = new Color(1f, .5f, .25f);

    [MenuItem("Tools/CoreBuster/Create Unstable Core Prefab")]
    public static void BuildCorePrefabMenu()
    {
        BuildCorePrefab();
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/CoreBuster/Install Core Execution")]
    public static void InstallAll()
    {
        BuildCorePrefab();
        BuildMuzzleVfx();
        BuildGasCloud();
        BuildCriticalVfx();
        BuildOverlayMaterial();
        foreach (string path in k_ArmouredEnemies)
        {
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(path))
            {
                Debug.LogWarning("Core execution: missing " + path);
                continue;
            }
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (InstallOnEnemy(contents)) PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
        InstallOnPlayer();
        AssetDatabase.SaveAssets();
        Debug.Log("Core execution installed.");
    }

    /// <summary>Puts an Unstable Core in an armoured enemy's chest and makes the enemy core-bustable.</summary>
    public static bool InstallOnEnemy(GameObject root)
    {
        var armour = root.GetComponent<ArmouredEnemy>();
        var animator = root.GetComponent<Animator>();
        if (!armour || !animator)
        {
            Debug.LogWarning("Core execution: " + root.name + " has no ArmouredEnemy/Animator, skipped.");
            return false;
        }
        var corePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CorePrefabPath) ?? BuildCorePrefab();

        foreach (var old in root.GetComponentsInChildren<UnstableCore>(true))
            Object.DestroyImmediate(old.gameObject);

        var bones = CyberSoldierBuilder.HumanBoneMap(animator.avatar, root.transform);
        Transform socket = FirstBone(bones, HumanBodyBones.UpperChest, HumanBodyBones.Chest, HumanBodyBones.Spine);
        if (!socket) socket = root.transform;

        var core = (GameObject)PrefabUtility.InstantiatePrefab(corePrefab, socket);
        core.name = "VFX_UnstableCore";
        core.transform.SetPositionAndRotation(ChestSurface(root.transform, armour.BodyRenderer, bones, socket),
            root.transform.rotation);
        Vector3 parentScale = socket.lossyScale;
        core.transform.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f / parentScale.z);

        var unstable = core.GetComponent<UnstableCore>();
        unstable.IgniteOnStart = false;
        // The galaxy body draws the orb through its surface with parallax.
        unstable.HostRenderers = armour.BodyRenderer ? new Renderer[] { armour.BodyRenderer } : new Renderer[0];

        var target = root.GetComponent<CoreExecutionTarget>();
        if (!target) target = root.AddComponent<CoreExecutionTarget>();
        target.Core = unstable;
        target.BodyAnimator = animator;
        target.BodyRenderers = armour.BodyRenderer ? new Renderer[] { armour.BodyRenderer } : new Renderer[0];
        target.HealthReward = 40f;
        armour.CoreTarget = target;

        // After the execution the body falls, overloads and bursts into gas.
        var corpse = root.GetComponent<ExecutedCorpse>();
        if (!corpse) corpse = root.AddComponent<ExecutedCorpse>();
        var gasCloud = AssetDatabase.LoadAssetAtPath<GameObject>(GasCloudPath) ?? BuildGasCloud();
        var critical = AssetDatabase.LoadAssetAtPath<GameObject>(CriticalVfxPath) ?? BuildCriticalVfx();
        corpse.GasCloudPrefab = gasCloud.GetComponent<CoreGasCloud>();
        corpse.CriticalVfxPrefab = critical.GetComponent<CoreCriticalVfx>();
        corpse.CoreSocket = socket;
        corpse.BeepSfx = SynthClip("CoreCriticalBeep", SynthBeep);
        corpse.WhineSfx = SynthClip("CoreCriticalWhine", SynthWhine);

        Debug.Log($"Core execution: core placed in {root.name} under '{socket.name}' at local " +
                  root.transform.InverseTransformPoint(core.transform.position).ToString("F3"));
        // The hatch the Disassembler opens onto the core sits over it, so it moves with it.
        DisassemblerBuilder.InstallWindow(root);
        return true;
    }

    internal static Transform FirstBone(Dictionary<HumanBodyBones, Transform> bones, params HumanBodyBones[] order)
    {
        foreach (var bone in order)
            if (bones.TryGetValue(bone, out Transform t) && t) return t;
        return null;
    }

    // The front of the chest at sternum height: the forward-most body vertex in a narrow column there.
    internal static Vector3 ChestSurface(Transform root, SkinnedMeshRenderer body, Dictionary<HumanBodyBones, Transform> bones,
        Transform socket)
    {
        Vector3 chest = root.InverseTransformPoint(socket.position);
        Transform neck = FirstBone(bones, HumanBodyBones.Neck, HumanBodyBones.Head);
        float height = neck ? Mathf.Lerp(chest.y, root.InverseTransformPoint(neck.position).y, .25f) : chest.y;
        var fallback = root.TransformPoint(new Vector3(chest.x, height, chest.z + .05f));
        if (!body || !body.sharedMesh) return fallback;

        var baked = new Mesh();
        body.BakeMesh(baked, true);
        Vector3[] vertices = baked.vertices;
        Object.DestroyImmediate(baked);

        Transform bodyTransform = body.transform;
        float front = float.NegativeInfinity;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 local = root.InverseTransformPoint(bodyTransform.position + bodyTransform.rotation * vertices[i]);
            if (Mathf.Abs(local.x - chest.x) > .06f || Mathf.Abs(local.y - height) > .05f) continue;
            front = Mathf.Max(front, local.z);
        }
        if (float.IsNegativeInfinity(front)) return fallback;
        // Deep enough that the whole orb is inside the chest; the galaxy shader shows it through the surface.
        return root.TransformPoint(new Vector3(chest.x, height, front - OrbRadius - OrbInset));
    }

    static void InstallOnPlayer()
    {
        var contents = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            var executioner = contents.GetComponent<CoreExecutioner>();
            if (!executioner) executioner = contents.AddComponent<CoreExecutioner>();
            executioner.MuzzleChargeVfx = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleVfxPath);
            executioner.ImpaleSfx = SynthClip("CoreImpale", SynthImpale);
            executioner.AbsorbSfx = SynthClip("CoreAbsorb", SynthAbsorb);
            executioner.RipSfx = SynthClip("CoreRip", SynthRip);
            executioner.HealSfx = FindClip("Pickup_Health");
            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // ------------------------------------------------------------------ Core prefab

    public static GameObject BuildCorePrefab()
    {
        EnsureFolder(MaterialFolder);
        Material orbMaterial = LoadOrCreateMaterial("M_UnstableOrb", OrbShader);
        Material haloMaterial = LoadOrCreateMaterial("M_CoreHalo", HaloShader);
        haloMaterial.SetFloat("_OrbFraction", HaloOrbFraction);
        EditorUtility.SetDirty(haloMaterial);
        Material spark = EnergyMaterial("M_CoreSpark", 0f, 4f, 1f, Color.white);
        Material ring = EnergyMaterial("M_CoreShockwave", 1f, 3f, 0f, Color.white);
        Material stream = EnergyMaterial("M_CoreStream", 0f, 3.5f, 1.2f, Color.white);

        var root = new GameObject("VFX_UnstableCore");
        try
        {
            // The orb, scaled from the sphere mesh's own radius (the built-in Sphere.fbx asset has radius 1).
            var sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            var orb = new GameObject("Orb");
            orb.transform.SetParent(root.transform, false);
            orb.transform.localScale = Vector3.one * (OrbRadius / sphere.bounds.extents.x);
            orb.AddComponent<MeshFilter>().sharedMesh = sphere;
            var orbRenderer = orb.AddComponent<MeshRenderer>();
            orbRenderer.sharedMaterial = orbMaterial;
            NoShadows(orbRenderer);

            var halo = new GameObject("Halo");
            halo.transform.SetParent(root.transform, false);
            halo.transform.localScale = Vector3.one * (2f * OrbRadius / HaloOrbFraction);
            halo.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var haloRenderer = halo.AddComponent<MeshRenderer>();
            haloRenderer.sharedMaterial = haloMaterial;
            NoShadows(haloRenderer);

            var lightObject = new GameObject("Core Light");
            lightObject.transform.SetParent(root.transform, false);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(.4f, .62f, 1f);
            light.range = 3.5f;
            light.intensity = 3f;
            light.shadows = LightShadows.None;

            var embers = Embers(root.transform, spark);
            var surge = SurgeBurst(root.transform, spark);
            var shockwave = Shockwave(root.transform, ring);
            var absorb = AbsorbStream(root.transform, stream);

            var hum = root.AddComponent<AudioSource>();
            hum.clip = SynthClip("CoreHum_Loop", SynthHum);
            hum.loop = true;
            hum.playOnAwake = false;
            hum.spatialBlend = 1f;
            hum.volume = .5f;
            hum.dopplerLevel = 0f;
            hum.rolloffMode = AudioRolloffMode.Linear;
            hum.minDistance = 1f;
            hum.maxDistance = 14f;

            var core = root.AddComponent<UnstableCore>();
            core.OrbRenderer = orbRenderer;
            core.HaloRenderer = haloRenderer;
            core.CoreLight = light;
            core.Embers = embers;
            core.SurgeBurst = surge;
            core.Shockwave = shockwave;
            core.AbsorbStream = absorb;
            core.HumSource = hum;
            core.SurgeSfx = SynthClip("CoreSurge", SynthSurge);
            core.IgniteSfx = core.SurgeSfx;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, CorePrefabPath);
            Debug.Log("Unstable Core prefab saved: " + CorePrefabPath);
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    internal static Material EnergyMaterial(string name, float shape, float intensity, float hotCore, Color tint)
    {
        var material = LoadOrCreateMaterial(name, EnergyShader);
        material.SetFloat("_Shape", shape);
        material.SetFloat("_Intensity", intensity);
        material.SetFloat("_HotCore", hotCore);
        material.SetFloat("_RingWidth", .07f);
        material.SetColor("_TintColor", tint);
        EditorUtility.SetDirty(material);
        return material;
    }

    internal static Material LoadOrCreateMaterial(string name, string shaderName)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var shader = Shader.Find(shaderName);
        if (!shader) Debug.LogError("Core execution: shader not found: " + shaderName);
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material)
        {
            if (shader && material.shader != shader) material.shader = shader;
            return material;
        }
        material = new Material(shader) { name = name };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static ParticleSystem Embers(Transform parent, Material material)
    {
        var ps = NewParticles("Embers", parent, material, 250);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.25f, .6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.8f, 2.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(.012f, .032f);
        main.startColor = new ParticleSystem.MinMaxGradient(k_Azure, k_Ice);
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 38f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = .05f;

        FadeOut(ps, k_Lavender);
        ShrinkOut(ps, .2f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = .7f;
        noise.frequency = 2.5f;
        noise.scrollSpeed = 1.5f;

        Stretch(ps, .07f, 1.4f);
        return ps;
    }

    static ParticleSystem SurgeBurst(Transform parent, Material material)
    {
        var ps = NewParticles("Surge Burst", parent, material, 400);
        var main = ps.main;
        main.loop = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.2f, .55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 7f);
        main.startSize = new ParticleSystem.MinMaxCurve(.015f, .045f);
        main.startColor = new ParticleSystem.MinMaxGradient(k_Ice, k_Azure);
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = .04f;

        var drag = ps.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.drag = 3f;

        FadeOut(ps, k_Lavender);
        ShrinkOut(ps, .1f);
        Stretch(ps, .05f, 1.2f);
        return ps;
    }

    static ParticleSystem Shockwave(Transform parent, Material material)
    {
        var ps = NewParticles("Shockwave", parent, material, 6);
        var main = ps.main;
        main.loop = false;
        main.duration = .5f;
        main.startLifetime = .45f;
        main.startSpeed = 0f;
        main.startSize = 1.5f;
        main.startColor = new Color(.55f, .65f, 1f, .9f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1), new ParticleSystem.Burst(.07f, 1) });

        var shape = ps.shape;
        shape.enabled = false;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, .08f, 0f, 4f), new Keyframe(1f, 1f, 0f, 0f)));

        FadeOut(ps, Color.white);
        return ps;
    }

    static ParticleSystem AbsorbStream(Transform parent, Material material)
    {
        var ps = NewParticles("Absorb Stream", parent, material, 600);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, .9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.3f, 1f);
        main.startSize = new ParticleSystem.MinMaxCurve(.015f, .04f);
        main.startColor = new ParticleSystem.MinMaxGradient(k_Azure, k_Ice);
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = .1f;

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = .3f;
        noise.frequency = 3f;

        Stretch(ps, .045f, 1.3f);
        return ps;
    }

    internal static ParticleSystem NewParticles(string name, Transform parent, Material material, int maxParticles)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.maxParticles = maxParticles;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.gravityModifier = 0f;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        NoShadows(renderer);
        return ps;
    }

    internal static void Stretch(ParticleSystem ps, float velocityScale, float lengthScale)
    {
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = velocityScale;
        renderer.lengthScale = lengthScale;
    }

    internal static void FadeOut(ParticleSystem ps, Color endColor)
    {
        var color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(endColor, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.8f, .5f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
    }

    internal static void ShrinkOut(ParticleSystem ps, float endSize)
    {
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, endSize));
    }

    internal static void NoShadows(Renderer renderer)
    {
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    // Sparks and a swelling glow converging into the barrel; CoreExecutioner drives the emission rate.
    static void BuildMuzzleVfx()
    {
        EnsureFolder(MaterialFolder);
        Material spark = EnergyMaterial("M_CoreSpark", 0f, 4f, 1f, Color.white);
        Material glow = EnergyMaterial("M_CoreMuzzleGlow", 0f, 1.2f, .4f, Color.white);

        var root = new GameObject("VFX_CoreMuzzleCharge");
        try
        {
            var converge = root.AddComponent<ParticleSystem>();
            converge.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = converge.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = 300;
            main.startLifetime = .22f;
            main.startSpeed = -1.1f;
            main.startSize = new ParticleSystem.MinMaxCurve(.006f, .016f);
            main.startColor = new ParticleSystem.MinMaxGradient(k_Azure, k_Ice);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = converge.emission;
            emission.rateOverTime = 0f;
            var shape = converge.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = .2f;
            shape.radiusThickness = 0f;
            FadeOut(converge, k_Lavender);
            var sparkRenderer = converge.GetComponent<ParticleSystemRenderer>();
            sparkRenderer.sharedMaterial = spark;
            NoShadows(sparkRenderer);
            Stretch(converge, .06f, 1.5f);

            var glowObject = new GameObject("Glow");
            glowObject.transform.SetParent(root.transform, false);
            var glowParticles = glowObject.AddComponent<ParticleSystem>();
            glowParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var glowMain = glowParticles.main;
            glowMain.playOnAwake = false;
            glowMain.loop = true;
            glowMain.maxParticles = 150;
            glowMain.startLifetime = .12f;
            glowMain.startSpeed = 0f;
            glowMain.startSize = new ParticleSystem.MinMaxCurve(.08f, .16f);
            glowMain.startColor = new Color(k_Azure.r, k_Azure.g, k_Azure.b, .2f);
            glowMain.simulationSpace = ParticleSystemSimulationSpace.Local;
            glowMain.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var glowEmission = glowParticles.emission;
            glowEmission.rateOverTime = 0f;
            var glowShape = glowParticles.shape;
            glowShape.enabled = false;
            FadeOut(glowParticles, k_Ice);
            var glowRenderer = glowObject.GetComponent<ParticleSystemRenderer>();
            glowRenderer.sharedMaterial = glow;
            NoShadows(glowRenderer);

            PrefabUtility.SaveAsPrefabAsset(root, MuzzleVfxPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // ------------------------------------------------------------------ Gas cloud and critical warning

    // The red galaxy gas an executed enemy bursts into: a flash, embers and a shockwave, then lingering nebula smoke.
    public static GameObject BuildGasCloud()
    {
        EnsureFolder(MaterialFolder);
        Material spark = EnergyMaterial("M_CoreSpark", 0f, 4f, 1f, Color.white);
        Material ring = EnergyMaterial("M_CoreShockwave", 1f, 3f, 0f, Color.white);
        Material gas = GasMaterial();
        const float radius = 3.2f;

        var root = new GameObject("VFX_CoreGasCloud");
        try
        {
            var flash = NewParticles("Blast Flash", root.transform, spark, 6);
            flash.transform.localPosition = Vector3.up;
            var main = flash.main;
            main.playOnAwake = true;
            main.loop = false;
            main.duration = .5f;
            main.startLifetime = .35f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(3.5f, 5f);
            main.startColor = new Color(1f, .25f, .15f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            Burst(flash, 3);
            FadeOut(flash, k_Blood);
            GrowOut(flash, .5f, 1.3f);

            var embers = NewParticles("Blast Embers", root.transform, spark, 120);
            embers.transform.localPosition = Vector3.up;
            main = embers.main;
            main.playOnAwake = true;
            main.loop = false;
            main.duration = .5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.4f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 12f);
            main.startSize = new ParticleSystem.MinMaxCurve(.03f, .08f);
            main.startColor = new ParticleSystem.MinMaxGradient(k_Blood, k_Ember);
            main.gravityModifier = .6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            Burst(embers, 70);
            SphereShape(embers, .3f);
            Drag(embers, 1.5f);
            FadeOut(embers, k_Blood);
            Stretch(embers, .05f, 1.3f);

            var shockwave = NewParticles("Blast Ring", root.transform, ring, 4);
            shockwave.transform.localPosition = Vector3.up * .1f;
            main = shockwave.main;
            main.playOnAwake = true;
            main.loop = false;
            main.duration = .5f;
            main.startLifetime = .55f;
            main.startSpeed = 0f;
            main.startSize = 2f * radius * 1.25f / RingRadiusFraction;
            main.startColor = new Color(1f, .25f, .2f, .9f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = shockwave.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1), new ParticleSystem.Burst(.08f, 1) });
            GrowOut(shockwave, .08f, 1f);
            FadeOut(shockwave, Color.white);
            shockwave.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;

            var gasParticles = NewParticles("Gas", root.transform, gas, 80);
            gasParticles.transform.localPosition = Vector3.up * .55f;
            main = gasParticles.main;
            main.playOnAwake = true;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.2f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(2.2f, 3.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white, new Color(.85f, .7f, .85f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            emission = gasParticles.emission;
            emission.rateOverTime = 6f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 20) });
            var shape = gasParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = radius * .6f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            // Flatten the dome so the gas rolls out along the floor rather than billowing upward.
            shape.scale = new Vector3(1f, 1f, .45f);
            Drag(gasParticles, 1.2f);
            var spin = gasParticles.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-.2f, .2f);
            GrowOut(gasParticles, .55f, 1.15f);
            var gasColor = gasParticles.colorOverLifetime;
            gasColor.enabled = true;
            var gasGradient = new Gradient();
            gasGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.85f, .12f), new GradientAlphaKey(.7f, .6f), new GradientAlphaKey(0f, 1f) });
            gasColor.color = gasGradient;
            var noise = gasParticles.noise;
            noise.enabled = true;
            noise.strength = .25f;
            noise.frequency = .4f;
            var gasRenderer = gasParticles.GetComponent<ParticleSystemRenderer>();
            gasRenderer.sortMode = ParticleSystemSortMode.Distance;
            GasVertexStreams(gasRenderer);

            var stars = NewParticles("Stars", root.transform, spark, 80);
            stars.transform.localPosition = Vector3.up * .9f;
            main = stars.main;
            main.playOnAwake = true;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.05f, .3f);
            main.startSize = new ParticleSystem.MinMaxCurve(.03f, .07f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .8f, .85f), Color.white);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            emission = stars.emission;
            emission.rateOverTime = 18f;
            SphereShape(stars, radius * .7f);
            var twinkle = stars.sizeOverLifetime;
            twinkle.enabled = true;
            twinkle.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(.5f, 1f), new Keyframe(1f, 0f)));

            var lightObject = new GameObject("Gas Light");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = Vector3.up * 1.2f;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, .18f, .12f);
            light.range = radius * 2.2f;
            light.intensity = 4f;
            light.shadows = LightShadows.None;

            var hiss = root.AddComponent<AudioSource>();
            hiss.clip = SynthClip("CoreGasHiss_Loop", SynthHiss);
            hiss.loop = true;
            hiss.playOnAwake = false;
            hiss.spatialBlend = 1f;
            hiss.volume = .6f;
            hiss.dopplerLevel = 0f;
            hiss.rolloffMode = AudioRolloffMode.Linear;
            hiss.minDistance = 2f;
            hiss.maxDistance = 25f;

            var cloud = root.AddComponent<CoreGasCloud>();
            cloud.Radius = radius;
            cloud.Gas = gasParticles;
            cloud.Stars = stars;
            cloud.CloudLight = light;
            cloud.Hiss = hiss;
            cloud.BlastSfx = SynthClip("CoreGasBlast", SynthGasBlast);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, GasCloudPath);
            Debug.Log("Core gas cloud prefab saved: " + GasCloudPath);
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // The countdown after an execution: red sparks and smoke from the chest, a light, and blast-radius rings on the floor.
    public static GameObject BuildCriticalVfx()
    {
        EnsureFolder(MaterialFolder);
        Material spark = EnergyMaterial("M_CoreSpark", 0f, 4f, 1f, Color.white);
        Material ring = EnergyMaterial("M_CoreShockwave", 1f, 3f, 0f, Color.white);
        Material gas = GasMaterial();
        var gasCloud = AssetDatabase.LoadAssetAtPath<GameObject>(GasCloudPath);
        float radius = gasCloud ? gasCloud.GetComponent<CoreGasCloud>().Radius : 3.2f;
        float ringSize = 2f * radius / RingRadiusFraction;

        var root = new GameObject("VFX_CoreCritical");
        try
        {
            var chest = new GameObject("Chest").transform;
            chest.SetParent(root.transform, false);
            var ground = new GameObject("Ground").transform;
            ground.SetParent(root.transform, false);

            var sparks = NewParticles("Sparks", chest, spark, 250);
            var main = sparks.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.2f, .5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(.015f, .04f);
            main.startColor = new ParticleSystem.MinMaxGradient(k_Blood, k_Ember);
            main.gravityModifier = .8f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = sparks.emission;
            emission.rateOverTime = 25f;
            UpwardCone(sparks, 35f, .08f);
            FadeOut(sparks, k_Blood);
            Stretch(sparks, .06f, 1.3f);

            var smoke = NewParticles("Smoke", chest, gas, 40);
            main = smoke.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.8f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.3f, .8f);
            main.startSize = new ParticleSystem.MinMaxCurve(.25f, .5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(1f, 1f, 1f, .6f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            emission = smoke.emission;
            emission.rateOverTime = 4f;
            UpwardCone(smoke, 25f, .1f);
            GrowOut(smoke, .4f, 1.4f);
            FadeOut(smoke, Color.white);
            GasVertexStreams(smoke.GetComponent<ParticleSystemRenderer>());

            var lightObject = new GameObject("Warning Light");
            lightObject.transform.SetParent(chest, false);
            lightObject.transform.localPosition = Vector3.up * .3f;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = k_Blood;
            light.range = 4f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;

            // Rings the size of the blast that shrink into the body, faster as the blast nears.
            var collapsing = NewParticles("Collapsing Rings", ground, ring, 24);
            collapsing.transform.localPosition = Vector3.up * .04f;
            main = collapsing.main;
            main.loop = true;
            main.startLifetime = .55f;
            main.startSpeed = 0f;
            main.startSize = ringSize;
            main.startColor = new Color(1f, .15f, .1f, 1f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            emission = collapsing.emission;
            emission.rateOverTime = 1.5f;
            var collapsingShape = collapsing.shape;
            collapsingShape.enabled = false;
            var shrink = collapsing.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f, 0f, -.5f), new Keyframe(1f, .06f, -2f, 0f)));
            var ringColor = collapsing.colorOverLifetime;
            ringColor.enabled = true;
            var ringGradient = new Gradient();
            ringGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(k_Ember, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .15f), new GradientAlphaKey(.4f, 1f) });
            ringColor.color = ringGradient;
            collapsing.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;

            // A steady pulse marking the edge of the blast.
            var boundary = NewParticles("Blast Boundary", ground, ring, 6);
            boundary.transform.localPosition = Vector3.up * .05f;
            main = boundary.main;
            main.playOnAwake = true;
            main.loop = true;
            main.startLifetime = .5f;
            main.startSpeed = 0f;
            main.startSize = ringSize;
            main.startColor = new Color(1f, .12f, .08f, .7f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            emission = boundary.emission;
            emission.rateOverTime = 2.5f;
            var boundaryShape = boundary.shape;
            boundaryShape.enabled = false;
            FadeOut(boundary, k_Blood);
            boundary.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;

            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.dopplerLevel = 0f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.minDistance = 2f;
            audio.maxDistance = 30f;

            var vfx = root.AddComponent<CoreCriticalVfx>();
            vfx.ChestGroup = chest;
            vfx.GroundGroup = ground;
            vfx.Sparks = sparks;
            vfx.Smoke = smoke;
            vfx.GroundRing = collapsing;
            vfx.WarningLight = light;
            vfx.Audio = audio;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, CriticalVfxPath);
            Debug.Log("Core critical warning prefab saved: " + CriticalVfxPath);
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static Material GasMaterial() => LoadOrCreateMaterial("M_GalaxyGas", GasShader);

    // The gas shader reads a per-puff random seed from TEXCOORD0.z.
    static void GasVertexStreams(ParticleSystemRenderer renderer)
    {
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal,
            ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.StableRandomX,
        });
    }

    // Lives in Resources so the HUD can load it at runtime and builds keep the shader.
    static void BuildOverlayMaterial()
    {
        EnsureFolder(Path.GetDirectoryName(OverlayMaterialPath).Replace('\\', '/'));
        var shader = Shader.Find(OverlayShader);
        if (!shader)
        {
            Debug.LogError("Core execution: shader not found: " + OverlayShader);
            return;
        }
        var material = AssetDatabase.LoadAssetAtPath<Material>(OverlayMaterialPath);
        if (material)
        {
            if (material.shader != shader) material.shader = shader;
            return;
        }
        AssetDatabase.CreateAsset(new Material(shader) { name = "M_ToxicGasOverlay" }, OverlayMaterialPath);
    }

    internal static void Burst(ParticleSystem ps, int count)
    {
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
    }

    internal static void SphereShape(ParticleSystem ps, float radius)
    {
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;
    }

    internal static void UpwardCone(ParticleSystem ps, float angle, float radius)
    {
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = angle;
        shape.radius = radius;
        shape.rotation = new Vector3(-90f, 0f, 0f);
    }

    internal static void Drag(ParticleSystem ps, float drag)
    {
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.drag = drag;
    }

    internal static void GrowOut(ParticleSystem ps, float startSize, float endSize)
    {
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, startSize, 1f, endSize));
    }

    // ------------------------------------------------------------------ Sound synthesis

    internal delegate float[] Synth(System.Random random);

    // Writes a generated clip the first time; afterwards the file on disk is kept, so it can be replaced.
    internal static AudioClip SynthClip(string name, Synth synth)
    {
        EnsureFolder(AudioFolder);
        string path = AudioFolder + "/" + name + ".wav";
        if (!File.Exists(path))
        {
            WriteWav(path, synth(new System.Random(name.GetHashCode())));
            AssetDatabase.ImportAsset(path);
        }
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    // Deep two-tone drone with a slow wobble, hiss and random electrical crackles. Loops seamlessly.
    static float[] SynthHum(System.Random random)
    {
        const float length = 2f, crossfade = .15f;
        int loop = (int)(length * SampleRate), fade = (int)(crossfade * SampleRate);
        var raw = new float[loop + fade];
        var crackles = new List<Vector2>();
        for (int i = 0; i < 16; i++) crackles.Add(new Vector2((float)random.NextDouble() * (length + crossfade), .1f + .25f * (float)random.NextDouble()));

        float hiss = 0f;
        for (int i = 0; i < raw.Length; i++)
        {
            float t = i / (float)SampleRate;
            float wobble = .8f + .2f * Mathf.Sin(2f * Mathf.PI * 3f * t);
            float s = .34f * Mathf.Sin(2f * Mathf.PI * 55f * t) +
                      .2f * Mathf.Sin(2f * Mathf.PI * 110.5f * t) +
                      .1f * Mathf.Sin(2f * Mathf.PI * 165f * t + .8f * Mathf.Sin(2f * Mathf.PI * 1.5f * t));
            s *= wobble;
            hiss += (Noise(random) - hiss) * .08f;
            s += hiss * .12f;
            foreach (var crackle in crackles)
            {
                float age = t - crackle.x;
                if (age >= 0f && age < .04f) s += Noise(random) * crackle.y * Mathf.Exp(-age * 90f);
            }
            raw[i] = s;
        }

        var clip = new float[loop];
        for (int i = 0; i < loop; i++)
        {
            float k = i < fade ? i / (float)fade : 1f;
            clip[i] = i < fade ? raw[i] * k + raw[loop + i] * (1f - k) : raw[i];
        }
        return Normalize(clip, .7f);
    }

    // A short electric zap for surges.
    static float[] SynthSurge(System.Random random)
    {
        var samples = new float[(int)(.4f * SampleRate)];
        var filter = new BandPass();
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float gate = Mathf.Sin(2f * Mathf.PI * (70f + 40f * t) * t) > 0f ? 1f : .2f;
            float zap = filter.Process(Noise(random), Mathf.Lerp(3200f, 900f, t / .4f), 3f) * gate;
            float chirp = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(1300f, 250f, t / .4f) * t) * .35f;
            float envelope = Mathf.Clamp01(t / .003f) * Mathf.Exp(-t * 11f);
            samples[i] = (zap * 2.2f + chirp) * envelope;
        }
        return Normalize(samples, .8f);
    }

    // The gun hitting the core: a heavy thump, a metallic clang and a sizzle.
    static float[] SynthImpale(System.Random random)
    {
        var samples = new float[(int)(.8f * SampleRate)];
        float low = 0f, phase = 0f;
        float[] clang = { 523f, 1187f, 1630f, 2270f, 3120f };
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            phase += 2f * Mathf.PI * Mathf.Lerp(85f, 38f, Mathf.Clamp01(t / .25f)) / SampleRate;
            float thump = Mathf.Sin(phase) * Mathf.Exp(-t * 7f);
            low += (Noise(random) - low) * .15f;
            float crunch = low * 2.5f * Mathf.Exp(-t * 28f);
            float metal = 0f;
            for (int c = 0; c < clang.Length; c++) metal += Mathf.Sin(2f * Mathf.PI * clang[c] * t) / (c + 1.5f);
            metal *= .45f * Mathf.Exp(-t * 8f);
            float sizzle = Noise(random) * .12f * Mathf.Exp(-t * 3.5f) * Mathf.Clamp01(t / .02f);
            samples[i] = (thump + crunch + metal + sizzle) * Mathf.Clamp01(t / .002f);
        }
        return Normalize(samples, .95f);
    }

    // The core being sucked into the barrel: rising filtered rush, climbing tone and building crackle.
    static float[] SynthAbsorb(System.Random random)
    {
        const float length = 1.8f;
        var samples = new float[(int)(length * SampleRate)];
        var rush = new BandPass();
        float phase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float p = t / length;
            float center = 250f * Mathf.Pow(12f, p);
            float wind = rush.Process(Noise(random), center, 4f) * (1.2f + 1.8f * p);
            phase += 2f * Mathf.PI * (Mathf.Lerp(90f, 540f, p * p) * (1f + .02f * Mathf.Sin(2f * Mathf.PI * 8f * t))) / SampleRate;
            float tone = (Mathf.Sin(phase) + .4f * Mathf.Sin(phase * 2.01f)) * (.25f + .35f * p);
            float crackle = random.NextDouble() < .0015 + .006 * p ? Noise(random) * 1.5f : 0f;
            float envelope = Mathf.Clamp01(t / .08f) * Mathf.Clamp01((length - t) / .06f);
            samples[i] = (wind + tone + crackle) * envelope;
        }
        return Normalize(samples, .85f);
    }

    // Tearing the core free: a downward whoosh into a deep boom.
    static float[] SynthRip(System.Random random)
    {
        const float length = 1.1f;
        var samples = new float[(int)(length * SampleRate)];
        var whoosh = new BandPass();
        float phase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float swept = whoosh.Process(Noise(random), Mathf.Lerp(3500f, 180f, Mathf.Clamp01(t / .45f)), 2.5f) *
                          Mathf.Exp(-t * 4f) * 2f;
            phase += 2f * Mathf.PI * Mathf.Lerp(70f, 32f, Mathf.Clamp01(t / .6f)) / SampleRate;
            float boom = Mathf.Sin(phase) * Mathf.Exp(-t * 3.2f) * 1.1f;
            float snap = Noise(random) * Mathf.Exp(-t * 60f);
            samples[i] = (swept + boom + snap) * Mathf.Clamp01(t / .002f) * Mathf.Clamp01((length - t) / .05f);
        }
        return Normalize(samples, .95f);
    }

    // A sharp warning beep.
    static float[] SynthBeep(System.Random random)
    {
        var samples = new float[(int)(.09f * SampleRate)];
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float tone = Mathf.Sin(2f * Mathf.PI * 1750f * t) + .35f * Mathf.Sin(2f * Mathf.PI * 3500f * t);
            samples[i] = tone * Mathf.Clamp01(t / .002f) * Mathf.Exp(-t * 28f);
        }
        return Normalize(samples, .8f);
    }

    // Two seconds of overload: a climbing whine with a tremolo that speeds up, and building crackle.
    static float[] SynthWhine(System.Random random)
    {
        const float length = 2f;
        var samples = new float[(int)(length * SampleRate)];
        float phase = 0f, detuned = 0f, tremolo = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float p = t / length;
            float frequency = 220f * Mathf.Pow(10f, p);
            phase += 2f * Mathf.PI * frequency / SampleRate;
            detuned += 2f * Mathf.PI * frequency * 1.012f / SampleRate;
            tremolo += 2f * Mathf.PI * Mathf.Lerp(6f, 30f, p) / SampleRate;
            float tone = (Mathf.Sin(phase) + .6f * Mathf.Sin(detuned)) * (.55f + .45f * Mathf.Sin(tremolo));
            float crackle = random.NextDouble() < .001 + .01 * p ? Noise(random) * 1.4f : 0f;
            float envelope = Mathf.Lerp(.25f, 1f, p * p) * Mathf.Clamp01(t / .05f) * Mathf.Clamp01((length - t) / .03f);
            samples[i] = (tone + crackle) * envelope;
        }
        return Normalize(samples, .7f);
    }

    // The core bursting into gas: a deep boom, a crack and a long venting hiss.
    static float[] SynthGasBlast(System.Random random)
    {
        const float length = 2.2f;
        var samples = new float[(int)(length * SampleRate)];
        var vent = new BandPass();
        float low = 0f, phase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            phase += 2f * Mathf.PI * Mathf.Lerp(60f, 26f, Mathf.Clamp01(t / .8f)) / SampleRate;
            float boom = Mathf.Sin(phase) * Mathf.Exp(-t * 2.6f) * 1.2f;
            low += (Noise(random) - low) * .1f;
            float blast = low * 3f * Mathf.Exp(-t * 9f);
            float crack = Noise(random) * Mathf.Exp(-t * 45f);
            float hiss = vent.Process(Noise(random), Mathf.Lerp(1800f, 600f, t / length), 1.5f) *
                         Mathf.Clamp01(t / .15f) * Mathf.Exp(-t * 1.1f) * 1.3f;
            samples[i] = (boom + blast + crack + hiss) * Mathf.Clamp01(t / .002f) * Mathf.Clamp01((length - t) / .1f);
        }
        return Normalize(samples, .95f);
    }

    // Gas venting and churning; loops seamlessly.
    static float[] SynthHiss(System.Random random)
    {
        const float length = 3f, crossfade = .3f;
        int loop = (int)(length * SampleRate), fade = (int)(crossfade * SampleRate);
        var raw = new float[loop + fade];
        var band = new BandPass();
        float rumble = 0f;
        for (int i = 0; i < raw.Length; i++)
        {
            float t = i / (float)SampleRate;
            float wobble = .75f + .25f * Mathf.Sin(2f * Mathf.PI * t / length * 2f);
            float hiss = band.Process(Noise(random), 900f + 300f * Mathf.Sin(2f * Mathf.PI * t / length), 1.2f);
            rumble += (Noise(random) - rumble) * .01f;
            raw[i] = hiss * wobble + rumble * 4f;
        }
        var clip = new float[loop];
        for (int i = 0; i < loop; i++)
        {
            float k = i < fade ? i / (float)fade : 1f;
            clip[i] = i < fade ? raw[i] * k + raw[loop + i] * (1f - k) : raw[i];
        }
        return Normalize(clip, .6f);
    }

    internal class BandPass
    {
        float m_Low, m_Band;

        public float Process(float input, float frequency, float resonance)
        {
            float f = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(frequency, SampleRate * .2f) / SampleRate);
            float q = 1f / Mathf.Max(.5f, resonance);
            m_Low += f * m_Band;
            float high = input - m_Low - q * m_Band;
            m_Band += f * high;
            return m_Band;
        }
    }

    internal static float Noise(System.Random random) => (float)random.NextDouble() * 2f - 1f;

    internal static float[] Normalize(float[] samples, float peak)
    {
        float max = 0f;
        foreach (float s in samples) max = Mathf.Max(max, Mathf.Abs(s));
        if (max > 0f)
            for (int i = 0; i < samples.Length; i++) samples[i] *= peak / max;
        return samples;
    }

    static void WriteWav(string path, float[] samples)
    {
        using (var stream = new FileStream(path, FileMode.Create))
        using (var writer = new BinaryWriter(stream))
        {
            int dataSize = samples.Length * 2;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)1); // mono
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);
            foreach (float s in samples)
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
        }
    }

    // ------------------------------------------------------------------ Helpers

    internal static AudioClip FindClip(string name)
    {
        foreach (var guid in AssetDatabase.FindAssets(name + " t:AudioClip"))
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
            if (clip && clip.name == name) return clip;
        }
        return null;
    }

    internal static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
