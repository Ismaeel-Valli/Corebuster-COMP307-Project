using System.Collections.Generic;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using K = CoreExecutionBuilder;

/// <summary>
/// Builds the keycard progression loop into MainScene. The spawn room, the centre room and the north-west room
/// each hold a security terminal. Accessing one locks the room and warps in waves of enemies. Clearing them
/// ejects a coloured floppy disk keycard (blue, yellow, red). Each card opens the next room's sealed doors at
/// its access machines. The Master terminal beside the boss-hall door takes all three to open the way to the
/// boss. Also converts the Sci-Fi Doors, Access Machine and floppy disk materials to URP and synthesizes the
/// sounds. Safe to re-run: the scene objects are rebuilt from scratch each time.
/// </summary>
public static class KeycardProgressionBuilder
{
    const string ScenePath = "Assets/FPS/Scenes/MainScene.unity";
    const string PlayerPath = "Assets/FPS/Prefabs/Player.prefab";
    const string DataFolder = "Assets/FPS/Data/Keycards";
    const string MaterialFolder = "Assets/FPS/Art/Materials/CoreBuster";
    const string WarpVfxPath = "Assets/FPS/Prefabs/VFX/VFX_EnemyWarpIn.prefab";
    const string GatePath = "Assets/MASH Virtual/Sci Fi Doors/Prefab/Sci Fi Gates.prefab";
    const string AccessMachinePath = "Assets/MASH Virtual/Sci fi Access Machine/Prefab/Gate Access Machine.prefab";
    const string ConsolePath = "Assets/Sci-Fi Styled Modular Pack/Prefabs/Decorative elements/console.prefab";
    const string StationPath = "Assets/Sci-Fi Styled Modular Pack/Prefabs/Decorative elements/computer_station.prefab";
    const string FloppyFolder = "Assets/HorrorPuzzleItems/Floppy";
    const string EnemyFolder = "Assets/FPS/Prefabs/Enemies/";
    const string RootName = "===== KEYCARD PROGRESSION =====";
    const int SampleRate = K.SampleRate;

    // The access machine panel is authored facing -X; scaled up so it reads from down a corridor.
    const float AccessMachineScale = 1.5f;
    // The card symbol on the panel's face, where the disk is tapped.
    static readonly Vector3 k_ReaderLocal = new Vector3(-.012f, -.11f, 0f);
    const float AccessMachineHeight = 1.3f;
    // How far each gate leaf slides into the wall to open.
    const float LeafSlide = 1.8f;
    // Where the Master terminal's card slots sit on its front.
    const float MasterSlotHeight = 1.15f;
    const float MasterSlotSpacing = .3f;

    static readonly Color k_Blue = new Color(.25f, .62f, 1f);
    static readonly Color k_Yellow = new Color(1f, .82f, .2f);
    static readonly Color k_Red = new Color(1f, .22f, .16f);
    static readonly Color k_Hostile = new Color(1f, .2f, .12f);
    static readonly Color k_Ember = new Color(1f, .5f, .25f);

    internal class DoorSpec
    {
        public string Name;
        public Vector3 Guess;
        public Vector3 Along;
        public Vector3 Outward;
        public bool StartLocked;
    }

    // Doorway centres (on the wall line) found by probing MainScene's rooms; outward points into the corridor.
    // The old spawn room is now the Workshop. Since the tutorial wing became the start (corebuster_tutorial_001) its door
    // starts sealed; TutorialWingBuilder adds the black-keycard access machine that opens it, so re-run
    // Tools/CoreBuster/Build Tutorial Wing after this builder.
    static readonly DoorSpec k_SpawnNorth = new DoorSpec { Name = "Spawn Room Door (N)", Guess = new Vector3(-163.88f, 0f, 34.40f), Along = Vector3.right, Outward = Vector3.forward, StartLocked = true };
    static readonly DoorSpec k_CentreSouth = new DoorSpec { Name = "Centre Room Door (S)", Guess = new Vector3(-191.98f, 0f, 50.90f), Along = Vector3.right, Outward = Vector3.back, StartLocked = true };
    static readonly DoorSpec k_CentreNorth = new DoorSpec { Name = "Centre Room Door (N)", Guess = new Vector3(-191.85f, 0f, 83.10f), Along = Vector3.right, Outward = Vector3.forward, StartLocked = true };
    static readonly DoorSpec k_CentreWest = new DoorSpec { Name = "Centre Room Door (W)", Guess = new Vector3(-214.50f, 0f, 60.43f), Along = Vector3.forward, Outward = Vector3.left, StartLocked = true };
    static readonly DoorSpec k_NorthWestEast = new DoorSpec { Name = "North-West Room Door (E)", Guess = new Vector3(-218.60f, 0f, 108.78f), Along = Vector3.forward, Outward = Vector3.right, StartLocked = true };
    static readonly DoorSpec k_BossDoor = new DoorSpec { Name = "Boss Hall Door", Guess = new Vector3(-250.90f, 0f, 115.48f), Along = Vector3.forward, Outward = Vector3.left, StartLocked = true };

    [MenuItem("Tools/CoreBuster/Install Keycard Progression")]
    public static void InstallAll()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError("Keycard progression: open " + ScenePath + " first.");
            return;
        }

        K.EnsureFolder(DataFolder);
        K.EnsureFolder(MaterialFolder);
        ConvertPackMaterials();
        var blue = BuildKeycard("Keycard_Blue", "BLUE KEYCARD", k_Blue, "Blue");
        var yellow = BuildKeycard("Keycard_Yellow", "YELLOW KEYCARD", k_Yellow, "Yellow");
        var red = BuildKeycard("Keycard_Red", "RED KEYCARD", k_Red, "Red");
        var warp = BuildWarpVfx();
        InstallOnPlayer();
        BuildScene(blue, yellow, red, warp);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Keycard progression installed in " + scene.name + ".");
    }

    // ------------------------------------------------------------------ materials

    // The packs ship Built-in pipeline materials, which render pink under URP.
    static void ConvertPackMaterials()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/MASH Virtual" }))
            ToLit(AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)), lit);
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { FloppyFolder }))
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (!material || material.shader == unlit) continue;
            var texture = material.GetTexture("_MainTex");
            material.shader = unlit;
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);
        }
    }

    static void ToLit(Material material, Shader lit)
    {
        if (!material || material.shader == lit || !material.shader.name.StartsWith("Standard")) return;
        var albedo = material.GetTexture("_MainTex");
        var color = material.GetColor("_Color");
        var normal = material.GetTexture("_BumpMap");
        var metallic = material.GetTexture("_MetallicGlossMap");
        var occlusion = material.GetTexture("_OcclusionMap");
        var emissionMap = material.GetTexture("_EmissionMap");
        var emission = material.GetColor("_EmissionColor");
        bool emissive = material.IsKeywordEnabled("_EMISSION");
        float gloss = material.HasProperty("_GlossMapScale") ? material.GetFloat("_GlossMapScale") : .5f;

        material.shader = lit;
        material.shaderKeywords = new string[0];
        material.SetTexture("_BaseMap", albedo);
        material.SetColor("_BaseColor", new Color(color.r, color.g, color.b, 1f));
        // Opaque, even where the source faded: the panel's transparency was only its trim.
        material.SetFloat("_Surface", 0f);
        material.renderQueue = -1;
        material.SetTexture("_BumpMap", normal);
        if (normal) material.EnableKeyword("_NORMALMAP");
        material.SetTexture("_MetallicGlossMap", metallic);
        if (metallic)
        {
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_Smoothness", gloss);
        }
        material.SetTexture("_OcclusionMap", occlusion);
        if (occlusion) material.EnableKeyword("_OCCLUSIONMAP");
        if (emissive)
        {
            material.SetTexture("_EmissionMap", emissionMap);
            material.SetColor("_EmissionColor", emission);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(material);
    }

    // Emissive lamps; each renderer is coloured at runtime through a property block.
    internal static Material LampMaterial()
    {
        var material = K.LoadOrCreateMaterial("M_StatusLamp", "Universal Render Pipeline/Lit");
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_EmissionColor", Color.white);
        material.SetFloat("_Smoothness", .8f);
        material.EnableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(material);
        return material;
    }

    internal static Material BezelMaterial()
    {
        var material = K.LoadOrCreateMaterial("M_DriveBezel", "Universal Render Pipeline/Lit");
        material.SetColor("_BaseColor", new Color(.04f, .045f, .05f));
        material.SetFloat("_Smoothness", .35f);
        EditorUtility.SetDirty(material);
        return material;
    }

    // ------------------------------------------------------------------ keycards

    static KeycardData BuildKeycard(string asset, string displayName, Color tint, string colour)
    {
        string path = DataFolder + "/" + asset + ".asset";
        var card = AssetDatabase.LoadAssetAtPath<KeycardData>(path);
        if (!card)
        {
            card = ScriptableObject.CreateInstance<KeycardData>();
            AssetDatabase.CreateAsset(card, path);
        }
        card.DisplayName = displayName;
        card.Tint = tint;
        card.Model = AssetDatabase.LoadAssetAtPath<GameObject>($"{FloppyFolder}/Prefabs/Floppy_{colour}.prefab");
        card.Icon = IconSprite($"{FloppyFolder}/Icons/Icon_Floopy_{colour}.png");
        EditorUtility.SetDirty(card);
        return card;
    }

    static Sprite IconSprite(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (!importer) return null;
        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ------------------------------------------------------------------ player

    static void InstallOnPlayer()
    {
        var contents = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            if (!contents.GetComponent<KeycardInventory>()) contents.AddComponent<KeycardInventory>();
            var interactor = contents.GetComponent<KeycardInteractor>();
            if (!interactor) interactor = contents.AddComponent<KeycardInteractor>();
            interactor.PickupSfx = K.SynthClip("KeycardPickup", SynthPickup);
            interactor.PocketSfx = K.SynthClip("KeycardPocket", SynthPocket);
            interactor.TapSfx = K.SynthClip("KeycardTap", SynthTap);
            interactor.InsertSfx = K.SynthClip("KeycardInsert", SynthInsert);
            // Label to the camera, shutter at the top (the floppy model lies flat, hub side up).
            interactor.FaceEuler = new Vector3(0f, 270f, 288f);
            interactor.TapReach = .85f;
            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // ------------------------------------------------------------------ scene

    static void BuildScene(KeycardData blue, KeycardData yellow, KeycardData red, GameObject warp)
    {
        var old = GameObject.Find(RootName);
        if (old) Object.DestroyImmediate(old);
        var root = new GameObject(RootName).transform;
        Physics.SyncTransforms();

        var lamp = LampMaterial();
        var bezel = BezelMaterial();
        var denied = K.SynthClip("KeycardDenied", SynthDenied);

        // --- Doors ---
        var spawnDoor = BuildDoor(k_SpawnNorth, root, lamp);
        var centreDoors = new[] { BuildDoor(k_CentreSouth, root, lamp), BuildDoor(k_CentreNorth, root, lamp), BuildDoor(k_CentreWest, root, lamp) };
        var northWestDoor = BuildDoor(k_NorthWestEast, root, lamp);
        var bossDoor = BuildDoor(k_BossDoor, root, lamp);
        Physics.SyncTransforms();

        // --- Access machines outside the sealed rooms ---
        foreach (var door in centreDoors) BuildAccessPoint(door, blue, centreDoors, root, lamp, denied);
        BuildAccessPoint(northWestDoor, yellow, new[] { northWestDoor }, root, lamp, denied);

        // --- Room terminals: spawn room (blue), centre room (yellow), north-west room (red) ---
        GameObject R(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(EnemyFolder + name + ".prefab");
        GameObject robotFist = R("Enemy_Robot1_PistonFist"), robotRivet = R("Enemy_Robot1_RivetCannon");
        GameObject soldierFist = R("Enemy_CyberSoldier_PistonFist"), soldierRivet = R("Enemy_CyberSoldier_RivetCannon");
        GameObject hoverFist = R("Enemy_HoverBot_PistonFist"), hoverScatter = R("Enemy_HoverBot_ScrapScattergun");
        GameObject eliteFist = R("Enemy_Elite_PistonFist");

        BuildTerminal("Spawn Room Terminal (Blue)", blue, new Vector3(-170.6f, 0f, 8f), Vector3.forward,
            new Rect(-187f, 2f, 32.7f, 32.6f), new[] { spawnDoor },
            new[]
            {
                Wave(robotFist, robotFist, robotRivet),
                Wave(soldierFist, robotRivet, hoverFist),
            }, warp, root, lamp, bezel, denied);
        BuildTerminal("Centre Room Terminal (Yellow)", yellow, new Vector3(-190f, 0f, 67f), Vector3.left,
            new Rect(-214.5f, 50.7f, 32.5f, 32.4f), centreDoors,
            new[]
            {
                Wave(soldierFist, soldierRivet, robotFist, hoverScatter),
                Wave(soldierRivet, soldierFist, robotRivet, hoverFist),
            }, warp, root, lamp, bezel, denied);
        var redTerminal = BuildTerminal("North-West Room Terminal (Red)", red, new Vector3(-234.5f, 0f, 124f), Vector3.back,
            new Rect(-251f, 99f, 33f, 33f), new[] { northWestDoor },
            new[]
            {
                Wave(soldierFist, soldierRivet, soldierFist, robotRivet, hoverScatter),
                Wave(eliteFist, soldierRivet, soldierFist, hoverFist),
            }, warp, root, lamp, bezel, denied);

        // --- Master terminal beside the boss-hall door ---
        var master = BuildMasterTerminal(new[] { blue, yellow, red }, new Vector3(-243f, 0f, 106f), Vector3.right,
            new[] { bossDoor }, root, lamp, bezel, denied);

        // --- Objective ---
        var objectiveObject = new GameObject("ObjectiveKeycards");
        objectiveObject.transform.SetParent(root, false);
        var objective = objectiveObject.AddComponent<ObjectiveKeycards>();
        objective.Title = "Unlock the core vault";
        objective.Description = "Recover the 3 keycards from the security terminals";
        objective.Terminal = master;

        Debug.Log($"Keycard progression: 6 doors, 4 access machines, 3 terminals and the Master terminal placed " +
                  $"({redTerminal.SpawnPoints.Length} spawn points in the red room).");
    }

    static KeycardTerminal.Wave Wave(params GameObject[] enemies)
    {
        var list = new List<GameObject>();
        foreach (var enemy in enemies)
            if (enemy) list.Add(enemy);
            else Debug.LogWarning("Keycard progression: a wave enemy prefab is missing.");
        return new KeycardTerminal.Wave { Enemies = list.ToArray() };
    }

    // --- doors ---

    internal static SecurityDoor BuildDoor(DoorSpec spec, Transform parent, Material lamp)
    {
        MeasureDoorway(spec, out Vector3 centre, out float width, out float outFace);

        var door = new GameObject(spec.Name).transform;
        door.SetParent(parent, false);
        // Local X points into the corridor, Z along the wall: the gate's own axes.
        door.SetPositionAndRotation(centre + spec.Outward * (outFace + .17f),
            Quaternion.LookRotation(Vector3.Cross(spec.Outward, Vector3.up), Vector3.up));

        var gate = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(GatePath), door);
        gate.name = "Sci Fi Gate";
        var frame = gate.transform.Find("Frame").GetComponent<Renderer>();
        Vector3 frameCentre = door.InverseTransformPoint(frame.bounds.center);
        float frameHeight = frame.bounds.size.y;
        gate.transform.localPosition = -frameCentre + Vector3.up * (frameHeight * .5f);

        var leafA = gate.transform.Find("Door_Left");
        var leafB = gate.transform.Find("Door_Right");
        var securityDoor = door.gameObject.AddComponent<SecurityDoor>();
        securityDoor.StartLocked = spec.StartLocked;
        securityDoor.LeafA = leafA;
        securityDoor.LeafB = leafB;
        securityDoor.SlideA = Vector3.forward * (LeafSlide * SlideSign(door, leafA, frame));
        securityDoor.SlideB = Vector3.forward * (LeafSlide * SlideSign(door, leafB, frame));

        // Blocks the whole doorway (some frames have open gaps beside them) while shut.
        float span = Mathf.Max(width + .3f, frame.bounds.size.x + frame.bounds.size.z - .3f);
        var blocker = door.gameObject.AddComponent<BoxCollider>();
        blocker.center = new Vector3(0f, 1.8f, 0f);
        blocker.size = new Vector3(.3f, 3.6f, span);
        securityDoor.Blocker = blocker;
        var obstacle = door.gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = blocker.center;
        obstacle.size = new Vector3(.6f, 3.6f, span);
        obstacle.carving = true;
        securityDoor.Obstacle = obstacle;

        // A lamp bar across the top of the frame, seen from both sides, and a light on the corridor side.
        securityDoor.Lamps = new[]
        {
            Box("Status Lamp", door, lamp, new Vector3(0f, frameHeight + .06f, 0f), new Vector3(.36f, .07f, 1.1f)),
        };
        securityDoor.LampLight = PointLight("Status Light", door, new Vector3(.7f, frameHeight - .1f, 0f), 5f);
        securityDoor.OpenSfx = K.SynthClip("SecurityDoorOpen", SynthDoorOpen);
        securityDoor.CloseSfx = K.SynthClip("SecurityDoorClose", SynthDoorClose);
        return securityDoor;
    }

    // Which way along the wall a leaf slides: away from the frame's middle.
    static float SlideSign(Transform door, Transform leaf, Renderer frame)
    {
        var renderer = leaf.GetComponent<Renderer>();
        float offset = door.InverseTransformPoint(renderer.bounds.center).z - door.InverseTransformPoint(frame.bounds.center).z;
        return offset >= 0f ? 1f : -1f;
    }

    // Finds the doorway's open span along the wall and the wall's outer face, by raycasting from inside the room.
    static void MeasureDoorway(DoorSpec spec, out Vector3 centre, out float width, out float outFace)
    {
        float floor = FloorHeight(spec.Guess - spec.Outward * 1.5f);
        float y = floor + 1.2f;
        const float step = .05f, reach = 5f;
        int count = Mathf.RoundToInt(2f * reach / step) + 1;

        // Walk along the wall from inside the room; the doorway is the open run nearest the preset centre.
        var open = new bool[count + 1];
        for (int i = 0; i < count; i++)
        {
            Vector3 origin = spec.Guess + spec.Along * (i * step - reach) - spec.Outward * 1.5f;
            origin.y = y;
            open[i] = !Physics.Raycast(origin, spec.Outward, 3f, ~0, QueryTriggerInteraction.Ignore);
        }
        float bestLo = 0f, bestHi = 0f, bestDistance = float.MaxValue;
        for (int i = 0, start = -1; i <= count; i++)
        {
            if (open[i] && start < 0) start = i;
            if (open[i] || start < 0) continue;
            float lo = start * step - reach, hi = (i - 1) * step - reach;
            if (Mathf.Abs(lo + hi) * .5f < bestDistance)
            {
                bestDistance = Mathf.Abs(lo + hi) * .5f;
                bestLo = lo;
                bestHi = hi;
            }
            start = -1;
        }
        if (bestDistance == float.MaxValue)
        {
            Debug.LogWarning($"Keycard progression: no opening found at {spec.Name}; using the preset centre.");
            bestLo = -1.05f;
            bestHi = 1.05f;
        }

        width = bestHi - bestLo + step;
        centre = spec.Guess + spec.Along * ((bestLo + bestHi) * .5f);
        centre.y = floor;

        outFace = .15f;
        Vector3 jamb = centre + spec.Along * (width * .5f + .4f);
        jamb.y = y;
        if (Physics.Raycast(jamb + spec.Outward * 3f, -spec.Outward, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            outFace = Vector3.Dot(hit.point - centre, spec.Outward);
    }

    static float FloorHeight(Vector3 position)
    {
        return Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore)
            ? hit.point.y : position.y;
    }

    // --- access machines ---

    internal static KeycardAccessPoint BuildAccessPoint(SecurityDoor door, KeycardData card, SecurityDoor[] doors, Transform parent, Material lamp,
        AudioClip denied)
    {
        if (DoorWallMount(door, out var point, out var normal))
            return CreateAccessMachine(point, normal, card, doors, parent, lamp, denied);

        Transform d = door.transform;
        // No room on the door's wall: on a corridor wall a little way out from the gate, on whichever side has one.
        Vector3 outward = d.right;
        Vector3 origin = d.position + outward * 1.5f + Vector3.up * AccessMachineHeight;
        RaycastHit hit = default;
        bool found = false;
        foreach (var side in new[] { d.forward, -d.forward })
        {
            if (Physics.Raycast(origin, side, out hit, 3.5f, ~0, QueryTriggerInteraction.Ignore) &&
                Mathf.Abs(hit.normal.y) < .3f)
            {
                found = true;
                break;
            }
        }
        if (!found)
        {
            Debug.LogWarning("Keycard progression: no corridor wall beside " + d.name + "; the access machine stands free.");
            hit.point = origin + d.forward * 1.6f;
            hit.normal = -d.forward;
        }
        return CreateAccessMachine(hit.point, hit.normal, card, doors, parent, lamp, denied);
    }

    // The machine's panel width along the wall, and the gap it keeps from the gate's frame.
    const float AccessMachineWidth = .4f * AccessMachineScale, FrameClearance = .12f;

    /// <summary>
    /// A spot on the gate's own wall, beside the gate on the corridor side: the corridors' side walls are window bays.
    /// The gates are not centred on their corridors, so the wider side is used: the machine sits just clear of the
    /// frame, or in the middle of the gap when that is narrower than the machine and its clearance.
    /// </summary>
    internal static bool DoorWallMount(SecurityDoor door, out Vector3 point, out Vector3 normal)
    {
        point = normal = default;
        Transform d = door.transform;
        Vector3 outward = d.right;
        float half = 1.85f;
        foreach (var r in door.GetComponentsInChildren<Renderer>(true))
            if (r.name == "Frame") half = Mathf.Abs(Vector3.Dot(r.bounds.extents, new Vector3(Mathf.Abs(d.forward.x), 0f, Mathf.Abs(d.forward.z))));
        Vector3 height = Vector3.up * AccessMachineHeight;

        float bestSpan = 0f;
        Vector3 bestSide = Vector3.zero;
        foreach (var side in new[] { d.forward, -d.forward })
        {
            // From just outside the frame, along the wall's face, to the corridor's side wall.
            Vector3 from = d.position + height + outward * .35f + side * (half + .02f);
            float span = Physics.Raycast(from, side, out var sideHit, 4f, ~0, QueryTriggerInteraction.Ignore) ? sideHit.distance : 4f;
            if (span > bestSpan)
            {
                bestSpan = span;
                bestSide = side;
            }
        }
        if (bestSpan < AccessMachineWidth + .05f) return false;

        float along = half + Mathf.Min(FrameClearance + AccessMachineWidth * .5f, bestSpan * .5f);
        Vector3 probe = d.position + height + bestSide * along + outward * 1.2f;
        if (!Physics.Raycast(probe, -outward, out var wall, 1.6f, ~0, QueryTriggerInteraction.Ignore) ||
            Vector3.Dot(wall.normal, outward) < .7f)
            return false;
        point = wall.point;
        normal = wall.normal;
        return true;
    }

    // The tutorial wing's Workshop machine is built by BuildAccessPoint too; its exit machine is placed by hand.
    const string WorkshopAccessPath = "===== TUTORIAL WING =====/Workshop Access";

    /// <summary>
    /// Moves the access machines built by <see cref="BuildAccessPoint"/> (the progression's, and the tutorial wing's
    /// Workshop machine) onto their gates' walls (see <see cref="DoorWallMount"/>) without rebuilding either.
    /// </summary>
    [MenuItem("Tools/CoreBuster/Remount Access Machines")]
    public static void RemountAccessMachines()
    {
        var root = GameObject.Find(RootName);
        if (!root)
        {
            Debug.LogError("Remount access machines: no " + RootName + " in the open scene.");
            return;
        }
        var points = new List<KeycardAccessPoint>(root.GetComponentsInChildren<KeycardAccessPoint>(true));
        var workshop = GameObject.Find(WorkshopAccessPath);
        if (workshop) points.AddRange(workshop.GetComponentsInChildren<KeycardAccessPoint>(true));
        var log = new System.Text.StringBuilder("Remount access machines:");
        foreach (var point in points)
        {
            SecurityDoor door = null;
            float nearest = float.MaxValue;
            foreach (var candidate in point.Doors)
            {
                if (!candidate) continue;
                float distance = (candidate.transform.position - point.transform.position).sqrMagnitude;
                if (distance < nearest)
                {
                    nearest = distance;
                    door = candidate;
                }
            }
            // Its own collider must not stand in the way of the probes.
            var colliders = point.GetComponentsInChildren<Collider>(true);
            foreach (var c in colliders) c.enabled = false;
            bool found = door && DoorWallMount(door, out var wallPoint, out var wallNormal) && Place(point.transform, wallPoint, wallNormal);
            foreach (var c in colliders) c.enabled = true;
            log.Append($"\n  {point.name} by {(door ? door.name : "no door")}: {(found ? "on the gate's wall at " + point.transform.position.ToString("F2") : "left where it was")}");
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log(log.ToString());

        bool Place(Transform machine, Vector3 wallPoint, Vector3 wallNormal)
        {
            Undo.RecordObject(machine, "Remount Access Machines");
            Vector3 n = Vector3.ProjectOnPlane(wallNormal, Vector3.up).normalized;
            // As CreateAccessMachine: the panel faces its local -X, out from the wall.
            machine.SetPositionAndRotation(wallPoint + n * (.012f * AccessMachineScale), Quaternion.LookRotation(Vector3.Cross(-n, Vector3.up), Vector3.up));
            return true;
        }
    }

    /// <summary>An access machine mounted on the wall at wallPoint, its panel facing out along wallNormal.</summary>
    internal static KeycardAccessPoint CreateAccessMachine(Vector3 wallPoint, Vector3 wallNormal, KeycardData card,
        SecurityDoor[] doors, Transform parent, Material lamp, AudioClip denied)
    {
        Vector3 normal = Vector3.ProjectOnPlane(wallNormal, Vector3.up).normalized;
        var machine = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(AccessMachinePath));
        machine.name = "Access Machine (" + card.DisplayName + ")";
        machine.transform.SetParent(parent, false);
        machine.transform.localScale = Vector3.one * AccessMachineScale;
        // The panel faces its local -X; that goes out from the wall.
        machine.transform.SetPositionAndRotation(wallPoint + normal * (.012f * AccessMachineScale),
            Quaternion.LookRotation(Vector3.Cross(-normal, Vector3.up), Vector3.up));
        var box = machine.AddComponent<BoxCollider>();
        box.size = new Vector3(.03f, .46f, .4f);

        var reader = new GameObject("Reader").transform;
        reader.SetParent(machine.transform, false);
        reader.localPosition = k_ReaderLocal;
        reader.rotation = Quaternion.LookRotation(normal, Vector3.up);

        var point = machine.AddComponent<KeycardAccessPoint>();
        point.RequiredCard = card;
        point.Doors = doors;
        point.Reader = reader;
        point.FocusPoint = reader;
        point.Range = 2.4f;
        point.DeniedSfx = denied;
        point.AcceptSfx = K.SynthClip("KeycardAccepted", SynthAccepted);
        point.Lamps = new[]
        {
            Box("Status Lamp", machine.transform, lamp, new Vector3(-.01f, .26f, 0f), new Vector3(.02f, .025f, .36f)),
        };
        point.LampLight = PointLight("Status Light", machine.transform, new Vector3(-.3f, .2f, 0f), 2.5f);
        return point;
    }

    // --- terminals ---

    static KeycardTerminal BuildTerminal(string name, KeycardData card, Vector3 probe, Vector3 facing, Rect room,
        SecurityDoor[] doors, KeycardTerminal.Wave[] waves, GameObject warp, Transform parent, Material lamp,
        Material bezel, AudioClip denied)
    {
        var console = PlaceAgainstWall(ConsolePath, name, probe, facing, parent, .05f);
        var terminal = console.AddComponent<KeycardTerminal>();
        var bounds = WorldBounds(console);

        // The drive slot on top of the console: the disk pops up out of it, standing with its label to the player.
        // A little forward of the middle, clear of the trim along the back edge.
        Vector3 above = bounds.center + facing * .12f + Vector3.up * (bounds.extents.y + .5f);
        Vector3 slotPosition = Physics.Raycast(above, Vector3.down, out RaycastHit top, bounds.size.y + 1f, ~0,
            QueryTriggerInteraction.Ignore) ? top.point : new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        var slot = new GameObject("Eject Slot").transform;
        slot.SetParent(console.transform, true);
        slot.SetPositionAndRotation(slotPosition, Quaternion.LookRotation(Vector3.up, -facing));
        Box("Drive Bezel", slot, bezel, new Vector3(0f, 0f, -.005f), new Vector3(.2f, .03f, .02f));

        terminal.Reward = card;
        terminal.EjectSlot = slot;
        terminal.FocusPoint = slot;
        terminal.Range = 2.6f;
        terminal.Doors = doors;
        terminal.Waves = waves;
        terminal.WarpVfx = warp;
        terminal.SpawnPoints = SpawnPoints(console.transform, room);
        terminal.DeniedSfx = denied;
        terminal.ActivateSfx = K.SynthClip("TerminalLockdown", SynthLockdown);
        terminal.AlarmLoop = K.SynthClip("TerminalAlarmLoop", SynthAlarm);
        terminal.WarpSfx = K.SynthClip("EnemyWarpIn", SynthWarp);
        terminal.ClearedSfx = K.SynthClip("RoomSecured", SynthSecured);
        terminal.EjectSfx = K.SynthClip("DiskEject", SynthEject);
        // Along the console's raised back edge, behind the slot.
        terminal.Lamps = new[]
        {
            Box("Status Lamp", console.transform, lamp,
                console.transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.max.y + .03f, bounds.center.z) -
                                                        facing * (Mathf.Abs(Vector3.Dot(bounds.extents, facing)) - .06f)),
                new Vector3(.6f, .04f, .04f)),
        };
        terminal.LampLight = PointLight("Status Light", console.transform,
            console.transform.InverseTransformPoint(bounds.center + Vector3.up * (bounds.extents.y + .5f)) + Vector3.forward * .3f, 6f);
        return terminal;
    }

    static MasterTerminal BuildMasterTerminal(KeycardData[] cards, Vector3 probe, Vector3 facing, SecurityDoor[] doors,
        Transform parent, Material lamp, Material bezel, AudioClip denied)
    {
        var station = PlaceAgainstWall(StationPath, "Master Terminal", probe, facing, parent, .02f);
        var master = station.AddComponent<MasterTerminal>();
        var bounds = WorldBounds(station);
        // Slots run left to right as the player sees them, in the HUD's card order.
        Vector3 right = Vector3.Cross(facing, Vector3.up);

        var slots = new Transform[cards.Length];
        var slotLamps = new Renderer[cards.Length];
        for (int i = 0; i < cards.Length; i++)
        {
            float offset = (i - (cards.Length - 1) * .5f) * MasterSlotSpacing;
            Vector3 point = SurfacePoint(station.transform.position + Vector3.up * MasterSlotHeight + right * offset, facing, .6f);
            var slot = new GameObject("Card Slot " + (i + 1)).transform;
            slot.SetParent(station.transform, true);
            // Forward points into the terminal, the way a card goes in.
            slot.SetPositionAndRotation(point, Quaternion.LookRotation(-facing, Vector3.up));
            Box("Slot Bezel", slot, bezel, new Vector3(0f, 0f, .004f), new Vector3(.03f, .19f, .02f));
            slotLamps[i] = Box("Slot Lamp", slot, lamp, new Vector3(0f, .14f, -.006f), new Vector3(.12f, .025f, .015f));
            slots[i] = slot;
        }

        var lookAt = new GameObject("Look At").transform;
        lookAt.SetParent(station.transform, true);
        lookAt.position = (slots[0].position + slots[slots.Length - 1].position) * .5f;

        master.RequiredCards = cards;
        master.Slots = slots;
        master.SlotLamps = slotLamps;
        master.LookAt = lookAt;
        master.FocusPoint = lookAt;
        master.Range = 2.6f;
        master.Doors = doors;
        master.DeniedSfx = denied;
        master.GrantedSfx = K.SynthClip("MasterOverride", SynthMasterGranted);
        master.Lamps = new[]
        {
            Box("Status Lamp", station.transform, lamp,
                station.transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.max.y + .03f, bounds.center.z)) +
                Vector3.forward * .1f, new Vector3(1.1f, .05f, .05f)),
        };
        master.LampLight = PointLight("Status Light", station.transform, new Vector3(0f, 2.2f, .8f), 7f);
        return master;
    }

    // Stands a prop against the wall found behind the probe point, facing into the room.
    internal static GameObject PlaceAgainstWall(string prefabPath, string name, Vector3 probe, Vector3 facing, Transform parent, float gap)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = name;
        go.transform.SetParent(parent, false);

        float floor = FloorHeight(probe);
        Vector3 origin = new Vector3(probe.x, floor + 1f, probe.z);
        Vector3 wall = Physics.Raycast(origin, -facing, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore)
            ? hit.point : origin - facing * 3f;

        go.transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
        go.transform.position = new Vector3(wall.x, floor, wall.z);
        // Push it out until its back clears the wall.
        var bounds = WorldBounds(go);
        float back = Vector3.Dot(bounds.center - go.transform.position, facing) - Mathf.Abs(Vector3.Dot(bounds.extents, facing));
        go.transform.position += facing * (gap - back);

        if (!go.GetComponentInChildren<Collider>())
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
                filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
        Physics.SyncTransforms();
        return go;
    }

    // Where a ray from in front of the prop, level at the given point, meets its front.
    static Vector3 SurfacePoint(Vector3 inside, Vector3 facing, float reach)
    {
        Vector3 origin = inside + facing * (reach + .6f);
        return Physics.Raycast(origin, -facing, out RaycastHit hit, reach + .6f, ~0, QueryTriggerInteraction.Ignore)
            ? hit.point : inside + facing * reach;
    }

    // Warp-in points spread round the room, on the navmesh and clear of the terminal.
    static Transform[] SpawnPoints(Transform terminal, Rect room)
    {
        var group = new GameObject("Spawn Points").transform;
        group.SetParent(terminal.parent, false);
        group.name = terminal.name + " Spawn Points";
        Vector2[] fractions =
        {
            new Vector2(.2f, .2f), new Vector2(.5f, .15f), new Vector2(.8f, .2f), new Vector2(.15f, .5f),
            new Vector2(.85f, .5f), new Vector2(.2f, .8f), new Vector2(.5f, .85f), new Vector2(.8f, .8f),
        };
        var points = new List<Transform>();
        foreach (var f in fractions)
        {
            var guess = new Vector3(room.xMin + room.width * f.x, terminal.position.y, room.yMin + room.height * f.y);
            if (!NavMesh.SamplePosition(guess, out NavMeshHit hit, 2.5f, NavMesh.AllAreas)) continue;
            if (Vector3.Distance(hit.position, terminal.position) < 4f) continue;
            var point = new GameObject("Spawn Point " + (points.Count + 1)).transform;
            point.SetParent(group, false);
            point.position = hit.position;
            points.Add(point);
        }
        if (points.Count == 0) Debug.LogWarning("Keycard progression: no navmesh spawn points found for " + terminal.name);
        return points.ToArray();
    }

    // --- small props ---

    // A small block without a collider: status lamps (coloured at runtime) and dark slot bezels.
    internal static Renderer Box(string name, Transform parent, Material material, Vector3 localPosition, Vector3 size)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(box.GetComponent<Collider>());
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPosition;
        // Sized in world units whatever the parent's scale.
        Vector3 scale = parent.lossyScale;
        box.transform.localScale = new Vector3(size.x / scale.x, size.y / scale.y, size.z / scale.z);
        var renderer = box.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return renderer;
    }

    internal static Light PointLight(string name, Transform parent, Vector3 localPosition, float range)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = range;
        light.intensity = 1f;
        light.shadows = LightShadows.None;
        return light;
    }

    internal static Bounds WorldBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    // ------------------------------------------------------------------ warp-in effect

    // Red sparks spiralling up round a shrinking ring of light, with a flash, where an enemy is about to appear.
    static GameObject BuildWarpVfx()
    {
        Material spark = K.EnergyMaterial("M_CoreSpark", 0f, 4f, 1f, Color.white);
        Material ring = K.EnergyMaterial("M_CoreShockwave", 1f, 3f, 0f, Color.white);

        var root = new GameObject("VFX_EnemyWarpIn");
        try
        {
            var collapse = K.NewParticles("Collapsing Ring", root.transform, ring, 6);
            collapse.transform.localPosition = Vector3.up * .05f;
            var main = collapse.main;
            main.playOnAwake = true;
            main.loop = false;
            main.duration = .5f;
            main.startLifetime = .5f;
            main.startSpeed = 0f;
            main.startSize = 3.2f;
            main.startColor = k_Hostile;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            K.Burst(collapse, 1);
            var shape = collapse.shape;
            shape.enabled = false;
            var shrink = collapse.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, .15f));
            K.FadeOut(collapse, k_Ember);
            collapse.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;

            var burst = K.NewParticles("Landing Ring", root.transform, ring, 4);
            burst.transform.localPosition = Vector3.up * .06f;
            main = burst.main;
            main.playOnAwake = true;
            main.loop = false;
            main.startDelay = .45f;
            main.startLifetime = .45f;
            main.startSpeed = 0f;
            main.startSize = .6f;
            main.startColor = k_Ember;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            K.Burst(burst, 1);
            shape = burst.shape;
            shape.enabled = false;
            K.GrowOut(burst, 1f, 5f);
            K.FadeOut(burst, k_Hostile);
            burst.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;

            var column = K.NewParticles("Spiral Sparks", root.transform, spark, 200);
            main = column.main;
            main.playOnAwake = true;
            main.loop = false;
            main.duration = .55f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.4f, .8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(.03f, .07f);
            main.startColor = new ParticleSystem.MinMaxGradient(k_Hostile, k_Ember);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = column.emission;
            emission.rateOverTime = 260f;
            shape = column.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = .7f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var velocity = column.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.orbitalY = 5f;
            velocity.radial = -.6f;
            velocity.x = velocity.z = 0f;
            velocity.y = 1.5f;
            K.FadeOut(column, k_Hostile);
            K.Stretch(column, .05f, 1.2f);

            var flash = K.NewParticles("Flash", root.transform, spark, 2);
            flash.transform.localPosition = Vector3.up * 1f;
            main = flash.main;
            main.playOnAwake = true;
            main.loop = false;
            main.startDelay = .42f;
            main.startLifetime = .25f;
            main.startSpeed = 0f;
            main.startSize = 3f;
            main.startColor = new Color(1f, .45f, .3f, .9f);
            K.Burst(flash, 1);
            shape = flash.shape;
            shape.enabled = false;
            K.FadeOut(flash, k_Hostile);

            var lightObject = new GameObject("Warp Light");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = Vector3.up * 1f;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = k_Hostile;
            light.range = 6f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            // Builds while the ring closes, peaks as the enemy lands, then dies away.
            lightObject.AddComponent<LightFlash>().Intensity = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(.45f, 6f), new Keyframe(1f, 0f));

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, WarpVfxPath);
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // ------------------------------------------------------------------ sound synthesis

    static float Noise(System.Random random) => K.Noise(random);
    static float Square(float phase) => Mathf.Sin(phase) >= 0f ? 1f : -1f;
    static float Saw(float phase) => (phase / (2f * Mathf.PI)) % 1f * 2f - 1f;

    static float[] Samples(float length) => new float[(int)(length * SampleRate)];

    // Servo whine climbing as the gate parts, a pneumatic hiss, and a clunk as it docks.
    static float[] SynthDoorOpen(System.Random random)
    {
        var samples = Samples(1f);
        var hiss = new K.BandPass();
        float phase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            phase += 2f * Mathf.PI * Mathf.Lerp(140f, 360f, Mathf.Clamp01(t / .7f)) / SampleRate;
            float servo = (Saw(phase) * .35f + Mathf.Sin(phase * 2f) * .25f) * Mathf.Clamp01(t / .05f) * Mathf.Clamp01((.75f - t) / .1f);
            float air = hiss.Process(Noise(random), 2400f, 1.2f) * Mathf.Exp(-t * 5f) * 1.6f;
            float clunk = t > .72f ? (Mathf.Sin(2f * Mathf.PI * 70f * t) + Noise(random) * .5f) * Mathf.Exp(-(t - .72f) * 30f) : 0f;
            samples[i] = servo + air + clunk;
        }
        return K.Normalize(samples, .7f);
    }

    // Servo winding down and a heavy slam.
    static float[] SynthDoorClose(System.Random random)
    {
        var samples = Samples(.9f);
        float phase = 0f;
        float low = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            phase += 2f * Mathf.PI * Mathf.Lerp(340f, 150f, Mathf.Clamp01(t / .6f)) / SampleRate;
            float servo = (Saw(phase) * .35f + Mathf.Sin(phase * 2f) * .2f) * Mathf.Clamp01(t / .04f) * Mathf.Clamp01((.62f - t) / .08f);
            low += (Noise(random) - low) * .05f;
            float slam = t > .6f ? (Mathf.Sin(2f * Mathf.PI * 52f * t) * 1.2f + low * 5f) * Mathf.Exp(-(t - .6f) * 14f) : 0f;
            samples[i] = servo + slam;
        }
        return K.Normalize(samples, .8f);
    }

    // Lockdown: three falling alert beeps over a burst of static.
    static float[] SynthLockdown(System.Random random)
    {
        var samples = Samples(.9f);
        float[] notes = { 1320f, 990f, 660f };
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            int note = Mathf.Min(2, Mathf.FloorToInt(t / .16f));
            float local = t - note * .16f;
            float beep = t < .48f ? Square(2f * Mathf.PI * notes[note] * t) * .4f * (local < .12f ? 1f : 0f) : 0f;
            float stat = Noise(random) * .25f * Mathf.Exp(-t * 6f);
            float drone = t > .48f ? Mathf.Sin(2f * Mathf.PI * 110f * t) * .5f * Mathf.Exp(-(t - .48f) * 4f) : 0f;
            samples[i] = beep + stat + drone;
        }
        return K.Normalize(samples, .75f);
    }

    // Two-tone klaxon; each tone holds a whole number of cycles, so the loop is seamless.
    static float[] SynthAlarm(System.Random random)
    {
        var samples = Samples(1.2f);
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float frequency = Mathf.FloorToInt(t / .3f) % 2 == 0 ? 660f : 440f;
            float local = t % .3f;
            float gate = Mathf.Clamp01(local / .01f) * Mathf.Clamp01((.3f - local) / .01f);
            float phase = 2f * Mathf.PI * frequency * t;
            samples[i] = (Square(phase) * .3f + Mathf.Sin(phase) * .5f + Mathf.Sin(phase * 2f) * .15f) * gate;
        }
        return K.Normalize(samples, .5f);
    }

    // Teleport: a rising zap with a shimmer, landing on a low boom.
    static float[] SynthWarp(System.Random random)
    {
        var samples = Samples(1f);
        var shimmer = new K.BandPass();
        float phase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float rise = Mathf.Clamp01(t / .45f);
            phase += 2f * Mathf.PI * Mathf.Lerp(200f, 1800f, rise * rise) / SampleRate;
            float zap = (Mathf.Sin(phase + Mathf.Sin(phase * 3.01f) * 2f) * .4f) * (t < .45f ? rise : Mathf.Exp(-(t - .45f) * 20f));
            float sparkle = shimmer.Process(Noise(random), Mathf.Lerp(1500f, 6000f, rise), 4f) * .8f * (t < .5f ? rise : Mathf.Exp(-(t - .5f) * 10f));
            float boom = t > .45f ? Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(90f, 40f, (t - .45f) / .55f) * t) * Mathf.Exp(-(t - .45f) * 7f) : 0f;
            samples[i] = zap + sparkle + boom;
        }
        return K.Normalize(samples, .7f);
    }

    // Room secured: a bright rising chime.
    static float[] SynthSecured(System.Random random)
    {
        var samples = Samples(1.3f);
        float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float s = 0f;
            for (int n = 0; n < notes.Length; n++)
            {
                float start = n * .09f;
                if (t < start) continue;
                float local = t - start;
                s += (Mathf.Sin(2f * Mathf.PI * notes[n] * t) * .5f + Mathf.Sin(2f * Mathf.PI * notes[n] * 2f * t) * .15f) *
                     Mathf.Exp(-local * 3.2f) * Mathf.Clamp01(local / .005f);
            }
            samples[i] = s;
        }
        return K.Normalize(samples, .7f);
    }

    // A drive motor pushing the disk out, ending in a click.
    static float[] SynthEject(System.Random random)
    {
        var samples = Samples(.6f);
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float motor = t < .42f ? (Saw(2f * Mathf.PI * 118f * t) * .4f + Noise(random) * .08f) *
                                     (.7f + .3f * Mathf.Sin(2f * Mathf.PI * 22f * t)) * Mathf.Clamp01(t / .03f) : 0f;
            float click = t > .42f ? (Mathf.Sin(2f * Mathf.PI * 2600f * t) + Noise(random) * .6f) * Mathf.Exp(-(t - .42f) * 90f) : 0f;
            samples[i] = motor + click;
        }
        return K.Normalize(samples, .65f);
    }

    // The disk touching the reader: a plastic tick.
    static float[] SynthTap(System.Random random)
    {
        var samples = Samples(.08f);
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            samples[i] = (Mathf.Sin(2f * Mathf.PI * 3100f * t) * .6f + Noise(random) * .5f) * Mathf.Exp(-t * 110f);
        }
        return K.Normalize(samples, .55f);
    }

    // Access granted: two rising beeps.
    static float[] SynthAccepted(System.Random random)
    {
        var samples = Samples(.45f);
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float frequency = t < .13f ? 988f : 1480f;
            float local = t < .13f ? t : t - .15f;
            float gate = t < .13f || t > .15f ? Mathf.Clamp01(local / .005f) * Mathf.Exp(-local * (t < .13f ? 4f : 7f)) : 0f;
            samples[i] = (Mathf.Sin(2f * Mathf.PI * frequency * t) * .6f + Square(2f * Mathf.PI * frequency * t) * .12f) * gate;
        }
        return K.Normalize(samples, .6f);
    }

    // Access denied: a low double buzz.
    static float[] SynthDenied(System.Random random)
    {
        var samples = Samples(.34f);
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float gate = t < .13f || (t > .17f && t < .3f) ? 1f : 0f;
            float phase = 2f * Mathf.PI * 150f * t;
            samples[i] = (Square(phase) * .5f + Saw(phase * 1.01f) * .3f) * gate;
        }
        return K.Normalize(samples, .45f);
    }

    // Taking the disk: a quick plastic swish.
    static float[] SynthPickup(System.Random random)
    {
        var samples = Samples(.3f);
        var swish = new K.BandPass();
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float air = swish.Process(Noise(random), Mathf.Lerp(800f, 3500f, t / .3f), 2f) * Mathf.Sin(t / .3f * Mathf.PI);
            float click = t > .03f ? Mathf.Sin(2f * Mathf.PI * 2200f * t) * Mathf.Exp(-(t - .03f) * 200f) * .5f : 0f;
            samples[i] = air * 1.5f + click;
        }
        return K.Normalize(samples, .5f);
    }

    // Slipped into a pocket: a soft cloth thump.
    static float[] SynthPocket(System.Random random)
    {
        var samples = Samples(.22f);
        float low = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            low += (Noise(random) - low) * .06f;
            samples[i] = (low * 4f + Mathf.Sin(2f * Mathf.PI * 120f * t) * .5f) * Mathf.Exp(-t * 26f);
        }
        return K.Normalize(samples, .45f);
    }

    // Seated in a Master terminal slot: a slide and a solid latch.
    static float[] SynthInsert(System.Random random)
    {
        var samples = Samples(.3f);
        var slide = new K.BandPass();
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float rub = t < .12f ? slide.Process(Noise(random), 1800f, 2f) * Mathf.Sin(t / .12f * Mathf.PI) : 0f;
            float latch = t > .12f ? (Mathf.Sin(2f * Mathf.PI * 1700f * t) * .5f + Mathf.Sin(2f * Mathf.PI * 180f * t)) *
                                     Mathf.Exp(-(t - .12f) * 45f) : 0f;
            samples[i] = rub + latch;
        }
        return K.Normalize(samples, .65f);
    }

    // Master override: a swelling chord over a deep boom.
    static float[] SynthMasterGranted(System.Random random)
    {
        var samples = Samples(2.2f);
        float[] chord = { 261.63f, 329.63f, 392f, 523.25f, 659.25f };
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float swell = Mathf.Clamp01(t / .35f) * Mathf.Exp(-Mathf.Max(0f, t - .35f) * 1.6f);
            float s = 0f;
            foreach (float note in chord)
                s += Mathf.Sin(2f * Mathf.PI * note * t + Mathf.Sin(2f * Mathf.PI * 5f * t) * .3f) * .22f;
            float boom = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(70f, 35f, Mathf.Clamp01(t / 1.2f)) * t) * Mathf.Exp(-t * 2f);
            samples[i] = s * swell + boom * .9f;
        }
        return K.Normalize(samples, .85f);
    }
}
