using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using static StartWorkshopDresser;

/// <summary>Project-owned graphic overlays for the existing workshop boards and the spawn-to-exit route.</summary>
public static class StartWorkshopGraphics
{
    public const string GroupName = "Wayfinding_Graphics";
    const string BoardPath = "Maintenance_EastWall_Detail/Blueprint_Board";
    const string SignPath = "Services_RearWall_Detail/Warning_Sign";
    const string BoardLabel = "Service_Plan_Overlay";
    const string SignLabel = "Core_Gas_Overlay";
    const string FloorLabel = "Route_To_Exit_Overlay";
    const string Art = "Assets/FPS/Art/";
    const string Textures = Art + "Textures/StartWorkshop/";
    const string Materials = Art + "Materials/Props/";

    [MenuItem("Tools/CoreBuster/Mark Start Workshop")]
    public static void Mark()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var level = scene.path == ScenePath ? FindLevel(scene) : null;
        var root = level != null ? level.Find(ContainerName) : null;
        if (root == null)
        {
            Debug.LogError($"Open {ScenePath} and run Tools/CoreBuster/Dress Start Workshop first.");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Mark Start Workshop");
        var frame = StartWorkshopFrame.Measure(scene, root);
        Build(root, frame);
        ApplyStaticConvention(root, GameObjectUtility.GetStaticEditorFlags(level.gameObject));
        StartWorkshopLitMaterials.ApplyTo(scene, root);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeTransform = root.Find(GroupName);
        Debug.Log("Start Workshop graphic overlays rebuilt.");
        StartWorkshopChecks.Run();
    }

    /// <summary>Can be composed with the first and detail dressing builders; repeated calls replace only our overlays.</summary>
    public static void Build(Transform root, StartWorkshopFrame frame)
    {
        var board = Require(root, BoardPath);
        var sign = Require(root, SignPath);
        Remove(root.Find(GroupName));
        Remove(board.Find(BoardLabel));
        Remove(sign.Find(SignLabel));

        var boardMaterial = MaterialFor("Workshop_Service_Plan", false);
        var warningMaterial = MaterialFor("Core_Gas_Warning", false);
        var floorMaterial = MaterialFor("Route_To_Exit", true);

        Bounds boardBounds = B(board.gameObject);
        float boardWidth = Mathf.Min(boardBounds.size.z * .72f, 1.6f);
        float boardHeight = Mathf.Min(boardBounds.size.y * .34f, 1f);
        if (boardWidth < .2f || boardHeight < .2f)
            throw new InvalidOperationException("Blueprint board is too small for its service-plan overlay.");
        Quad(board, BoardLabel,
            new Vector3(boardBounds.min.x - .018f, boardBounds.min.y + boardBounds.size.y * .69f, boardBounds.center.z),
            Quaternion.LookRotation(Vector3.right, Vector3.up), new Vector2(boardWidth, boardHeight), boardMaterial);

        Bounds signBounds = B(sign.gameObject);
        float signWidth = signBounds.size.x * .78f;
        float signHeight = signBounds.size.y * .68f;
        if (signWidth < .15f || signHeight < .15f)
            throw new InvalidOperationException("Warning sign is too small for its label overlay.");
        Quad(sign, SignLabel,
            new Vector3(signBounds.center.x, signBounds.center.y, signBounds.max.z + .018f),
            Quaternion.LookRotation(Vector3.back, Vector3.up), new Vector2(signWidth, signHeight), warningMaterial);

        var group = new GameObject(GroupName).transform;
        group.SetParent(root, false);
        Vector3 route = frame.RouteEnd - frame.Spawn;
        route.y = 0;
        Vector3 position = Vector3.Lerp(frame.Spawn, frame.RouteEnd, .55f);
        position.y = frame.Floor + .024f;
        Quad(group, FloorLabel, position, Quaternion.LookRotation(Vector3.up, route.normalized),
            new Vector2(2.4f, 1.2f), floorMaterial);
        Undo.RegisterCreatedObjectUndo(group.gameObject, "Mark Start Workshop");
    }

    public static void Validate(Transform root, StartWorkshopFrame frame, List<string> errors)
    {
        var board = root.Find(BoardPath + "/" + BoardLabel);
        var sign = root.Find(SignPath + "/" + SignLabel);
        var floor = root.Find(GroupName + "/" + FloorLabel);
        if (board == null || sign == null || floor == null)
        {
            errors.Add("The service-plan, warning, or floor way-finding overlay is missing.");
            return;
        }
        foreach (var graphic in new[] { board, sign, floor })
        {
            if (graphic.GetComponent<MeshRenderer>() == null || graphic.GetComponent<Collider>() != null)
                errors.Add(graphic.name + " must be a visible, collider-free quad.");
        }
        Bounds b = B(floor.gameObject);
        if (b.min.y < frame.Floor || b.max.y > frame.Floor + .05f ||
            StartWorkshopFrame.FlatDistance(frame.Spawn, b) > 16f)
            errors.Add("The floor way-finding overlay is not flush with the workshop route.");
    }

    static Transform Require(Transform root, string path) => root.Find(path) ??
        throw new InvalidOperationException($"Missing workshop geometry: {path}. Run Dress Start Workshop first.");

    static void Remove(Transform transform)
    {
        if (transform != null) Undo.DestroyObjectImmediate(transform.gameObject);
    }

    static void Quad(Transform parent, string name, Vector3 position, Quaternion rotation, Vector2 size, Material material)
    {
        var graphic = GameObject.CreatePrimitive(PrimitiveType.Quad);
        graphic.name = name;
        graphic.transform.SetParent(parent, true);
        graphic.transform.SetPositionAndRotation(position, rotation);
        graphic.transform.localScale = new Vector3(size.x, size.y, 1);
        var collider = graphic.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        graphic.GetComponent<MeshRenderer>().sharedMaterial = material;
        Undo.RegisterCreatedObjectUndo(graphic, "Mark Start Workshop");
    }

    static Material MaterialFor(string name, bool transparent)
    {
        string texturePath = Textures + name + ".png";
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null) throw new InvalidOperationException("Missing project-owned texture: " + texturePath);
        string materialPath = Materials + "M_" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("URP Unlit shader is unavailable.");
            material = new Material(shader) { name = "M_" + name };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", Color.white);
        if (transparent)
        {
            material.SetFloat("_Surface", 1);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_Cull", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
        }
        EditorUtility.SetDirty(material);
        return material;
    }
}
