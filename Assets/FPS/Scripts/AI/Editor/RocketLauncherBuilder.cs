using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds the rocket launcher (the "armour buster") and its containment cradle in the Boss Hall (corebuster_game_004).
///
/// The launcher hovers over the Final Assembly cradle's deck, held by two containment beams (one to its barrel, one to
/// its stock) from emitter posts on the deck. A power panel on the cradle's front face is overloaded exactly as the
/// reactor consoles are, by injecting a full Disassembler's three cores. When it is spent the beams cut out, the
/// launcher drops onto the deck and can be taken, as the sidearm is taken from its locker.
///
/// The launcher fires rockets built from the Elite Rifleman's projectile (its steel bolt, hot tip and trail). Their
/// blast tears the armour off every armoured enemy in reach but deals no damage (<see cref="ArmourBreakBlast"/>): the
/// exposed body is then finished with the main gun or a core execution.
///
/// Everything is generated from code: the launcher, rocket, emitter and panel meshes, the materials, the HUD icon and
/// the explosion (layered on the template's VFX_Explosion with fire, sparks, embers, smoke and a brief light, about a
/// hundred particles in all). Scene objects live under their own root, rebuilt from scratch each run; the prefabs and
/// art are rewritten in place.
/// </summary>
public static class RocketLauncherBuilder
{
    const string ScenePath = "Assets/FPS/Scenes/MainScene.unity";
    const string RootName = "===== ROCKET LAUNCHER =====";
    const string ArtRoot = "Assets/FPS/Art/RocketLauncher";
    const string MeshFolder = ArtRoot + "/Meshes";
    const string MaterialFolder = ArtRoot + "/Materials";
    const string TextureFolder = ArtRoot + "/Textures";
    const string WeaponPath = "Assets/FPS/Prefabs/Weapons/Weapon_RocketLauncher.prefab";
    const string ProjectilePath = "Assets/FPS/Prefabs/Projectiles/Projectile_Rocket.prefab";
    const string ExplosionPath = "Assets/FPS/Prefabs/VFX/VFX_RocketExplosion.prefab";
    const string BlasterPath = "Assets/FPS/Prefabs/Weapons/Weapon_Blaster.prefab";
    const string EliteRivetPath = "Assets/FPS/Prefabs/Projectiles/Projectile_Rivet_Elite.prefab";
    const string ClassicExplosionPath = "Assets/FPS/Prefabs/VFX/VFX_Explosion.prefab";
    const string MuzzleFlashPath = "Assets/FPS/Prefabs/VFX/VFX_MuzzleFlashRed.prefab";
    const string LightningPath = "Assets/FPS/Art/Materials/CoreBuster/M_Lightning.mat";
    const string HeartMaterialPath = "Assets/FPS/Art/Materials/CoreBuster/M_UnstableOrb.mat";
    const string SparkMaterialPath = "Assets/FPS/Art/Materials/VFX/Spark.mat";
    const string SmokeMaterialPath = "Assets/FPS/Art/Materials/VFX/SmokeOverheat.mat";
    const string FlareMaterialPath = "Assets/FPS/Art/Materials/VFX/ImpactFlare.mat";
    const string TitleFontPath = "Assets/Gentleland/SteampunkUI/Fonts/BebasNeue/BebasNeue SDF.asset";
    const string BodyFontPath = "Assets/FPS/Art/Fonts/Roboto-Regular SDF.asset";

    // The Final Assembly cradle (world): its deck top and front face, the hall floor in front of it, the point the
    // launcher hovers at between the two emitters, and the panel's place on the front face (mirroring the sign).
    const float DeckTop = 8.7f, DeckFront = -324.43f, HallFloor = 6.2f;
    static readonly Vector3 k_Hover = new Vector3(-325.7f, 9.5f, 115.3f);
    static readonly float[] k_EmitterZ = { 112.2f, 118.4f };
    const float PanelZ = 109.2f;

    // The launcher, in its own space: +Z out of the muzzle, origin at the grip, the tube's axis on Y = 0. A little longer
    // than the blaster, so the two read as a set.
    const float TubeRadius = .072f, TubeRear = -.47f, TubeFront = .46f, GripDrop = .2f;
    // The cradle's display model is shown larger than the one in hand, so it reads across the hall.
    const float DisplayScale = 1.4f;
    // One rocket, back after this many seconds.
    const float Cooldown = 15f;
    const string HudPrefabPath = "Assets/FPS/Prefabs/UI/GameHUD.prefab";

    class Mats
    {
        public Material Body, Dark, Grill, Glow, Steel, Hazard, Warhead, Lamp, Port, PanelLamp;
        public Material RivetSteel, RivetTip, RivetTrail, Lightning, Spark, Smoke, Flare;
    }

    [MenuItem("Tools/CoreBuster/Build Rocket Launcher")]
    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError("Rocket launcher: open " + ScenePath + " first.");
            return;
        }
        foreach (var folder in new[] { MeshFolder, MaterialFolder, TextureFolder }) EnsureFolder(folder);

        var m = BuildMaterials();
        var launcherMesh = LauncherMesh();
        var rocketMesh = RocketMesh();

        var explosion = BuildExplosion(m);
        var projectile = BuildProjectile(m, rocketMesh, explosion);
        var weapon = BuildWeapon(m, launcherMesh, projectile);

        var old = GameObject.Find(RootName);
        if (old) Object.DestroyImmediate(old);
        var root = new GameObject(RootName).transform;
        var cradle = BuildCradle(root, m, launcherMesh, weapon);
        BuildPanel(root, m, cradle);
        BuildHudGauge();

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Rocket launcher built: {WeaponPath}, {ProjectilePath}, {ExplosionPath} and the cradle under {RootName}.");
    }

    // ------------------------------------------------------------------ materials

    static Mats BuildMaterials()
    {
        var rivet = AssetDatabase.LoadAssetAtPath<GameObject>(EliteRivetPath);
        Material RivetMaterial(string part) => rivet.transform.Find("Rivet Bolt/" + part)?.GetComponent<Renderer>()?.sharedMaterial;
        var m = new Mats
        {
            // The blaster's palette: rust-red plating, near-black shroud and fittings, dark ribbed grilles and
            // glowing yellow hoops and studs.
            Body = Lit("M_RL_Body", new Color(.42f, .15f, .11f), .55f, .45f),
            Dark = Lit("M_RL_Dark", new Color(.07f, .065f, .07f), .65f, .5f),
            Grill = Lit("M_RL_Grill", new Color(.14f, .11f, .11f), .5f, .3f),
            Glow = Emissive("M_RL_Glow", new Color(1f, .78f, .12f), 3f),
            Steel = Lit("M_RL_Steel", new Color(.24f, .25f, .27f), .75f, .5f),
            Hazard = Lit("M_RL_Hazard", new Color(.92f, .66f, .08f), .2f, .45f),
            Lamp = Emissive("M_RL_EmitterLamp", new Color(.35f, .75f, 1f), 3f),
            PanelLamp = Emissive("M_RL_PanelLamp", new Color(1f, .55f, .1f), 2f),
            Port = Lit("M_RL_Port", new Color(.02f, .02f, .025f), .6f, .7f),
            RivetSteel = RivetMaterial("Shank"),
            RivetTip = RivetMaterial("Hot tip"),
            RivetTrail = rivet.GetComponent<TrailRenderer>()?.sharedMaterial,
            Lightning = AssetDatabase.LoadAssetAtPath<Material>(LightningPath),
            Spark = AssetDatabase.LoadAssetAtPath<Material>(SparkMaterialPath),
            Smoke = AssetDatabase.LoadAssetAtPath<Material>(SmokeMaterialPath),
            Flare = AssetDatabase.LoadAssetAtPath<Material>(FlareMaterialPath),
        };
        // The loaded rocket's nose, glowing in the muzzle, is the elite rivet's hot tip.
        m.Warhead = m.RivetTip ? m.RivetTip : m.Glow;
        return m;
    }

    static Material LoadOrCreate(string name)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader) material.shader = shader;
        return material;
    }

    static Material Lit(string name, Color color, float metallic, float smoothness)
    {
        var material = LoadOrCreate(name);
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        material.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material Emissive(string name, Color color, float intensity)
    {
        var material = LoadOrCreate(name);
        material.SetColor("_BaseColor", color * .3f);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", .7f);
        material.SetColor("_EmissionColor", color * intensity);
        material.EnableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(material);
        return material;
    }

    // ------------------------------------------------------------------ meshes

    // A lathe profile (radius, z) turned about the launcher's Z axis.
    static void LatheZ(TWMesh m, Vector2[] profile, int sides, float y = 0f)
    {
        m.At(new Vector3(0f, y, 0f), Quaternion.Euler(90f, 0f, 0f));
        m.Lathe(profile, sides, Vector3.zero);
        m.At(Vector3.zero, Quaternion.identity);
    }

    /// <summary>
    /// The launcher, styled after the blaster: a rust-red tube with a black venturi and a heavy black muzzle shroud (a
    /// loaded rocket's hot nose showing in it), a plated top housing, dark ribbed cooling grilles down both flanks, two
    /// glowing yellow hoops round the tube and glowing studs, a black pistol grip and foregrip, an angled rust brace
    /// down to a shoulder pad, and a sight with a glowing lens. Material slots: body, dark, grille, glow, warhead.
    /// </summary>
    static Mesh LauncherMesh()
    {
        var m = new TWMesh();
        // Tube, venturi and muzzle shroud (back inside the bore).
        m.Sub(0);
        LatheZ(m, new[] { new Vector2(TubeRadius, TubeRear + .06f), new Vector2(TubeRadius, TubeFront - .17f) }, 24);
        m.Sub(1);
        LatheZ(m, new[]
        {
            new Vector2(.056f, TubeRear + .06f), new Vector2(.056f, TubeRear), new Vector2(.097f, TubeRear),
            new Vector2(.097f, TubeRear + .04f), new Vector2(TubeRadius, TubeRear + .06f),
        }, 24);
        LatheZ(m, new[]
        {
            new Vector2(TubeRadius, TubeFront - .17f), new Vector2(.1f, TubeFront - .14f), new Vector2(.1f, TubeFront - .03f),
            new Vector2(.107f, TubeFront), new Vector2(.068f, TubeFront), new Vector2(.068f, TubeFront - .16f),
        }, 24);
        // A band of dark ribs round the shroud's waist.
        for (int i = 0; i < 8; i++)
        {
            var rotation = Quaternion.Euler(0f, 0f, i * 45f);
            m.Box(rotation * new Vector3(0f, .103f, TubeFront - .085f), new Vector3(.025f, .012f, .07f), rotation);
        }
        // The loaded rocket's nose, just out of the muzzle.
        m.Sub(4);
        LatheZ(m, new[]
        {
            new Vector2(0f, TubeFront + .03f), new Vector2(.028f, TubeFront - .005f), new Vector2(.055f, TubeFront - .06f),
            new Vector2(.055f, TubeFront - .15f),
        }, 18);
        // Plated top housing with a sloped nose.
        m.Sub(0);
        m.Box(new Vector3(0f, .085f, -.04f), new Vector3(.1f, .055f, .44f));
        m.Box(new Vector3(0f, .07f, .21f), new Vector3(.085f, .035f, .1f), Quaternion.Euler(12f, 0f, 0f));
        m.Sub(1);
        m.Box(new Vector3(0f, .116f, -.04f), new Vector3(.07f, .012f, .36f));
        // Ribbed cooling grilles on both flanks, on dark backing plates.
        foreach (float side in new[] { -1f, 1f })
        {
            m.Sub(1);
            m.Box(new Vector3(side * .077f, 0f, -.04f), new Vector3(.012f, .085f, .34f));
            m.Sub(2);
            for (int i = 0; i < 7; i++)
                m.Box(new Vector3(side * .088f, 0f, -.19f + i * .05f), new Vector3(.018f, .072f, .016f));
        }
        // Two glowing hoops round the tube, as on the blaster, and glowing studs on the housing.
        m.Sub(3);
        foreach (float z in new[] { -.27f, .17f })
        {
            var ring = new List<Vector2>();
            for (int i = 0; i <= 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                ring.Add(new Vector2(.094f + Mathf.Cos(a) * .009f, z + Mathf.Sin(a) * .009f));
            }
            LatheZ(m, ring.ToArray(), 24);
        }
        foreach (float side in new[] { -1f, 1f })
            foreach (float z in new[] { .02f, .1f })
                m.Box(new Vector3(side * .051f, .085f, z), new Vector3(.006f, .018f, .018f));
        // Pistol grip, trigger guard and foregrip.
        m.Sub(1);
        m.Box(new Vector3(0f, -.115f, .02f), new Vector3(.04f, .15f, .055f), Quaternion.Euler(-14f, 0f, 0f));
        m.Box(new Vector3(0f, -.078f, .07f), new Vector3(.012f, .012f, .08f));
        m.Box(new Vector3(0f, -.1f, .24f), new Vector3(.036f, .11f, .045f), Quaternion.Euler(-8f, 0f, 0f));
        m.Box(new Vector3(0f, -.062f, .02f), new Vector3(.05f, .03f, .13f));
        // Angled brace down to the shoulder pad.
        m.Sub(0);
        m.Box(new Vector3(0f, -.075f, -.29f), new Vector3(.028f, .028f, .2f), Quaternion.Euler(-22f, 0f, 0f));
        m.Sub(1);
        m.Box(new Vector3(0f, -.13f, -.4f), new Vector3(.05f, .09f, .04f));
        // Sight on the left, its lens glowing.
        m.Box(new Vector3(-.055f, .135f, .06f), new Vector3(.032f, .04f, .09f));
        m.Sub(3);
        m.Box(new Vector3(-.055f, .138f, .106f), new Vector3(.022f, .026f, .004f));
        return m.Save(MeshFolder + "/SM_RL_Launcher.asset");
    }

    /// <summary>The rocket, +Z forward: a steel body, the elite rivet's hot nose, and four fins. Slots: steel, tip, fins.</summary>
    static Mesh RocketMesh()
    {
        var m = new TWMesh();
        m.Sub(0);
        LatheZ(m, new[]
        {
            new Vector2(0f, -.26f), new Vector2(.035f, -.26f), new Vector2(.05f, -.22f), new Vector2(.052f, .1f),
        }, 16);
        m.Sub(1);
        LatheZ(m, new[] { new Vector2(.052f, .1f), new Vector2(.04f, .19f), new Vector2(.016f, .25f), new Vector2(0f, .26f) }, 16);
        m.Sub(2);
        for (int i = 0; i < 4; i++)
        {
            var rotation = Quaternion.Euler(0f, 0f, 45f + i * 90f);
            m.Box(rotation * new Vector3(0f, .075f, -.19f), new Vector3(.008f, .055f, .12f), rotation);
        }
        return m.Save(MeshFolder + "/SM_RL_Rocket.asset");
    }

    /// <summary>A containment emitter post, base on the origin: plate, column, coil rings and a glowing head. Slots: steel, lamp.</summary>
    static Mesh EmitterMesh(out float headHeight)
    {
        headHeight = .8f;
        var m = new TWMesh();
        m.Sub(0);
        m.Lathe(new[] { new Vector2(0f, 0f), new Vector2(.16f, 0f), new Vector2(.16f, .04f), new Vector2(.09f, .07f), new Vector2(0f, .07f) }, 20, Vector3.zero);
        m.Cylinder(Vector3.zero, new Vector3(0f, headHeight - .08f, 0f), .045f, 14);
        m.Sub(1);
        foreach (float y in new[] { .32f, .45f, .58f })
            m.Lathe(new[] { new Vector2(.06f, y - .015f), new Vector2(.085f, y), new Vector2(.06f, y + .015f) }, 18, Vector3.zero);
        var ball = new List<Vector2>();
        for (int i = 0; i <= 10; i++)
        {
            float a = Mathf.Lerp(-90f, 90f, i / 10f) * Mathf.Deg2Rad;
            ball.Add(new Vector2(Mathf.Cos(a) * .075f, headHeight + Mathf.Sin(a) * .075f));
        }
        m.Lathe(ball, 18, Vector3.zero);
        return m.Save(MeshFolder + "/SM_RL_Emitter.asset");
    }

    /// <summary>
    /// The power panel, in its own space (+Z out toward the player, origin on the floor at the wall): a housing with a
    /// hazard frame, the gun port at chest height, status bars round the port and two electrode horns on top for the
    /// overload's arcs. Slots: steel, hazard, port, lamps.
    /// </summary>
    static Mesh PanelMesh(out Vector3 port, out Vector3[] electrodes)
    {
        port = new Vector3(0f, 1.2f, .27f);
        electrodes = new[] { new Vector3(-.38f, 1.62f, .12f), new Vector3(.38f, 1.62f, .12f) };
        var m = new TWMesh();
        m.Sub(0);
        m.Box(new Vector3(0f, .95f, .1f), new Vector3(.9f, 1.15f, .2f));
        foreach (var e in electrodes) m.Cylinder(new Vector3(e.x, 1.5f, e.z), e, .03f, 10);
        m.Sub(1);
        m.Box(new Vector3(0f, 1.545f, .12f), new Vector3(.98f, .05f, .24f));
        m.Box(new Vector3(0f, .355f, .12f), new Vector3(.98f, .05f, .24f));
        m.Box(new Vector3(-.465f, .95f, .12f), new Vector3(.05f, 1.24f, .24f));
        m.Box(new Vector3(.465f, .95f, .12f), new Vector3(.05f, 1.24f, .24f));
        m.Sub(2);
        m.Box(new Vector3(0f, port.y, .215f), new Vector3(.34f, .34f, .03f));
        m.Cylinder(new Vector3(0f, port.y, .23f), new Vector3(0f, port.y, port.z), .09f, 16);
        m.Sub(3);
        m.Box(new Vector3(0f, port.y + .2f, .23f), new Vector3(.3f, .025f, .02f));
        m.Box(new Vector3(0f, port.y - .2f, .23f), new Vector3(.3f, .025f, .02f));
        m.Box(new Vector3(-.2f, port.y, .23f), new Vector3(.025f, .3f, .02f));
        m.Box(new Vector3(.2f, port.y, .23f), new Vector3(.025f, .3f, .02f));
        m.Box(new Vector3(0f, .62f, .21f), new Vector3(.6f, .06f, .02f));
        var ball = new List<Vector2>();
        for (int i = 0; i <= 8; i++)
        {
            float a = Mathf.Lerp(-90f, 90f, i / 8f) * Mathf.Deg2Rad;
            ball.Add(new Vector2(Mathf.Cos(a) * .05f, Mathf.Sin(a) * .05f));
        }
        foreach (var e in electrodes) m.Lathe(ball, 12, e);
        return m.Save(MeshFolder + "/SM_RL_Panel.asset");
    }

    // ------------------------------------------------------------------ explosion

    static GameObject BuildExplosion(Mats m)
    {
        var root = new GameObject("VFX_RocketExplosion");
        var blast = root.AddComponent<ArmourBreakBlast>();
        blast.Radius = 5.5f;
        blast.MaxTargets = 3;

        // The template launcher's blast: its burst, impact ring, speed lines and flare. Its sound (the turret's death)
        // is swapped for the launcher's explosion, played through the same mixer settings.
        var classic = AssetDatabase.LoadAssetAtPath<GameObject>(ClassicExplosionPath);
        if (classic)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(classic, root.transform);
            instance.transform.localScale = Vector3.one * 1.5f;
            var classicSound = instance.GetComponent<AudioSource>();
            if (classicSound)
            {
                var sound = root.AddComponent<AudioSource>();
                EditorUtility.CopySerialized(classicSound, sound);
                sound.clip = Clip("Launcher_Explosion");
                sound.playOnAwake = true;
                classicSound.enabled = false;
            }
        }

        // A rolling fireball, sparks thrown hard, slower embers and a smoke cloud, all one burst.
        var fire = new Gradient();
        fire.SetKeys(
            new[] { new GradientColorKey(new Color(1f, .95f, .8f), 0f), new GradientColorKey(new Color(1f, .55f, .12f), .25f),
                new GradientColorKey(new Color(.6f, .12f, .03f), .7f), new GradientColorKey(new Color(.15f, .05f, .03f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.85f, .4f), new GradientAlphaKey(0f, 1f) });
        var fireball = Particles(root.transform, "Fireball", m.Flare, 10, new Vector2(.35f, .6f), new Vector2(.8f, 3f),
            new Vector2(1.6f, 2.8f), fire, -.15f, ParticleSystemRenderMode.Billboard, .5f);
        Grow(fireball, 1f, 1.8f);

        var hot = new Gradient();
        hot.SetKeys(
            new[] { new GradientColorKey(new Color(1f, .95f, .7f), 0f), new GradientColorKey(new Color(1f, .6f, .15f), .5f),
                new GradientColorKey(new Color(1f, .25f, .05f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, .6f), new GradientAlphaKey(0f, 1f) });
        var sparks = Particles(root.transform, "Sparks", m.Spark, 44, new Vector2(.3f, .8f), new Vector2(9f, 19f),
            new Vector2(.035f, .07f), hot, 1.6f, ParticleSystemRenderMode.Stretch, .25f);
        var sparkRenderer = sparks.GetComponent<ParticleSystemRenderer>();
        sparkRenderer.velocityScale = .045f;
        sparkRenderer.lengthScale = 1f;
        Particles(root.transform, "Embers", m.Spark, 18, new Vector2(.9f, 1.7f), new Vector2(2f, 6f),
            new Vector2(.05f, .1f), hot, .5f, ParticleSystemRenderMode.Billboard, .6f);

        var smokeColor = new Gradient();
        smokeColor.SetKeys(
            new[] { new GradientColorKey(new Color(.32f, .28f, .26f), 0f), new GradientColorKey(new Color(.16f, .15f, .15f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.55f, .12f), new GradientAlphaKey(0f, 1f) });
        var smoke = Particles(root.transform, "Smoke", m.Smoke, 8, new Vector2(1.2f, 2f), new Vector2(.4f, 1.6f),
            new Vector2(1.6f, 2.6f), smokeColor, -.05f, ParticleSystemRenderMode.Billboard, .8f);
        Grow(smoke, 1f, 1.7f);

        // A brief orange light, off within a quarter second.
        var lightObject = new GameObject("Flash");
        lightObject.transform.SetParent(root.transform, false);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, .55f, .2f);
        light.range = 10f;
        light.intensity = 0f;
        light.shadows = LightShadows.None;
        var flash = lightObject.AddComponent<LightFlash>();
        flash.Intensity = new AnimationCurve(new Keyframe(0f, 9f), new Keyframe(.06f, 6f), new Keyframe(.28f, 0f));

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, ExplosionPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static ParticleSystem Particles(Transform parent, string name, Material material, int burst, Vector2 life, Vector2 speed,
        Vector2 size, Gradient color, float gravity, ParticleSystemRenderMode mode, float radius, bool loop = false, float rate = 0f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = loop ? 1f : .2f;
        main.loop = loop;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = Color.white;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(burst, Mathf.CeilToInt(rate * life.y) + 2);
        main.playOnAwake = true;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        emission.SetBursts(burst > 0 ? new[] { new ParticleSystem.Burst(0f, (short)burst) } : new ParticleSystem.Burst[0]);
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = color;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = mode;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return ps;
    }

    static void Grow(ParticleSystem ps, float from, float to)
    {
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, from, 1f, to));
    }

    // ------------------------------------------------------------------ projectile

    static ProjectileStandard BuildProjectile(Mats m, Mesh rocketMesh, GameObject explosion)
    {
        var root = new GameObject("Projectile_Rocket");
        var body = new GameObject("Rocket");
        body.transform.SetParent(root.transform, false);
        body.AddComponent<MeshFilter>().sharedMesh = rocketMesh;
        var bodyRenderer = body.AddComponent<MeshRenderer>();
        bodyRenderer.sharedMaterials = new[] { m.RivetSteel ? m.RivetSteel : m.Steel, m.Warhead, m.Hazard };
        bodyRenderer.shadowCastingMode = ShadowCastingMode.Off;
        var tip = new GameObject("Tip").transform;
        tip.SetParent(root.transform, false);
        tip.localPosition = new Vector3(0f, 0f, .27f);

        // The elite rivet's trail, thickened, and a smoke wake off the motor.
        var trail = root.AddComponent<TrailRenderer>();
        trail.sharedMaterial = m.RivetTrail;
        trail.time = .32f;
        trail.widthMultiplier = .22f;
        trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        var trailColor = new Gradient();
        trailColor.SetKeys(
            new[] { new GradientColorKey(new Color(1f, .8f, .45f), 0f), new GradientColorKey(new Color(1f, .35f, .08f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = trailColor;
        trail.minVertexDistance = .1f;
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows = false;

        var smokeColor = new Gradient();
        smokeColor.SetKeys(
            new[] { new GradientColorKey(new Color(.55f, .5f, .46f), 0f), new GradientColorKey(new Color(.3f, .3f, .3f), 1f) },
            new[] { new GradientAlphaKey(.45f, 0f), new GradientAlphaKey(0f, 1f) });
        var wake = Particles(root.transform, "Smoke Wake", m.Smoke, 0, new Vector2(.45f, .75f), new Vector2(.2f, .6f),
            new Vector2(.22f, .4f), smokeColor, -.05f, ParticleSystemRenderMode.Billboard, .04f, true, 45f);
        wake.transform.localPosition = new Vector3(0f, 0f, -.28f);
        Grow(wake, 1f, 2.2f);
        var flameColor = new Gradient();
        flameColor.SetKeys(
            new[] { new GradientColorKey(new Color(1f, .85f, .5f), 0f), new GradientColorKey(new Color(1f, .4f, .1f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        var flame = Particles(root.transform, "Motor Flame", m.Flare, 0, new Vector2(.06f, .1f), new Vector2(0f, .2f),
            new Vector2(.35f, .5f), flameColor, 0f, ParticleSystemRenderMode.Billboard, .01f, true, 40f);
        flame.transform.localPosition = new Vector3(0f, 0f, -.3f);
        var flameMain = flame.main;
        flameMain.simulationSpace = ParticleSystemSimulationSpace.Local;

        var projectile = root.AddComponent<ProjectileStandard>();
        projectile.Radius = .2f;
        projectile.Root = root.transform;
        projectile.Tip = tip;
        projectile.MaxLifeTime = 4f;
        projectile.ImpactVfx = explosion;
        projectile.ImpactVfxLifetime = 3f;
        projectile.ImpactVfxSpawnOffset = .2f;
        projectile.ImpactSfxClip = null;
        projectile.Speed = 40f;
        projectile.GravityDownAcceleration = 0f;
        projectile.TrajectoryCorrectionDistance = 5f;
        projectile.Damage = 0f;
        projectile.AreaOfDamage = null;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, ProjectilePath);
        Object.DestroyImmediate(root);
        return prefab.GetComponent<ProjectileStandard>();
    }

    // ------------------------------------------------------------------ weapon

    static WeaponController BuildWeapon(Mats m, Mesh launcherMesh, ProjectileStandard projectile)
    {
        var blaster = AssetDatabase.LoadAssetAtPath<GameObject>(BlasterPath).GetComponent<WeaponController>();
        var root = new GameObject("Weapon_RocketLauncher");
        var source = root.AddComponent<AudioSource>();
        EditorUtility.CopySerialized(blaster.GetComponent<AudioSource>(), source);

        // Held where the blaster is, its front hoop in view and its rear off the bottom edge (placement tuned from
        // Play-mode captures).
        var launcherRoot = new GameObject("LauncherRoot").transform;
        launcherRoot.SetParent(root.transform, false);
        launcherRoot.localPosition = new Vector3(.02f, 0f, .16f);
        var model = new GameObject("Model");
        model.transform.SetParent(launcherRoot, false);
        model.AddComponent<MeshFilter>().sharedMesh = launcherMesh;
        var renderer = model.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = LauncherMaterials(m);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        var muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(launcherRoot, false);
        muzzle.localPosition = new Vector3(0f, 0f, TubeFront + .06f);

        var weapon = root.AddComponent<WeaponController>();
        weapon.WeaponName = "Rocket Launcher";
        weapon.WeaponIcon = BuildIcon();
        weapon.CrosshairDataDefault = blaster.CrosshairDataDefault;
        weapon.CrosshairDataDefault.CrosshairSize = 70;
        weapon.CrosshairDataDefault.CrosshairColor = new Color(1f, .62f, .2f);
        weapon.CrosshairDataTargetInSight = blaster.CrosshairDataTargetInSight;
        weapon.CrosshairDataTargetInSight.CrosshairSize = 60;
        weapon.WeaponRoot = launcherRoot.gameObject;
        weapon.WeaponMuzzle = muzzle;
        weapon.ShootType = WeaponShootType.Manual;
        weapon.ProjectilePrefab = projectile;
        weapon.DelayBetweenShots = .5f;
        weapon.BulletSpreadAngle = 0f;
        weapon.BulletsPerShot = 1;
        weapon.RecoilForce = 6f;
        weapon.AimZoomRatio = .8f;
        weapon.AimOffset = new Vector3(.12f, .02f, 0f);
        weapon.AutomaticReload = true;
        weapon.HasPhysicalBullets = false;
        // One rocket, refilled over the cooldown by the weapon's own automatic reload.
        weapon.ClipSize = 1;
        weapon.MaxAmmo = 1;
        weapon.AmmoReloadRate = 1f / Cooldown;
        weapon.AmmoReloadDelay = 0f;
        weapon.MuzzleFlashPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFlashPath);
        weapon.UnparentMuzzleFlash = false;
        weapon.ShootSfx = Clip("Launcher_Release");
        weapon.ChangeWeaponSfx = Clip("Pickup_Weapon_Medium");
        var cooldown = root.AddComponent<LauncherCooldown>();
        cooldown.ReadySfx = Clip("DisassemblerCharged");

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, WeaponPath);
        Object.DestroyImmediate(root);
        return prefab.GetComponent<WeaponController>();
    }

    static Material[] LauncherMaterials(Mats m) => new[] { m.Body, m.Dark, m.Grill, m.Glow, m.Warhead };

    // The HUD's weapon icon: a white side-on silhouette, as the template's icons are.
    static Sprite BuildIcon()
    {
        const int w = 88, h = 56;
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var pixels = new Color32[w * h];
        void Fill(int x0, int y0, int x1, int y1)
        {
            for (int x = Mathf.Max(0, x0); x < Mathf.Min(w, x1); x++)
                for (int y = Mathf.Max(0, y0); y < Mathf.Min(h, y1); y++)
                    pixels[y * w + x] = new Color32(255, 255, 255, 255);
        }
        Fill(4, 30, 10, 42);   // rear venturi
        Fill(8, 32, 78, 40);   // tube
        Fill(74, 31, 84, 41);  // muzzle collar
        Fill(44, 40, 54, 46);  // sight
        Fill(30, 18, 36, 32);  // pistol grip
        Fill(56, 20, 61, 32);  // foregrip
        Fill(12, 27, 26, 32);  // shoulder pad
        texture.SetPixels32(pixels);
        texture.Apply();
        string path = TextureFolder + "/HUD_Weapon_RocketLauncher.png";
        TutorialWingArt.SavePng(texture, path, true, true);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ------------------------------------------------------------------ the cradle

    static WeaponCradle BuildCradle(Transform root, Mats m, Mesh launcherMesh, WeaponController weapon)
    {
        var group = new GameObject("Containment Cradle").transform;
        group.SetParent(root, false);
        group.position = k_Hover;

        // Barrel to the south, its sight side toward the hall.
        var rotation = Quaternion.Euler(0f, 180f, 0f);
        var display = new GameObject("Rocket Launcher (Display)").transform;
        display.SetParent(group, false);
        display.SetPositionAndRotation(k_Hover, rotation);
        display.localScale = Vector3.one * DisplayScale;
        var model = new GameObject("Model");
        model.transform.SetParent(display, false);
        model.AddComponent<MeshFilter>().sharedMesh = launcherMesh;
        model.AddComponent<MeshRenderer>().sharedMaterials = LauncherMaterials(m);
        var barrel = new GameObject("Barrel Contact").transform;
        barrel.SetParent(display, false);
        barrel.localPosition = new Vector3(0f, 0f, TubeFront + .02f);
        var stock = new GameObject("Stock Contact").transform;
        stock.SetParent(display, false);
        stock.localPosition = new Vector3(0f, 0f, TubeRear - .02f);

        var rest = new GameObject("Rest Point").transform;
        rest.SetParent(group, false);
        rest.SetPositionAndRotation(new Vector3(k_Hover.x, DeckTop + GripDrop * DisplayScale + .02f, k_Hover.z), rotation);

        // The two emitters on the deck, south (to the barrel) and north (to the stock).
        var emitterMesh = EmitterMesh(out float headHeight);
        var heads = new List<Transform>();
        var lamps = new List<Renderer>();
        for (int i = 0; i < k_EmitterZ.Length; i++)
        {
            var emitter = new GameObject("Emitter " + (i == 0 ? "South" : "North"));
            emitter.transform.SetParent(group, false);
            emitter.transform.position = new Vector3(k_Hover.x, DeckTop, k_EmitterZ[i]);
            emitter.AddComponent<MeshFilter>().sharedMesh = emitterMesh;
            var emitterRenderer = emitter.AddComponent<MeshRenderer>();
            emitterRenderer.sharedMaterials = new[] { m.Steel, m.Lamp };
            lamps.Add(emitterRenderer);
            var head = new GameObject("Head").transform;
            head.SetParent(emitter.transform, false);
            head.localPosition = new Vector3(0f, headHeight, 0f);
            heads.Add(head);
        }
        // Each emitter's head sits level with the end of the launcher it holds.
        float lift = k_Hover.y - (DeckTop + headHeight);
        if (Mathf.Abs(lift) > .01f)
            foreach (var head in heads) head.localPosition += Vector3.up * lift;

        // Two interleaved strands per beam.
        var lines = new List<LineRenderer>();
        for (int i = 0; i < 4; i++)
        {
            var line = new GameObject("Beam " + (i / 2 == 0 ? "Barrel" : "Stock") + " Strand " + (i % 2 + 1)).AddComponent<LineRenderer>();
            line.transform.SetParent(group, false);
            line.sharedMaterial = m.Lightning;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.widthMultiplier = .05f;
            line.numCapVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPositions(new[] { heads[i / 2].position, (i / 2 == 0 ? barrel : stock).position });
            lines.Add(line);
        }

        // Sparks where the beams bite, and a burst along the launcher when they cut out.
        var blue = new Gradient();
        blue.SetKeys(
            new[] { new GradientColorKey(new Color(.85f, .95f, 1f), 0f), new GradientColorKey(new Color(.35f, .7f, 1f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        var contacts = new List<ParticleSystem>();
        foreach (var contact in new[] { barrel, stock })
        {
            var sparks = Particles(contact, "Contact Sparks", m.Spark, 0, new Vector2(.15f, .35f), new Vector2(1.5f, 4f),
                new Vector2(.025f, .05f), blue, 1.2f, ParticleSystemRenderMode.Stretch, .04f, true, 14f);
            var r = sparks.GetComponent<ParticleSystemRenderer>();
            r.velocityScale = .05f;
            contacts.Add(sparks);
        }
        var burst = Particles(display, "Release Burst", m.Spark, 40, new Vector2(.3f, .8f), new Vector2(3f, 9f),
            new Vector2(.03f, .06f), blue, 1.4f, ParticleSystemRenderMode.Stretch, .5f);
        burst.GetComponent<ParticleSystemRenderer>().velocityScale = .05f;
        var burstMain = burst.main;
        burstMain.playOnAwake = false;
        var burstShape = burst.shape;
        burstShape.shapeType = ParticleSystemShapeType.Box;
        burstShape.scale = new Vector3(.2f, .2f, 1.4f);

        var hum = group.gameObject.AddComponent<AudioSource>();
        hum.clip = Clip("Workshop_TubeBuzz_Loop");
        hum.loop = true;
        hum.playOnAwake = true;
        hum.spatialBlend = 1f;
        hum.volume = .5f;
        hum.minDistance = 2f;
        hum.maxDistance = 16f;
        hum.rolloffMode = AudioRolloffMode.Linear;

        // Looked at over the deck's front edge, clear of the cradle's collision hull.
        var focus = new GameObject("Focus").transform;
        focus.SetParent(group, false);
        focus.position = new Vector3(DeckFront - .12f, DeckTop + .55f, k_Hover.z);

        var cradle = group.gameObject.AddComponent<WeaponCradle>();
        cradle.Weapon = weapon;
        cradle.Display = display;
        cradle.RestPoint = rest;
        cradle.FocusPoint = focus;
        cradle.Range = 4.2f;
        cradle.BeamSources = heads.ToArray();
        cradle.BeamTargets = new[] { barrel, stock };
        cradle.BeamLines = lines.ToArray();
        cradle.ContactSparks = contacts.ToArray();
        cradle.ReleaseBurst = burst;
        cradle.Lamps = lamps.ToArray();
        cradle.Hum = hum;
        cradle.DeniedSfx = Clip("CoreCriticalBeep");
        cradle.SurgeSfx = Clip("CoreSurge");
        cradle.CutSfx = Clip("ElectrocuteZap");
        cradle.LandSfx = Clip("Land");
        cradle.TakeSfx = Clip("Pickup_Weapon_Medium");

        Label(group, "Label", new Vector3(DeckFront - .012f, DeckTop - .42f, k_Hover.z),
            "<color=#FF9E33>XR-7 ARMOUR BUSTER</color>  /  ROCKET LAUNCHER",
            "CONTAINED  -  OVERLOAD THE POWER PANEL WITH A FULL DISASSEMBLER TO RELEASE", 4f, m.Port);
        return cradle;
    }

    static void BuildPanel(Transform root, Mats m, WeaponCradle cradle)
    {
        var panel = new GameObject("Containment Power Panel").transform;
        panel.SetParent(root, false);
        panel.SetPositionAndRotation(new Vector3(DeckFront, HallFloor, PanelZ), Quaternion.LookRotation(Vector3.right));
        var mesh = PanelMesh(out Vector3 port, out Vector3[] electrodes);
        panel.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = panel.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { m.Steel, m.Hazard, m.Port, m.PanelLamp };
        var collider = panel.gameObject.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, .95f, .12f);
        collider.size = new Vector3(.98f, 1.24f, .24f);

        var slot = new GameObject("Gun Port").transform;
        slot.SetParent(panel, false);
        slot.localPosition = port;
        var anchors = new List<Transform> { slot };
        foreach (var e in electrodes)
        {
            var anchor = new GameObject("Electrode").transform;
            anchor.SetParent(panel, false);
            anchor.localPosition = e;
            anchors.Add(anchor);
        }

        // A conduit from the panel's top up over the deck edge and along to the south emitter.
        var south = cradle.BeamSources[0].parent.position;
        var top = panel.TransformPoint(new Vector3(0f, 1.58f, .06f));
        var path = new[]
        {
            top, new Vector3(top.x, DeckTop + .06f, top.z), new Vector3(DeckFront - .3f, DeckTop + .06f, top.z),
            new Vector3(DeckFront - .3f, DeckTop + .06f, south.z), new Vector3(south.x, DeckTop + .06f, south.z),
        };
        var conduit = new TWMesh();
        conduit.Sub(0).Tube(path, .035f, 10, true);
        var conduitObject = new GameObject("Conduit");
        conduitObject.transform.SetParent(root, false);
        conduitObject.AddComponent<MeshFilter>().sharedMesh = conduit.Save(MeshFolder + "/SM_RL_Conduit.asset");
        conduitObject.AddComponent<MeshRenderer>().sharedMaterial = m.Hazard;

        var interactable = panel.gameObject.AddComponent<OverloadPanel>();
        interactable.Cradle = cradle;
        interactable.Slot = slot;
        interactable.FocusPoint = slot;
        interactable.Range = 2.8f;
        interactable.CoreOrbMaterial = AssetDatabase.LoadAssetAtPath<Material>(HeartMaterialPath);
        interactable.Body = renderer;
        interactable.Lamps = new Renderer[] { renderer };
        interactable.ArcAnchors = anchors.ToArray();
        interactable.DeniedSfx = Clip("CoreCriticalBeep");
        interactable.CoreInjectedSfx = Clip("CoreSurge");
        interactable.OverloadSfx = Clip("CoreCriticalWhine");
        interactable.SpentSfx = Clip("SecurityDoorClose");

        Label(panel.parent, "Panel Label", panel.TransformPoint(new Vector3(0f, 1.95f, .01f)),
            "CONTAINMENT POWER", "INJECT A FULL DISASSEMBLER (3 CORES) TO OVERLOAD", 1.6f, m.Port);
    }

    // A two-line label on the cradle's front face, readable from the hall (facing +X), on a dark plate so it holds up
    // under the cradle's floodlight.
    static void Label(Transform parent, string name, Vector3 position, string heading, string body, float width, Material plate)
    {
        var group = new GameObject(name).transform;
        group.SetParent(parent, false);
        group.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.left));
        var backing = GameObject.CreatePrimitive(PrimitiveType.Quad);
        backing.name = "Plate";
        Object.DestroyImmediate(backing.GetComponent<Collider>());
        backing.transform.SetParent(group, false);
        backing.transform.localPosition = new Vector3(0f, -.01f, .006f);
        backing.transform.localScale = new Vector3(width + .12f, .36f, 1f);
        backing.GetComponent<Renderer>().sharedMaterial = plate;
        backing.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        Text(group, "Heading", heading, AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath), .14f, new Vector2(0f, .07f), width, Color.white);
        Text(group, "Body", body, AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath), .055f, new Vector2(0f, -.1f), width,
            new Color(.8f, .9f, 1f));
    }

    static void Text(Transform parent, string name, string text, TMP_FontAsset font, float capHeight, Vector2 position, float width, Color color)
    {
        var tmp = new GameObject(name).AddComponent<TextMeshPro>();
        tmp.transform.SetParent(parent, false);
        tmp.transform.localPosition = position;
        tmp.font = font;
        tmp.text = text;
        tmp.fontSize = capHeight / .12f;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.rectTransform.sizeDelta = new Vector2(width, capHeight * 2.6f);
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = tmp.fontSize * .5f;
        tmp.fontSizeMax = tmp.fontSize;
    }

    // ------------------------------------------------------------------ HUD

    // The recharge gauge: a copy of the Disassembler's (same frame, fill, label and status), above it in the HUD's
    // bottom-left stack, written into the GameHUD prefab so every HUD instance gets it.
    static void BuildHudGauge()
    {
        var hud = PrefabUtility.LoadPrefabContents(HudPrefabPath);
        try
        {
            var source = FindDeep(hud.transform, "DisassemblerCharge");
            if (!source)
            {
                Debug.LogWarning("Rocket launcher: no DisassemblerCharge gauge in " + HudPrefabPath + "; run the HUD skin builder first.");
                return;
            }
            var corner = source.parent;
            var old = corner.Find("RocketLauncherCharge");
            if (old) Object.DestroyImmediate(old.gameObject);

            var gauge = Object.Instantiate(source.gameObject, corner);
            gauge.name = "RocketLauncherCharge";
            gauge.transform.SetSiblingIndex(source.GetSiblingIndex());
            Object.DestroyImmediate(gauge.GetComponent("DisassemblerChargeHUD"));
            // One continuous fill for the countdown: no cell dividers.
            var ticks = new List<GameObject>();
            foreach (var t in gauge.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Tick ")) ticks.Add(t.gameObject);
            foreach (var tick in ticks) Object.DestroyImmediate(tick);

            var body = (RectTransform)gauge.transform.Find("Body");
            var label = body.Find("Label").GetComponent<TMP_Text>();
            label.text = "ROCKET LAUNCHER";
            var status = body.Find("Status").GetComponent<TMP_Text>();
            status.text = "READY";
            var layout = gauge.GetComponent<UnityEngine.UI.LayoutElement>();
            if (!layout) layout = gauge.AddComponent<UnityEngine.UI.LayoutElement>();

            var charge = gauge.AddComponent<Unity.FPS.UI.RocketLauncherChargeHUD>();
            charge.Body = body;
            charge.Fill = body.Find("Bar/FillImage").GetComponent<UnityEngine.UI.Image>();
            charge.Frame = body.Find("Bar/Frame").GetComponent<UnityEngine.UI.Image>();
            charge.Label = label;
            charge.Status = status;
            charge.Layout = layout;
            PrefabUtility.SaveAsPrefabAsset(hud, HudPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(hud);
        }
    }

    static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    // ------------------------------------------------------------------ helpers

    static AudioClip Clip(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets(name + " t:AudioClip"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
        Debug.LogWarning("Rocket launcher: no audio clip named " + name);
        return null;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
