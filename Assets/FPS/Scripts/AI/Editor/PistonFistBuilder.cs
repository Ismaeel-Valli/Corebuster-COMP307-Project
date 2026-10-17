using Unity.FPS.AI;
using Unity.FPS.Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class PistonFistBuilder
{
    public const string PrefabPath = "Assets/FPS/Prefabs/Enemies/Enemy_PistonFist.prefab";

    [MenuItem("Tools/CoreBuster/Create Piston Fist Prefab")]
    public static void Build()
    {
        const string folder = "Assets/FPS/Art/Materials/PistonFist";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/FPS/Art/Materials", "PistonFist");
        Material steel = MakeMaterial(folder, "Steel", new Color(.19f, .24f, .29f), .75f);
        Material yellow = MakeMaterial(folder, "SafetyYellow", new Color(.95f, .55f, .06f), .4f);
        Material dark = MakeMaterial(folder, "Dark", new Color(.035f, .045f, .055f), .3f);
        Material chrome = MakeMaterial(folder, "Chrome", new Color(.65f, .72f, .78f), .9f);
        Material warning = MakeMaterial(folder, "Warning", new Color(1f, .35f, 0f), .1f);
        warning.EnableKeyword("_EMISSION");
        var root = new GameObject("Enemy_PistonFist");
        try
        {
            root.AddComponent<Health>().MaxHealth = 100f;
            var actor = root.AddComponent<Actor>();
            // The player is affiliation 1 in this project; enemies must be affiliation 0.
            actor.Affiliation = 0;
            root.AddComponent<NavMeshAgent>().enabled = false;
            var enemy = root.AddComponent<EnemyController>();
            enemy.OnHitBodyGradient = new Gradient();
            enemy.OrientationSpeed = 5f;
            Part(root.transform, "Forearm housing", PrimitiveType.Cube, new Vector3(0, 1.2f, -.25f), new Vector3(1.3f, 1.05f, 1.5f), steel, true).AddComponent<Damageable>();
            Part(root.transform, "Mount", PrimitiveType.Cube, new Vector3(0, .25f, -.4f), new Vector3(1.5f, .5f, 1.6f), dark, true).AddComponent<Damageable>();
            Part(root.transform, "Collar", PrimitiveType.Cube, new Vector3(0, 1.2f, .45f), new Vector3(1.4f, 1.15f, .2f), yellow, false);
            var cylinder = Part(root.transform, "Sliding cylinder", PrimitiveType.Cylinder, new Vector3(0, 1.2f, .5f), new Vector3(.28f, .5f, .28f), chrome, false);
            cylinder.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var fist = new GameObject("Fist").transform;
            fist.SetParent(root.transform, false);
            fist.localPosition = new Vector3(0, 1.2f, 1);
            Part(fist, "Blocky fist", PrimitiveType.Cube, Vector3.zero, new Vector3(1.1f, .9f, .8f), yellow, false);
            for (int i = 0; i < 4; i++)
                Part(fist, "Knuckle " + (i + 1), PrimitiveType.Cube, new Vector3(-.405f + i * .27f, .07f, .4f), new Vector3(.23f, .65f, .12f), steel, false);
            Part(fist, "Thumb", PrimitiveType.Cube, new Vector3(.59f, -.18f, -.06f), new Vector3(.22f, .45f, .5f), steel, false);
            var lamp = Part(root.transform, "Wind-up warning", PrimitiveType.Cube, new Vector3(0, 1.78f, .18f), new Vector3(.8f, .12f, .3f), warning, false);
            var eye = new GameObject("Detection and aim point").transform;
            eye.SetParent(root.transform, false);
            eye.localPosition = new Vector3(0, 1.85f, .55f);
            actor.AimPoint = eye;
            enemy.DeathVfxSpawnPoint = eye;
            var detection = root.AddComponent<DetectionModule>();
            detection.DetectionSourcePoint = eye;
            detection.DetectionRange = 12f;
            detection.AttackRange = 3.2f;
            var piston = root.AddComponent<PistonFist>();
            piston.Fist = fist;
            piston.SlidingCylinder = cylinder.transform;
            piston.WarningLamp = lamp.GetComponent<Renderer>();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Piston fist prefab created: " + PrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    [MenuItem("Tools/CoreBuster/Create Piston Fist Robot Prefab")]
    public static void BuildAttachedRobot()
    {
        const string sourcePath = "Assets/FPS/Prefabs/Enemies/Enemy_HoverBot.prefab";
        const string outputPath = "Assets/FPS/Prefabs/Enemies/Enemy_HoverBot_PistonFist.prefab";
        const string folder = "Assets/FPS/Art/Materials/PistonFist";

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (!source)
        {
            Debug.LogError("Could not find the robot prefab at " + sourcePath);
            return;
        }

        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/FPS/Art/Materials", "PistonFist");

        Material steel = MakeMaterial(folder, "Steel", new Color(.19f, .24f, .29f), .75f);
        Material yellow = MakeMaterial(folder, "SafetyYellow", new Color(.95f, .55f, .06f), .4f);
        Material chrome = MakeMaterial(folder, "Chrome", new Color(.65f, .72f, .78f), .9f);
        Material warning = MakeMaterial(folder, "Warning", new Color(1f, .35f, 0f), .1f);
        warning.EnableKeyword("_EMISSION");

        var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
        root.name = "Enemy_HoverBot_PistonFist";
        try
        {
            var arm = new GameObject("Piston Fist Arm").transform;
            arm.SetParent(root.transform, false);
            arm.localPosition = new Vector3(-.72f, -.55f, .25f);

            Part(arm, "Forearm housing", PrimitiveType.Cube, new Vector3(0, 1.2f, -.25f),
                new Vector3(1.1f, .9f, 1.3f), steel, true);
            var cylinder = Part(arm, "Sliding cylinder", PrimitiveType.Cylinder, new Vector3(0, 1.2f, .5f),
                new Vector3(.24f, .5f, .24f), chrome, false);
            cylinder.transform.localRotation = Quaternion.Euler(90, 0, 0);

            var fist = new GameObject("Fist").transform;
            fist.SetParent(arm, false);
            fist.localPosition = new Vector3(0, 1.2f, 1f);
            Part(fist, "Blocky fist", PrimitiveType.Cube, Vector3.zero,
                new Vector3(.95f, .8f, .75f), yellow, false);
            for (int i = 0; i < 4; i++)
                Part(fist, "Knuckle " + (i + 1), PrimitiveType.Cube,
                    new Vector3(-.35f + i * .23f, .07f, .38f), new Vector3(.2f, .55f, .1f), steel, false);

            var lamp = Part(arm, "Wind-up warning", PrimitiveType.Cube, new Vector3(0, 1.78f, .18f),
                new Vector3(.7f, .1f, .25f), warning, false);

            var piston = root.AddComponent<PistonFist>();
            piston.Fist = fist;
            piston.SlidingCylinder = cylinder.transform;
            piston.WarningLamp = lamp.GetComponent<Renderer>();
            piston.HitHalfExtents = new Vector3(.5f, .4f, .36f);
            piston.Damage = 25f;
            piston.HitMask = ~0;

            var actor = root.GetComponent<Actor>();
            if (actor) actor.Affiliation = 0;

            PrefabUtility.SaveAsPrefabAsset(root, outputPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Piston fist robot prefab created: " + outputPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static Material MakeMaterial(string folder, string name, Color color, float metallic)
    {
        string path = folder + "/" + name + ".mat";
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

    static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material, bool collider)
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
