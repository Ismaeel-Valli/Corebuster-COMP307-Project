using Unity.FPS.AI;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEngine;

public static class ScrapScattergunBuilder
{
    const string WeaponPath = "Assets/FPS/Prefabs/Weapons/Weapon_ScrapScattergun.prefab";
    const string ProjectilePath = "Assets/FPS/Prefabs/Projectiles/Projectile_ScrapFragment.prefab";
    const string RobotPath = "Assets/FPS/Prefabs/Enemies/Enemy_HoverBot_ScrapScattergun.prefab";
    const string MaterialFolder = "Assets/FPS/Art/Materials/ScrapScattergun";

    [MenuItem("Tools/CoreBuster/Create Scrap Scattergun Weapon Prefab")]
    public static void BuildWeapon()
    {
        EnsureMaterialFolder();
        var projectile = BuildProjectile();
        if (!projectile)
        {
            Debug.LogError("Could not create the Scrap Scattergun projectile from Projectile_Shotgun.prefab.");
            return;
        }
        Material steel = MakeMaterial("ScrapSteel", new Color(.16f, .19f, .2f), .75f);
        Material brass = MakeMaterial("ScrapBrass", new Color(.55f, .31f, .08f), .7f);
        Material warning = MakeMaterial("ScrapWarning", new Color(1f, .22f, .02f), .1f);
        warning.EnableKeyword("_EMISSION");

        var root = new GameObject("Weapon_ScrapScattergun");
        try
        {
            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            var weapon = root.AddComponent<WeaponController>();
            var scattergun = root.AddComponent<ScrapScattergun>();

            var weaponRoot = new GameObject("WeaponRoot").transform;
            weaponRoot.SetParent(root.transform, false);
            Part(weaponRoot, "Receiver", PrimitiveType.Cube, new Vector3(0f, 0f, .3f),
                new Vector3(.85f, .5f, 1.15f), steel, false);

            var barrelAssembly = new GameObject("OpeningBarrels").transform;
            barrelAssembly.SetParent(weaponRoot, false);
            barrelAssembly.localPosition = new Vector3(0f, 0f, .9f);
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -.22f : .22f;
                var barrel = Part(barrelAssembly, "Barrel " + (i + 1), PrimitiveType.Cylinder,
                    new Vector3(x, 0f, .45f), new Vector3(.13f, .55f, .13f), brass, false);
                barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(weaponRoot, false);
            muzzle.localPosition = new Vector3(0f, 0f, 1.1f);

            var lamp = Part(weaponRoot, "Cocking lamp", PrimitiveType.Sphere,
                new Vector3(0f, .3f, .2f), new Vector3(.12f, .12f, .12f), warning, false);

            weapon.WeaponName = "Scrap Scattergun";
            weapon.WeaponRoot = weaponRoot.gameObject;
            weapon.WeaponMuzzle = muzzle;
            weapon.ShootType = WeaponShootType.Charge;
            weapon.ProjectilePrefab = projectile;
            weapon.DelayBetweenShots = 1.35f;
            weapon.BulletSpreadAngle = 16f;
            weapon.BulletsPerShot = 10;
            weapon.RecoilForce = 2f;
            weapon.AutomaticReload = true;
            weapon.MaxAmmo = 4;
            weapon.AmmoReloadDelay = 1.5f;
            weapon.AmmoReloadRate = 1.2f;
            weapon.AutomaticReleaseOnCharged = true;
            weapon.MaxChargeDuration = .55f;
            weapon.AmmoUsedOnStartCharge = 1f;
            weapon.AmmoUsageRateWhileCharging = 0f;
            weapon.MuzzleFlashPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/FPS/Prefabs/VFX/VFX_MuzzleFlashRed.prefab");

            scattergun.BarrelAssembly = barrelAssembly;
            scattergun.WeaponRoot = weaponRoot;
            scattergun.RecoilDistance = .28f;
            scattergun.CockingColor = new Color(1f, .3f, .02f);

            PrefabUtility.SaveAsPrefabAsset(root, WeaponPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Scrap Scattergun weapon created: " + WeaponPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [MenuItem("Tools/CoreBuster/Create Scrap Scattergun Robot Prefab")]
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
            Debug.LogError("Could not find the base robot or Scrap Scattergun weapon prefab.");
            return;
        }

        var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
        root.name = "Enemy_HoverBot_ScrapScattergun";
        try
        {
            foreach (var existingWeapon in root.GetComponentsInChildren<WeaponController>(true))
                Object.DestroyImmediate(existingWeapon.gameObject);

            var weapon = (GameObject)PrefabUtility.InstantiatePrefab(weaponSource, root.transform);
            weapon.name = "Weapon_ScrapScattergun";
            weapon.transform.localPosition = new Vector3(0f, 1.1f, .35f);
            weapon.transform.localRotation = Quaternion.identity;

            var detection = root.GetComponentInChildren<DetectionModule>();
            if (detection)
            {
                detection.DetectionRange = 20f;
                detection.AttackRange = 8f;
            }

            var mobile = root.GetComponent<EnemyMobile>();
            if (mobile) mobile.AttackStopDistanceRatio = .5f;

            var actor = root.GetComponent<Actor>();
            if (actor) actor.Affiliation = 0;

            PrefabUtility.SaveAsPrefabAsset(root, RobotPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Scrap Scattergun robot created: " + RobotPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static ProjectileBase BuildProjectile()
    {
        var existing = AssetDatabase.LoadAssetAtPath<ProjectileBase>(ProjectilePath);
        if (existing) return existing;

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/FPS/Prefabs/Projectiles/Projectile_Shotgun.prefab");
        if (!source) return null;
        var fragmentMaterial = MakeMaterial("FragmentSteel", new Color(.25f, .27f, .28f), .85f);
        var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
        root.name = "Projectile_ScrapFragment";
        try
        {
            var projectile = root.GetComponent<ProjectileStandard>();
            projectile.Speed = 110f;
            projectile.GravityDownAcceleration = 30f;
            projectile.Damage = 5f;
            projectile.MaxLifeTime = 2.5f;
            projectile.Radius = .045f;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterial = fragmentMaterial;

            return PrefabUtility.SaveAsPrefabAsset(root, ProjectilePath)
                .GetComponent<ProjectileBase>();
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static void EnsureMaterialFolder()
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/FPS/Art/Materials"))
                AssetDatabase.CreateFolder("Assets/FPS/Art", "Materials");
            AssetDatabase.CreateFolder("Assets/FPS/Art/Materials", "ScrapScattergun");
        }
    }

    static Material MakeMaterial(string name, Color color, float metallic)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material) return material;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) shader = Shader.Find("Standard");
        material = new Material(shader) { name = name, color = color };
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", .55f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 position,
        Vector3 scale, Material material, bool collider)
    {
        var part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
        if (!collider) Object.DestroyImmediate(part.GetComponent<Collider>());
        return part;
    }
}
