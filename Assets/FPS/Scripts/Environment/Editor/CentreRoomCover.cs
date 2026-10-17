using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>Builds the four independently owned, low Centre Room cover barriers.</summary>
public static class CentreRoomCover
{
    const string Owner = "corebuster_env_024";
    const string Output = "ReviewCaptures/CentreRoom/024";
    const string MaterialFolder = "Assets/FPS/Art/StartWorkshop/HeavyIndustrialKit/Materials/";
    const float Width = 2.1f;
    const float Depth = .72f;
    const float Height = 1.05f;

    [MenuItem("Tools/CoreBuster/Build Centre Room Cover")]
    public static void BuildMenu()
    {
        var layout = CentreRoomFramework.MeasureLayout();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Build Centre Room Cover");
        Build(layout);
        Undo.CollapseUndoOperations(group);
    }

    public static void BuildBatch()
    {
        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        var layout = CentreRoomFramework.MeasureLayout();
        Build(layout);
        if (!Validate(layout, true)) throw new InvalidOperationException("Cover validation failed before save.");
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        layout = CentreRoomFramework.MeasureLayout();
        if (!Validate(layout, true)) throw new InvalidOperationException("Cover validation failed after reload.");
        Debug.Log("CENTRE ROOM COVER BUILD VERIFIED: four low barriers, saved-scene reload and framework checks passed.");
    }

    [MenuItem("Tools/CoreBuster/Validate Centre Room Cover")]
    public static void ValidateMenu()
    {
        Validate(CentreRoomFramework.MeasureLayout(), true);
    }

    public static void CaptureBatch()
    {
        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        CaptureMenu();
    }

    [MenuItem("Tools/CoreBuster/Capture Centre Room Cover")]
    public static void CaptureMenu()
    {
        var layout = CentreRoomFramework.MeasureLayout();
        if (!Validate(layout, true)) throw new InvalidOperationException("Validate the saved cover before capturing it.");
        Transform section = CentreRoomFramework.FindSection(EditorSceneManager.GetActiveScene(), CentreRoomFramework.Section.Cover);
        Directory.CreateDirectory(Output);
        var cameraObject = new GameObject("Centre Room cover camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.fieldOfView = 72;
        camera.nearClipPlane = .05f;
        camera.farClipPlane = 150;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        try
        {
            foreach (Transform barrier in section)
            {
                Vector3 target = barrier.position + Vector3.up * .7f;
                Vector3 inward = layout.RingCenter - barrier.position;
                inward.y = 0;
                Vector3 eye = barrier.position + inward.normalized * 5f + Vector3.up * 1.7f;
                Capture(camera, eye, target, barrier.name);
            }
            Vector3 southEye = new Vector3(layout.report.gates[0].position.x, layout.Floor + 1.7f,
                layout.interior.min.z + 4.8f);
            Capture(camera, southEye, layout.RingCenter + Vector3.up * 2f, "Overview_From_South");
        }
        finally { UnityEngine.Object.DestroyImmediate(cameraObject); }
        if (EditorSceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Cover capture dirtied MainScene.");
        Debug.Log("CENTRE ROOM COVER CAPTURES WRITTEN: " + Output);
    }

    static void Capture(Camera camera, Vector3 eye, Vector3 target, string name)
    {
        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
        var rt = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = rt;
            camera.Render(); camera.Render();
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            texture.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    static void Build(CentreRoomFramework.Layout layout)
    {
        Material steel = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "M_HeavyPipe_BareSteel.mat");
        Material yellow = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "M_HeavyPipe_HazardYellow.mat");
        Material oxide = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "M_HeavyPipe_RedOxide.mat");
        if (!steel || !yellow || !oxide) throw new InvalidOperationException("Heavy Industrial Kit materials are missing.");

        Vector3 min = layout.interior.min;
        Vector3 max = layout.interior.max;
        float y = layout.Floor;
        // Deliberately use the four outer pockets. The continuous radius-4-to-8 ring and
        // measured spawn/gate/terminal connectors remain empty, even before the reactor is built.
        var sites = new[]
        {
            new Vector3(min.x + 3.7f, y, min.z + 3.5f),
            new Vector3(max.x - 2.8f, y, min.z + 3.64f),
            new Vector3(min.x + 3.7f, y, max.z - 4.2f),
            new Vector3(max.x - 3.7f, y, max.z - 3.5f),
        };
        var labels = new[] { "SW", "SE", "NW", "NE" };
        for (int i = 0; i < sites.Length; i++)
        {
            Bounds footprint = new Bounds(sites[i] + Vector3.up * (Height * .5f),
                new Vector3(Width + .16f, Height, Depth + .16f));
            string violation = layout.KeepOutViolation(CentreRoomFramework.Section.Cover, footprint);
            if (violation != null) throw new InvalidOperationException(labels[i] + " cover " + violation);
        }

        Transform section = CentreRoomFramework.BeginSectionRebuild(layout, CentreRoomFramework.Section.Cover, Owner);
        for (int i = 0; i < sites.Length; i++)
        {
            var root = new GameObject("Low_Barrier_" + labels[i]);
            Undo.RegisterCreatedObjectUndo(root, "Create Centre Room cover");
            root.transform.SetParent(section, false);
            root.transform.position = sites[i];

            // A broad, grounded steel plinth has one simple physical collider. Trim is visual only.
            Part(root.transform, "Body", new Vector3(0, .5f, 0),
                new Vector3(Width, 1f, Depth), steel, true);
            Part(root.transform, "Oxide_Face", new Vector3(0, .50f, -Depth * .5f - .015f),
                new Vector3(Width - .18f, .66f, .03f), oxide, false);
            Part(root.transform, "Hazard_Rail", new Vector3(0, Height - .035f, 0),
                new Vector3(Width + .12f, .07f, Depth + .12f), yellow, false);
            Part(root.transform, "Left_End", new Vector3(-Width * .5f + .07f, .5f, 0),
                new Vector3(.14f, .92f, Depth + .08f), yellow, false);
            Part(root.transform, "Right_End", new Vector3(Width * .5f - .07f, .5f, 0),
                new Vector3(.14f, .92f, Depth + .08f), yellow, false);

            var obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = new Vector3(0, Height * .5f, 0);
            obstacle.size = new Vector3(Width, Height, Depth);
            obstacle.carving = true;
            obstacle.carveOnlyStationary = false;
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        if (!Validate(layout, true)) throw new InvalidOperationException("Built cover failed validation; undo the build.");
    }

    static void Part(Transform parent, string name, Vector3 position, Vector3 scale, Material material, bool collider)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        Undo.RegisterCreatedObjectUndo(part, "Create Centre Room cover part");
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
        if (!collider) UnityEngine.Object.DestroyImmediate(part.GetComponent<BoxCollider>());
    }

    static bool Validate(CentreRoomFramework.Layout layout, bool log)
    {
        Physics.SyncTransforms();
        Scene scene = EditorSceneManager.GetActiveScene();
        Transform section = CentreRoomFramework.FindSection(scene, CentreRoomFramework.Section.Cover);
        var errors = new List<string>();
        if (!section || section.childCount != 4) errors.Add("Expected four low cover structures.");
        if (section)
        {
            foreach (Transform barrier in section)
            {
                Collider[] colliders = barrier.GetComponentsInChildren<Collider>();
                NavMeshObstacle[] obstacles = barrier.GetComponents<NavMeshObstacle>();
                if (colliders.Length != 1 || !(colliders[0] is BoxCollider))
                    errors.Add(barrier.name + " must have one box collider on its body.");
                if (obstacles.Length != 1 || !obstacles[0].carving)
                    errors.Add(barrier.name + " must have one carving navigation obstacle.");
                if (colliders.Length == 1)
                {
                    Bounds b = colliders[0].bounds;
                    if (b.size.y > Height + .01f || Mathf.Abs(b.min.y - layout.Floor) > .025f)
                        errors.Add(barrier.name + " height or floor contact is wrong: " + b.min + " to " + b.max + ".");
                    string violation = layout.KeepOutViolation(CentreRoomFramework.Section.Cover, b);
                    if (violation != null) errors.Add(barrier.name + " " + violation);
                }
            }
        }
        if (!CentreRoomFramework.Validate(scene, layout, log)) errors.Add("Framework validation failed.");
        if (!CentreRoomPerimeter.Validate(scene, layout, log)) errors.Add("Perimeter collision/lane validation failed.");
        if (log)
        {
            if (errors.Count == 0) Debug.Log("CENTRE ROOM COVER CHECKS PASSED: four grounded barriers, collision, carving and measured keep-outs.");
            else Debug.LogError("CENTRE ROOM COVER CHECKS FAILED: " + string.Join("; ", errors));
        }
        return errors.Count == 0;
    }
}
