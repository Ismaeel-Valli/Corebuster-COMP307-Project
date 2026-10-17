using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.FPS.AI;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.FPS.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using K = CoreExecutionBuilder;
using KP = KeycardProgressionBuilder;

/// <summary>
/// Builds the tutorial and asset showcase wing into MainScene (corebuster_tutorial_001): the emergency officer's
/// quarters where the player is revived beside a hibernation pod, the fabrication gallery of first-principles assets
/// and custom shaders on podiums behind stanchions, the live-fire drill against three dormant enemies, and the
/// clearance terminal that issues the black keycard for the wing's exit and the Workshop door. Everything lives under
/// its own scene root, rebuilt from scratch each run, and its art is generated into Assets/FPS/Art/TutorialWing.
/// Outside that root it only: gives the wing's shell pieces lit material copies and collision for its two kit
/// doorways, deactivates the shell pieces it replaces with kit skylights and windows (corebuster_tutorial_002), seals
/// the Workshop door (adding its black-card access machine) and moves the Player to the pod.
/// </summary>
public static partial class TutorialWingBuilder
{
    const string ScenePath = "Assets/FPS/Scenes/MainScene.unity";
    const string RootName = "===== TUTORIAL WING =====";
    const string ShipAIVoiceFolder = "Assets/FPS/Audio/Voice/ShipAI";
    const string ArtRoot = "Assets/FPS/Art/TutorialWing";
    const string MeshFolder = ArtRoot + "/Meshes";
    const string MaterialFolder = ArtRoot + "/Materials";
    const string TextureFolder = ArtRoot + "/Textures";
    const string PrefabFolder = ArtRoot + "/Prefabs";
    // Lit copies of the pack materials used in the wing, at their source's path below Assets/.
    const string LitCopyRoot = ArtRoot + "/Materials/Pack";
    const string KeycardPath = "Assets/FPS/Data/Keycards/Keycard_Black.asset";
    const string FloppyFolder = "Assets/HorrorPuzzleItems/Floppy";
    const string EnemyFolder = "Assets/FPS/Prefabs/Enemies/";
    const string BlasterPath = "Assets/FPS/Prefabs/Weapons/Weapon_Blaster.prefab";
    const string DisassemblerGunPath = "Assets/FPS/Prefabs/Weapons/Weapon_Disassembler.prefab";
    const string ScattergunPath = "Assets/FPS/Prefabs/Weapons/Weapon_ScrapScattergun.prefab";
    const string RivetCannonPath = "Assets/FPS/Prefabs/Weapons/Weapon_RivetCannon.prefab";
    const string ConsolePath = "Assets/Sci-Fi Styled Modular Pack/Prefabs/Decorative elements/console.prefab";
    const string Modular = "Assets/Sci-Fi Styled Modular Pack/Prefabs/";
    const string Warehouse = "Assets/SciFi Warehouse Kit/Prefabs/Props/";
    const string Structures = "Assets/SciFi Warehouse Kit/Prefabs/Structures/";
    const string LampPrefab = "Assets/FPS/Art/Corridors/CorridorLamp/Prefabs/P_CorridorLamp_Wall.prefab";
    const string TankPrefab = "Assets/FPS/Art/StartWorkshop/HeavyIndustrialKit/Prefabs/P_PressureTank_P3.prefab";
    const string TitleFontPath = "Assets/Gentleland/SteampunkUI/Fonts/BebasNeue/BebasNeue SDF.asset";
    const string BodyFontPath = "Assets/FPS/Art/Fonts/Roboto-Regular SDF.asset";
    const string LogoPath = "Assets/FPS/Art/UI/CoreBuster_Logo.png";

    // Where the two kit doorways are (the Wall Door 02 pieces have no colliders of their own).
    const string QuartersDoorPiece = "Wall Door 02 (1)";
    const string ExitDoorPiece = "Wall Door 02";
    const string WorkshopDoorName = "Spawn Room Door (N)";

    // Shell pieces of the wing (scene roots by name) that get the lit copies.
    static readonly string[] ShellPrefixes = { "Wall Plain", "Floor Tile 01", "Ceiling Closed", "Wall Door 02" };

    static readonly Color k_Cyan = new Color(.3f, .8f, 1f);
    static readonly Color k_Amber = new Color(1f, .62f, .2f);
    static readonly Color k_Red = new Color(1f, .22f, .16f);
    static readonly Color k_Warm = new Color(1f, .9f, .78f);
    static readonly Color k_Card = new Color(.82f, .86f, .95f);

    /// <summary>What kind of first-principles asset a plaque presents; each has its own colour on the plaque.</summary>
    enum Category { Npc, Environment, Shader, Weapon }

    static readonly string[] k_CategoryNames = { "NPC", "ENVIRONMENT", "SHADER", "WEAPON" };
    static readonly Color[] k_CategoryColors =
    {
        new Color(1f, .34f, .28f), new Color(.38f, 1f, .52f), new Color(.78f, .5f, 1f), new Color(1f, .62f, .2f),
    };

    // Shell pieces opened to the sky: kit ceiling tiles that become skylights and wall panels that become windows,
    // found by their centre (world X/Z). The originals are deactivated and the kit's open variants placed in their stead.
    const string SkylightPrefab = Structures + "Ceiling/Ceiling Skylight.prefab";
    const string WindowPrefab = Structures + "Walls/Wall Window.prefab";
    static readonly Vector2[] k_Skylights =
    {
        new Vector2(-240.1f, -32.8f), // quarters
        new Vector2(-252.1f, -32.8f), new Vector2(-252.1f, -20.8f), // gallery walkway
        new Vector2(-246.1f, -14.8f), new Vector2(-246.1f, -2.8f), // north hall
    };
    static readonly Vector2[] k_Windows =
    {
        new Vector2(-261.3f, -32.8f), new Vector2(-261.3f, -26.9f), new Vector2(-261.3f, -20.9f), new Vector2(-261.3f, -14.9f),
        new Vector2(-261.3f, -9f), // west wall, behind the gallery and the drill
        new Vector2(-240.1f, -41.9f), new Vector2(-231f, -32.9f), // quarters: south wall and above the pod
    };

    /// <summary>The wing's measured interior faces (world X/Z) and floor height.</summary>
    struct Frame
    {
        public float Floor;
        public float QuartersEast, QuartersWest, QuartersSouth, QuartersNorth;
        public float GalleryWest, GalleryEast, HallNorth;
        public float Ceiling;
    }

    class Mats
    {
        public Material PodShell, PodTrim, PodGlow, PodPadding, PodGlass, PodScreen;
        public Material PodiumBody, PodiumTop, PodiumGlow;
        public Material Chrome, Velvet, Brass, Carpet;
        public Material Plaque, PlaqueFace, PlaqueGlow, RackPlate, RackFrame;
        public Material Rail, HoloPanel, HoloFrame, Hazard, GuideGlow, Occluder;
        public Material Lamp, Bezel;
        public Material Nebula;
        public Material LightCone;
        public Material[] CategoryGlow;
    }

    class Fonts
    {
        public TMP_FontAsset Title, Body;
    }

    static Mats s_M;
    static Fonts s_F;

    [MenuItem("Tools/CoreBuster/Build Tutorial Wing")]
    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError("Tutorial wing: open " + ScenePath + " first.");
            return;
        }
        var player = Object.FindAnyObjectByType<PlayerCharacterController>();
        if (!player)
        {
            Debug.LogError("Tutorial wing: no Player in the scene.");
            return;
        }

        var old = GameObject.Find(RootName);
        if (old) Object.DestroyImmediate(old);
        Physics.SyncTransforms();

        K.EnsureFolder(MeshFolder);
        K.EnsureFolder(MaterialFolder);
        K.EnsureFolder(TextureFolder);
        K.EnsureFolder(PrefabFolder);
        s_Meshes.Clear();
        s_F = new Fonts
        {
            Title = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath),
            Body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath),
        };
        s_M = BuildMaterials();
        var card = BuildBlackKeycard();

        var root = new GameObject(RootName).transform;
        // Opened first, so the measurements below cast against the shell as it will stand.
        int opened = OpenToSky(Section(root, "Sky Openings"));
        Physics.SyncTransforms();
        var frame = Measure();
        Debug.Log($"Tutorial wing frame: quarters X [{frame.QuartersWest:F2}, {frame.QuartersEast:F2}] Z [{frame.QuartersSouth:F2}, " +
                  $"{frame.QuartersNorth:F2}], gallery X [{frame.GalleryWest:F2}, {frame.GalleryEast:F2}], hall north {frame.HallNorth:F2}, " +
                  $"floor {frame.Floor:F2}, ceiling {frame.Ceiling:F2}.");

        BuildDoorwayCollision(Section(root, "Shell Collision"));
        Physics.SyncTransforms();

        var quarters = Section(root, "Officer Quarters");
        var gallery = Section(root, "Fabrication Gallery");
        var drill = Section(root, "Combat Drill");
        var exit = Section(root, "Exit");

        var director = new GameObject("Tutorial Director").AddComponent<TutorialDirector>();
        director.transform.SetParent(root, false);

        BuildQuarters(quarters, frame, director, player);
        BuildGallery(gallery, frame);
        BuildDrill(drill, frame, director);
        BuildExit(exit, frame, director, card);
        BuildWorkshopAccess(Section(root, "Workshop Access"), card);
        BuildDressing(Section(root, "Dressing"), frame);
        BuildVolume(Section(root, "Post Volume"), frame);
        // Last, so its survey of the level sees the finished wing.
        Physics.SyncTransforms();
        BuildShipMap(quarters, frame);

        director.WingBounds = new Bounds(
            new Vector3((frame.GalleryWest + frame.QuartersEast) * .5f, frame.Floor + 5f, (frame.QuartersSouth + frame.HallNorth) * .5f),
            new Vector3(frame.QuartersEast - frame.GalleryWest, 12f, frame.HallNorth - frame.QuartersSouth));
        director.TaskDoneSfx = K.SynthClip("TutorialTaskDone", SynthTaskDone);
        director.TransmissionSfx = K.SynthClip("TutorialTransmission", SynthTransmission);
        director.StepSfx = K.SynthClip("TutorialStep", SynthStep);
        // The ship AI's voice, from Tools/Art/generate_ship_ai_voice.py (one clip per wake-up line, in order).
        for (int i = 0; i < director.WakeUpLines.Length; i++)
            director.WakeUpLines[i].Voice = AssetDatabase.LoadAssetAtPath<AudioClip>($"{ShipAIVoiceFolder}/VO_ShipAI_WakeUp_{i + 1:00}.wav");

        int swapped = ApplyLitCopies(root, frame);
        Physics.SyncTransforms();

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Tutorial wing built: {opened} shell pieces opened to the sky, {swapped} renderers on lit copies. " +
                  $"Player revived at {player.transform.position}.");
    }

    static Transform Section(Transform root, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(root, false);
        return t;
    }

    // ------------------------------------------------------------------ measuring

    static Frame Measure()
    {
        float Cast(Vector3 from, Vector3 dir, float fallback)
        {
            return Physics.Raycast(from, dir, out RaycastHit hit, 40f, ~0, QueryTriggerInteraction.Ignore)
                ? Vector3.Dot(hit.point, dir.x != 0f ? Vector3.right : Vector3.forward) : fallback;
        }
        var f = new Frame();
        Vector3 quarters = new Vector3(-240f, 1.2f, -38f);
        f.Floor = Physics.Raycast(new Vector3(-240f, 3f, -33f), Vector3.down, out RaycastHit floor, 6f) ? floor.point.y : .08f;
        f.Ceiling = Physics.Raycast(new Vector3(-240f, 3f, -33f), Vector3.up, out RaycastHit ceiling, 12f) ? ceiling.point.y : 9.9f;
        f.QuartersEast = Cast(quarters, Vector3.right, -231.12f);
        f.QuartersWest = Cast(quarters, Vector3.left, -249.08f);
        f.QuartersSouth = Cast(quarters, Vector3.back, -41.75f);
        f.QuartersNorth = Cast(new Vector3(-240f, 1.2f, -33f), Vector3.forward, -24.35f);
        Vector3 gallery = new Vector3(-255f, 1.2f, -38f);
        f.GalleryWest = Cast(gallery, Vector3.left, -261.12f);
        f.GalleryEast = Cast(gallery, Vector3.right, -249.38f);
        f.HallNorth = Cast(new Vector3(-255f, 1.2f, -20f), Vector3.forward, .05f);
        return f;
    }

    // Swaps the chosen kit ceiling tiles and wall panels for the kit's skylight and window pieces (same footprint, pivot
    // and collider), so the nebula sky shows through and the sun reaches the floor. The originals are only
    // deactivated; they are found by their pivot frame, which still holds on a rebuild when they are already inactive.
    static int OpenToSky(Transform parent)
    {
        int opened = 0;
        foreach (var go in HierarchyFolder.TopLevel(EditorSceneManager.GetActiveScene()))
        {
            bool ceiling = go.name.StartsWith("Ceiling Closed"), wall = go.name.StartsWith("Wall Plain");
            if (!ceiling && !wall) continue;
            // Each piece's footprint centre in its own frame (the kit pivots sit on a corner).
            Vector3 centre = go.transform.TransformPoint(ceiling ? new Vector3(3f, 0f, 3f) : new Vector3(0f, 4f, 3f));
            var at = new Vector2(centre.x, centre.z);
            if (!(ceiling ? k_Skylights : k_Windows).Any(c => Vector2.Distance(c, at) < .6f)) continue;

            var open = Prop(parent, ceiling ? SkylightPrefab : WindowPrefab, go.transform.position, 0f);
            if (!open) continue;
            open.transform.SetPositionAndRotation(go.transform.position, go.transform.rotation);
            open.transform.localScale = go.transform.localScale;
            open.name = (ceiling ? "Skylight" : "Window") + " (replaces " + go.name + ")";
            // The glass lets the sun through.
            foreach (var r in open.GetComponentsInChildren<Renderer>(true))
                if (r.name.Contains("Glass")) r.shadowCastingMode = ShadowCastingMode.Off;
            if (go.activeSelf)
            {
                Undo.RecordObject(go, "Open the tutorial wing to the sky");
                go.SetActive(false);
                if (PrefabUtility.IsPartOfPrefabInstance(go)) PrefabUtility.RecordPrefabInstancePropertyModifications(go);
            }
            opened++;
        }
        if (opened != k_Skylights.Length + k_Windows.Length)
            Debug.LogWarning($"Tutorial wing: opened {opened} of {k_Skylights.Length + k_Windows.Length} shell pieces to the sky.");
        return opened;
    }

    // The kit doorway pieces have no colliders: block their solid wall around the frame opening.
    static void BuildDoorwayCollision(Transform parent)
    {
        foreach (var name in new[] { QuartersDoorPiece, ExitDoorPiece })
        {
            var piece = GameObject.Find(name);
            if (!piece)
            {
                Debug.LogWarning("Tutorial wing: no " + name + " doorway piece.");
                continue;
            }
            var wall = piece.transform.Find("Alternate Wall Door")?.GetComponent<Renderer>();
            var frameRenderer = piece.transform.Find("Alternate Wall Door Frame")?.GetComponent<Renderer>();
            if (!wall || !frameRenderer) continue;
            Bounds w = wall.bounds, fr = frameRenderer.bounds;
            const float post = .1f;
            float x0 = w.min.x, x1 = w.max.x, y0 = w.min.y, y1 = w.max.y;
            float open0 = fr.min.z + post, open1 = fr.max.z - post, lintel = fr.max.y - post;
            var group = new GameObject(name + " Collision").transform;
            group.SetParent(parent, false);
            void Block(string label, float za, float zb, float ya, float yb)
            {
                if (zb - za < .01f || yb - ya < .01f) return;
                var go = new GameObject(label);
                go.transform.SetParent(group, false);
                go.transform.position = new Vector3((x0 + x1) * .5f, (ya + yb) * .5f, (za + zb) * .5f);
                go.AddComponent<BoxCollider>().size = new Vector3(x1 - x0, yb - ya, zb - za);
            }
            Block("Jamb A", w.min.z, open0, y0, y1);
            Block("Jamb B", open1, w.max.z, y0, y1);
            Block("Lintel", open0, open1, lintel, y1);
        }
    }

    // ------------------------------------------------------------------ quarters

    static void BuildQuarters(Transform parent, Frame f, TutorialDirector director, PlayerCharacterController player)
    {
        float floor = f.Floor;
        float east = f.QuartersEast, west = f.QuartersWest, north = f.QuartersNorth, south = f.QuartersSouth;
        const float spawnZ = -31.9f;
        float spawnX = east - 3.55f;

        // --- Hibernation pod against the east wall, the player revived beside it ---
        var pod = BuildPod(parent, new Vector3(east - .95f, floor, spawnZ + 1f));
        director.Pod = pod;
        Plaque(parent, new Vector3(east - 2.3f, floor, spawnZ - 2.5f), Quaternion.Euler(0f, -28f, 0f),
            "HIBERNATION POD", "Emergency officer's revival pod: tub, canopy, rings and vitals console, all procedural meshes.",
            "FIRST-PRINCIPLES MESH  /  VITALS SCREEN TEXTURE DRAWN IN CODE", Category.Environment);

        // --- The weapon rack straight ahead on the north wall ---
        var rackPosition = new Vector3(spawnX, floor + 1.45f, north);
        var rack = MeshObject("Sidearm Locker", parent, SaveMesh("SM_TW_WeaponRack", TutorialWingArt.WeaponRack),
            new[] { s_M.RackPlate, s_M.RackFrame }, rackPosition, Quaternion.Euler(0f, 180f, 0f));
        rack.AddComponent<BoxCollider>().size = new Vector3(1.42f, .9f, .25f);
        rack.GetComponent<BoxCollider>().center = new Vector3(0f, 0f, .12f);
        var locker = rack.AddComponent<WeaponLocker>();
        locker.Weapon = AssetDatabase.LoadAssetAtPath<WeaponController>(BlasterPath);
        locker.DisplayModel = DisplayWeapon(rack.transform, BlasterPath, new Vector3(0f, -.02f, .2f),
            Quaternion.LookRotation(Vector3.right, Vector3.up), 1f, "Blaster (Display)");
        var focus = new GameObject("Focus").transform;
        focus.SetParent(rack.transform, false);
        focus.localPosition = new Vector3(0f, 0f, .2f);
        locker.FocusPoint = focus;
        locker.Range = 2.7f;
        locker.Lamps = new[] { KP.Box("Status Lamp", rack.transform, s_M.Lamp, new Vector3(0f, .395f, .1f), new Vector3(.9f, .025f, .04f)) };
        locker.LampLight = KP.PointLight("Status Light", rack.transform, new Vector3(0f, .3f, .5f), 3f);
        locker.TakeSfx = K.SynthClip("TutorialLockerTake", SynthLockerTake);
        director.Locker = locker;
        Spot(parent, "Locker Spot", rackPosition + new Vector3(0f, 2.3f, -1.6f), rackPosition, 7f, 46f, 9f, new Color(.8f, .9f, 1f));
        Label(parent, "EMERGENCY ISSUE  /  OFFICER SIDEARM", new Vector3(spawnX, floor + .86f, north - .02f),
            Quaternion.identity, .055f, 1.4f, k_Cyan, s_F.Title);

        // Ship status screen above the rack: the story at a glance.
        WallScreen(parent, "Ship Status", new Vector3(spawnX, floor + 3.55f, north - .06f), Quaternion.identity, 3.4f, 1.5f,
            "<color=#FF5A3C>SHIP STATUS:  CORE ANOMALY</color>",
            "All crew cores reading HOSTILE.\nAutomated revival: EMERGENCY OFFICER.\nVitals: LOW.  Sidearm: IN LOCKER.");

        // Above the door, inside.
        Label(parent, "EMERGENCY OFFICER  /  QUARTERS", new Vector3(west + .03f, floor + 3.4f, -32.98f), Quaternion.Euler(0f, -90f, 0f),
            .16f, 4f, k_Amber, s_F.Title);

        // --- The quarters door, gate on the inside ---
        var door = KP.BuildDoor(new KP.DoorSpec
        {
            Name = "Officer Quarters Door", Guess = new Vector3(-249.23f, 0f, -32.98f), Along = Vector3.forward,
            Outward = Vector3.right, StartLocked = true,
        }, parent, s_M.Lamp);
        director.QuartersDoor = door;

        // --- Furniture: officer's desk on the west wall, storage along the south, shelving by the rack ---
        PropFit(parent, Modular + "Decorative elements/Tables/desk.prefab", new Vector3(west, floor, -28.4f), 90f, Vector3.right);
        PropFit(parent, Modular + "Decorative elements/pilot_seat.prefab", new Vector3(west + 2.9f, floor, -28.4f), 270f);
        PropFit(parent, Modular + "Decorative elements/cabinet.prefab", new Vector3(west, floor, north - 1.05f), 90f, Vector3.right);
        PropFit(parent, Modular + "Decorative elements/shelf.prefab", new Vector3(spawnX - 2.2f, floor, north), 180f, Vector3.back);
        PropFit(parent, Modular + "Decorative elements/shelf.prefab", new Vector3(spawnX + 2.2f, floor, north), 180f, Vector3.back);
        PropFit(parent, Warehouse + "Shelves/Shelf Variation 01.prefab", new Vector3(-245.2f, floor, south), 0f, Vector3.forward);
        PropFit(parent, Modular + "Machines/vending_machine.prefab", new Vector3(-239.4f, floor, south), 0f, Vector3.forward);
        PropFit(parent, Modular + "Machines/Battery.prefab", new Vector3(east - 1.25f, floor, south + 1.25f), 0f);
        PropFit(parent, Warehouse + "Misc Props/Fire Extinguisher.prefab", new Vector3(west, floor, -36.3f), 90f, Vector3.right);
        PropFit(parent, Warehouse + "Crates Barrels Pallets/Crate Short.prefab", new Vector3(east - 1.2f, floor, -36.4f), 15f);
        PropFit(parent, Warehouse + "Crates Barrels Pallets/Crate Short.prefab", new Vector3(east - 1.95f, floor, -37.1f), -10f);
        PropFit(parent, Warehouse + "Crates Barrels Pallets/Crate Short.prefab", new Vector3(east - 1.5f, floor + .6f, -36.8f), 40f);
        HazardStrip(parent, new Vector3(east - .95f, floor, spawnZ + 1f), new Vector2(2.1f, 3.6f), .14f);

        // --- Lighting: hung lamps, the custom caged lamps as emergency lighting, and the pod's glow ---
        HangingLamp(parent, new Vector3(-243.5f, floor + 4.6f, -29.5f), f.Ceiling, 13f, 9f, k_Warm);
        HangingLamp(parent, new Vector3(-237f, floor + 4.6f, -36.5f), f.Ceiling, 13f, 9f, k_Warm);
        foreach (var (position, yaw) in new[]
                 {
                     (new Vector3(east, floor + 2.6f, -36f), 270f), (new Vector3(west, floor + 2.6f, -37.5f), 90f),
                     (new Vector3(west, floor + 2.6f, -28f), 90f), (new Vector3(-238f, floor + 2.6f, south), 0f),
                 })
            CorridorLamp(parent, position, yaw, 4.5f, 1.4f);

        // --- Revive the player beside the pod, facing the rack ---
        Undo.RecordObject(player.transform, "Place player in the tutorial wing");
        player.transform.SetPositionAndRotation(new Vector3(spawnX, player.transform.position.y, spawnZ), Quaternion.identity);
        if (PrefabUtility.IsPartOfPrefabInstance(player.transform)) PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
    }

    static HibernationPod BuildPod(Transform parent, Vector3 position)
    {
        var root = new GameObject("Hibernation Pod").transform;
        root.SetParent(parent, false);
        root.position = position;
        var body = MeshObject("Pod Base", root, SaveMesh("SM_TW_PodBase", TutorialWingArt.PodBase),
            new[] { s_M.PodShell, s_M.PodTrim, s_M.PodGlow, s_M.PodPadding }, position, Quaternion.identity);
        body.AddComponent<MeshCollider>().sharedMesh = body.GetComponent<MeshFilter>().sharedMesh;
        MeshObject("Vitals Screen", root, SaveMesh("SM_TW_PodScreen", TutorialWingArt.PodScreen), new[] { s_M.PodScreen },
            position, Quaternion.identity);

        // The canopy's hinge, in the tilted body's frame.
        Quaternion tilt = Quaternion.Euler(-TutorialWingArt.PodTilt, 0f, 0f);
        var hinge = new GameObject("Canopy Hinge").transform;
        hinge.SetParent(root, false);
        hinge.localPosition = TutorialWingArt.PodBodyOrigin + tilt * TutorialWingArt.CanopyHinge;
        hinge.localRotation = tilt;
        var canopy = MeshObject("Canopy", hinge, SaveMesh("SM_TW_PodCanopy", TutorialWingArt.PodCanopy),
            new[] { s_M.PodGlass, s_M.PodTrim }, Vector3.zero, Quaternion.identity, local: true);
        canopy.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        var pod = root.gameObject.AddComponent<HibernationPod>();
        pod.Canopy = hinge;
        pod.OpenEuler = new Vector3(68f, 0f, 0f);
        pod.HissSfx = K.SynthClip("TutorialPodHiss", SynthPodHiss);
        pod.LatchSfx = K.SynthClip("TutorialPodLatch", SynthPodLatch);

        var light = new GameObject("Interior Light").AddComponent<Light>();
        light.transform.SetParent(root, false);
        light.transform.localPosition = TutorialWingArt.PodBodyOrigin + new Vector3(0f, .5f, 0f);
        light.type = LightType.Point;
        light.range = 3.4f;
        light.shadows = LightShadows.None;
        pod.InteriorLight = light;

        // Cold mist rolling out of the tub as it unseals.
        var mistMaterial = K.EnergyMaterial("M_TW_PodMist", 0f, .55f, 0f, new Color(.6f, .85f, 1f));
        var mist = K.NewParticles("Revival Mist", root, mistMaterial, 160);
        mist.transform.localPosition = TutorialWingArt.PodBodyOrigin + new Vector3(0f, .1f, 0f);
        var main = mist.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 2.5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.2f, .7f);
        main.startSize = new ParticleSystem.MinMaxCurve(.5f, 1.2f);
        main.startColor = new Color(.75f, .9f, 1f, .35f);
        main.gravityModifier = .03f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = mist.emission;
        emission.rateOverTime = 50f;
        var shape = mist.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(1f, .2f, 2.2f);
        K.GrowOut(mist, .6f, 1.8f);
        K.FadeOut(mist, new Color(.6f, .8f, 1f));
        pod.Mist = new[] { mist };
        return pod;
    }

    // ------------------------------------------------------------------ gallery

    class Exhibit
    {
        public string Title, Body, Tag;
        public Category Category;
        public System.Action<Transform, Vector3> Build;
    }

    // The gallery's first exhibit: both first-principles wall lamps on one backboard.
    static readonly Exhibit k_LampExhibit = new Exhibit
    {
        Title = "CAGED WALL LAMPS",
        Body = "Left: the jelly-jar fitting, with its red globe and wire guard. Right: the amber cage lamp that lights the corridors.",
        Tag = "FIRST-PRINCIPLES MESHES  /  CORRIDOR LAMP + WORKSHOP LIGHTING BUILDERS",
        Category = Category.Environment,
        Build = ExhibitLamp,
    };

    // South to north: environment, the shaders, then the weapons side by side nearest the drill.
    static Exhibit[] GalleryExhibits() => new[]
    {
        k_LampExhibit,
        new Exhibit { Title = "UNSTABLE CORE", Body = "Every enemy carries one. A swirling plasma that ignites when its armour gives way.", Tag = "CUSTOM HLSL SHADER  /  UNSTABLE ORB", Category = Category.Shader, Build = (t, p) => ShaderBall(t, p, "Assets/FPS/Art/Materials/CoreBuster/M_UnstableOrb.mat", .34f, 0f) },
        new Exhibit { Title = "GALAXY HUSK", Body = "The enemies' skin: a living nebula that drains dark as their core is torn out.", Tag = "CUSTOM HLSL SHADER  /  GALAXY HUSK", Category = Category.Shader, Build = (t, p) => ShaderBall(t, p, "Assets/FPS/Art/Materials/CyberSoldier/CyberSoldier_Galaxy.mat", .36f, 14f) },
        new Exhibit { Title = "PROCEDURAL NEBULA SKY", Body = "The ship's sky, generated on the GPU: nebula, core glow and twinkling stars. No textures.", Tag = "CUSTOM SKYBOX SHADER  /  ON A GLOBE", Category = Category.Shader, Build = ExhibitSkyGlobe },
        new Exhibit { Title = "CORE GAS", Body = "A spent core bursts into a lingering toxic nebula. Stay out of it.", Tag = "CUSTOM HLSL SHADER  /  GALAXY GAS", Category = Category.Shader, Build = (t, p) => ShaderBall(t, p, "Assets/FPS/Art/Materials/CoreBuster/M_GalaxyGas.mat", .42f, 6f) },
        new Exhibit { Title = "PISTON FIST", Body = "Hydraulic melee arm: winds up, slams the piston out and retracts. Built from primitives.", Tag = "FIRST-PRINCIPLES MESH  /  ANIMATED IN CODE", Category = Category.Weapon, Build = ExhibitPistonFist },
        new Exhibit { Title = "DISASSEMBLER", Body = "The hip blunderbuss: eleven bolts that electrocute and force armour hatches open.", Tag = "FIRST-PRINCIPLES MESH  /  ELECTRIC ARC SHADER", Category = Category.Weapon, Build = ExhibitDisassembler },
        new Exhibit { Title = "SCRAP SCATTERGUN", Body = "The hover-bots' shotgun: cocks its brass barrels, glows, then fans ten hot scrap fragments.", Tag = "FIRST-PRINCIPLES MESH  /  SCRAP SCATTERGUN BUILDER", Category = Category.Weapon, Build = ExhibitScattergun },
        new Exhibit { Title = "RIVET CANNON", Body = "The armoured soldiers' forearm cannon: tracks you, flashes its warning lamp, then fires heavy rivet bolts in bursts.", Tag = "FIRST-PRINCIPLES MESH  /  RIVET CANNON BUILDER", Category = Category.Weapon, Build = ExhibitRivetCannon },
    };

    // What BuildExhibitLine adds to the gallery, by name, so the line can be rebuilt on its own.
    static readonly string[] k_ExhibitLineParts = { "Stanchions", "Carpet Runner", "Light Track", "Gallery Fill" };

    static void BuildGallery(Transform parent, Frame f)
    {
        BuildExhibitLine(parent, f);

        float floor = f.Floor;
        var podiumMats = new[] { s_M.PodiumBody, s_M.PodiumTop, s_M.PodiumGlow };
        const float trackHeight = 4.6f;

        // --- The piston-fist sentry, a first-principles NPC, on a plinth at the gallery's south end ---
        var plinthCentre = new Vector3(-255.2f, floor, f.QuartersSouth + 2.3f);
        var plinth = MeshObject("NPC Plinth", parent, SaveMesh("SM_TW_Podium", TutorialWingArt.Podium), podiumMats, plinthCentre, Quaternion.identity);
        plinth.transform.localScale = new Vector3(2.1f, .35f, 2.1f);
        var plinthCollider = plinth.AddComponent<BoxCollider>();
        plinthCollider.center = new Vector3(0f, TutorialWingArt.PodiumHeight * .5f, 0f);
        plinthCollider.size = new Vector3(1.2f, TutorialWingArt.PodiumHeight, 1.2f);
        // Side-on to the walkway, fist toward the exhibit line, so its whole piston reads.
        DisplayNpc(parent, plinthCentre + Vector3.up * (TutorialWingArt.PodiumHeight * .35f), -45f);
        Plaque(parent, new Vector3(-252.6f, floor, f.QuartersSouth + 4.1f), Quaternion.Euler(0f, 40f, 0f), "PISTON-FIST SENTRY",
            "A bolted-down brawler: mount, forearm housing, sliding piston and fist. It winds up and slams anything in reach.",
            "FIRST-PRINCIPLES MESH  /  PISTON FIST BUILDER", Category.Npc);
        ShowcaseSpot(parent, new Vector3(-253.6f, floor + trackHeight + .4f, plinthCentre.z + 1.6f), plinthCentre + Vector3.up * 1.3f, 44f, 30f, f.Ceiling);

        // --- Menu shaders on a wall screen across from the line, north of the quarters door ---
        MenuShaderScreen(parent, new Vector3(f.GalleryEast - .1f, floor + 2.7f, -27.4f), Quaternion.Euler(0f, 90f, 0f));

        // --- Banner over the line ---
        Label(parent, "FABRICATION GALLERY", new Vector3(f.GalleryWest + .04f, floor + 6.3f, -24f), Quaternion.Euler(0f, -90f, 0f),
            .55f, 14f, k_Amber, s_F.Title);
        Label(parent, "Built aboard, from first principles: meshes, textures and shaders written in code",
            new Vector3(f.GalleryWest + .04f, floor + 5.6f, -24f), Quaternion.Euler(0f, -90f, 0f), .16f, 14f, new Color(.85f, .92f, 1f), s_F.Body);
    }

    // The exhibit line along the gallery's west wall: a podium, exhibit and plaque for each of GalleryExhibits, the
    // stanchions and velvet ropes, the carpet runner, and the light track with a spot over each showcase.
    static void BuildExhibitLine(Transform parent, Frame f)
    {
        float floor = f.Floor;
        float podiumX = f.GalleryWest + 1.3f;
        float ropeX = podiumX + 2.25f;
        float lecternX = podiumX + 1.35f;
        float top = floor + TutorialWingArt.PodiumHeight;

        var exhibits = GalleryExhibits();

        var podiumMesh = SaveMesh("SM_TW_Podium", TutorialWingArt.Podium);
        var podiumMats = new[] { s_M.PodiumBody, s_M.PodiumTop, s_M.PodiumGlow };
        // Nine showcases between the gallery's south end and the combat drill's hazard tape (z -11.65).
        const float firstZ = -36f, spacing = 2.75f;
        for (int i = 0; i < exhibits.Length; i++)
        {
            float z = firstZ + i * spacing;
            var group = new GameObject("Exhibit " + (i + 1) + " - " + exhibits[i].Title).transform;
            group.SetParent(parent, false);
            var podium = MeshObject("Podium", group, podiumMesh, podiumMats, new Vector3(podiumX, floor, z), Quaternion.identity);
            var collider = podium.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, .55f, 0f);
            collider.height = 1.1f;
            collider.radius = .62f;
            exhibits[i].Build(group, new Vector3(podiumX, top, z));
            Plaque(group, new Vector3(lecternX, floor, z), Quaternion.Euler(0f, 90f, 0f), exhibits[i].Title, exhibits[i].Body, exhibits[i].Tag,
                exhibits[i].Category);
        }

        // --- Stanchions and velvet ropes in front of the line, on a carpet runner ---
        var post = SaveMesh("SM_TW_Stanchion", TutorialWingArt.Stanchion);
        var rope = SaveMesh("SM_TW_VelvetRope", m => TutorialWingArt.Rope(m, spacing, .3f));
        var ropes = new GameObject("Stanchions").transform;
        ropes.SetParent(parent, false);
        float zStart = firstZ - spacing * .5f, zEnd = firstZ + (exhibits.Length - .5f) * spacing;
        for (float z = zStart; z <= zEnd + .01f; z += spacing)
        {
            var p = MeshObject("Stanchion", ropes, post, new[] { s_M.Chrome }, new Vector3(ropeX, floor, z), Quaternion.identity);
            var c = p.AddComponent<CapsuleCollider>();
            c.center = new Vector3(0f, .5f, 0f);
            c.height = 1.05f;
            c.radius = .12f;
            if (z + spacing <= zEnd + .01f)
                MeshObject("Velvet Rope", ropes, rope, new[] { s_M.Velvet, s_M.Brass }, new Vector3(ropeX, floor, z),
                    Quaternion.Euler(0f, -90f, 0f));
        }
        // An invisible barrier along the rope line keeps the exhibits out of reach of a jump.
        var barrier = new GameObject("Rope Barrier");
        barrier.transform.SetParent(ropes, false);
        barrier.transform.position = new Vector3(ropeX, floor + 1.2f, (zStart + zEnd) * .5f);
        barrier.AddComponent<BoxCollider>().size = new Vector3(.1f, 2.4f, zEnd - zStart);
        var carpet = GameObject.CreatePrimitive(PrimitiveType.Cube);
        carpet.name = "Carpet Runner";
        carpet.transform.SetParent(parent, false);
        carpet.transform.position = new Vector3(ropeX + 1.3f, floor + .006f, (zStart + zEnd) * .5f);
        carpet.transform.localScale = new Vector3(2.2f, .012f, zEnd - zStart + 1f);
        Object.DestroyImmediate(carpet.GetComponent<Collider>());
        carpet.GetComponent<Renderer>().sharedMaterial = s_M.Carpet;

        // --- Gallery lighting: a track straight over the line, a spot and its beam above each showcase, plus fill ---
        const float trackHeight = 4.6f;
        float trackX = podiumX + .45f;
        var rail = MeshObject("Light Track", parent, SaveMesh("SM_TW_Rail", m => TutorialWingArt.Rail(m, zEnd - zStart + 2f, f.Ceiling - (floor + trackHeight))),
            new[] { s_M.Rail }, new Vector3(trackX, floor + trackHeight, zStart - 1f), Quaternion.identity);
        rail.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        for (int i = 0; i < exhibits.Length; i++)
        {
            float z = firstZ + i * spacing;
            ShowcaseSpot(rail.transform, new Vector3(trackX, floor + trackHeight - .05f, z), new Vector3(podiumX, top + .3f, z), 38f, 22f);
        }
        // Two high fills over the walkway; few, so the big floor tiles keep light slots for the showcase spots.
        foreach (float z in new[] { -31f, -19f })
            Point(parent, "Gallery Fill", new Vector3(ropeX + 4.5f, floor + 6.5f, z), 16f, 14f, new Color(.8f, .88f, 1f));
    }

    /// <summary>
    /// Rebuilds only the gallery's exhibit line (podiums, exhibits, plaques, ropes, carpet and showcase lights) in the
    /// open MainScene, from GalleryExhibits, leaving the rest of the wing as it is. Its materials are loaded as they are.
    /// </summary>
    [MenuItem("Tools/CoreBuster/Rebuild Gallery Exhibit Line")]
    public static void RebuildExhibitLine()
    {
        var gallery = GameObject.Find(RootName + "/Fabrication Gallery");
        if (!gallery)
        {
            Debug.LogError("Tutorial wing: no Fabrication Gallery to rebuild; run Build Tutorial Wing.");
            return;
        }
        s_F = new Fonts
        {
            Title = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath),
            Body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath),
        };
        s_M = LoadExhibitMaterials();
        s_Meshes.Clear();
        foreach (var child in gallery.transform.Cast<Transform>().ToList())
            if (child.name.StartsWith("Exhibit ") || k_ExhibitLineParts.Contains(child.name))
                Object.DestroyImmediate(child.gameObject);
        Physics.SyncTransforms();
        BuildExhibitLine(gallery.transform, Measure());
        EditorSceneManager.MarkSceneDirty(gallery.scene);
        Debug.Log($"Tutorial wing: gallery exhibit line rebuilt ({GalleryExhibits().Length} exhibits).");
    }

    static void ExhibitLamp(Transform parent, Vector3 top)
    {
        // Both lamps side by side on a short backboard, facing up the walkway.
        var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.name = "Backboard";
        board.transform.SetParent(parent, false);
        board.transform.position = top + new Vector3(-.2f, .45f, 0f);
        board.transform.localScale = new Vector3(.06f, .9f, 1.1f);
        board.GetComponent<Renderer>().sharedMaterial = s_M.PodiumBody;
        Object.DestroyImmediate(board.GetComponent<Collider>());
        Prop(parent, LampPrefab, top + new Vector3(-.17f, .6f, -.28f), 90f);
        Point(parent, "Lamp Glow", top + new Vector3(.1f, .55f, -.28f), 2.2f, 1.2f, k_Red);
        // The cage lamp's own spots would take light slots from the gallery's showcase spots: a small amber glow stands
        // in for them, as the red one does for the jelly-jar lamp.
        var cage = Prop(parent, CorridorLockdownBuilder.CageLampPath(), top + new Vector3(-.17f, .6f, .28f), 90f);
        if (cage)
            foreach (var light in cage.GetComponentsInChildren<Light>(true))
                light.enabled = false;
        Point(parent, "Cage Lamp Glow", top + new Vector3(.1f, .55f, .28f), 2.2f, 1.2f, k_Amber);
    }

    /// <summary>
    /// Rebuilds only the gallery's lamp exhibit (its podium stays) in the open MainScene, so a change to it does not
    /// need the whole wing rebuilt.
    /// </summary>
    // The finishes the gallery's exhibit line uses, loaded as they are. BuildMaterials would rewrite every M_TW_* material,
    // undoing hand tuning (M_TW_PodGlass's blend) that a full wing rebuild is expected to reset but this is not.
    static Mats LoadExhibitMaterials()
    {
        Material Load(string name) => AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + name + ".mat");
        var m = new Mats
        {
            PodiumBody = Load("M_TW_PodiumBody"),
            PodiumTop = Load("M_TW_PodiumTop"),
            PodiumGlow = Load("M_TW_PodiumGlow"),
            Plaque = Load("M_TW_PlaqueBody"),
            PlaqueFace = Load("M_TW_PlaqueFace"),
            PlaqueGlow = Load("M_TW_PlaqueGlow"),
            Chrome = Load("M_TW_Chrome"),
            Velvet = Load("M_TW_Velvet"),
            Brass = Load("M_TW_Brass"),
            Carpet = Load("M_TW_Carpet"),
            Rail = Load("M_TW_Rail"),
            RackFrame = Load("M_TW_RackFrame"),
            Occluder = Load("M_TW_GlobeOccluder"),
            Nebula = Load("M_TW_NebulaGlobe"),
            LightCone = Load("M_TW_LightCone"),
        };
        m.CategoryGlow = Enumerable.Range(0, k_CategoryNames.Length)
            .Select(i => (Category)i == Category.Weapon ? m.PlaqueGlow : Load("M_TW_PlaqueGlow_" + (Category)i)).ToArray();
        return m;
    }

    [MenuItem("Tools/CoreBuster/Rebuild Tutorial Lamp Exhibit")]
    public static void RebuildLampExhibit()
    {
        var gallery = GameObject.Find(RootName + "/Fabrication Gallery");
        var group = gallery ? gallery.transform.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith("Exhibit 1 - ")) : null;
        var podium = group ? group.Find("Podium") : null;
        if (!podium)
        {
            Debug.LogError("Tutorial wing: no gallery lamp exhibit (Exhibit 1) to rebuild; run Build Tutorial Wing.");
            return;
        }
        s_F = new Fonts
        {
            Title = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath),
            Body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath),
        };
        s_M = LoadExhibitMaterials();
        for (int i = group.childCount - 1; i >= 0; i--)
            if (group.GetChild(i) != podium) Object.DestroyImmediate(group.GetChild(i).gameObject);

        Vector3 p = podium.position;
        k_LampExhibit.Build(group, new Vector3(p.x, p.y + TutorialWingArt.PodiumHeight, p.z));
        Plaque(group, new Vector3(p.x + 1.35f, p.y, p.z), Quaternion.Euler(0f, 90f, 0f), k_LampExhibit.Title, k_LampExhibit.Body,
            k_LampExhibit.Tag, k_LampExhibit.Category);
        group.name = "Exhibit 1 - " + k_LampExhibit.Title;
        EditorSceneManager.MarkSceneDirty(group.gameObject.scene);
        Debug.Log("Tutorial wing: lamp exhibit rebuilt.");
    }

    static void ExhibitSkyGlobe(Transform parent, Vector3 top)
    {
        // The skybox shader maps object-space direction to the sky; drawn after opaques, the inner occluder hides the far side.
        var globe = Ball(parent, "Nebula Globe", top + Vector3.up * .5f, .4f, s_M.Nebula);
        var inner = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        inner.name = "Occluder";
        Object.DestroyImmediate(inner.GetComponent<Collider>());
        inner.transform.SetParent(globe.transform, false);
        inner.transform.localScale = Vector3.one * .97f;
        inner.GetComponent<Renderer>().sharedMaterial = s_M.Occluder;
        Spin(globe, 8f);
        Cradle(parent, top, .3f);
    }

    static void ExhibitPistonFist(Transform parent, Vector3 top)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyFolder + "Enemy_Robot1_PistonFist.prefab");
        var arm = source ? source.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("Robot1 Piston Fist Arm")) : null;
        if (!arm)
        {
            Debug.LogWarning("Tutorial wing: the Robot1 piston fist arm wasn't found.");
            return;
        }
        var copy = Object.Instantiate(arm.gameObject, parent);
        copy.name = "Piston Fist (Display)";
        StripGameplay(copy);
        copy.transform.rotation = Quaternion.Euler(0f, 90f, -70f);
        // Shown about 0.8 m long, whatever scale the arm had on the rig.
        var bounds = KP.WorldBounds(copy);
        copy.transform.localScale *= .8f / Mathf.Max(.01f, Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z));
        bounds = KP.WorldBounds(copy);
        copy.transform.position += top + Vector3.up * .3f - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        Cradle(parent, top, .22f);
    }

    static void ExhibitDisassembler(Transform parent, Vector3 top)
    {
        var gun = DisplayWeapon(parent, DisassemblerGunPath, Vector3.zero, Quaternion.identity, 1.6f, "Disassembler (Display)");
        if (gun)
        {
            gun.transform.rotation = Quaternion.Euler(-12f, 180f, 0f);
            var bounds = KP.WorldBounds(gun);
            gun.transform.position += top + new Vector3(0f, .62f, 0f) - bounds.center;
        }
        // Electrodes either side with the Electric Arc shader jumping between them.
        var posts = MeshObject("Tesla Posts", parent, SaveMesh("SM_TW_TeslaPosts", m => TutorialWingArt.TeslaPosts(m, .9f)),
            new[] { s_M.Rail, s_M.Chrome }, top, Quaternion.Euler(0f, 0f, 0f));
        var arcMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/FPS/Art/Materials/CoreBuster/M_Lightning.mat");
        for (int i = 0; i < 3; i++)
        {
            var arc = new GameObject("Arc " + (i + 1)).AddComponent<LineRenderer>();
            arc.transform.SetParent(parent, false);
            arc.useWorldSpace = true;
            arc.sharedMaterial = arcMaterial;
            arc.widthMultiplier = .07f - i * .015f;
            arc.shadowCastingMode = ShadowCastingMode.Off;
            arc.textureMode = LineTextureMode.Stretch;
            var points = new List<Vector3>();
            for (int k = 0; k <= 8; k++)
            {
                float t = k / 8f;
                points.Add(top + new Vector3(0f, .37f + Mathf.Sin(t * Mathf.PI) * (.12f + i * .1f), Mathf.Lerp(-.45f, .45f, t)));
            }
            arc.positionCount = points.Count;
            arc.SetPositions(points.ToArray());
        }
        posts.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        Point(parent, "Arc Glow", top + new Vector3(.2f, .5f, 0f), 2.5f, 1.5f, k_Cyan);
    }

    static void ExhibitRivetCannon(Transform parent, Vector3 top)
    {
        var gun = DisplayWeapon(parent, RivetCannonPath, Vector3.zero, Quaternion.identity, 1f, "Rivet Cannon (Display)");
        if (!gun) return;
        // Broadside to the walkway, muzzle tipped up a little, about 0.95 m long whatever the prefab's scale.
        gun.transform.rotation = Quaternion.Euler(-10f, 180f, 0f);
        var bounds = KP.WorldBounds(gun);
        gun.transform.localScale *= .95f / Mathf.Max(.01f, Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z));
        bounds = KP.WorldBounds(gun);
        gun.transform.position += top + new Vector3(0f, .14f + bounds.extents.y, 0f) - bounds.center;
        Cradle(parent, top, .3f);
    }

    static void ExhibitScattergun(Transform parent, Vector3 top)
    {
        var gun = DisplayWeapon(parent, ScattergunPath, Vector3.zero, Quaternion.identity, 1f, "Scrap Scattergun (Display)");
        if (!gun) return;
        // Broadside to the walkway, muzzle tipped up a little, about 1 m long whatever the prefab's scale.
        gun.transform.rotation = Quaternion.Euler(-10f, 180f, 0f);
        var bounds = KP.WorldBounds(gun);
        gun.transform.localScale *= 1.05f / Mathf.Max(.01f, Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z));
        bounds = KP.WorldBounds(gun);
        gun.transform.position += top + new Vector3(0f, .14f + bounds.extents.y, 0f) - bounds.center;
        Cradle(parent, top, .3f);
    }

    // A spot on the track over a showcase, its can and a faint beam down to what it lights.
    // Off the track, give the ceiling's height to hang the can on a rod.
    static void ShowcaseSpot(Transform parent, Vector3 from, Vector3 target, float angle, float intensity, float ceiling = 0f)
    {
        var head = SaveMesh("SM_TW_SpotHead", TutorialWingArt.SpotHead);
        var can = MeshObject("Spot Head", parent, head, new[] { s_M.Rail }, from, Quaternion.LookRotation(target - from));
        can.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        if (ceiling > from.y)
        {
            var rod = MeshObject("Spot Rod", parent, SaveMesh("SM_TW_LampRod", m => m.Cylinder(Vector3.zero, Vector3.up, .015f, 8)),
                new[] { s_M.Rail }, from, Quaternion.identity);
            rod.transform.localScale = new Vector3(1f, ceiling - from.y, 1f);
        }
        Vector3 lens = from + (target - from).normalized * .2f + Vector3.down * .1f;
        var spot = Spot(can.transform, "Exhibit Spot", lens, target, Vector3.Distance(lens, target) + 3f, angle, intensity, k_Warm);
        // Important: within the pipeline's four additional lights per object, the exhibit's own spot comes first.
        spot.renderMode = LightRenderMode.ForcePixel;
        // The beam: a cone from the lens to the target, just inside the spot's cone.
        float length = Vector3.Distance(lens, target) - .1f;
        var beam = MeshObject("Beam", parent, SaveMesh("SM_TW_LightCone", TutorialWingArt.LightCone), new[] { s_M.LightCone },
            lens, Quaternion.FromToRotation(Vector3.down, (target - lens).normalized));
        float radius = Mathf.Tan(angle * .5f * .9f * Mathf.Deg2Rad) * length;
        beam.transform.localScale = new Vector3(radius, length, radius);
        beam.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        beam.GetComponent<MeshRenderer>().receiveShadows = false;
    }

    // The piston-fist sentry (Enemy_PistonFist, built from primitives by PistonFistBuilder) as a visual-only display.
    static void DisplayNpc(Transform parent, Vector3 standOn, float yaw)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PistonFistBuilder.PrefabPath);
        if (!prefab)
        {
            Debug.LogWarning("Tutorial wing: no " + PistonFistBuilder.PrefabPath + "; run Tools/CoreBuster/Create Piston Fist Prefab.");
            return;
        }
        var npc = Object.Instantiate(prefab, parent);
        npc.name = "Piston-Fist Sentry (Display)";
        StripGameplay(npc);
        foreach (var agent in npc.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) Object.DestroyImmediate(agent);
        npc.transform.SetPositionAndRotation(standOn, Quaternion.Euler(0f, yaw, 0f));
        npc.transform.localScale = Vector3.one * .85f;
        var bounds = KP.WorldBounds(npc);
        npc.transform.position += new Vector3(standOn.x - bounds.center.x, standOn.y - bounds.min.y, standOn.z - bounds.center.z);
    }

    static GameObject ShaderBall(Transform parent, Vector3 top, string materialPath, float radius, float spin)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!material) Debug.LogWarning("Tutorial wing: missing exhibit material " + materialPath);
        var ball = Ball(parent, material ? material.name + " Ball" : "Shader Ball", top + Vector3.up * (radius + .12f), radius, material);
        if (spin > 0f) Spin(ball, spin);
        Cradle(parent, top, radius * .7f);
        return ball;
    }

    static GameObject Ball(Transform parent, string name, Vector3 position, float radius, Material material)
    {
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = name;
        Object.DestroyImmediate(ball.GetComponent<Collider>());
        ball.transform.SetParent(parent, false);
        ball.transform.position = position;
        ball.transform.localScale = Vector3.one * radius * 2f;
        ball.GetComponent<Renderer>().sharedMaterial = material;
        return ball;
    }

    // A slim ring cradle under a displayed object.
    static void Cradle(Transform parent, Vector3 top, float radius)
    {
        var mesh = SaveMesh("SM_TW_Cradle", m =>
        {
            m.Lathe(new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, .05f), new Vector2(.86f, .1f), new Vector2(.82f, .1f), new Vector2(.82f, .07f), new Vector2(0f, .07f) }, 24, Vector3.zero);
        });
        var cradle = MeshObject("Cradle", parent, mesh, new[] { s_M.Chrome }, top, Quaternion.identity);
        cradle.transform.localScale = new Vector3(radius, 1f, radius);
    }

    static void Spin(GameObject go, float degreesPerSecond)
    {
        var rotation = go.AddComponent<ConstantRotation>();
        rotation.RotatingSpeed = degreesPerSecond;
    }

    static void MenuShaderScreen(Transform parent, Vector3 position, Quaternion rotation)
    {
        const float width = 3.6f, height = 2.025f;
        var group = new GameObject("Menu Shader Screen").transform;
        group.SetParent(parent, false);
        group.SetPositionAndRotation(position, rotation);
        // Bezel.
        var bezel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bezel.name = "Bezel";
        bezel.transform.SetParent(group, false);
        bezel.transform.localPosition = new Vector3(0f, 0f, .04f);
        bezel.transform.localScale = new Vector3(width + .2f, height + .2f, .08f);
        bezel.GetComponent<Renderer>().sharedMaterial = s_M.RackFrame;
        Object.DestroyImmediate(bezel.GetComponent<Collider>());

        // A world-space canvas laid out like the main menu: the galaxy background and the logo in its core glow.
        var canvasObject = new GameObject("Screen", typeof(RectTransform));
        canvasObject.transform.SetParent(group, false);
        canvasObject.transform.localPosition = new Vector3(0f, 0f, -.005f);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rect = (RectTransform)canvasObject.transform;
        rect.sizeDelta = new Vector2(1600f, 900f);
        rect.localScale = Vector3.one * (width / 1600f);
        var galaxy = new GameObject("Galaxy", typeof(RectTransform)).AddComponent<RawImage>();
        galaxy.transform.SetParent(rect, false);
        var galaxyRect = galaxy.rectTransform;
        galaxyRect.anchorMin = Vector2.zero;
        galaxyRect.anchorMax = Vector2.one;
        galaxyRect.offsetMin = galaxyRect.offsetMax = Vector2.zero;
        galaxy.material = AssetDatabase.LoadAssetAtPath<Material>("Assets/FPS/Art/Materials/UI/M_UIGalaxy.mat");
        galaxy.raycastTarget = false;

        var logoRoot = new GameObject("Core Logo", typeof(RectTransform)).GetComponent<RectTransform>();
        logoRoot.SetParent(rect, false);
        logoRoot.sizeDelta = new Vector2(420f, 420f);
        logoRoot.anchoredPosition = new Vector2(0f, 60f);
        var glow = new GameObject("Glow", typeof(RectTransform)).AddComponent<Image>();
        glow.transform.SetParent(logoRoot, false);
        glow.rectTransform.sizeDelta = new Vector2(420f * 2.3f, 420f * 2.3f);
        glow.material = AssetDatabase.LoadAssetAtPath<Material>("Assets/FPS/Art/Materials/UI/M_UICoreGlow.mat");
        glow.raycastTarget = false;
        var logo = new GameObject("Logo", typeof(RectTransform)).AddComponent<Image>();
        logo.transform.SetParent(logoRoot, false);
        logo.rectTransform.sizeDelta = new Vector2(420f, 420f);
        logo.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
        logo.preserveAspect = true;
        logo.raycastTarget = false;
        var animator = logoRoot.gameObject.AddComponent<CoreLogoGlow>();
        animator.Glow = glow;
        animator.Logo = logo.rectTransform;

        var caption = new GameObject("Caption", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        caption.transform.SetParent(rect, false);
        caption.rectTransform.anchoredPosition = new Vector2(0f, -330f);
        caption.rectTransform.sizeDelta = new Vector2(1500f, 160f);
        caption.font = s_F.Title;
        caption.fontSize = 64f;
        caption.characterSpacing = 6f;
        caption.alignment = TextAlignmentOptions.Center;
        caption.text = "<color=#" + ColorUtility.ToHtmlStringRGB(k_CategoryColors[(int)Category.Shader]) + ">SHADER</color>   " +
                       "MENU SHADERS  <color=#7FD8FF>UI GALAXY  +  UI CORE GLOW</color>";
        caption.raycastTarget = false;

        Point(group, "Screen Glow", position + rotation * new Vector3(0f, 0f, -1.2f), 5f, 1.4f, new Color(.55f, .45f, 1f));
    }

    // ------------------------------------------------------------------ drill

    static void BuildDrill(Transform parent, Frame f, TutorialDirector director)
    {
        float floor = f.Floor;
        // Well clear of the north wall: an executed target doubles over and falls backwards.
        float z = f.HallNorth - 4.5f;
        var robot = Dummy(parent, "Enemy_Robot1_PistonFist", new Vector3(-255.2f, floor, z));
        var armoured = Dummy(parent, "Enemy_CyberSoldier_PistonFist", new Vector3(-259.2f, floor, z));
        var elite = Dummy(parent, "Enemy_Elite_PistonFist", new Vector3(-251.2f, floor, z));
        director.ShowcaseRobot = robot ? robot.GetComponent<Health>() : null;
        director.Armoured = armoured ? armoured.GetComponent<CoreExecutionTarget>() : null;
        director.Elite = elite ? elite.GetComponent<CoreExecutionTarget>() : null;
        // Two executions bring the revived officer back to full health.
        SetReward(director.Armoured, 35f);
        SetReward(director.Elite, 40f);

        var drillPoint = new GameObject("Drill Point").transform;
        drillPoint.SetParent(parent, false);
        drillPoint.position = new Vector3(-255.2f, floor, z - 1.5f);
        director.DrillPoint = drillPoint;
        director.DrillRadius = 12.5f;

        // Holograms over each target: the armour lesson is told here, in the environment, not on the HUD.
        // Holograms floating just behind and above each target.
        float signZ = z + .8f;
        var robotSign = HoloSign(parent, "Hologram - Drone", new Vector3(-255.2f, floor + 3.5f, signZ), "PISTON-FIST DRONE",
            "Unarmoured melee chassis. Its hydraulic fist hits hard up close.\n<size=70%><color=#9FB8C8>DISPLAY UNIT  /  SHIELDED</color></size>", k_Cyan);
        var armourSign = HoloSign(parent, "Hologram - Armoured", new Vector3(-259.2f, floor + 3.5f, signZ), "ARMOURED CHASSIS",
            "Plating soaks up gunfire.\nSustained fire cracks it and exposes the core.", k_Amber);
        var eliteSign = HoloSign(parent, "Hologram - Elite", new Vector3(-251.2f, floor + 3.5f, signZ), "ELITE CHASSIS",
            "Heavy plating shrugs off small arms.\nA Disassembler shock forces its chest hatch open.", k_Red);

        foreach (float x in new[] { -259.2f, -255.2f, -251.2f })
            Spot(parent, "Target Spot", new Vector3(x, floor + 5.2f, z - 3.2f), new Vector3(x, floor + 1.1f, z), 8f, 38f,
                Mathf.Approximately(x, -255.2f) ? 2.5f : 8f, k_Warm);

        // The firing line: hazard stripes across the hall and a floor stencil.
        float lineZ = z - 7.2f;
        HazardStrip(parent, new Vector3((f.GalleryWest + f.GalleryEast) * .5f, floor, lineZ), new Vector2(f.GalleryEast - f.GalleryWest - .2f, .32f), .32f, true);
        var stencil = Label(parent, "LIVE-FIRE DRILL", new Vector3((f.GalleryWest + f.GalleryEast) * .5f, floor + .012f, lineZ + 1f),
            Quaternion.Euler(90f, 0f, 0f), .5f, 8f, new Color(.95f, .75f, .2f, .85f), s_F.Title);
        stencil.name = "Drill Stencil";

        director.Reveals = new[]
        {
            new TutorialDirector.Reveal { From = TutorialDirector.Step.Drill, Objects = new[] { armourSign, eliteSign } },
        };
        robotSign.SetActive(true);
    }

    static GameObject Dummy(Transform parent, string prefabName, Vector3 position)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyFolder + prefabName + ".prefab");
        if (!prefab)
        {
            Debug.LogError("Tutorial wing: missing enemy prefab " + prefabName);
            return null;
        }
        var enemy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        enemy.name = "Showcase " + prefabName.Replace("Enemy_", "");
        enemy.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 180f, 0f));
        enemy.AddComponent<ShowcaseDummy>();
        return enemy;
    }

    static void SetReward(CoreExecutionTarget target, float reward)
    {
        if (!target) return;
        target.HealthReward = reward;
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }

    // ------------------------------------------------------------------ exit

    static void BuildExit(Transform parent, Frame f, TutorialDirector director, KeycardData card)
    {
        float floor = f.Floor;
        var denied = K.FindClip("KeycardDenied");
        var door = KP.BuildDoor(new KP.DoorSpec
        {
            Name = "Tutorial Wing Exit Door", Guess = new Vector3(-230.97f, 0f, -14.95f), Along = Vector3.forward,
            Outward = Vector3.left, StartLocked = true,
        }, parent, s_M.Lamp);
        director.ExitDoor = door;
        float doorZ = door.transform.position.z;

        // The reader on the wall just right of the gate (south of it, seen from the hall).
        var reader = KP.CreateAccessMachine(new Vector3(f.QuartersEast, floor + 1.3f, doorZ - 2.5f), Vector3.left, card,
            new[] { door }, parent, s_M.Lamp, denied);
        reader.name = "Access Machine (BLACK KEYCARD) - Exit";
        director.ExitReader = reader;

        // The clearance terminal further right, against the wall, lit to catch the eye.
        var console = KP.PlaceAgainstWall(ConsolePath, "Clearance Terminal", new Vector3(f.QuartersEast - 1.5f, floor, doorZ - 6.2f),
            Vector3.left, parent, .05f);
        var dispenser = console.AddComponent<KeycardDispenser>();
        var bounds = KP.WorldBounds(console);
        Vector3 above = bounds.center + Vector3.left * .12f + Vector3.up * (bounds.extents.y + .5f);
        Vector3 slotPosition = Physics.Raycast(above, Vector3.down, out RaycastHit hit, bounds.size.y + 1f, ~0,
            QueryTriggerInteraction.Ignore) ? hit.point : new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        var slot = new GameObject("Eject Slot").transform;
        slot.SetParent(console.transform, true);
        slot.SetPositionAndRotation(slotPosition, Quaternion.LookRotation(Vector3.up, Vector3.right));
        KP.Box("Drive Bezel", slot, KP.BezelMaterial(), new Vector3(0f, 0f, -.005f), new Vector3(.2f, .03f, .02f));
        dispenser.Card = card;
        dispenser.EjectSlot = slot;
        dispenser.FocusPoint = slot;
        dispenser.Range = 2.6f;
        dispenser.DeniedSfx = denied;
        dispenser.AcceptSfx = K.FindClip("KeycardAccepted");
        dispenser.EjectSfx = K.FindClip("DiskEject");
        dispenser.Lamps = new[]
        {
            KP.Box("Status Lamp", console.transform, s_M.Lamp,
                console.transform.InverseTransformPoint(new Vector3(bounds.max.x - .06f, bounds.max.y + .03f, bounds.center.z)), new Vector3(.6f, .04f, .04f)),
        };
        dispenser.LampLight = KP.PointLight("Status Light", console.transform,
            console.transform.InverseTransformPoint(bounds.center + Vector3.up * (bounds.extents.y + .5f) + Vector3.left * .4f), 6f);
        director.Terminal = dispenser;

        // Its holographic readout, one panel per state.
        Vector3 screenAt = new Vector3(f.QuartersEast - .05f, floor + 2.75f, console.transform.position.z);
        Quaternion screenFacing = Quaternion.Euler(0f, 90f, 0f);
        dispenser.StateScreens = new[]
        {
            HoloSign(parent, "Terminal - Standby", screenAt, "OFFICER CLEARANCE", "STANDBY\n<size=70%>Complete the combat drill</size>", k_Amber, screenFacing, 2.1f),
            HoloSign(parent, "Terminal - Ready", screenAt, "OFFICER CLEARANCE", "CLEARANCE READY\n<size=70%>Access the terminal</size>", k_Card, screenFacing, 2.1f),
            HoloSign(parent, "Terminal - Issuing", screenAt, "OFFICER CLEARANCE", "KEYCARD ISSUED\n<size=70%>Take your card</size>", k_Card, screenFacing, 2.1f),
            HoloSign(parent, "Terminal - Issued", screenAt, "OFFICER CLEARANCE", "CLEARANCE GRANTED\n<size=70%>Tap the card on the door's reader</size>", new Color(.3f, 1f, .5f), screenFacing, 2.1f),
        };

        // Revealed when the drill is done: a beacon on the terminal and a guide line from the targets to it.
        var beacon = Spot(parent, "Terminal Beacon", new Vector3(f.QuartersEast - 2.4f, floor + 4.2f, console.transform.position.z),
            console.transform.position + Vector3.up * 1f, 7f, 36f, 14f, k_Card).gameObject;
        var guide = MeshObject("Guide Line", parent, SaveMesh("SM_TW_GuideLine", m => TutorialWingArt.FloorDashes(m, new[]
            {
                new Vector3(-251.2f, 0f, f.HallNorth - 7f), new Vector3(-240f, 0f, f.HallNorth - 7f),
                new Vector3(-234.5f, 0f, console.transform.position.z),
            }, .55f, .45f, .14f)), new[] { s_M.GuideGlow }, new Vector3(0f, floor, 0f), Quaternion.identity);
        guide.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        var reveals = new List<TutorialDirector.Reveal>(director.Reveals ?? new TutorialDirector.Reveal[0])
        {
            new TutorialDirector.Reveal { From = TutorialDirector.Step.Clearance, Objects = new[] { beacon, guide } },
        };
        director.Reveals = reveals.ToArray();

        Label(parent, "EXIT  /  CORRIDOR TO THE WORKSHOP", new Vector3(f.QuartersEast - .04f, floor + 4.1f, doorZ), Quaternion.Euler(0f, 90f, 0f),
            .2f, 5f, k_Amber, s_F.Title);
        HazardStrip(parent, new Vector3(f.QuartersEast - 1.6f, floor, doorZ), new Vector2(.32f, 4.2f), .32f, true);

        // North-east corner: storage and a hanging lamp over the exit approach.
        PropFit(parent, Warehouse + "Shelves/Shelf Variation 02.prefab", new Vector3(-243.5f, floor, f.HallNorth), 180f, Vector3.back);
        PropFit(parent, TankPrefab, new Vector3(f.QuartersEast - 1.6f, floor, f.HallNorth - 1.8f), 0f);
        HangingLamp(parent, new Vector3(-238.5f, floor + 4.8f, -11f), f.Ceiling, 12f, 10f, k_Warm);
        HangingLamp(parent, new Vector3(-238.5f, floor + 4.8f, -20.5f), f.Ceiling, 12f, 8f, k_Warm);
        CorridorLamp(parent, new Vector3(f.QuartersEast, floor + 2.6f, doorZ + 3.2f), 270f, 4.5f, 1.2f);
        CorridorLamp(parent, new Vector3(-245f, floor + 2.6f, f.HallNorth), 180f, 4.5f, 1.2f);
    }

    static void BuildWorkshopAccess(Transform parent, KeycardData card)
    {
        var door = Object.FindObjectsByType<SecurityDoor>(FindObjectsInactive.Include)
            .FirstOrDefault(d => d.name == WorkshopDoorName);
        if (!door)
        {
            Debug.LogWarning("Tutorial wing: no " + WorkshopDoorName + "; run Tools/CoreBuster/Install Keycard Progression first.");
            return;
        }
        // The Workshop was the old start; now it is sealed until the officer brings the black keycard.
        var serialized = new SerializedObject(door);
        serialized.FindProperty(nameof(SecurityDoor.StartLocked)).boolValue = true;
        serialized.ApplyModifiedProperties();
        var machine = KP.BuildAccessPoint(door, card, new[] { door }, parent, s_M.Lamp, K.FindClip("KeycardDenied"));
        machine.name = "Access Machine (BLACK KEYCARD) - Workshop";
    }

    // ------------------------------------------------------------------ dressing

    // Kit props on the walls and floors that were bare: furniture, wall fixtures, ducts and clutter, plus one hall fill.
    // Modular-pack props face their local +Z, the warehouse wall fixtures their local +X.
    static void BuildDressing(Transform parent, Frame f)
    {
        float floor = f.Floor;
        float east = f.QuartersEast, west = f.QuartersWest, south = f.QuartersSouth, north = f.HallNorth;
        // The hall side of the quarters' north wall.
        float quartersOuter = Physics.Raycast(new Vector3(-240f, 1.2f, -20f), Vector3.back, out RaycastHit hit, 10f, ~0,
            QueryTriggerInteraction.Ignore) ? hit.point.z : f.QuartersNorth + .3f;
        string deco = Modular + "Decorative elements/";

        // --- Quarters: the officer's bed, a sitting corner on a rug, a skirting trim and wall lights ---
        var quarters = Section(parent, "Quarters");
        PropFit(quarters, deco + "bed.prefab", new Vector3(west + 1.13f, floor, south), 0f, Vector3.forward);
        Rug(quarters, new Vector3(-242.6f, floor, -37f), new Vector2(3.6f, 2.4f));
        PropFit(quarters, deco + "Tables/decorative_table_small_glass.prefab", new Vector3(-242.6f, floor, -37f), 0f);
        PropFit(quarters, deco + "decorative_chair.prefab", new Vector3(-244.3f, floor, -37f), 90f);
        PropFit(quarters, deco + "decorative_chair.prefab", new Vector3(-240.9f, floor, -37f), 270f);
        PropFit(quarters, deco + "floor_corner_ornament.prefab", new Vector3(east, floor, -26.4f), YawZ(Vector3.left), Vector3.left);
        WallLight(quarters, new Vector3(east, floor + 2.4f, -26.4f), Vector3.left);
        WallLight(quarters, new Vector3(west, floor + 2.9f, -28.4f), Vector3.right);

        // --- Gallery: a notice board by the quarters door, a bench facing the exhibits and a crate in the corner ---
        var gallery = Section(parent, "Gallery");
        PropFit(gallery, deco + "bulletin_board_big.prefab", new Vector3(f.GalleryEast, floor, -38.6f), YawZ(Vector3.left), Vector3.left);
        PropFit(gallery, deco + "decorative_sofa.prefab", new Vector3(-250.9f, floor, -20.2f), YawZ(Vector3.left));
        PropFit(gallery, Modular + "Machines/container_small.prefab", new Vector3(f.GalleryWest + .9f, floor, south + .9f), 0f);
        // Ducting high on the west wall, above the windows and the banner.
        for (float z = -35.9f; z < north - 5f; z += 6f)
            Prop(gallery, Warehouse + "Ducts/Duct Straight.prefab", new Vector3(f.GalleryWest + .42f, floor + 7.5f, z), 0f);

        // --- North hall: braced panels behind the drill, ducts, clutter along the walls, a terminal panel and notice board, lights ---
        var hall = Section(parent, "North Hall");
        foreach (float x in new[] { -259.2f, -255.2f, -251.2f })
            PropFit(hall, Modular + "Walls/decorative_wall_3.prefab", new Vector3(x, floor, north), 180f, Vector3.back);
        for (float x = f.GalleryWest; x < east - 1f; x += 6f)
            Prop(hall, Warehouse + "Ducts/Duct Straight.prefab", new Vector3(x, floor + 7.5f, north - .42f), 90f);
        PropFit(hall, Warehouse + "Crates Barrels Pallets/Pallet.prefab", new Vector3(-247.3f, floor, north - 1f), 0f);
        PropFit(hall, Warehouse + "Crates Barrels Pallets/Crate Long.prefab", new Vector3(-247.3f, floor + .2f, north - 1f), 90f);
        PropFit(hall, Warehouse + "Crates Barrels Pallets/Barrel.prefab", new Vector3(-245.6f, floor, north - .75f), 0f);
        PropFit(hall, Warehouse + "Misc Props/Cart.prefab", new Vector3(-239.8f, floor, north - .9f), 0f);
        PropFit(hall, Warehouse + "Crates Barrels Pallets/Barrel.prefab", new Vector3(-236.7f, floor, north - .75f), 0f);
        PropFit(hall, Warehouse + "Crates Barrels Pallets/Barrel.prefab", new Vector3(-235.8f, floor, north - 1.6f), 30f);
        PropFit(hall, Modular + "Walls/decorative_wall_4_computer.prefab", new Vector3(-246.3f, floor, quartersOuter), 0f, Vector3.forward);
        PropFit(hall, deco + "bulletin_board_big.prefab", new Vector3(-242.3f, floor, quartersOuter), 0f, Vector3.forward);
        PropFit(hall, Warehouse + "Misc Props/Garbage Bin.prefab", new Vector3(-240.2f, floor, quartersOuter + .4f), 0f);
        PropFit(hall, deco + "computer_station.prefab", new Vector3(east, floor, -6.5f), YawZ(Vector3.left), Vector3.left);
        // A cargo platform under the hall skylight, west of the guide line to the terminal.
        var platform = PropFit(hall, Modular + "Machines/storage_container_small.prefab", new Vector3(-244.5f, floor, -15f), 0f);
        float platformTop = platform ? KP.WorldBounds(platform).max.y : floor + 1.07f;
        PropFit(hall, Modular + "Machines/container_small.prefab", new Vector3(-245.4f, platformTop, -15.8f), 15f);
        PropFit(hall, Warehouse + "Crates Barrels Pallets/Crate Short.prefab", new Vector3(-243.6f, platformTop, -14.2f), -20f);
        PropFit(hall, deco + "floor_corner_ornament.prefab", new Vector3(f.GalleryWest, floor, -9f), YawZ(Vector3.right), Vector3.right);
        Prop(hall, Warehouse + "Misc Props/Security Cam.prefab", new Vector3(east - .3f, floor + 6.5f, north - .3f), 135f);
        foreach (float x in new[] { -247.3f, -239.8f })
            WallLight(hall, new Vector3(x, floor + 3.2f, north), Vector3.back);
        WallLight(hall, new Vector3(-237.6f, floor + 2.6f, quartersOuter), Vector3.forward);
        WallLight(hall, new Vector3(east, floor + 3f, -3.5f), Vector3.left);
        Point(hall, "Drill Fill", new Vector3(-255.2f, floor + 6.5f, -8f), 16f, 14f, new Color(.8f, .88f, 1f));
    }

    // Heading (degrees about Y) that turns a prop's local +Z to face the given direction.
    static float YawZ(Vector3 facing) => Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;

    // The warehouse kit's caged wall light (emissive, no light of its own) at wallPoint, with a sconce light that
    // washes the wall around it.
    static void WallLight(Transform parent, Vector3 wallPoint, Vector3 facing)
    {
        var fixture = PropFit(parent, Warehouse + "Misc Props/Wall Light.prefab", wallPoint, YawZ(facing) - 90f, facing);
        if (!fixture) return;
        Point(fixture.transform, "Sconce Light", KP.WorldBounds(fixture).center + facing * .35f, 5f, 3.5f, k_Warm);
    }

    // A local post volume over the wing (like the Workshop's): a lift in exposure so the dim kit shell reads, blended
    // out at the doorways. Its profile is an asset beside the wing's art.
    static void BuildVolume(Transform parent, Frame f)
    {
        string path = ArtRoot + "/TW_PostVolume.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (!profile)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }
        if (!profile.TryGet(out UnityEngine.Rendering.Universal.ColorAdjustments adjustments))
        {
            adjustments = profile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>();
            adjustments.name = "ColorAdjustments";
            AssetDatabase.AddObjectToAsset(adjustments, profile);
        }
        adjustments.postExposure.Override(.75f);
        adjustments.contrast.Override(8f);
        EditorUtility.SetDirty(adjustments);
        EditorUtility.SetDirty(profile);

        var go = new GameObject("Wing Post Volume");
        go.transform.SetParent(parent, false);
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        go.transform.position = new Vector3((f.GalleryWest + f.QuartersEast) * .5f, (f.Floor + f.Ceiling) * .5f, (f.QuartersSouth + f.HallNorth) * .5f);
        box.size = new Vector3(f.QuartersEast - f.GalleryWest, f.Ceiling - f.Floor, f.HallNorth - f.QuartersSouth);
        var volume = go.AddComponent<Volume>();
        volume.isGlobal = false;
        volume.priority = 10f;
        volume.blendDistance = .5f;
        volume.sharedProfile = profile;
    }

    static void Rug(Transform parent, Vector3 centre, Vector2 size)
    {
        var rug = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rug.name = "Rug";
        rug.transform.SetParent(parent, false);
        rug.transform.position = centre + Vector3.up * .006f;
        rug.transform.localScale = new Vector3(size.x, .012f, size.y);
        Object.DestroyImmediate(rug.GetComponent<Collider>());
        rug.GetComponent<Renderer>().sharedMaterial = s_M.Carpet;
    }

    // ------------------------------------------------------------------ keycard

    static KeycardData BuildBlackKeycard()
    {
        string folder = ArtRoot + "/Keycard";
        K.EnsureFolder(folder);
        var texture = TutorialWingArt.SavePng(TutorialWingArt.BlackFloppy(FloppyFolder + "/Textures/Floppy_Blue.png"), folder + "/Floppy_Black.png");
        var iconTexturePath = folder + "/Icon_Floppy_Black.png";
        TutorialWingArt.SavePng(TutorialWingArt.BlackFloppy(FloppyFolder + "/Icons/Icon_Floopy_Blue.png"), iconTexturePath, true, true);
        var importer = (TextureImporter)AssetImporter.GetAtPath(iconTexturePath);
        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        // The disk material: like the other floppies (URP/Unlit over the painted texture).
        var material = LoadOrCreate(folder + "/Floppy_Black.mat", "Universal Render Pipeline/Unlit");
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(material);

        // The disk model: the blue floppy's prefab with the black material.
        string prefabPath = folder + "/Floppy_Black.prefab";
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(FloppyFolder + "/Prefabs/Floppy_Blue.prefab");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.name = "Floppy_Black";
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();
        var model = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        Object.DestroyImmediate(instance);

        var card = AssetDatabase.LoadAssetAtPath<KeycardData>(KeycardPath);
        if (!card)
        {
            card = ScriptableObject.CreateInstance<KeycardData>();
            AssetDatabase.CreateAsset(card, KeycardPath);
        }
        card.DisplayName = "BLACK KEYCARD";
        card.Tint = k_Card;
        card.Model = model;
        card.Icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconTexturePath);
        EditorUtility.SetDirty(card);
        return card;
    }

    // ------------------------------------------------------------------ materials

    static Mats BuildMaterials()
    {
        var m = new Mats
        {
            PodShell = Lit("M_TW_PodShell", new Color(.8f, .83f, .86f), .7f, .15f),
            PodTrim = Lit("M_TW_PodTrim", new Color(.08f, .09f, .1f), .55f, .4f),
            PodGlow = Emissive("M_TW_PodGlow", new Color(.15f, .6f, 1f), 4f),
            PodPadding = Lit("M_TW_PodPadding", new Color(.06f, .2f, .26f), .25f, 0f),
            PodGlass = Glass("M_TW_PodGlass", new Color(.4f, .85f, .95f, .22f)),
            PodScreen = Emissive("M_TW_PodScreen", Color.white, 1.4f, TutorialWingArt.SavePng(TutorialWingArt.PodScreenTexture(), TextureFolder + "/T_TW_PodScreen.png")),
            PodiumBody = Lit("M_TW_PodiumBody", new Color(.12f, .13f, .15f), .5f, .35f),
            PodiumTop = Lit("M_TW_PodiumTop", new Color(.55f, .57f, .6f), .75f, .4f),
            PodiumGlow = Emissive("M_TW_PodiumGlow", new Color(.3f, .75f, 1f), 3f),
            Chrome = Lit("M_TW_Chrome", new Color(.78f, .8f, .83f), .85f, .75f),
            Velvet = Lit("M_TW_Velvet", new Color(.42f, .03f, .05f), .3f, 0f),
            Brass = Lit("M_TW_Brass", new Color(.75f, .55f, .22f), .7f, .7f),
            Carpet = Lit("M_TW_Carpet", new Color(.32f, .04f, .05f), .1f, 0f),
            Plaque = Lit("M_TW_PlaqueBody", new Color(.1f, .11f, .12f), .45f, .35f),
            PlaqueFace = Lit("M_TW_PlaqueFace", new Color(.03f, .04f, .05f), .6f, .2f),
            PlaqueGlow = Emissive("M_TW_PlaqueGlow", new Color(1f, .62f, .2f), 2.5f),
            RackPlate = Lit("M_TW_RackPlate", new Color(.2f, .23f, .26f), .5f, .35f),
            RackFrame = Lit("M_TW_RackFrame", new Color(.06f, .065f, .07f), .45f, .4f),
            Rail = Lit("M_TW_Rail", new Color(.1f, .1f, .11f), .4f, .4f),
            HoloPanel = Glass("M_TW_HoloPanel", new Color(.1f, .45f, .7f, .16f), true),
            HoloFrame = Emissive("M_TW_HoloFrame", new Color(.35f, .8f, 1f), 3f),
            Hazard = Lit("M_TW_Hazard", Color.white, .35f, 0f, TutorialWingArt.SavePng(TutorialWingArt.HazardTexture(), TextureFolder + "/T_TW_Hazard.png")),
            GuideGlow = Emissive("M_TW_GuideGlow", new Color(.8f, .86f, 1f), 3.5f),
            Occluder = Lit("M_TW_GlobeOccluder", new Color(.01f, .01f, .02f), 0f, 0f),
            Lamp = KP.LampMaterial(),
            Bezel = KP.BezelMaterial(),
        };
        // The nebula skybox drawn on a globe: a copy after the opaque queue so it depth-tests against the scene.
        var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/FPS/Art/Materials/Skybox/NebulaSkybox.mat");
        string nebulaPath = MaterialFolder + "/M_TW_NebulaGlobe.mat";
        var nebula = AssetDatabase.LoadAssetAtPath<Material>(nebulaPath);
        if (!nebula)
        {
            nebula = new Material(sky);
            AssetDatabase.CreateAsset(nebula, nebulaPath);
        }
        else nebula.CopyPropertiesFromMaterial(sky);
        nebula.shader = sky.shader;
        nebula.renderQueue = 2450;
        EditorUtility.SetDirty(nebula);
        m.Nebula = nebula;

        // Each plaque category's glow bar; the weapon bar keeps the original amber plaque glow.
        m.CategoryGlow = new Material[k_CategoryNames.Length];
        for (int i = 0; i < m.CategoryGlow.Length; i++)
            m.CategoryGlow[i] = (Category)i == Category.Weapon ? m.PlaqueGlow
                : Emissive("M_TW_PlaqueGlow_" + ((Category)i), k_CategoryColors[i], 2.5f);

        // The showcase beams: additive, brightest at the lens and fading out toward the exhibit.
        var beam = LoadOrCreate(MaterialFolder + "/M_TW_LightCone.mat", "Universal Render Pipeline/Unlit");
        string fadePath = TextureFolder + "/T_TW_BeamFade.png";
        var fade = TutorialWingArt.SavePng(TutorialWingArt.BeamFadeTexture(), fadePath);
        var fadeImporter = (TextureImporter)AssetImporter.GetAtPath(fadePath);
        if (fadeImporter.wrapMode != TextureWrapMode.Clamp)
        {
            fadeImporter.wrapMode = TextureWrapMode.Clamp;
            fadeImporter.SaveAndReimport();
            fade = AssetDatabase.LoadAssetAtPath<Texture2D>(fadePath);
        }
        beam.SetTexture("_BaseMap", fade);
        beam.SetTextureScale("_BaseMap", new Vector2(1f, 1f / TutorialWingArt.LightConeSlant));
        // URP's additive mode blends SrcAlpha/One, so the strength lives in the colour and alpha stays 1.
        beam.SetColor("_BaseColor", new Color(.3f, .25f, .19f, 1f));
        beam.SetFloat("_Surface", 1f);
        beam.SetFloat("_Blend", 2f);
        beam.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        beam.SetFloat("_DstBlend", (float)BlendMode.One);
        beam.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        beam.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        beam.SetFloat("_ZWrite", 0f);
        beam.SetFloat("_Cull", (float)CullMode.Off);
        beam.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        beam.SetOverrideTag("RenderType", "Transparent");
        beam.renderQueue = (int)RenderQueue.Transparent;
        beam.SetShaderPassEnabled("DepthOnly", false);
        beam.SetShaderPassEnabled("ShadowCaster", false);
        EditorUtility.SetDirty(beam);
        m.LightCone = beam;
        return m;
    }

    static Material LoadOrCreate(string path, string shaderName)
    {
        var shader = Shader.Find(shaderName);
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader) material.shader = shader;
        return material;
    }

    static Material Lit(string name, Color color, float smoothness, float metallic, Texture texture = null)
    {
        var material = LoadOrCreate(MaterialFolder + "/" + name + ".mat", "Universal Render Pipeline/Lit");
        material.SetColor("_BaseColor", color);
        material.SetTexture("_BaseMap", texture);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", metallic);
        material.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material Emissive(string name, Color color, float intensity, Texture texture = null)
    {
        var material = LoadOrCreate(MaterialFolder + "/" + name + ".mat", "Universal Render Pipeline/Lit");
        material.SetColor("_BaseColor", texture ? Color.black : color * .4f);
        material.SetTexture("_BaseMap", null);
        material.SetFloat("_Smoothness", .6f);
        material.SetFloat("_Metallic", 0f);
        material.SetTexture("_EmissionMap", texture);
        material.SetColor("_EmissionColor", color * intensity);
        material.EnableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material Glass(string name, Color color, bool unlit = false)
    {
        var material = LoadOrCreate(MaterialFolder + "/" + name + ".mat",
            unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        if (!unlit)
        {
            material.SetFloat("_Smoothness", .95f);
            material.SetFloat("_Metallic", 0f);
        }
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetShaderPassEnabled("DepthOnly", false);
        material.SetShaderPassEnabled("ShadowCaster", false);
        EditorUtility.SetDirty(material);
        return material;
    }

    // Gives the wing's pack-material renderers (props under the root and the wing's shell pieces) lit copies.
    static int ApplyLitCopies(Transform root, Frame f)
    {
        var renderers = new List<Renderer>(root.GetComponentsInChildren<Renderer>(true));
        var wing = new Bounds();
        // Floor tiles run up to ~1.2 m under the walls; nothing of a neighbouring space lies within this margin.
        const float allowance = 1.3f;
        wing.SetMinMax(new Vector3(f.GalleryWest - allowance, f.Floor - .6f, f.QuartersSouth - allowance),
            new Vector3(f.QuartersEast + allowance, f.Ceiling + .8f, f.HallNorth + allowance));
        foreach (var go in HierarchyFolder.TopLevel(root.gameObject.scene))
        {
            if (!ShellPrefixes.Any(p => go.name.StartsWith(p))) continue;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                var b = renderer.bounds;
                if (wing.Contains(b.min) && wing.Contains(b.max)) renderers.Add(renderer);
            }
        }

        var sources = new HashSet<Material>();
        foreach (var renderer in renderers)
            foreach (var material in renderer.sharedMaterials)
                if (NeedsCopy(material)) sources.Add(material);

        var copies = new Dictionary<Material, Material>();
        var targets = new List<(string, Material)>();
        foreach (var source in sources)
        {
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string copyPath = LitCopyRoot + sourcePath.Substring("Assets".Length);
            var copy = AssetDatabase.LoadAssetAtPath<Material>(copyPath);
            if (copy) EditorUtility.CopySerialized(source, copy);
            else
            {
                K.EnsureFolder(copyPath.Substring(0, copyPath.LastIndexOf('/')));
                AssetDatabase.CopyAsset(sourcePath, copyPath);
                copy = AssetDatabase.LoadAssetAtPath<Material>(copyPath);
            }
            if (copy.parent) copy.parent = null;
            copies[source] = copy;
            targets.Add((copyPath, copy));
        }
        if (targets.Count > 0) WorkshopMaterialUpgrade.Upgrade(targets);
        foreach (var copy in copies.Values)
        {
            // As in the Workshop: the kit's high metallic leaves almost no diffuse, so it is brought into a painted-steel range.
            if (copy.shader.name != "Universal Render Pipeline/Lit" || copy.GetTexture("_MetallicGlossMap")) continue;
            float metallic = copy.GetFloat("_Metallic");
            if (metallic > .1f) copy.SetFloat("_Metallic", Mathf.Min(.4f, .1f + .3f * (metallic - .1f) / .8f));
            EditorUtility.SetDirty(copy);
        }

        int swapped = 0;
        foreach (var renderer in renderers)
        {
            var materials = renderer.sharedMaterials;
            bool any = false;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] && copies.TryGetValue(materials[i], out var copy))
                {
                    materials[i] = copy;
                    any = true;
                }
            if (!any) continue;
            renderer.sharedMaterials = materials;
            if (PrefabUtility.IsPartOfPrefabInstance(renderer)) PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            swapped++;
        }
        return swapped;
    }

    static bool NeedsCopy(Material material)
    {
        if (!material) return false;
        string path = AssetDatabase.GetAssetPath(material);
        return WorkshopMaterialUpgrade.PackRoots.Any(r => path.StartsWith(r + "/")) && WorkshopMaterialUpgrade.RendersFlat(material.shader);
    }

    // ------------------------------------------------------------------ building blocks

    static readonly Dictionary<string, Mesh> s_Meshes = new Dictionary<string, Mesh>();

    // Each named mesh is written once per build and shared.
    static Mesh SaveMesh(string name, System.Action<TWMesh> build)
    {
        if (s_Meshes.TryGetValue(name, out var saved) && saved) return saved;
        var mesh = new TWMesh();
        build(mesh);
        return s_Meshes[name] = mesh.Save(MeshFolder + "/" + name + ".asset");
    }

    static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material[] materials, Vector3 position, Quaternion rotation,
        bool local = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        if (local) go.transform.SetLocalPositionAndRotation(position, rotation);
        else go.transform.SetPositionAndRotation(position, rotation);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = materials;
        return go;
    }

    static GameObject Prop(Transform parent, string path, Vector3 position, float yaw, float scale = 1f)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!prefab)
        {
            Debug.LogWarning("Tutorial wing: missing prop " + path);
            return null;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        if (!Mathf.Approximately(scale, 1f)) go.transform.localScale = Vector3.one * scale;
        return go;
    }

    // Places a prop by its bounds: footprint centred on the anchor, standing on it, and backed onto a wall when given
    // the wall's normal (the anchor then lies on the wall face).
    static GameObject PropFit(Transform parent, string path, Vector3 anchor, float yaw, Vector3 wallNormal = default)
    {
        var go = Prop(parent, path, anchor, yaw);
        if (!go || !go.GetComponentInChildren<Renderer>()) return go;
        var b = KP.WorldBounds(go);
        Vector3 shift = anchor - b.center;
        shift.y = anchor.y - b.min.y;
        if (wallNormal != Vector3.zero) shift += wallNormal * (Mathf.Abs(Vector3.Dot(b.extents, wallNormal)) + .03f);
        go.transform.position += shift;
        return go;
    }

    // A visual-only copy of a weapon prefab (no scripts, colliders or audio), on the default layer.
    static GameObject DisplayWeapon(Transform parent, string path, Vector3 localPosition, Quaternion localRotation, float scale, string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!prefab) return null;
        var copy = Object.Instantiate(prefab, parent);
        copy.name = name;
        StripGameplay(copy);
        copy.transform.SetLocalPositionAndRotation(localPosition, localRotation);
        copy.transform.localScale = Vector3.one * scale;
        foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        return copy;
    }

    static void StripGameplay(GameObject go)
    {
        // Behaviours first (some require others), then physics and audio.
        // Last added first, so a behaviour that requires another goes before the one it requires.
        foreach (var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true).Reverse())
            if (behaviour) Object.DestroyImmediate(behaviour);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (var c in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(c);
        foreach (var c in go.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(c);
        foreach (var c in go.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(c);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.On;
    }

    static Light Spot(Transform parent, string name, Vector3 from, Vector3 target, float range, float angle, float intensity, Color color)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.transform.SetParent(parent, false);
        light.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
        light.type = LightType.Spot;
        light.range = range;
        light.spotAngle = angle;
        light.innerSpotAngle = angle * .55f;
        light.intensity = intensity;
        light.color = color;
        light.shadows = LightShadows.None;
        return light;
    }

    static Light Point(Transform parent, string name, Vector3 position, float range, float intensity, Color color)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.transform.SetParent(parent, false);
        light.transform.position = position;
        light.type = LightType.Point;
        light.range = range;
        light.intensity = intensity;
        light.color = color;
        light.shadows = LightShadows.None;
        return light;
    }

    // A kit hanging lamp on a rod from the ceiling, with its light.
    static void HangingLamp(Transform parent, Vector3 position, float ceiling, float range, float intensity, Color color)
    {
        var lamp = Prop(parent, Warehouse + "Misc Props/Hanging Light.prefab", position, 0f);
        if (!lamp) return;
        var rod = MeshObject("Lamp Rod", lamp.transform, SaveMesh("SM_TW_LampRod", m => m.Cylinder(Vector3.zero, Vector3.up, .015f, 8)),
            new[] { s_M.Rail }, position, Quaternion.identity);
        rod.transform.localScale = new Vector3(1f, ceiling - position.y, 1f);
        Point(lamp.transform, "Lamp Light", position + Vector3.down * .7f, range, intensity, color);
    }

    // The project's own caged corridor lamp on a wall, as emergency lighting.
    static void CorridorLamp(Transform parent, Vector3 wallPoint, float yaw, float range, float intensity)
    {
        var lamp = Prop(parent, LampPrefab, wallPoint, yaw);
        if (!lamp) return;
        Point(lamp.transform, "Emergency Light", wallPoint + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * .45f, range, intensity, k_Red);
    }

    // Hazard tape on the floor: a rectangle outline (or a single band when filled).
    static void HazardStrip(Transform parent, Vector3 center, Vector2 size, float width, bool filled = false)
    {
        var mesh = SaveMesh("SM_TW_Hazard_" + Mathf.RoundToInt(size.x * 100) + "x" + Mathf.RoundToInt(size.y * 100) + (filled ? "F" : ""), m =>
        {
            if (filled)
            {
                m.Box(new Vector3(0f, .004f, 0f), new Vector3(size.x, .008f, size.y));
                return;
            }
            m.Box(new Vector3(0f, .004f, size.y * .5f), new Vector3(size.x, .008f, width));
            m.Box(new Vector3(0f, .004f, -size.y * .5f), new Vector3(size.x, .008f, width));
            m.Box(new Vector3(size.x * .5f, .004f, 0f), new Vector3(width, .008f, size.y));
            m.Box(new Vector3(-size.x * .5f, .004f, 0f), new Vector3(width, .008f, size.y));
        });
        var strip = MeshObject("Hazard Tape", parent, mesh, new[] { s_M.Hazard }, center, Quaternion.identity);
        strip.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    static GameObject Label(Transform parent, string text, Vector3 position, Quaternion rotation, float capHeight, float width, Color color,
        TMP_FontAsset font)
    {
        var tmp = new GameObject("Label - " + text).AddComponent<TextMeshPro>();
        tmp.transform.SetParent(parent, false);
        tmp.transform.SetPositionAndRotation(position, rotation);
        tmp.font = font;
        tmp.text = text;
        tmp.fontSize = capHeight / .12f;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.characterSpacing = 4f;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.rectTransform.sizeDelta = new Vector2(width, capHeight * 3f);
        return tmp.gameObject;
    }

    // An exhibit's plaque on a lectern facing local +Z: its category (also the colour of the glow bar), title,
    // description and the technique tag.
    static void Plaque(Transform parent, Vector3 position, Quaternion rotation, string title, string body, string tag, Category category)
    {
        var mesh = SaveMesh("SM_TW_Lectern", m => TutorialWingArt.Lectern(m, out _, out _));
        var lectern = MeshObject("Plaque - " + title, parent, mesh,
            new[] { s_M.Plaque, s_M.PlaqueFace, s_M.CategoryGlow[(int)category] }, position, rotation);
        var box = lectern.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, .55f, 0f);
        box.size = new Vector3(.4f, 1.1f, .3f);
        var tm = new TWMesh();
        TutorialWingArt.Lectern(tm, out Vector3 plateCenter, out Quaternion plateRotation);
        var face = new GameObject("Text").transform;
        face.SetParent(lectern.transform, false);
        face.SetLocalPositionAndRotation(plateCenter + plateRotation * new Vector3(0f, 0f, .012f), plateRotation * Quaternion.Euler(0f, 180f, 0f));
        var badge = WorldText(face, "Category", "CATEGORY:  " + k_CategoryNames[(int)category], s_F.Title, .038f, new Vector2(0f, .172f),
            new Vector2(.62f, .055f), k_CategoryColors[(int)category], TextAlignmentOptions.Center);
        badge.characterSpacing = 8f;
        WorldText(face, "Title", title, s_F.Title, .05f, new Vector2(0f, .11f), new Vector2(.6f, .07f), k_Amber, TextAlignmentOptions.Center);
        WorldText(face, "Body", body, s_F.Body, .03f, new Vector2(0f, -.025f), new Vector2(.6f, .18f), new Color(.88f, .93f, 1f), TextAlignmentOptions.Center);
        WorldText(face, "Tag", tag, s_F.Title, .026f, new Vector2(0f, -.17f), new Vector2(.62f, .05f), k_Cyan, TextAlignmentOptions.Center);
    }

    static TextMeshPro WorldText(Transform parent, string name, string text, TMP_FontAsset font, float capHeight, Vector2 position, Vector2 size,
        Color color, TextAlignmentOptions alignment)
    {
        var tmp = new GameObject(name).AddComponent<TextMeshPro>();
        tmp.transform.SetParent(parent, false);
        tmp.transform.localPosition = position;
        tmp.font = font;
        tmp.text = text;
        tmp.fontSize = capHeight / .12f;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.rectTransform.sizeDelta = size;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = tmp.fontSize * .5f;
        tmp.fontSizeMax = tmp.fontSize;
        return tmp;
    }

    // A wall screen: a dark panel with a glowing frame, a heading and a few lines of text. Its front faces local -Z.
    static void WallScreen(Transform parent, string name, Vector3 position, Quaternion rotation, float width, float height, string heading, string body)
    {
        var group = new GameObject(name).transform;
        group.SetParent(parent, false);
        group.SetPositionAndRotation(position, rotation);
        var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "Panel";
        Object.DestroyImmediate(panel.GetComponent<Collider>());
        panel.transform.SetParent(group, false);
        panel.transform.localPosition = new Vector3(0f, 0f, .03f);
        panel.transform.localScale = new Vector3(width, height, .05f);
        panel.GetComponent<Renderer>().sharedMaterial = s_M.PlaqueFace;
        MeshObject("Frame", group, SaveMesh("SM_TW_ScreenFrame_" + Mathf.RoundToInt(width * 10) + "x" + Mathf.RoundToInt(height * 10), m =>
        {
            m.Box(new Vector3(0f, height * .5f, 0f), new Vector3(width + .08f, .04f, .02f));
            m.Box(new Vector3(0f, -height * .5f, 0f), new Vector3(width + .08f, .04f, .02f));
            m.Box(new Vector3(width * .5f, 0f, 0f), new Vector3(.04f, height, .02f));
            m.Box(new Vector3(-width * .5f, 0f, 0f), new Vector3(.04f, height, .02f));
        }), new[] { s_M.HoloFrame }, Vector3.zero, Quaternion.identity, local: true);
        var face = new GameObject("Text").transform;
        face.SetParent(group, false);
        face.localPosition = new Vector3(0f, 0f, -.01f);
        WorldText(face, "Heading", heading, s_F.Title, height * .15f, new Vector2(0f, height * .3f), new Vector2(width * .94f, height * .26f), Color.white, TextAlignmentOptions.Center);
        WorldText(face, "Body", body, s_F.Body, height * .085f, new Vector2(0f, -height * .14f), new Vector2(width * .92f, height * .58f), new Color(.85f, .92f, 1f), TextAlignmentOptions.Center);
        Point(group, "Screen Glow", position + rotation * new Vector3(0f, 0f, -.9f), 3.5f, .8f, new Color(1f, .45f, .3f));
    }

    // A holographic sign: a faint panel with a glowing frame, a heading and text. Faces rotation's back (-Z, toward the viewer).
    static GameObject HoloSign(Transform parent, string name, Vector3 position, string heading, string body, Color accent,
        Quaternion? facing = null, float width = 2.9f)
    {
        float height = width * .44f;
        Quaternion rotation = facing ?? Quaternion.identity;
        var group = new GameObject(name).transform;
        group.SetParent(parent, false);
        group.SetPositionAndRotation(position, rotation);
        var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
        panel.name = "Panel";
        Object.DestroyImmediate(panel.GetComponent<Collider>());
        panel.transform.SetParent(group, false);
        panel.transform.localScale = new Vector3(width, height, 1f);
        panel.GetComponent<Renderer>().sharedMaterial = s_M.HoloPanel;
        panel.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        var frame = MeshObject("Frame", group, SaveMesh("SM_TW_HoloFrame_" + Mathf.RoundToInt(width * 10), m =>
        {
            m.Box(new Vector3(0f, height * .5f, 0f), new Vector3(width, .025f, .01f));
            m.Box(new Vector3(0f, -height * .5f, 0f), new Vector3(width, .025f, .01f));
            m.Box(new Vector3(-width * .5f, height * .35f, 0f), new Vector3(.025f, height * .3f, .01f));
            m.Box(new Vector3(width * .5f, -height * .35f, 0f), new Vector3(.025f, height * .3f, .01f));
        }), new[] { s_M.HoloFrame }, Vector3.zero, Quaternion.identity, local: true);
        frame.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        var face = new GameObject("Text").transform;
        face.SetParent(group, false);
        face.localPosition = new Vector3(0f, 0f, -.01f);
        WorldText(face, "Heading", heading, s_F.Title, height * .2f, new Vector2(0f, height * .27f), new Vector2(width * .94f, height * .32f), accent, TextAlignmentOptions.Center);
        WorldText(face, "Body", body, s_F.Body, height * .1f, new Vector2(0f, -height * .15f), new Vector2(width * .94f, height * .52f), new Color(.88f, .95f, 1f), TextAlignmentOptions.Center);
        return group.gameObject;
    }

    // ------------------------------------------------------------------ sounds

    static float[] Samples(float seconds) => new float[Mathf.CeilToInt(seconds * K.SampleRate)];

    // A task ticked off: a soft rising two-note blip.
    static float[] SynthTaskDone(System.Random random)
    {
        var s = Samples(.32f);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)K.SampleRate;
            float f = t < .1f ? 1320f : 1760f;
            float local = t < .1f ? t : t - .1f;
            s[i] = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Clamp01(local / .004f) * Mathf.Exp(-local * 14f) * .7f;
        }
        return K.Normalize(s, .45f);
    }

    // A ship systems line opening: a short radio squelch and a two-tone chirp.
    static float[] SynthTransmission(System.Random random)
    {
        var s = Samples(.36f);
        float hiss = 0f;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)K.SampleRate;
            hiss += (K.Noise(random) - hiss) * .35f;
            float squelch = t < .12f ? hiss * Mathf.Exp(-t * 18f) * .8f : 0f;
            float chirp = t > .1f ? Mathf.Sin(2f * Mathf.PI * (t < .22f ? 880f : 1175f) * t) * Mathf.Exp(-(t - .1f) * 9f) * .5f : 0f;
            s[i] = squelch + chirp;
        }
        return K.Normalize(s, .4f);
    }

    // A new step: a gentle bell.
    static float[] SynthStep(System.Random random)
    {
        var s = Samples(.9f);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)K.SampleRate;
            s[i] = (Mathf.Sin(2f * Mathf.PI * 660f * t) + .5f * Mathf.Sin(2f * Mathf.PI * 990f * t) + .25f * Mathf.Sin(2f * Mathf.PI * 1650f * t)) *
                   Mathf.Clamp01(t / .006f) * Mathf.Exp(-t * 5f);
        }
        return K.Normalize(s, .4f);
    }

    // The pod unsealing: a long, falling hiss of cold gas.
    static float[] SynthPodHiss(System.Random random)
    {
        var s = Samples(2.4f);
        float low = 0f, band = 0f;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)K.SampleRate;
            float n = K.Noise(random);
            low += (n - low) * Mathf.Lerp(.5f, .12f, t / 2.4f);
            band = low - band * .3f;
            s[i] = band * Mathf.Clamp01(t / .05f) * Mathf.Exp(-t * 1.1f);
        }
        return K.Normalize(s, .55f);
    }

    // The pod's seals letting go: a heavy double clunk.
    static float[] SynthPodLatch(System.Random random)
    {
        var s = Samples(.5f);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)K.SampleRate;
            float a = Mathf.Sin(2f * Mathf.PI * 90f * t) * Mathf.Exp(-t * 30f);
            float b = t > .16f ? Mathf.Sin(2f * Mathf.PI * 70f * (t - .16f)) * Mathf.Exp(-(t - .16f) * 22f) : 0f;
            s[i] = a + b + K.Noise(random) * Mathf.Exp(-t * 60f) * .3f;
        }
        return K.Normalize(s, .7f);
    }

    // Lifting the gun off its rack: a metal clack and a short servo whirr.
    static float[] SynthLockerTake(System.Random random)
    {
        var s = Samples(.55f);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)K.SampleRate;
            float clack = (Mathf.Sin(2f * Mathf.PI * 1900f * t) * .5f + K.Noise(random) * .6f) * Mathf.Exp(-t * 45f);
            float servo = t > .08f && t < .45f ? Mathf.Sin(2f * Mathf.PI * (320f + 400f * (t - .08f)) * t) * .25f * Mathf.Sin((t - .08f) / .37f * Mathf.PI) : 0f;
            s[i] = clack + servo;
        }
        return K.Normalize(s, .6f);
    }
}
