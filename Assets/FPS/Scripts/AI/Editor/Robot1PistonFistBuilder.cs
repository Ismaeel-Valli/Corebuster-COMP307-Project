using Unity.FPS.AI;
using Unity.FPS.Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class Robot1PistonFistBuilder
{
    const string ModelPath = "Assets/Models/Robot1.fbx";
    const string OutputPath = "Assets/FPS/Prefabs/Enemies/Enemy_Robot1_PistonFist.prefab";
    const string MaterialFolder = "Assets/FPS/Art/Materials/PistonFist";
    const string NebulaMaterialPath = "Assets/Materials/Textured.mat";

    [MenuItem("Tools/CoreBuster/Create Robot1 Piston Fist Prefab")]
    public static void Build()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (!model)
        {
            Debug.LogError("Robot1 model not found at " + ModelPath);
            return;
        }

        Material steel = LoadMaterial("Steel", new Color(.19f, .24f, .29f), .75f);
        Material yellow = LoadMaterial("SafetyYellow", new Color(.95f, .55f, .06f), .4f);
        Material chrome = LoadMaterial("Chrome", new Color(.65f, .72f, .78f), .9f);
        Material warning = LoadMaterial("Warning", new Color(1f, .35f, 0f), .1f);
        Material nebula = AssetDatabase.LoadAssetAtPath<Material>(NebulaMaterialPath);
        warning.EnableKeyword("_EMISSION");

        var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
        root.name = "Enemy_Robot1_PistonFist";
        try
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = CalculateBounds(renderers, root.transform);
            float visualScale = Mathf.Clamp(bounds.size.y / 2.5f, .35f, 2f);

            // Use the existing animated galaxy material on the imported robot body.
            // The procedural animator repeats this at runtime for prefab instances.
            if (nebula)
            {
                foreach (var renderer in renderers)
                    renderer.sharedMaterial = nebula;
            }

            var health = root.AddComponent<Health>();
            health.MaxHealth = 100f;
            var actor = root.AddComponent<Actor>();
            actor.Affiliation = 0;

            var collider = root.AddComponent<CapsuleCollider>();
            collider.center = root.transform.InverseTransformPoint(bounds.center);
            collider.height = Mathf.Max(.5f, bounds.size.y);
            collider.radius = Mathf.Max(.2f, Mathf.Max(bounds.size.x, bounds.size.z) * .35f);
            root.AddComponent<Damageable>();

            var agent = root.AddComponent<NavMeshAgent>();
            agent.speed = 3.5f;
            agent.acceleration = 8f;
            agent.angularSpeed = 180f;
            agent.stoppingDistance = 2.8f;
            agent.radius = collider.radius;
            agent.height = collider.height;

            var aimPoint = NewChild(root.transform, "Robot1 Aim Point");
            aimPoint.localPosition = root.transform.InverseTransformPoint(
                bounds.center + Vector3.up * (bounds.size.y * .25f));
            actor.AimPoint = aimPoint;

            var detection = root.AddComponent<DetectionModule>();
            detection.DetectionSourcePoint = aimPoint;
            detection.DetectionRange = 14f;
            detection.AttackRange = 3.8f;

            root.AddComponent<EnemyController>();
            root.AddComponent<Robot1ChaseController>();
            var proceduralAnimator = root.AddComponent<Robot1ProceduralAnimator>();
            proceduralAnimator.NebulaMaterial = nebula;

            // Parent the weapon to the imported arm/hand transform so it follows the model,
            // including any future animation or rig movement.
            Transform socket = FindArmSocket(root.transform);
            var arm = NewChild(socket ? socket : root.transform, "Robot1 Piston Fist Arm");
            arm.rotation = socket ? socket.rotation : root.transform.rotation;

            // The piston is a forearm-sized attachment, not a second robot body. Keep its
            // world size small enough to sit beside the existing arm geometry.
            float pistonScale = visualScale * .42f;

            if (socket)
            {
                // The piston geometry is authored with its housing centered 1.2 units above
                // the arm object, so place that housing at the wrist/hand socket.
                arm.position = socket.position - socket.up * (1.2f * pistonScale);

                // Preserve the intended world size even when the imported bone has scale.
                Vector3 parentScale = socket.lossyScale;
                arm.localScale = new Vector3(
                    pistonScale / Mathf.Max(.0001f, Mathf.Abs(parentScale.x)),
                    pistonScale / Mathf.Max(.0001f, Mathf.Abs(parentScale.y)),
                    pistonScale / Mathf.Max(.0001f, Mathf.Abs(parentScale.z)));
            }
            else
            {
                arm.position = bounds.center + root.transform.forward * .35f;
                arm.localScale = Vector3.one * pistonScale;
            }

            var housing = Part(arm, "Piston forearm housing", PrimitiveType.Cube,
                new Vector3(0f, 1.2f, -.25f), new Vector3(1.2f, .95f, 1.35f), steel, true);
            housing.AddComponent<Damageable>();
            var cylinder = Part(arm, "Piston sliding cylinder", PrimitiveType.Cylinder,
                new Vector3(0f, 1.2f, .5f), new Vector3(.26f, .5f, .26f), chrome, false);
            cylinder.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var fist = NewChild(arm, "Piston Fist");
            fist.localPosition = new Vector3(0f, 1.2f, 1f);
            Part(fist, "Blocky fist", PrimitiveType.Cube, Vector3.zero,
                new Vector3(1.05f, .85f, .78f), yellow, false);
            for (int i = 0; i < 4; i++)
                Part(fist, "Knuckle " + (i + 1), PrimitiveType.Cube,
                    new Vector3(-.39f + i * .26f, .07f, .4f), new Vector3(.22f, .6f, .11f), steel, false);

            var lamp = Part(arm, "Piston warning lamp", PrimitiveType.Cube,
                new Vector3(0f, 1.78f, .18f), new Vector3(.75f, .11f, .28f), warning, false);

            var piston = root.AddComponent<PistonFist>();
            piston.Fist = fist;
            piston.SlidingCylinder = cylinder.transform;
            piston.WarningLamp = lamp.GetComponent<Renderer>();
            piston.ArmPivot = arm;
            piston.AlignArmToBoneDirection = true;
            piston.RaiseArmDuringAttack = true;
            piston.AimArmBlendSpeed = 14f;
            piston.CloseDamageRadius = 3.25f;
            piston.Damage = 25f;
            piston.HitHalfExtents = new Vector3(.55f, .42f, .38f) * pistonScale;
            piston.HitMask = ~0;

            PrefabUtility.SaveAsPrefabAsset(root, OutputPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Robot1 Piston Fist prefab created: " + OutputPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static Transform FindArmSocket(Transform root)
    {
        Transform best = null;
        int bestScore = 0;
        foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
        {
            string name = candidate.name.ToLowerInvariant();
            int score = 0;
            if (name.Contains("hand")) score += 100;
            if (name.Contains("wrist")) score += 90;
            if (name.Contains("forearm")) score += 80;
            if (name.Contains("arm")) score += 60;
            if (name.Contains("right")) score += 10;
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best;
    }

    static Bounds CalculateBounds(Renderer[] renderers, Transform root)
    {
        if (renderers.Length == 0) return new Bounds(root.position + Vector3.up, Vector3.one * 2f);
        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    static Material LoadMaterial(string name, Color color, float metallic)
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

    static Transform NewChild(Transform parent, string name)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
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
