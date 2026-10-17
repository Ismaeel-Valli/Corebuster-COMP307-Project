using System.Collections.Generic;
using Unity.FPS.AI;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Builds the armoured Cyber Soldier enemies: copies of the Robot1 piston brawler and rivet gunner
/// with the Cyber Soldier model, hit sparks, a breakable armour shell and tougher stats.
/// </summary>
public static class CyberSoldierBuilder
{
    const string ModelPath = "Assets/MyAssets/CyberSoldier/CyberSoldier.fbx";
    const string TextureFolder = "Assets/MyAssets/CyberSoldier/";
    const string MaterialFolder = "Assets/FPS/Art/Materials/CyberSoldier";
    internal const string GalaxySourcePath = "Assets/Materials/Textured.mat";
    internal const string SparksPath = "Assets/FPS/Prefabs/VFX/VFX_HitSparks.prefab";
    const string EnemyFolder = "Assets/FPS/Prefabs/Enemies/";
    internal const string RivetProjectilePath = "Assets/FPS/Prefabs/Projectiles/Projectile_Rivet.prefab";
    const string HeavyRivetProjectilePath = "Assets/FPS/Prefabs/Projectiles/Projectile_Rivet_Heavy.prefab";

    // Robot1's mesh is authored in centimetres and the Cyber Soldier's in metres; the galaxy shader
    // tiles in object space, so the soldier's copy needs a proportionally larger tiling scale.
    const float GalaxyTriplanarScale = 1.1f;

    static readonly HumanBodyBones[] k_SparkBones =
    {
        HumanBodyBones.Head, HumanBodyBones.Chest, HumanBodyBones.Spine, HumanBodyBones.Hips,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
    };

    [MenuItem("Tools/CoreBuster/Create Armoured Cyber Soldier Prefabs")]
    public static void BuildAll()
    {
        BuildVariant(EnemyFolder + "Enemy_Robot1_PistonFist.prefab",
            EnemyFolder + "Enemy_CyberSoldier_PistonFist.prefab", ConfigureBrawler);
        BuildVariant(EnemyFolder + "Enemy_Robot1_RivetCannon.prefab",
            EnemyFolder + "Enemy_CyberSoldier_RivetCannon.prefab", ConfigureGunner);
        AssetDatabase.SaveAssets();
    }

    // Robot1 brawler: 100 hp, 25 per punch, 6.5 m/s, 0.55 s between punches.
    static void ConfigureBrawler(GameObject root)
    {
        root.GetComponent<Health>().MaxHealth = 150f;
        var agent = root.GetComponent<NavMeshAgent>();
        agent.speed = 7.5f;
        agent.acceleration = 30f;
        var detection = root.GetComponent<DetectionModule>();
        detection.DetectionRange = 18f;
        var piston = root.GetComponent<PistonFist>();
        piston.Damage = 35f;
        piston.CooldownDuration = .4f;
    }

    // Robot1 gunner: 100 hp, 4 x 12 damage bursts every ~2.7 s, 4.5 m/s.
    static void ConfigureGunner(GameObject root)
    {
        root.GetComponent<Health>().MaxHealth = 150f;
        root.GetComponent<NavMeshAgent>().speed = 5.5f;
        var detection = root.GetComponent<DetectionModule>();
        detection.DetectionRange = 26f;
        detection.AttackRange = 18f;

        var cannon = root.GetComponentInChildren<RivetCannon>();
        cannon.ShotsPerBurst = 5;
        cannon.ShotInterval = .17f;
        cannon.CooldownDuration = 1.3f;
        cannon.BurstTurnSpeed = 50f;

        var heavy = BuildHeavyRivet();
        var weapon = cannon.GetComponent<WeaponController>();
        if (heavy) weapon.ProjectilePrefab = heavy;
        // Reload a full burst within the cooldown + wind-up window.
        weapon.MaxAmmo = Mathf.Max(weapon.MaxAmmo, cannon.ShotsPerBurst);
        weapon.AmmoReloadRate = cannon.ShotsPerBurst;
    }

    static ProjectileBase BuildHeavyRivet()
    {
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(HeavyRivetProjectilePath))
        {
            if (!AssetDatabase.CopyAsset(RivetProjectilePath, HeavyRivetProjectilePath)) return null;
            AssetDatabase.ImportAsset(HeavyRivetProjectilePath);
        }

        var contents = PrefabUtility.LoadPrefabContents(HeavyRivetProjectilePath);
        try
        {
            contents.name = "Projectile_Rivet_Heavy";
            var projectile = contents.GetComponent<ProjectileStandard>();
            projectile.Damage = 15f;
            projectile.Speed = 36f;
            PrefabUtility.SaveAsPrefabAsset(contents, HeavyRivetProjectilePath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
        return AssetDatabase.LoadAssetAtPath<ProjectileBase>(HeavyRivetProjectilePath);
    }

    static void BuildVariant(string sourcePath, string outputPath, System.Action<GameObject> configure)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var sparks = AssetDatabase.LoadAssetAtPath<GameObject>(SparksPath);
        if (!source || !model)
        {
            Debug.LogError("Missing " + (!source ? sourcePath : ModelPath));
            return;
        }

        Avatar soldierAvatar = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            if (asset is Avatar avatar) soldierAvatar = avatar;

        Material armour = BuildArmourMaterial();
        Material galaxy = BuildGalaxyMaterial();

        // A plain copy, so later Robot1 edits don't leak into the soldiers.
        var root = Object.Instantiate(source);
        root.name = System.IO.Path.GetFileNameWithoutExtension(outputPath);
        try
        {
            var soldierBones = SwapBody(root, model, soldierAvatar);
            var body = root.GetComponentInChildren<SkinnedMeshRenderer>();
            body.sharedMaterial = armour;
            Bounds bounds = body.bounds;
            FitToBody(root, bounds);

            var proceduralAnimator = root.GetComponent<Robot1ProceduralAnimator>();
            proceduralAnimator.ApplyNebulaMaterial = false; // The armour covers the galaxy body until it breaks.
            proceduralAnimator.NebulaMaterial = galaxy;

            var armourComponent = root.AddComponent<ArmouredEnemy>();
            armourComponent.BodyRenderer = body;
            armourComponent.ExposedMaterial = galaxy;
            armourComponent.DebrisMaterial = BuildDebrisMaterial(armour);
            armourComponent.BreakSfx = FindClip("Launcher_Explosion");
            armourComponent.HitSparks = AddSparks(sparks, soldierBones, bounds.center);

            var enemy = root.GetComponent<EnemyController>();
            enemy.DamageTick = FindClip("Damage_Tick2");

            configure(root);
            CoreExecutionBuilder.InstallOnEnemy(root);
            PistonFistArmFitter.Fit(root);

            PrefabUtility.SaveAsPrefabAsset(root, outputPath);
            Debug.Log("Armoured Cyber Soldier created: " + outputPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// Replaces a Robot1-based enemy's body and skeleton with a humanoid model, carrying the forearm
    /// weapons across to the same place along the new forearms. Returns the new skeleton's bone map.
    /// </summary>
    internal static Dictionary<HumanBodyBones, Transform> SwapBody(GameObject root, GameObject model, Avatar avatar)
    {
        var animator = root.GetComponent<Animator>();
        var robotBones = HumanBoneMap(animator.avatar, root.transform);

        // Lift the weapons off Robot1's forearms, remembering where they sat along each forearm.
        var attachments = new List<Attachment>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            bool isAttachment = t.name.StartsWith("Robot1 Piston Fist Arm") || t.GetComponent<WeaponController>();
            if (!isAttachment || !TryGetHumanBone(robotBones, t.parent, out HumanBodyBones bone)) continue;
            var attachment = new Attachment { Transform = t, Bone = bone, WorldScale = t.lossyScale };
            if (!TryGetLimbFrame(robotBones, bone, root.transform, out attachment.Frame)) continue;
            attachment.Capture();
            attachments.Add(attachment);
        }
        foreach (var attachment in attachments) attachment.Transform.SetParent(root.transform, true);

        // Swap the Robot1 body and skeleton for the new model.
        var keep = new HashSet<Transform>();
        foreach (var attachment in attachments) keep.Add(attachment.Transform);
        var actor = root.GetComponent<Actor>();
        if (actor.AimPoint) keep.Add(actor.AimPoint);
        var oldChildren = new List<GameObject>();
        foreach (Transform child in root.transform)
            if (!keep.Contains(child)) oldChildren.Add(child.gameObject);
        foreach (var child in oldChildren) Object.DestroyImmediate(child);

        var instance = Object.Instantiate(model);
        try
        {
            var modelChildren = new List<Transform>();
            foreach (Transform child in instance.transform) modelChildren.Add(child);
            foreach (var child in modelChildren) child.SetParent(root.transform, false);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
        animator.avatar = avatar;

        var newBones = HumanBoneMap(avatar, root.transform);
        foreach (var attachment in attachments)
        {
            if (!newBones.TryGetValue(attachment.Bone, out Transform newBone) ||
                !TryGetLimbFrame(newBones, attachment.Bone, root.transform, out LimbFrame frame))
            {
                Debug.LogWarning(model.name + " has no " + attachment.Bone + " for " + attachment.Transform.name);
                continue;
            }
            attachment.Apply(frame, newBone);
        }
        return newBones;
    }

    /// <summary>Sizes the body collider, NavMesh agent and aim point to the given body bounds.</summary>
    internal static void FitToBody(GameObject root, Bounds bounds)
    {
        var capsule = root.GetComponent<CapsuleCollider>();
        capsule.center = root.transform.InverseTransformPoint(bounds.center);
        capsule.height = bounds.size.y;
        var agent = root.GetComponent<NavMeshAgent>();
        agent.height = capsule.height;
        agent.radius = capsule.radius;
        var actor = root.GetComponent<Actor>();
        if (actor.AimPoint)
            actor.AimPoint.position = bounds.center + Vector3.up * (bounds.size.y * .25f);
    }

    internal static ParticleSystem[] AddSparks(GameObject sparksPrefab, Dictionary<HumanBodyBones, Transform> bones, Vector3 bodyCenter)
    {
        var result = new List<ParticleSystem>();
        if (!sparksPrefab) return result.ToArray();
        foreach (var bone in k_SparkBones)
        {
            if (!bones.TryGetValue(bone, out Transform socket)) continue;
            var spark = (GameObject)PrefabUtility.InstantiatePrefab(sparksPrefab, socket);
            spark.name = "VFX_HitSparks " + bone;
            Vector3 outward = socket.position - new Vector3(bodyCenter.x, socket.position.y, bodyCenter.z);
            if (outward.sqrMagnitude < .0001f) outward = Vector3.forward;
            spark.transform.SetPositionAndRotation(socket.position, Quaternion.LookRotation(outward));
            result.Add(spark.GetComponent<ParticleSystem>());
        }
        return result.ToArray();
    }

    static Material BuildArmourMaterial()
    {
        EnsureFolder();
        string path = MaterialFolder + "/CyberSoldier_Armour.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material) return material;

        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "CyberSoldier_Armour" };
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "CyberSoldier_Diff.tga");
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "CyberSoldier_Normal.tga");
        var specular = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "CyberSoldier_Spec.tga");
        var emissive = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "CyberBody_Emissive.tga");

        material.SetTexture("_BaseMap", albedo);
        material.SetTexture("_MainTex", albedo);
        material.SetColor("_BaseColor", Color.white);
        if (normal)
        {
            material.SetTexture("_BumpMap", normal);
            material.EnableKeyword("_NORMALMAP");
        }
        if (specular)
        {
            // The pack ships a specular map, so use URP's specular workflow.
            material.SetFloat("_WorkflowMode", 0f);
            material.EnableKeyword("_SPECULAR_SETUP");
            material.SetTexture("_SpecGlossMap", specular);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_Smoothness", .75f);
        }
        if (emissive)
        {
            material.SetTexture("_EmissionMap", emissive);
            material.SetColor("_EmissionColor", new Color(.46f, .46f, .65f) * 1.5f);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.EnableKeyword("_EMISSION");
        }
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // The chunks are open pieces of the body surface, so render their inside too.
    static Material BuildDebrisMaterial(Material armour)
    {
        string path = MaterialFolder + "/CyberSoldier_ArmourDebris.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material) return material;

        material = new Material(armour) { name = "CyberSoldier_ArmourDebris" };
        material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        material.doubleSidedGI = true;
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static Material BuildGalaxyMaterial()
    {
        EnsureFolder();
        string path = MaterialFolder + "/CyberSoldier_Galaxy.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material) return material;

        var source = AssetDatabase.LoadAssetAtPath<Material>(GalaxySourcePath);
        if (!source) return null;
        material = new Material(source) { name = "CyberSoldier_Galaxy" };
        material.SetFloat("_TriplanarScale", GalaxyTriplanarScale);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/FPS/Art/Materials", "CyberSoldier");
    }

    internal static AudioClip FindClip(string name)
    {
        foreach (var guid in AssetDatabase.FindAssets(name + " t:AudioClip"))
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
            if (clip && clip.name == name) return clip;
        }
        return null;
    }

    // The avatar is not always initialised on an edit-time copy, so resolve bones from its description.
    internal static Dictionary<HumanBodyBones, Transform> HumanBoneMap(Avatar avatar, Transform root)
    {
        var map = new Dictionary<HumanBodyBones, Transform>();
        if (!avatar) return map;

        var byName = new Dictionary<string, Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (!byName.ContainsKey(t.name)) byName[t.name] = t;

        var humanBones = new Dictionary<string, HumanBodyBones>();
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            humanBones[HumanTrait.BoneName[i].Replace(" ", "")] = (HumanBodyBones)i;

        foreach (var human in avatar.humanDescription.human)
        {
            if (humanBones.TryGetValue(human.humanName.Replace(" ", ""), out HumanBodyBones bone) &&
                byName.TryGetValue(human.boneName, out Transform t))
                map[bone] = t;
        }
        return map;
    }

    static bool TryGetHumanBone(Dictionary<HumanBodyBones, Transform> map, Transform t, out HumanBodyBones bone)
    {
        foreach (var pair in map)
        {
            if (pair.Value == t)
            {
                bone = pair.Key;
                return true;
            }
        }
        bone = HumanBodyBones.LastBone;
        return false;
    }

    // A rig-independent frame along a limb bone: forward runs to the next joint, up follows the body.
    struct LimbFrame
    {
        public Vector3 Origin;
        public Quaternion Rotation;
        public float Length;
    }

    static bool TryGetLimbFrame(Dictionary<HumanBodyBones, Transform> map, HumanBodyBones bone, Transform root,
        out LimbFrame frame)
    {
        frame = default;
        if (!map.TryGetValue(bone, out Transform start) || !map.TryGetValue(NextJoint(bone), out Transform end))
            return false;

        Vector3 along = end.position - start.position;
        if (along.sqrMagnitude < .000001f) return false;
        Vector3 up = Vector3.ProjectOnPlane(root.up, along);
        if (up.sqrMagnitude < .0001f) up = Vector3.ProjectOnPlane(root.forward, along);

        frame.Origin = start.position;
        frame.Rotation = Quaternion.LookRotation(along, up);
        frame.Length = along.magnitude;
        return true;
    }

    static HumanBodyBones NextJoint(HumanBodyBones bone)
    {
        switch (bone)
        {
            case HumanBodyBones.LeftUpperArm: return HumanBodyBones.LeftLowerArm;
            case HumanBodyBones.RightUpperArm: return HumanBodyBones.RightLowerArm;
            case HumanBodyBones.LeftLowerArm: return HumanBodyBones.LeftHand;
            case HumanBodyBones.RightLowerArm: return HumanBodyBones.RightHand;
            case HumanBodyBones.LeftUpperLeg: return HumanBodyBones.LeftLowerLeg;
            case HumanBodyBones.RightUpperLeg: return HumanBodyBones.RightLowerLeg;
            case HumanBodyBones.LeftLowerLeg: return HumanBodyBones.LeftFoot;
            case HumanBodyBones.RightLowerLeg: return HumanBodyBones.RightFoot;
            default: return HumanBodyBones.LastBone;
        }
    }

    class Attachment
    {
        public Transform Transform;
        public HumanBodyBones Bone;
        public Vector3 WorldScale;
        public LimbFrame Frame;
        Vector3 m_LocalPosition;
        Quaternion m_LocalRotation;

        // Offsets are stored in forearm lengths so they carry over to a differently proportioned arm.
        public void Capture()
        {
            Quaternion inverse = Quaternion.Inverse(Frame.Rotation);
            m_LocalPosition = inverse * (Transform.position - Frame.Origin) / Frame.Length;
            m_LocalRotation = inverse * Transform.rotation;
        }

        public void Apply(LimbFrame frame, Transform newParent)
        {
            Transform.SetParent(newParent, true);
            Transform.SetPositionAndRotation(frame.Origin + frame.Rotation * (m_LocalPosition * frame.Length),
                frame.Rotation * m_LocalRotation);
            Vector3 parentScale = newParent.lossyScale;
            Transform.localScale = new Vector3(
                WorldScale.x / Mathf.Max(.0001f, Mathf.Abs(parentScale.x)),
                WorldScale.y / Mathf.Max(.0001f, Mathf.Abs(parentScale.y)),
                WorldScale.z / Mathf.Max(.0001f, Mathf.Abs(parentScale.z)));
        }
    }
}
