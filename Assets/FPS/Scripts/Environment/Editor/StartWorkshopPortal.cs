using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static StartWorkshopDresser;

/// <summary>Room-side, collider-free exit dressing. All dimensions follow the measured opening and lintel.</summary>
public static class StartWorkshopPortal
{
    public const string GroupName = "Portal_013";
    const string UndoName = "Build Start Workshop Portal";
    const string BoardTexture = "Assets/FPS/Art/Textures/StartWorkshop/Portal_To_Corridors.png";
    const string BoardMaterial = "Assets/FPS/Art/Materials/Props/M_Portal_To_Corridors.mat";
    const string HazardMaterial = "Assets/FPS/Art/Materials/Props/M_Hazard_Trim.mat";
    const float PostWidth = .22f, PostDepth = .14f, GapMargin = .035f;

    [MenuItem("Tools/CoreBuster/Build Start Workshop Portal")]
    public static void Rebuild()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var level = scene.path == ScenePath ? FindLevel(scene) : null;
        var root = level != null ? level.Find(ContainerName) : null;
        if (root == null) { Debug.LogError($"Open {ScenePath} and dress the Start Workshop first."); return; }
        var frame = StartWorkshopFrame.Measure(scene, root);
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        var old = root.Find(GroupName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        try
        {
            Build(root, frame);
            ApplyStaticConvention(root.Find(GroupName), GameObjectUtility.GetStaticEditorFlags(level.gameObject));
            var portalErrors = new List<string>();
            Validate(root, frame, portalErrors);
            if (portalErrors.Count != 0) throw new InvalidOperationException(string.Join("; ", portalErrors));
            Undo.RegisterCreatedObjectUndo(root.Find(GroupName).gameObject, UndoName);
            StartWorkshopLitMaterials.ApplyTo(scene, root);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeTransform = root.Find(GroupName);
            StartWorkshopChecks.Run();
        }
        catch
        {
            var created = root.Find(GroupName);
            if (created != null) UnityEngine.Object.DestroyImmediate(created.gameObject);
            throw;
        }
    }

    public static void BuildBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Rebuild();
        EditorSceneManager.SaveScene(scene);
        // The retained Overhead_Structure_007 has known pre-existing geometry errors. Persist this independent
        // group after its own Build/Validate checks, while still returning a failing batch status for the full gate.
        if (!StartWorkshopChecks.Run()) EditorApplication.Exit(1);
    }

    public static void Build(Transform root, StartWorkshopFrame f)
    {
        if (root.Find(GroupName) != null) throw new InvalidOperationException(GroupName + " already exists.");
        var sign = root.Find("Exit_Signage/Exit_Sign");
        var westLamp = root.Find(StartWorkshopLighting.GroupName + "/" + StartWorkshopLighting.ExitWest);
        var eastLamp = root.Find(StartWorkshopLighting.GroupName + "/" + StartWorkshopLighting.ExitEast);
        var strip = root.Find(StartWorkshopLighting.GroupName + "/" + StartWorkshopLighting.StripName);
        var liner = root.Find("Liners_007/Liner_Alternate Door (4)");
        if (sign == null || westLamp == null || eastLamp == null || strip == null || liner == null)
            throw new InvalidOperationException("Portal anchors missing: Exit_Sign, both exit lamps, Exit_Strip and door liner are required.");
        if (!f.FindLintel(root, out float lintel, out _))
            throw new InvalidOperationException("Cannot measure the exit lintel for Portal_013.");
        if (f.OpeningMaxX - f.OpeningMinX < 1.8f)
            throw new InvalidOperationException("The measured exit gap is too narrow for the portal frame.");

        var finish = new StartWorkshopWorkstation.Finishes();
        var hazard = AssetDatabase.LoadAssetAtPath<Material>(HazardMaterial);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(BoardTexture);
        if (hazard == null || texture == null)
            throw new InvalidOperationException("Missing portal art. Run python Tools/Art/generate_exit_portal.py and Build Start Workshop Cladding.");
        var boardMat = AssetDatabase.LoadAssetAtPath<Material>(BoardMaterial);
        if (boardMat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("URP Unlit shader is unavailable.");
            boardMat = new Material(shader) { name = "M_Portal_To_Corridors" };
            AssetDatabase.CreateAsset(boardMat, BoardMaterial);
        }
        boardMat.SetTexture("_BaseMap", texture);
        boardMat.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(boardMat);

        var group = Group(root, GroupName);
        // The posts sit wholly outside the raycast-measured gap. They are visual dressing, with no new collision.
        float faceZ = f.North - .20f;
        float postHeight = lintel - f.Floor;
        if (postHeight < StartWorkshopFrame.HeadHeight)
            throw new InvalidOperationException("Measured lintel is below the required walking headroom.");
        for (int side = -1; side <= 1; side += 2)
        {
            float edge = side < 0 ? f.OpeningMinX : f.OpeningMaxX;
            float x = edge + side * (GapMargin + PostWidth * .5f);
            string name = side < 0 ? "Jamb_West" : "Jamb_East";
            var jamb = Group(group, name);
            Block(jamb, "Steel", new Vector3(x, f.Floor + postHeight * .5f, faceZ + .025f),
                new Vector3(PostWidth, postHeight, PostDepth), finish.PaintedSteel, false);
            // A thin project-owned striped face, drawn above the steel without touching the passage.
            Block(jamb, "Chevron", new Vector3(x, f.Floor + postHeight * .5f, faceZ - .05f),
                new Vector3(PostWidth - .045f, postHeight - .12f, .008f), hazard, false);
            for (int i = 0; i < 3; i++)
                Block(jamb, "Bolt_" + i, new Vector3(x, f.Floor + .22f + i * (postHeight - .44f) * .5f, faceZ - .061f),
                    new Vector3(.025f, .025f, .012f), finish.BareSteel, false);
        }

        float width = f.OpeningMaxX - f.OpeningMinX + .55f;
        Block(group, "Header", new Vector3(f.OpeningCenter, lintel + .07f, faceZ + .025f),
            new Vector3(width, .14f, PostDepth), finish.PaintedSteel, false);

        // Keep the old Exit_Sign and the strip visible; the new board starts well above both.
        float boardBottom = Mathf.Max(B(sign.gameObject).max.y, B(strip.gameObject).max.y) + .32f;
        const float boardHeight = .86f;
        if (boardBottom + boardHeight > f.Ceiling - .3f)
            throw new InvalidOperationException("No clear lintel space above the existing exit sign and strip for the portal board.");
        const float boardWidth = 4.15f;
        Block(group, "Board_Back", new Vector3(f.OpeningCenter, boardBottom + boardHeight * .5f, faceZ + .03f),
            new Vector3(boardWidth + .12f, boardHeight + .12f, .07f), finish.BareSteel, false);
        var board = GameObject.CreatePrimitive(PrimitiveType.Quad);
        board.name = "To_Corridors_Board";
        board.transform.SetParent(group, false);
        // Unity's Quad is visible from local -Z; +Z points into the wall and the printed face points into the room.
        board.transform.SetPositionAndRotation(new Vector3(f.OpeningCenter, boardBottom + boardHeight * .5f, faceZ - .008f),
            Quaternion.LookRotation(Vector3.forward));
        board.transform.localScale = new Vector3(boardWidth, boardHeight, 1);
        UnityEngine.Object.DestroyImmediate(board.GetComponent<Collider>());
        board.GetComponent<MeshRenderer>().sharedMaterial = boardMat;

        // A flush slotted threshold reads as grating while leaving the original floor and walking collision intact.
        var grate = Group(group, "Threshold_Grate");
        float thresholdZ = f.North - .30f;
        Block(grate, "Bed", new Vector3(f.OpeningCenter, f.Floor + .004f, thresholdZ),
            new Vector3(f.OpeningMaxX - f.OpeningMinX - .1f, .008f, .32f), finish.Rubber, false);
        for (int i = 0; i < 7; i++)
            Block(grate, "Slat_" + i, new Vector3(f.OpeningCenter, f.Floor + .012f, thresholdZ - .135f + i * .045f),
                new Vector3(f.OpeningMaxX - f.OpeningMinX - .14f, .008f, .018f), finish.BareSteel, false);
    }

    public static void Validate(Transform root, StartWorkshopFrame f, List<string> errors)
    {
        var group = root.Find(GroupName);
        if (group == null) { errors.Add(GroupName + " is missing."); return; }
        foreach (var name in new[] { "Jamb_West", "Jamb_East", "Header", "Board_Back", "To_Corridors_Board", "Threshold_Grate" })
            if (group.Find(name) == null) errors.Add(GroupName + "/" + name + " is missing.");
        if (group.GetComponentsInChildren<Collider>().Length != 0)
            errors.Add(GroupName + " must have no colliders; the exit collision is owned by the shell.");
        var west = group.Find("Jamb_West");
        var east = group.Find("Jamb_East");
        if (west != null && B(west.gameObject).max.x > f.OpeningMinX - GapMargin + .002f)
            errors.Add("West portal jamb shrinks the measured gap.");
        if (east != null && B(east.gameObject).min.x < f.OpeningMaxX + GapMargin - .002f)
            errors.Add("East portal jamb shrinks the measured gap.");
        var board = group.Find("To_Corridors_Board");
        if (board != null && (board.GetComponent<MeshRenderer>() == null || board.GetComponent<Collider>() != null))
            errors.Add("Portal board must be a visible collider-free quad.");
        foreach (var r in group.GetComponentsInChildren<Renderer>())
            if (r.bounds.min.z < f.North - .7f || r.bounds.max.z > f.North + .01f || r.bounds.max.y > f.Ceiling)
                errors.Add("Portal piece outside the room: " + r.name);
    }
}
