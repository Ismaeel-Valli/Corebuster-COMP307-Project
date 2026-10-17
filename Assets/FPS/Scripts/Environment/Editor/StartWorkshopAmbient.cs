using System;
using System.Collections.Generic;
using Unity.FPS.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static StartWorkshopDresser;

/// <summary>Rebuildable, visual-only corebuster_env_017 pass. The kit fan mesh and first-principles particles stay room-local.</summary>
public static class StartWorkshopAmbient
{
    public const string GroupName = "Ambient_017";
    const string UndoName = "Build Start Workshop Ambient";
    const string FanPrefab = "Assets/SciFi Warehouse Kit/Prefabs/Structures/Walls/Wall Fan.prefab";
    const string TextureFolder = "Assets/FPS/Art/Textures/StartWorkshop/Ambient/";
    const string MaterialFolder = "Assets/FPS/Art/Materials/Props/";

    [MenuItem("Tools/CoreBuster/Build Start Workshop Ambient")]
    public static void Rebuild()
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        Transform level = scene.path == ScenePath ? FindLevel(scene) : null;
        Transform room = level != null ? level.Find(ContainerName) : null;
        if (room == null) throw new InvalidOperationException("Open MainScene and build the Start Workshop first.");
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        RebuildIn(scene, room);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeTransform = room.Find(GroupName);
        StartWorkshopChecks.Run();
    }

    public static void BuildBatch()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Transform level = FindLevel(scene);
        Transform room = level != null ? level.Find(ContainerName) : null;
        if (room == null) throw new InvalidOperationException("Build the Start Workshop before Ambient_017.");
        RebuildIn(scene, room);
        EditorSceneManager.SaveScene(scene);
        StartWorkshopChecks.Run();
    }

    static void RebuildIn(Scene scene, Transform room)
    {
        Transform old = room.Find(GroupName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        RemoveBindings(room);
        Build(room, StartWorkshopFrame.Measure(scene, room));
        ApplyStaticConvention(room.Find(GroupName), GameObjectUtility.GetStaticEditorFlags(room.parent.gameObject));
        ClearMovingFlags(room.Find(GroupName));
        StartWorkshopLitMaterials.ApplyTo(scene, room);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    public static bool IsGroup(Transform group) => group != null && group.name == GroupName;

    public static void Build(Transform room, StartWorkshopFrame frame)
    {
        if (room.Find(GroupName) != null) throw new InvalidOperationException(GroupName + " already exists.");
        var group = new GameObject(GroupName).transform;
        group.SetParent(room, false);
        Undo.RegisterCreatedObjectUndo(group.gameObject, UndoName);

        // Fan roots and guards stay fixed. Only the mesh rotor moves; its inherited mesh collider is removed.
        Fan(group, frame, "Fan_West", new Vector3(frame.West + .1f, frame.Floor + 6.1f, frame.South + 8f),
            Quaternion.Euler(0, 180f, 0), 130f, 0f);
        Fan(group, frame, "Fan_East", new Vector3(frame.East - .1f, frame.Floor + 6.1f, frame.North - 8f),
            Quaternion.identity, -116f, 1.4f);

        Transform tube = Require(room, "Workstation_010/Task_Lamp/Tube");
        Transform bench = Require(room, StartWorkshopWorkstation.BenchTopPath);
        Transform pipe = Require(room, "Pipework_009/Drop_South");
        BindLamp(room);
        BindScreens(room);
        Bounds top = B(bench.gameObject);
        Bounds pipeBounds = B(pipe.gameObject);
        Vector3 steam = new Vector3(pipeBounds.center.x, frame.Floor + 1.8f, pipeBounds.max.z + .08f);
        Particle(group, "Steam_Puffs", steam, "Steam", new Color(.75f, .85f, .86f, .36f),
            12, .8f, 2.4f, .65f, .13f, -.02f, Vector3.up, new Vector3(.25f, .08f, .25f));
        Vector3 sparks = new Vector3(top.min.x + .15f, top.max.y + .12f, top.min.z + .3f);
        Particle(group, "Bench_Sparks", sparks, "Spark", new Color(1f, .72f, .3f, 1f),
            16, .45f, .8f, 1.2f, .035f, .7f, Vector3.up, new Vector3(.12f, .04f, .15f));
        Particle(group, "Light_Dust", tube.position + new Vector3(-.55f, -.3f, 0), "Dust",
            new Color(.95f, .85f, .64f, .4f), 48, 4f, 2.2f, .08f, .045f, -.015f,
            Vector3.up, new Vector3(.7f, .4f, 1.8f));
    }

    public static void BindLamp(Transform room)
    {
        Transform lamp = Require(room, "Lighting_006/Task_Light");
        var light = lamp.GetComponent<Light>();
        if (light == null) throw new InvalidOperationException("Lighting_006/Task_Light has no Light.");
        if (lamp.GetComponent<WorkshopAmbientMotion>() == null)
            Undo.AddComponent<WorkshopAmbientMotion>(lamp.gameObject).ConfigureLamp(light, .65f);
    }

    public static void BindScreens(Transform room)
    {
        foreach (Transform overlay in Require(room, StartWorkshopScreens.GroupName))
            if (overlay.name.EndsWith("_Led", StringComparison.Ordinal))
            {
                var renderer = overlay.GetComponent<Renderer>();
                if (renderer == null) throw new InvalidOperationException(overlay.name + " is missing its LED renderer.");
                if (overlay.GetComponent<WorkshopAmbientMotion>() == null)
                    Undo.AddComponent<WorkshopAmbientMotion>(overlay.gameObject).ConfigureLed(renderer, overlay.GetSiblingIndex() * 1.31f);
            }
    }

    static Transform Require(Transform room, string path) => room.Find(path) ??
        throw new InvalidOperationException("Ambient_017 requires " + path + ". Build its owning pass first.");

    static void Fan(Transform parent, StartWorkshopFrame frame, string name, Vector3 position,
        Quaternion rotation, float speed, float phase)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FanPrefab);
        if (prefab == null) throw new InvalidOperationException("Missing kit fan: " + FanPrefab);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = name;
        instance.transform.SetParent(parent, false);
        instance.transform.SetPositionAndRotation(position, rotation);
        instance.transform.localScale = Vector3.one * .34f;
        Transform rotor = instance.transform.Find("LP_vent_fan_snaps");
        if (rotor == null) throw new InvalidOperationException("Kit fan has no LP_vent_fan_snaps rotor.");
        foreach (var collider in rotor.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
        // The kit prefab has a fixed frame and a separate rotating mesh; no root or guard physics moves.
        instance.AddComponent<WorkshopAmbientMotion>().ConfigureFan(rotor, speed, phase);
        Bounds bounds = B(instance);
        // The prefab's frame seats a little inside the existing wall panel; no moving mesh reaches outside it.
        if (bounds.min.x < frame.West - .25f || bounds.max.x > frame.East + .25f ||
            bounds.min.z < frame.South - .1f || bounds.max.z > frame.North + .1f ||
            bounds.max.y > frame.Ceiling)
            throw new InvalidOperationException(name + " extends beyond the measured workshop interior: " + bounds.min + " to " + bounds.max +
                " (frame X " + frame.West + ".." + frame.East + ", Z " + frame.South + ".." + frame.North +
                ", ceiling " + frame.Ceiling + ").");
    }

    static void Particle(Transform parent, string name, Vector3 position, string sprite, Color colour,
        int maximum, float lifetime, float rate, float speed, float size, float gravity,
        Vector3 direction, Vector3 box)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.rotation = Quaternion.FromToRotation(Vector3.forward, direction);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = maximum;
        main.startLifetime = lifetime;
        main.startSpeed = speed;
        main.startSize = size;
        main.startColor = colour;
        main.gravityModifier = gravity;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = box;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = Material(sprite);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static Material Material(string name)
    {
        string path = MaterialFolder + "M_Env017_" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + name + ".png");
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (texture == null || shader == null) throw new InvalidOperationException("Generate ambient particle textures and import URP before building.");
        material = new Material(shader) { name = "M_Env017_" + name };
        material.SetTexture("_BaseMap", texture);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void RemoveBindings(Transform room)
    {
        foreach (var motion in room.GetComponentsInChildren<WorkshopAmbientMotion>(true))
            if (motion.transform.IsChildOf(room.Find("Lighting_006")) ||
                motion.transform.IsChildOf(room.Find(StartWorkshopScreens.GroupName)))
                Undo.DestroyObjectImmediate(motion);
    }

    public static void ClearMovingFlags(Transform group)
    {
        foreach (Transform t in group.GetComponentsInChildren<Transform>(true))
            if (t.name == "LP_vent_fan_snaps" || t.GetComponent<ParticleSystem>() != null)
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                if (PrefabUtility.IsPartOfPrefabInstance(t)) PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            }
    }

    public static bool ExemptsStatic(Transform t, Transform room)
    {
        var group = room.Find(GroupName);
        return group != null && t.IsChildOf(group) &&
            (t.name == "LP_vent_fan_snaps" || t.GetComponent<ParticleSystem>() != null);
    }

    public static void Validate(Transform room, List<string> errors)
    {
        Transform group = room.Find(GroupName);
        if (group == null) { errors.Add("Missing Ambient_017."); return; }
        if (group.GetComponentsInChildren<WorkshopAmbientMotion>().Length != 2)
            errors.Add("Ambient_017 must have two animated fans.");
        var systems = group.GetComponentsInChildren<ParticleSystem>();
        if (systems.Length != 3) errors.Add("Ambient_017 must have steam, sparks and dust particle systems.");
        foreach (var ps in systems)
            if (ps.GetComponent<Collider>() != null || ps.main.maxParticles > 48)
                errors.Add(ps.name + " has a collider or exceeds the ambient particle cap.");
        foreach (var fan in group.GetComponentsInChildren<WorkshopAmbientMotion>())
            if (fan.transform.Find("LP_vent_fan_snaps")?.GetComponent<Collider>() != null)
                errors.Add(fan.name + " has a moving collider.");
        if (Require(room, "Lighting_006/Task_Light").GetComponent<WorkshopAmbientMotion>() == null)
            errors.Add("Ambient_017 task-light flicker is missing.");
        int leds = 0;
        foreach (Transform overlay in Require(room, StartWorkshopScreens.GroupName))
            if (overlay.name.EndsWith("_Led", StringComparison.Ordinal))
            {
                leds++;
                if (overlay.GetComponent<WorkshopAmbientMotion>() == null)
                    errors.Add(overlay.name + " LED pulse is missing.");
            }
        if (leds != 5) errors.Add("Ambient_017 expected five screen LED strips.");
    }
}
