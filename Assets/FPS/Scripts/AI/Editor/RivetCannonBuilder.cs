using Unity.FPS.AI;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEngine;

public static class RivetCannonBuilder
{
    const string WeaponPath = "Assets/FPS/Prefabs/Weapons/Weapon_RivetCannon.prefab";
    const string ProjectilePath = "Assets/FPS/Prefabs/Projectiles/Projectile_Rivet.prefab";
    const string RobotPath = "Assets/FPS/Prefabs/Enemies/Enemy_HoverBot_RivetCannon.prefab";
    const string Robot1SourcePath = "Assets/FPS/Prefabs/Enemies/Enemy_Robot1_PistonFist.prefab";
    const string Robot1Path = "Assets/FPS/Prefabs/Enemies/Enemy_Robot1_RivetCannon.prefab";
    // World-space size of the cannon on Robot1's forearm (the weapon prefab is authored ~1.25 m long).
    const float Robot1CannonScale = .4f;
    const string MaterialFolder = "Assets/FPS/Art/Materials/RivetCannon";

    [MenuItem("Tools/CoreBuster/Create Rivet Cannon Weapon Prefab")]
    public static void BuildWeapon()
    {
        EnsureMaterialFolder();
        var projectile = BuildProjectile();
        if (!projectile)
        {
            Debug.LogError("Could not create the Rivet projectile from Projectile_Hoverbot.prefab.");
            return;
        }
        Material gunmetal = MakeMaterial("RivetGunmetal", new Color(.14f, .15f, .17f), .8f);
        Material brass = MakeMaterial("RivetBrass", new Color(.6f, .4f, .12f), .75f);
        Material rust = MakeMaterial("RivetRust", new Color(.36f, .16f, .07f), .3f);
        Material warning = MakeMaterial("RivetWarning", new Color(1f, .5f, .05f), .1f);
        MakeEmissive(warning);

        var root = new GameObject("Weapon_RivetCannon");
        try
        {
            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            var weapon = root.AddComponent<WeaponController>();
            var cannon = root.AddComponent<RivetCannon>();

            // Origin is the forearm mount point; local +Z is the firing direction.
            var weaponRoot = new GameObject("WeaponRoot").transform;
            weaponRoot.SetParent(root.transform, false);
            Cylinder(weaponRoot, "Forearm sleeve", new Vector3(0f, 0f, 0f), new Vector3(.42f, .35f, .42f), rust);
            Cylinder(weaponRoot, "Clamp band front", new Vector3(0f, 0f, .2f), new Vector3(.46f, .035f, .46f), brass);
            Cylinder(weaponRoot, "Clamp band rear", new Vector3(0f, 0f, -.2f), new Vector3(.46f, .035f, .46f), brass);

            var aimPivot = new GameObject("AimPivot").transform;
            aimPivot.SetParent(weaponRoot, false);
            aimPivot.localPosition = new Vector3(0f, 0f, .35f);
            Part(aimPivot, "Breech housing", PrimitiveType.Cube, new Vector3(0f, 0f, .2f),
                new Vector3(.36f, .36f, .5f), gunmetal);
            var drum = Part(aimPivot, "Rivet feed drum", PrimitiveType.Cylinder, new Vector3(.25f, 0f, .15f),
                new Vector3(.3f, .08f, .3f), brass);
            drum.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            var barrel = new GameObject("Barrel").transform;
            barrel.SetParent(aimPivot, false);
            barrel.localPosition = new Vector3(0f, 0f, .45f);
            Cylinder(barrel, "Barrel sleeve", new Vector3(0f, 0f, .35f), new Vector3(.2f, .35f, .2f), gunmetal);
            Cylinder(barrel, "Muzzle ring", new Vector3(0f, 0f, .7f), new Vector3(.27f, .05f, .27f), brass);

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(aimPivot, false);
            muzzle.localPosition = new Vector3(0f, 0f, 1.25f);

            var lamp = Part(aimPivot, "Warning lamp", PrimitiveType.Sphere, new Vector3(0f, .23f, .15f),
                new Vector3(.11f, .11f, .11f), warning);

            weapon.WeaponName = "Rivet Cannon";
            weapon.WeaponRoot = weaponRoot.gameObject;
            weapon.WeaponMuzzle = muzzle;
            weapon.ShootType = WeaponShootType.Manual;
            weapon.ProjectilePrefab = projectile;
            // RivetCannon sets the burst cadence; this only needs to be shorter than ShotInterval.
            weapon.DelayBetweenShots = .05f;
            weapon.BulletSpreadAngle = 1.5f;
            weapon.BulletsPerShot = 1;
            weapon.RecoilForce = 1f;
            weapon.AutomaticReload = true;
            weapon.MaxAmmo = cannon.ShotsPerBurst;
            weapon.AmmoReloadDelay = .5f;
            weapon.AmmoReloadRate = cannon.ShotsPerBurst / 1.2f;
            weapon.MuzzleFlashPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/FPS/Prefabs/VFX/VFX_MuzzleFlashRed.prefab");
            weapon.ShootSfx = AssetDatabase.LoadAssetAtPath<AudioClip>(
                "Assets/FPS/Audio/SFX/Weapons/Shotgun_Shot.wav");

            cannon.AimPivot = aimPivot;
            cannon.Barrel = barrel;
            cannon.WarningLamp = lamp.GetComponent<Renderer>();
            cannon.WarningSfx = AssetDatabase.LoadAssetAtPath<AudioClip>(
                "Assets/FPS/Audio/SFX/Weapons/Launcher_Charge_BuildUp.wav");

            PrefabUtility.SaveAsPrefabAsset(root, WeaponPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Rivet Cannon weapon created: " + WeaponPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [MenuItem("Tools/CoreBuster/Create Rivet Cannon Robot Prefab")]
    public static void BuildRobot()
    {
        EnsureMaterialFolder();
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(WeaponPath))
        {
            BuildWeapon();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/FPS/Prefabs/Enemies/Enemy_HoverBot.prefab");
        var weaponSource = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponPath);
        if (!source || !weaponSource)
        {
            Debug.LogError("Could not find the base robot or Rivet Cannon weapon prefab.");
            return;
        }

        var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
        root.name = "Enemy_HoverBot_RivetCannon";
        try
        {
            foreach (var existingWeapon in root.GetComponentsInChildren<WeaponController>(true))
                Object.DestroyImmediate(existingWeapon.gameObject);

            var weapon = (GameObject)PrefabUtility.InstantiatePrefab(weaponSource, root.transform);
            weapon.name = "Weapon_RivetCannon";
            weapon.transform.localPosition = new Vector3(.45f, 1f, .2f);
            weapon.transform.localRotation = Quaternion.identity;

            var detection = root.GetComponentInChildren<DetectionModule>();
            if (detection)
            {
                detection.DetectionRange = 25f;
                detection.AttackRange = 16f;
            }

            var mobile = root.GetComponent<EnemyMobile>();
            if (mobile) mobile.AttackStopDistanceRatio = .6f;

            var actor = root.GetComponent<Actor>();
            if (actor) actor.Affiliation = 0;

            PrefabUtility.SaveAsPrefabAsset(root, RobotPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Rivet Cannon test robot created: " + RobotPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [MenuItem("Tools/CoreBuster/Create Robot1 Rivet Cannon Prefab")]
    public static void BuildRobot1()
    {
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(WeaponPath))
        {
            BuildWeapon();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        else
        {
            BuildProjectile(); // Repairs a projectile saved before its Tip was assigned.
        }

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Robot1SourcePath);
        var weaponSource = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponPath);
        if (!source || !weaponSource)
        {
            Debug.LogError("Could not find Enemy_Robot1_PistonFist or the Rivet Cannon weapon prefab.");
            return;
        }

        // A plain copy, so later piston-robot edits don't leak into the gunner.
        var root = Object.Instantiate(source);
        root.name = "Enemy_Robot1_RivetCannon";
        try
        {
            var piston = root.GetComponent<PistonFist>();
            if (piston) Object.DestroyImmediate(piston);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t && t.name.StartsWith("Robot1 Piston Fist Arm"))
                    Object.DestroyImmediate(t.gameObject);

            var animator = root.GetComponent<Animator>();
            Transform foreArm = animator ? animator.GetBoneTransform(HumanBodyBones.RightLowerArm) : null;
            Transform hand = animator ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            // The avatar is not always initialised on an edit-time copy; fall back to the Mixamo names.
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!foreArm && t.name == "mixamorig:RightForeArm") foreArm = t;
                if (!hand && t.name == "mixamorig:RightHand") hand = t;
            }
            if (!foreArm || !hand)
            {
                Debug.LogError("Robot1 avatar is missing the right forearm or hand bone.");
                return;
            }

            // Clamp the cannon along the forearm: +Z runs elbow -> wrist so it fires where the arm points.
            Vector3 along = hand.position - foreArm.position;
            Vector3 up = Vector3.ProjectOnPlane(root.transform.up, along.normalized);
            if (up.sqrMagnitude < .0001f) up = Vector3.ProjectOnPlane(root.transform.forward, along.normalized);
            var weapon = (GameObject)PrefabUtility.InstantiatePrefab(weaponSource, foreArm);
            weapon.name = "Weapon_RivetCannon";
            weapon.transform.SetPositionAndRotation(foreArm.position + along * .45f,
                Quaternion.LookRotation(along.normalized, up.normalized));
            Vector3 parentScale = foreArm.lossyScale;
            weapon.transform.localScale = new Vector3(Robot1CannonScale / parentScale.x,
                Robot1CannonScale / parentScale.y, Robot1CannonScale / parentScale.z);

            // A gunner: slower than the brawler and engages from range. It keeps advancing and only
            // plants its feet while a burst is winding up or firing (RivetCannon.PlantFeetWhileFiring).
            var detection = root.GetComponentInChildren<DetectionModule>();
            if (detection)
            {
                detection.DetectionRange = 24f;
                detection.AttackRange = 16f;
            }
            var chase = root.GetComponent<Robot1ChaseController>();
            if (chase) chase.StoppingDistance = 5f;
            var agent = root.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent)
            {
                agent.speed = 4.5f;
                agent.stoppingDistance = 5f;
            }

            PrefabUtility.SaveAsPrefabAsset(root, Robot1Path);
            AssetDatabase.SaveAssets();
            Debug.Log("Robot1 Rivet Cannon robot created: " + Robot1Path);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static ProjectileBase BuildProjectile()
    {
        var existing = AssetDatabase.LoadAssetAtPath<ProjectileBase>(ProjectilePath);
        if (existing)
        {
            RepairProjectileTip(existing.gameObject);
            return existing;
        }

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/FPS/Prefabs/Projectiles/Projectile_Hoverbot.prefab");
        if (!source) return null;
        Material boltSteel = MakeMaterial("RivetBoltSteel", new Color(.42f, .43f, .45f), .9f);
        Material hotTip = MakeMaterial("RivetHotTip", new Color(1f, .45f, .08f), .2f);
        hotTip.SetColor("_EmissionColor", new Color(1f, .4f, .05f) * 4f);
        MakeEmissive(hotTip);

        // A plain copy (not a prefab instance) so the original bullet mesh can be removed.
        var root = Object.Instantiate(source);
        root.name = "Projectile_Rivet";
        try
        {
            var projectile = root.GetComponent<ProjectileStandard>();
            projectile.Speed = 32f;
            projectile.GravityDownAcceleration = 0f;
            projectile.Damage = 12f;
            projectile.MaxLifeTime = 4f;
            projectile.Radius = .15f;

            var oldMesh = root.transform.Find("Mesh_Projectile");
            if (oldMesh) Object.DestroyImmediate(oldMesh.gameObject);

            // Shank points forward, head trails behind, with a heated tip so it reads in a dark arena.
            var bolt = new GameObject("Rivet Bolt").transform;
            bolt.SetParent(root.transform, false);
            Cylinder(bolt, "Shank", new Vector3(0f, 0f, -.1f), new Vector3(.1f, .2f, .1f), boltSteel);
            Cylinder(bolt, "Head", new Vector3(0f, 0f, -.3f), new Vector3(.22f, .035f, .22f), boltSteel);
            Part(bolt, "Hot tip", PrimitiveType.Sphere, new Vector3(0f, 0f, .1f),
                new Vector3(.11f, .11f, .11f), hotTip);
            // The Hoverbot's Tip lived under the removed mesh. Without it hit detection throws every frame.
            AssignProjectileTip(projectile);

            var trailShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (trailShader)
            {
                var trail = root.AddComponent<TrailRenderer>();
                trail.time = .25f;
                trail.startWidth = .14f;
                trail.endWidth = 0f;
                var trailMaterial = MakeMaterial("RivetTrail", new Color(1f, .5f, .1f, .7f), 0f, trailShader);
                MakeTransparent(trailMaterial);
                trail.sharedMaterial = trailMaterial;
                trail.startColor = new Color(1f, .55f, .15f, .8f);
                trail.endColor = new Color(1f, .3f, .05f, 0f);
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            return PrefabUtility.SaveAsPrefabAsset(root, ProjectilePath)
                .GetComponent<ProjectileBase>();
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // URP strips _EMISSION on revalidation while the GI flags say "emissive is black".
    static void MakeEmissive(Material material)
    {
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        material.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(material);
    }

    static void AssignProjectileTip(ProjectileStandard projectile)
    {
        var tip = projectile.transform.Find("Tip");
        if (!tip)
        {
            tip = new GameObject("Tip").transform;
            tip.SetParent(projectile.transform, false);
        }
        tip.localPosition = new Vector3(0f, 0f, .16f);
        projectile.Tip = tip;
        if (!projectile.Root) projectile.Root = projectile.transform;
    }

    static void RepairProjectileTip(GameObject prefab)
    {
        var existing = prefab.GetComponent<ProjectileStandard>();
        if (!existing || (existing.Tip && existing.Root)) return;

        var contents = PrefabUtility.LoadPrefabContents(ProjectilePath);
        try
        {
            AssignProjectileTip(contents.GetComponent<ProjectileStandard>());
            PrefabUtility.SaveAsPrefabAsset(contents, ProjectilePath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    static void EnsureMaterialFolder()
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/FPS/Art/Materials"))
                AssetDatabase.CreateFolder("Assets/FPS/Art", "Materials");
            AssetDatabase.CreateFolder("Assets/FPS/Art/Materials", "RivetCannon");
        }
    }

    static Material MakeMaterial(string name, Color color, float metallic, Shader shader = null)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material) return material;
        if (!shader) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) shader = Shader.Find("Standard");
        material = new Material(shader) { name = name, color = color };
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", .55f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // Alpha-blended URP particle setup so the trail fades out instead of ending in a hard edge.
    static void MakeTransparent(Material material)
    {
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
    }

    // Unity's cylinder primitive runs along Y; these are turned to run along the firing axis (Z).
    static GameObject Cylinder(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        var part = Part(parent, name, PrimitiveType.Cylinder, position, scale, material);
        part.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        return part;
    }

    static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 position,
        Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
        // Colliders would block the robot's own bolts and its movement sweep.
        Object.DestroyImmediate(part.GetComponent<Collider>());
        return part;
    }
}
