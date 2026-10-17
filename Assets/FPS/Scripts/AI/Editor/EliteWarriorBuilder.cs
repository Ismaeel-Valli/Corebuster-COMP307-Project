using System.Collections.Generic;
using System.Linq;
using Unity.FPS.AI;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Builds the Elite enemies for the boss room from the Sci-Fi Warrior (Robot Hero PBR HP Polyart) pack:
/// an elite piston brawler and an elite rifleman whose assault rifle fires rivet bursts. Same brains,
/// armour shell and core execution as the Cyber Soldiers, with tougher stats and a crimson glow.
/// </summary>
public static class EliteWarriorBuilder
{
    const string PackFolder = "Assets/SciFiWarriorPBRHPPolyart/";
    const string ModelPath = PackFolder + "Mesh/PBR_HP_Mesh.fbx";
    // The pack's prefabs drive the PBR mesh with the avatar imported on the Polyart mesh (same skeleton).
    const string AvatarModelPath = PackFolder + "Mesh/Polyart_Mesh.fbx";
    const string TextureFolder = PackFolder + "Textures/";
    const string ClipFolder = PackFolder + "Animations/";

    const string MaterialFolder = "Assets/FPS/Art/Materials/EliteWarrior";
    const string BodyMeshPath = "Assets/FPS/Art/Models/EliteWarrior_Body.asset";
    const string AnimationFolder = "Assets/FPS/Animation/EliteWarrior";
    const string RifleControllerPath = AnimationFolder + "/EliteRifleman.controller";
    const string UpperBodyMaskPath = AnimationFolder + "/EliteRifleman_UpperBody.mask";
    const string EliteRivetProjectilePath = "Assets/FPS/Prefabs/Projectiles/Projectile_Rivet_Elite.prefab";

    const string EnemyFolder = "Assets/FPS/Prefabs/Enemies/";
    const string BrawlerPath = EnemyFolder + "Enemy_Elite_PistonFist.prefab";
    const string RiflemanPath = EnemyFolder + "Enemy_Elite_Rifleman.prefab";

    // Every part except the rifle is skinned to the same skeleton; they are merged into one body so the
    // armour shell, its debris and the core execution treat the warrior as a single renderer.
    static readonly string[] k_BodyParts = { "Body1", "Arm1", "Leg1", "head1", "Backpack1" };
    const string RiflePart = "AssaultRifle";
    // Prop bone the rifle is skinned to. The clips animate it beside the hips, in step with the right hand.
    const string RifleBone = "ArmPosition_Right";

    // Measured ground speed of the pack's in-place Run_guard_AR clip on this avatar (3.2 m/s at a
    // 1.21 human scale), in the human-scale units Robot1ProceduralAnimator feeds the Speed parameter.
    const float RunClipSpeed = 2.64f;

    // Tiling for the galaxy shader's object-space triplanar mapping; checked in play mode against
    // the Cyber Soldier's look, which uses the same value.
    const float GalaxyTriplanarScale = 1.1f;
    static readonly Color k_EliteEmission = new Color(2.4f, .22f, .12f);

    [MenuItem("Tools/CoreBuster/Create Elite Warrior Prefabs")]
    public static void BuildAll()
    {
        BuildVariant(EnemyFolder + "Enemy_Robot1_PistonFist.prefab", BrawlerPath, ConfigureBrawler);
        BuildVariant(EnemyFolder + "Enemy_Robot1_RivetCannon.prefab", RiflemanPath, ConfigureRifleman);
        AssetDatabase.SaveAssets();
    }

    // Cyber Soldier brawler: 150 hp, 35 per punch, 7.5 m/s, 0.4 s between punches.
    static void ConfigureBrawler(GameObject root, Dictionary<HumanBodyBones, Transform> bones)
    {
        // The brawler fights with its piston fists, so it leaves the rifle behind.
        var rifle = FindChild(root.transform, RiflePart);
        if (rifle) Object.DestroyImmediate(rifle.gameObject);

        root.GetComponent<Health>().MaxHealth = 320f;
        var agent = root.GetComponent<NavMeshAgent>();
        agent.speed = 7f;
        agent.acceleration = 32f;
        var detection = root.GetComponent<DetectionModule>();
        detection.DetectionRange = 24f;
        var piston = root.GetComponent<PistonFist>();
        piston.Damage = 45f;
        piston.RetractDuration = .3f;
        piston.WarningDuration = .35f;
        piston.CooldownDuration = .35f;
    }

    // Cyber Soldier gunner: 150 hp, 5 x 15 damage bursts, 5.5 m/s, 26 m detection, 18 m attack range.
    static void ConfigureRifleman(GameObject root, Dictionary<HumanBodyBones, Transform> bones)
    {
        root.GetComponent<Health>().MaxHealth = 280f;
        root.GetComponent<NavMeshAgent>().speed = 5f;
        var detection = root.GetComponent<DetectionModule>();
        detection.DetectionRange = 32f;
        detection.AttackRange = 24f;
        var chase = root.GetComponent<Robot1ChaseController>();
        chase.StoppingDistance = 13f;
        root.GetComponent<NavMeshAgent>().stoppingDistance = chase.StoppingDistance;

        var cannon = root.GetComponentInChildren<RivetCannon>();
        Transform mount = MountRifle(root, bones, cannon);
        if (!mount) return;

        var animator = root.GetComponent<Animator>();
        animator.runtimeAnimatorController = BuildRifleController();
        var procedural = root.GetComponent<Robot1ProceduralAnimator>();
        procedural.LocomotionAnimator = animator;
        procedural.FastestClipSpeed = RunClipSpeed;
        procedural.WeaponAimReference = mount;

        cannon.ShotsPerBurst = 7;
        cannon.ShotInterval = .11f;
        cannon.WarningDuration = .5f;
        cannon.CooldownDuration = 1.2f;
        cannon.AimTurnSpeed = 260f;
        cannon.BurstTurnSpeed = 60f;
        // Bolts leave along the rifle; the torso turn does the real aiming.
        cannon.MaxAimAngle = 25f;
        cannon.RecoilDistance = 0f;
        cannon.ArmRaiseSpeed = 5f;
        cannon.WarningColor = new Color(1f, .15f, .05f);

        var weapon = cannon.GetComponent<WeaponController>();
        var projectile = BuildEliteRivet();
        if (projectile) weapon.ProjectilePrefab = projectile;
        weapon.WeaponName = "Elite Rivet Rifle";
        weapon.BulletSpreadAngle = 2f;
        weapon.MaxAmmo = Mathf.Max(weapon.MaxAmmo, cannon.ShotsPerBurst);
        weapon.AmmoReloadRate = cannon.ShotsPerBurst;
    }

    static void BuildVariant(string sourcePath, string outputPath,
        System.Action<GameObject, Dictionary<HumanBodyBones, Transform>> configure)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var avatar = AssetDatabase.LoadAllAssetsAtPath(AvatarModelPath).OfType<Avatar>().FirstOrDefault();
        var sparks = AssetDatabase.LoadAssetAtPath<GameObject>(CyberSoldierBuilder.SparksPath);
        if (!source || !model || !avatar)
        {
            Debug.LogError("Missing " + (!source ? sourcePath : !model ? ModelPath : AvatarModelPath + " avatar"));
            return;
        }

        EnsureFolder(MaterialFolder);
        Material armour = BuildArmourMaterial();
        Material galaxy = BuildGalaxyMaterial();

        // A plain copy, so later Robot1 edits don't leak into the elites.
        var root = Object.Instantiate(source);
        root.name = System.IO.Path.GetFileNameWithoutExtension(outputPath);
        try
        {
            var bones = CyberSoldierBuilder.SwapBody(root, model, avatar);
            var body = MergeBody(root);
            if (!body) return;
            body.sharedMaterial = armour;
            var rifle = FindChild(root.transform, RiflePart);
            if (rifle) rifle.GetComponent<SkinnedMeshRenderer>().sharedMaterial = armour;

            Bounds bounds = body.bounds;
            CyberSoldierBuilder.FitToBody(root, bounds);

            var proceduralAnimator = root.GetComponent<Robot1ProceduralAnimator>();
            proceduralAnimator.ApplyNebulaMaterial = false; // The armour covers the galaxy body until it breaks.
            proceduralAnimator.NebulaMaterial = galaxy;

            var armourComponent = root.AddComponent<ArmouredEnemy>();
            armourComponent.BodyRenderer = body;
            armourComponent.ExposedMaterial = galaxy;
            armourComponent.DebrisMaterial = BuildDebrisMaterial(armour);
            armourComponent.BreakSfx = CyberSoldierBuilder.FindClip("Launcher_Explosion");
            armourComponent.HitSparks = CyberSoldierBuilder.AddSparks(sparks, bones, bounds.center);
            // Elite plating soaks more, but still breaks at the same point to expose the core.
            armourComponent.ArmouredDamageMultiplier = .6f;

            var enemy = root.GetComponent<EnemyController>();
            enemy.DamageTick = CyberSoldierBuilder.FindClip("Damage_Tick2");

            configure(root, bones);
            if (CoreExecutionBuilder.InstallOnEnemy(root))
                root.GetComponent<CoreExecutionTarget>().HealthReward = 60f;
            PistonFistArmFitter.Fit(root);

            PrefabUtility.SaveAsPrefabAsset(root, outputPath);
            Debug.Log("Elite warrior created: " + outputPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // Merges the body parts into the Body1 renderer. Each part has its own bindposes (the parts sit at
    // different offsets in the FBX), so its vertices are moved into Body1's mesh space first.
    static SkinnedMeshRenderer MergeBody(GameObject root)
    {
        var parts = new List<SkinnedMeshRenderer>();
        foreach (var name in k_BodyParts)
        {
            var part = FindChild(root.transform, name);
            if (part && part.GetComponent<SkinnedMeshRenderer>()) parts.Add(part.GetComponent<SkinnedMeshRenderer>());
        }
        if (parts.Count == 0)
        {
            Debug.LogError("Elite warrior: no body parts found on " + ModelPath);
            return null;
        }

        SkinnedMeshRenderer body = parts[0];
        Matrix4x4[] bindposes = body.sharedMesh.bindposes;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var tangents = new List<Vector4>();
        var uvs = new List<Vector2>();
        var weights = new List<BoneWeight>();
        var triangles = new List<int>();
        Bounds localBounds = body.localBounds;

        foreach (var part in parts)
        {
            if (!part.bones.SequenceEqual(body.bones))
            {
                Debug.LogError("Elite warrior: " + part.name + " is skinned to a different skeleton, skipped.");
                continue;
            }
            Mesh mesh = part.sharedMesh;
            // bindpose = boneFromMesh, so the same bone gives partMesh -> bodyMesh for every bone.
            Matrix4x4 toBody = bindposes[0].inverse * mesh.bindposes[0];
            int offset = vertices.Count;
            vertices.AddRange(mesh.vertices.Select(v => toBody.MultiplyPoint3x4(v)));
            normals.AddRange(mesh.normals.Select(n => toBody.MultiplyVector(n).normalized));
            tangents.AddRange(mesh.tangents.Select(t =>
            {
                Vector3 d = toBody.MultiplyVector(t).normalized;
                return new Vector4(d.x, d.y, d.z, t.w);
            }));
            uvs.AddRange(mesh.uv);
            weights.AddRange(mesh.boneWeights);
            triangles.AddRange(mesh.triangles.Select(i => i + offset));
            // Local bounds are relative to the shared root bone, so they combine directly.
            localBounds.Encapsulate(part.localBounds);
        }

        var merged = new Mesh { name = "EliteWarrior_Body" };
        merged.SetVertices(vertices);
        merged.SetNormals(normals);
        if (tangents.Count == vertices.Count) merged.SetTangents(tangents);
        merged.SetUVs(0, uvs);
        merged.boneWeights = weights.ToArray();
        merged.bindposes = bindposes;
        merged.SetTriangles(triangles, 0);
        merged.RecalculateBounds();
        merged = SaveMesh(merged, BodyMeshPath);

        body.sharedMesh = merged;
        body.localBounds = localBounds;
        body.name = "EliteWarrior_Body";
        foreach (var part in parts)
            if (part != body) Object.DestroyImmediate(part.gameObject);
        return body;
    }

    /// <summary>
    /// Moves the rivet cannon's firing parts onto the tip of the warrior's rifle. The rifle bone is
    /// re-parented into the right hand so the rifle follows the torso when the rifleman aims.
    /// </summary>
    static Transform MountRifle(GameObject root, Dictionary<HumanBodyBones, Transform> bones, RivetCannon cannon)
    {
        var rifle = FindChild(root.transform, RiflePart)?.GetComponent<SkinnedMeshRenderer>();
        var rifleBone = FindChild(root.transform, RifleBone);
        if (!rifle || !rifleBone || !cannon || !bones.TryGetValue(HumanBodyBones.RightHand, out Transform hand))
        {
            Debug.LogError("Elite rifleman: missing rifle, rifle bone, rivet cannon or right hand.");
            return null;
        }

        var aimClip = LoadClip("Idle_Shoot_ar.fbx");

        // Pose the rifle shouldered, pointing along the body's forward axis, then put everything back.
        var rest = new Dictionary<Transform, (Vector3, Quaternion, Vector3)>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            rest[t] = (t.localPosition, t.localRotation, t.localScale);
        Vector3 barrelAxis, upAxis;
        try
        {
            aimClip.SampleAnimation(root, aimClip.length * .25f);
            // The clips hold the rifle at a fixed offset from the right hand; now it's no longer
            // animated beside the hips, it stays in the hand.
            rifleBone.SetParent(hand, true);
            rest.Remove(rifleBone);
            barrelAxis = rifleBone.InverseTransformDirection(root.transform.forward).normalized;
            upAxis = rifleBone.InverseTransformDirection(root.transform.up).normalized;
        }
        finally
        {
            foreach (var pair in rest)
            {
                if (!pair.Key) continue;
                pair.Key.localPosition = pair.Value.Item1;
                pair.Key.localRotation = pair.Value.Item2;
                pair.Key.localScale = pair.Value.Item3;
            }
        }

        // Rifle vertices in the rifle bone's space; the muzzle is the far end along the barrel.
        Mesh mesh = rifle.sharedMesh;
        int boneIndex = System.Array.IndexOf(rifle.bones, rifleBone);
        Matrix4x4 bind = mesh.bindposes[boneIndex];
        BoneWeight[] boneWeights = mesh.boneWeights;
        Vector3[] meshVertices = mesh.vertices;
        var points = new List<Vector3>();
        for (int i = 0; i < meshVertices.Length; i++)
            if (boneWeights[i].boneIndex0 == boneIndex) points.Add(bind.MultiplyPoint3x4(meshVertices[i]));
        float front = points.Max(p => Vector3.Dot(p, barrelAxis));
        float back = points.Min(p => Vector3.Dot(p, barrelAxis));
        float tipDepth = (front - back) * .02f;
        Vector3 muzzle = Vector3.zero;
        int tipCount = 0;
        foreach (var p in points)
        {
            if (Vector3.Dot(p, barrelAxis) < front - tipDepth) continue;
            muzzle += p;
            tipCount++;
        }
        muzzle /= Mathf.Max(1, tipCount);

        var mount = new GameObject("Elite Rifle Mount").transform;
        mount.SetParent(rifleBone, false);
        mount.localPosition = muzzle;
        mount.localRotation = Quaternion.LookRotation(barrelAxis, upAxis);
        float boneScale = Mathf.Abs(rifleBone.lossyScale.x);
        Debug.Log($"Elite rifleman: rifle {(front - back) * boneScale:F2} m long, muzzle at {muzzle.ToString("F1")} on {RifleBone}");

        // Keep the cannon's weapon, burst logic, muzzle and warning lamp; drop its forearm hardware.
        // The weapon is scaled back to world metres under the centimetre-scaled rifle bone.
        Transform weapon = cannon.transform;
        var weaponController = cannon.GetComponent<WeaponController>();
        weapon.SetParent(mount, false);
        weapon.localPosition = Vector3.zero;
        weapon.localRotation = Quaternion.identity;
        weapon.localScale = Vector3.one / Mathf.Max(.0001f, Mathf.Abs(mount.lossyScale.x));
        weapon.name = "Weapon_EliteRivetRifle";
        var keep = new HashSet<Transform> { weapon, cannon.AimPivot, weaponController.WeaponMuzzle };
        if (cannon.WarningLamp) keep.Add(cannon.WarningLamp.transform);
        if (weaponController.WeaponRoot) keep.Add(weaponController.WeaponRoot.transform);
        foreach (var t in weapon.GetComponentsInChildren<Transform>(true))
            if (t && !keep.Contains(t)) Object.DestroyImmediate(t.gameObject);
        cannon.Barrel = null;

        if (weaponController.WeaponRoot)
        {
            weaponController.WeaponRoot.transform.localPosition = Vector3.zero;
            weaponController.WeaponRoot.transform.localRotation = Quaternion.identity;
            weaponController.WeaponRoot.transform.localScale = Vector3.one;
        }
        cannon.AimPivot.localPosition = Vector3.zero;
        cannon.AimPivot.localRotation = Quaternion.identity;
        cannon.AimPivot.localScale = Vector3.one;
        weaponController.WeaponMuzzle.localPosition = new Vector3(0f, 0f, .05f);
        weaponController.WeaponMuzzle.localRotation = Quaternion.identity;

        // Sit the warning lamp on top of the barrel shroud just behind the muzzle (the bare barrel
        // further back has no vertices along its length to measure).
        if (cannon.WarningLamp)
        {
            const float lampSetBack = .15f;
            float top = 0f;
            foreach (var p in points)
            {
                Vector3 local = cannon.AimPivot.InverseTransformPoint(rifleBone.TransformPoint(p));
                if (local.z > -lampSetBack - .1f && local.z < -lampSetBack + .1f) top = Mathf.Max(top, local.y);
            }
            Transform lamp = cannon.WarningLamp.transform;
            lamp.localPosition = new Vector3(0f, top + .025f, -lampSetBack);
            lamp.localRotation = Quaternion.identity;
            lamp.localScale = Vector3.one * .06f;
        }
        return mount;
    }

    static RuntimeAnimatorController BuildRifleController()
    {
        EnsureFolder(AnimationFolder);
        AvatarMask mask = BuildUpperBodyMask();
        var idle = LoadClip("Idle_guard_ar.fbx");
        var run = LoadClip("Run_guard_AR.fbx");
        var shouldered = LoadClip("Idle_Shoot_ar.fbx");

        // Rebuilt from scratch each time; only the elite prefabs (rebuilt below) reference it.
        AssetDatabase.DeleteAsset(RifleControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(RifleControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter(new AnimatorControllerParameter
        {
            name = "PlaybackSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f,
        });

        // Base layer: rifle held at guard, idle to run, driven by Robot1ProceduralAnimator.
        var locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(idle, 0f);
        tree.AddChild(run, RunClipSpeed);
        locomotion.speedParameter = "PlaybackSpeed";
        locomotion.speedParameterActive = true;

        // Aim layer: shoulders the rifle over the legs. RivetCannon raises it before every burst.
        controller.AddLayer(Robot1ProceduralAnimator.DefaultAimLayerName);
        var layers = controller.layers;
        layers[1].defaultWeight = 0f;
        layers[1].avatarMask = mask;
        layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
        controller.layers = layers;
        var aimState = controller.layers[1].stateMachine.AddState("Shouldered");
        aimState.motion = shouldered;

        EditorUtility.SetDirty(controller);
        return controller;
    }

    static AvatarMask BuildUpperBodyMask()
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
        if (!mask)
        {
            mask = new AvatarMask { name = "EliteRifleman_UpperBody" };
            AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
        }
        for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
        {
            bool upper = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head ||
                         part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                         part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers;
            mask.SetHumanoidBodyPartActive(part, upper);
        }
        EditorUtility.SetDirty(mask);
        return mask;
    }

    static ProjectileBase BuildEliteRivet()
    {
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(EliteRivetProjectilePath))
        {
            if (!AssetDatabase.CopyAsset(CyberSoldierBuilder.RivetProjectilePath, EliteRivetProjectilePath)) return null;
            AssetDatabase.ImportAsset(EliteRivetProjectilePath);
        }

        var contents = PrefabUtility.LoadPrefabContents(EliteRivetProjectilePath);
        try
        {
            contents.name = "Projectile_Rivet_Elite";
            // Lighter, faster bolts than the Cyber Soldier's heavy rivets: rifle fire, not cannon fire.
            var projectile = contents.GetComponent<ProjectileStandard>();
            projectile.Damage = 10f;
            projectile.Speed = 50f;
            PrefabUtility.SaveAsPrefabAsset(contents, EliteRivetProjectilePath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
        return AssetDatabase.LoadAssetAtPath<ProjectileBase>(EliteRivetProjectilePath);
    }

    // The pack ships Built-in Standard materials, which render pink in URP. Rebuilt in place on every
    // run so tuning here reaches the existing prefabs.
    static Material BuildArmourMaterial()
    {
        var material = LoadOrCreateMaterial("EliteWarrior_Armour", Shader.Find("Universal Render Pipeline/Lit"));
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "PBR_Albedo.png");
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "PBR_Free_NM.png");
        var metallic = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "PBR_Free_MS.png");
        var occlusion = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "PBR_Free_AO.png");
        var emissive = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "PBR_Free_EM.png");

        material.SetTexture("_BaseMap", albedo);
        material.SetTexture("_MainTex", albedo);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_WorkflowMode", 1f);
        if (normal)
        {
            material.SetTexture("_BumpMap", normal);
            material.EnableKeyword("_NORMALMAP");
        }
        if (metallic)
        {
            // Metallic in RGB, smoothness in alpha, as the pack's Standard material used it.
            material.SetTexture("_MetallicGlossMap", metallic);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetFloat("_Smoothness", 1f);
        }
        if (occlusion)
        {
            material.SetTexture("_OcclusionMap", occlusion);
            material.EnableKeyword("_OCCLUSIONMAP");
        }
        if (emissive)
        {
            // The pack's orange trim, re-lit crimson so elites read differently from the blue-lit soldiers.
            material.SetTexture("_EmissionMap", emissive);
            material.SetColor("_EmissionColor", k_EliteEmission);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.EnableKeyword("_EMISSION");
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    // The chunks are open pieces of the body surface, so render their inside too.
    static Material BuildDebrisMaterial(Material armour)
    {
        var material = LoadOrCreateMaterial("EliteWarrior_ArmourDebris", armour.shader);
        material.CopyPropertiesFromMaterial(armour);
        material.shaderKeywords = armour.shaderKeywords;
        material.globalIlluminationFlags = armour.globalIlluminationFlags;
        material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        material.doubleSidedGI = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material BuildGalaxyMaterial()
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(CyberSoldierBuilder.GalaxySourcePath);
        if (!source) return null;
        var material = LoadOrCreateMaterial("EliteWarrior_Galaxy", source.shader);
        material.CopyPropertiesFromMaterial(source);
        material.shaderKeywords = source.shaderKeywords;
        material.SetFloat("_TriplanarScale", GalaxyTriplanarScale);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material LoadOrCreateMaterial(string name, Shader shader)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material)
        {
            if (material.shader != shader) material.shader = shader;
            return material;
        }
        material = new Material(shader) { name = name };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // Overwrites an existing mesh asset in place so prefabs keep their reference.
    static Mesh SaveMesh(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing)
        {
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static AnimationClip LoadClip(string fileName)
    {
        return AssetDatabase.LoadAllAssetsAtPath(ClipFolder + fileName).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview"));
    }

    static Transform FindChild(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }
}
