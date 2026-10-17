using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Section = CentreRoomFramework.Section;

/// <summary>
/// Rebuildable corebuster_env_025 pass: dresses the Centre Room's walls and perimeter and leaves the centre open.
/// It owns three <see cref="CentreRoomFramework"/> sections:
/// <list type="bullet">
/// <item><c>Wall_Services</c>: a pilaster rhythm, heavy-pipe riser banks in the west corners, one red header loop over
/// the east wall, thin kit conduit, fuseboxes and cameras.</item>
/// <item><c>Perimeter_Machinery</c>: floor-standing machines, shelving and moderate crates, barrels and pallets, all
/// within about 3 m of a wall, each with a carving NavMeshObstacle.</item>
/// <item><c>Overhead</c>: a truss ring under the ceiling around the reactor zone and a duct trunk along the north and
/// south walls, all above the movement band.</item>
/// </list>
/// Every prop is laid out in a wall-local frame (+X into the room, +Z along the wall, origin on the floor at the wall
/// face) from the live corebuster_env_022 measurement, then checked against the measured keep-outs and every prop
/// already placed, and slid along its wall until clear. Pack prefabs keep their shared materials; no asset is written.
/// </summary>
public static class CentreRoomPerimeter
{
    public const string Owner = "corebuster_env_025";
    public const string Output = "ReviewCaptures/CentreRoom/025";
    const string UndoName = "Build Centre Room Perimeter";

    const string Kit = "Assets/SciFi Warehouse Kit/Prefabs/";
    const string WallSupport = Kit + "Structures/Pillars and Beams/Wall Support.prefab";
    const string CeilingSupport = Kit + "Structures/Pillars and Beams/Ceiling Support.prefab";
    const string DuctStraight = Kit + "Props/Ducts/Duct Straight.prefab";
    const string DuctVent = Kit + "Props/Ducts/Duct Vent.prefab";
    const string PipesCorner = Kit + "Structures/Structure Props/Pipes 02.prefab";
    const string PipesLowRun = Kit + "Structures/Structure Props/Pipes 03.prefab";
    const string PipesHangA = Kit + "Structures/Structure Props/Pipes 04.prefab";
    const string PipesHangB = Kit + "Structures/Structure Props/Pipes 06.prefab";
    const string FuseboxLow = Kit + "Structures/Structure Props/Fusebox 01.prefab";
    const string FuseboxPost = Kit + "Structures/Structure Props/Fusebox 02.prefab";
    const string SecurityCam = Kit + "Props/Misc Props/Security Cam.prefab";
    const string ShelvesEmpty = Kit + "Props/Shelves/Shelves Empty.prefab";
    const string Barrel = Kit + "Props/Crates Barrels Pallets/Barrel.prefab";
    const string CrateLong = Kit + "Props/Crates Barrels Pallets/Crate Long.prefab";
    const string CrateShort = Kit + "Props/Crates Barrels Pallets/Crate Short.prefab";
    // Pallet variants without Rigidbodies: 02 and 05 are loaded, 08 is a leaning stack and 09 a flat one.
    const string PalletCrates = Kit + "Props/Crates Barrels Pallets/Pallet Variations/Pallet Variation 02.prefab";
    const string PalletBarrels = Kit + "Props/Crates Barrels Pallets/Pallet Variations/Pallet Variation 05.prefab";
    const string PalletLeaning = Kit + "Props/Crates Barrels Pallets/Pallet Variations/Pallet Variation 08.prefab";
    const string PalletFlat = Kit + "Props/Crates Barrels Pallets/Pallet Variations/Pallet Variation 09.prefab";

    // The Modular Pack faces local +Z, the kit's wall props local +X. Its pastel default palette is swapped per
    // instance for the pack's own darker palettes; the shared pack materials are referenced, never edited.
    const string Modular = "Assets/Sci-Fi Styled Modular Pack/";
    const string Rotor = Modular + "Prefabs/Machines/rotor.prefab";
    const string Capacitor = Modular + "Prefabs/Machines/Capacitor.prefab";
    const string BatteryBig = Modular + "Prefabs/Machines/Battery_big.prefab";
    const string Generator = Modular + "Prefabs/Machines/generator.prefab";
    const string ContainerBig = Modular + "Prefabs/Machines/container_big.prefab";
    const string ComputerStation = Modular + "Prefabs/Decorative elements/computer_station.prefab";
    const string PackPalette = "base color";
    const string DarkPalette = Modular + "Materials/Palletes/dark color.mat";
    const string AccentPalette = Modular + "Materials/Palletes/iron man color.mat";
    const float ZFront = 90;

    const string Heavy = "Assets/FPS/Art/StartWorkshop/HeavyIndustrialKit/";
    const string StraightPrefab = Heavy + "Prefabs/P_HeavyPipe_Straight_2m.prefab";
    const string ElbowPrefab = Heavy + "Prefabs/P_HeavyPipe_Elbow_90.prefab";
    const string FlangePrefab = Heavy + "Prefabs/P_HeavyPipe_Flange.prefab";
    const string ValvePrefab = Heavy + "Prefabs/P_HeavyPipe_ValveMount.prefab";
    const string SteelFinish = Heavy + "Materials/M_HeavyPipe_BareSteel.mat";
    const string RedPaint = Heavy + "Materials/M_HeavyPipe_RedOxide.mat";

    // Pilasters: the first stands this far from each corner, the rest divide the wall evenly.
    const float ColumnInset = 3.2f;
    const int ColumnsPerWall = 6;
    // Floor machinery stands in front of the 0.4 m pilasters, leaving a service gap behind it.
    const float MachineGap = .5f;
    const float MachinerySpacing = .3f;
    // Horizontal lane kept between machinery and any corebuster_env_024 cover piece.
    const float CoverLane = 1.2f;
    // "Near the perimeter": no machinery renderer reaches further than this from its nearest wall.
    const float MaxMachineryDepth = 3f;
    const float RiserStandoff = .5f, HeaderStandoff = .75f, HeaderHeight = 7.2f, HeaderInset = 1.2f;
    const float ValveHeight = 1.3f;
    const float TrussHalfSide = 9f;
    const float DuctStandoff = 1.5f, DuctInset = 4f;
    const float Tolerance = .01f;

    enum Wall { West, East, South, North }
    enum Kind { Services, Machinery, Overhead, Cover, Other }

    static readonly string[] RequiredServices =
    {
        "Riser_Bank_SW", "Riser_Bank_NW", "Header_East", "Fusebox_Panel_S", "Fusebox_Panel_N",
        "Conduit_S_A", "Conduit_S_B", "Conduit_N_A", "Conduit_N_B", "Conduit_W_A", "Conduit_W_B", "Conduit_E_A", "Conduit_E_B",
        "Camera_S", "Camera_N", "Camera_W",
    };
    static readonly string[] RequiredMachinery =
    {
        "Console_Bank_W", "Battery_Bay_W", "Generator_W", "Rotor_Cradle_W",
        "Barrel_Row_NW", "Capacitor_N", "Container_N", "Pallet_Stack_N", "Crate_Stack_NE",
        "Rotor_Cradle_E", "Generator_E", "Capacitor_E", "Battery_Bay_E",
        "Crate_Stack_SW", "Shelving_S", "Pallet_Row_S", "Barrel_Pair_SE",
    };
    static readonly string[] RequiredOverhead = { "Ring_Truss", "Duct_Trunk_S", "Duct_Trunk_N" };

    [MenuItem("Tools/CoreBuster/Build Centre Room Perimeter")]
    public static void BuildMenu()
    {
        try
        {
            var layout = CentreRoomFramework.MeasureLayout();
            Build(layout);
            Validate(SceneManager.GetActiveScene(), layout, true);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            throw;
        }
    }

    [MenuItem("Tools/CoreBuster/Validate Centre Room Perimeter")]
    public static bool ValidateMenu()
    {
        try { return Validate(SceneManager.GetActiveScene(), CentreRoomFramework.MeasureLayout(), true); }
        catch (Exception ex) { Debug.LogError("CENTRE ROOM PERIMETER CHECKS FAILED: " + ex.Message); return false; }
    }

    /// <summary>Batch build: builds, validates, saves, reloads, validates again and proves nothing else changed.</summary>
    public static void BuildBatch()
    {
        var scene = EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        var layout = CentreRoomFramework.MeasureLayout();
        var before = Snapshot(scene);
        Build(layout);
        if (!Validate(scene, layout, true))
            throw new InvalidOperationException("Centre Room perimeter validation failed before save.");
        EditorSceneManager.SaveScene(scene);

        scene = EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        layout = CentreRoomFramework.MeasureLayout();
        if (!Validate(scene, layout, true))
            throw new InvalidOperationException("Centre Room perimeter validation failed after saved-scene reload.");
        var after = Snapshot(scene);
        var changes = new List<string>();
        foreach (var pair in before)
            if (!after.TryGetValue(pair.Key, out string value)) changes.Add("missing after reload: " + pair.Value);
            else if (value != pair.Value) changes.Add("changed: " + pair.Value + " -> " + value);
        foreach (var pair in after) if (!before.ContainsKey(pair.Key)) changes.Add("new outside the owned sections: " + pair.Value);
        if (changes.Count > 0)
            throw new InvalidOperationException($"{changes.Count} object(s) outside the {Owner} sections changed:\n  " +
                string.Join("\n  ", changes.Take(40)));
        Debug.Log($"CENTRE ROOM PERIMETER BUILD VERIFIED: save/reload passed and all {before.Count} transforms and their " +
            $"renderers outside the three {Owner} sections are unchanged.");
    }

    /// <summary>Clears and rebuilds exactly the three corebuster_env_025 sections, in one Undo group.</summary>
    public static void Build(CentreRoomFramework.Layout layout)
    {
        Scene scene = SceneManager.GetActiveScene();
        Undo.IncrementCurrentGroup();
        int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        var builder = new Builder(layout, scene);
        try
        {
            builder.Run();
        }
        catch
        {
            builder.DiscardPending();
            Undo.RevertAllDownToGroup(undo);
            throw;
        }
        Undo.CollapseUndoOperations(undo);
        EditorSceneManager.MarkSceneDirty(scene);
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/Build_Report.txt", builder.Report);
        Debug.Log("Centre Room perimeter rebuilt.\n" + builder.Report);
    }

    sealed class Placed
    {
        public Transform prop;
        public Kind kind;
        public Bounds[] parts;
    }

    sealed class Builder
    {
        readonly CentreRoomFramework.Layout m_Layout;
        readonly Scene m_Scene;
        readonly float m_West, m_East, m_South, m_North, m_Floor, m_Height;
        readonly List<Placed> m_Placed = new List<Placed>();
        readonly StringBuilder m_Report = new StringBuilder();
        readonly Material m_Steel = Load<Material>(SteelFinish), m_Red = Load<Material>(RedPaint);
        readonly Material m_Dark = Load<Material>(DarkPalette), m_Accent = Load<Material>(AccentPalette);
        readonly float m_StraightLength = Port(StraightPrefab).z, m_Bend = Port(ElbowPrefab).z;
        readonly float m_ValveLength = Port(ValvePrefab).z, m_FlangeLength = Port(FlangePrefab).z;
        Transform m_Services, m_Machinery, m_Overhead;
        GameObject m_Pending;

        public string Report => m_Report.ToString();

        public Builder(CentreRoomFramework.Layout layout, Scene scene)
        {
            m_Layout = layout;
            m_Scene = scene;
            m_West = layout.interior.min.x; m_East = layout.interior.max.x;
            m_South = layout.interior.min.z; m_North = layout.interior.max.z;
            m_Floor = layout.Floor; m_Height = layout.Ceiling - layout.Floor;
        }

        public void DiscardPending()
        {
            if (m_Pending) Object.DestroyImmediate(m_Pending);
        }

        public void Run()
        {
            m_Machinery = CentreRoomFramework.BeginSectionRebuild(m_Layout, Section.PerimeterMachinery, Owner);
            m_Services = CentreRoomFramework.BeginSectionRebuild(m_Layout, Section.WallServices, Owner);
            m_Overhead = CentreRoomFramework.BeginSectionRebuild(m_Layout, Section.Overhead, Owner);
            // Level is offset from the world origin, so every group is posed in world space; only scale must be 1.
            if ((m_Services.lossyScale - Vector3.one).sqrMagnitude > 1e-8f)
                throw new InvalidOperationException("The Centre Room sections must be unscaled in world space.");

            // Whatever the other Centre Room passes have already saved is an obstacle, never something to move.
            foreach (Section other in Enum.GetValues(typeof(Section)))
            {
                if (CentreRoomFramework.Spec(other).owner == Owner) continue;
                foreach (Transform child in CentreRoomFramework.FindSection(m_Scene, other))
                    m_Placed.Add(new Placed { prop = child, kind = other == Section.Cover ? Kind.Cover : Kind.Other, parts = PlacementParts(child, m_Layout) });
            }
            m_Report.AppendLine(FormattableString.Invariant(
                $"Frame X [{m_West:F2}, {m_East:F2}], Z [{m_South:F2}, {m_North:F2}], floor {m_Floor:F2}, height {m_Height:F2}; {m_Placed.Count} prop(s) from other passes."));

            BuildServices();
            BuildMachinery();
            BuildOverhead();

            var flags = GameObjectUtility.GetStaticEditorFlags(m_Services.parent.parent.gameObject);
            foreach (var root in new[] { m_Services, m_Machinery, m_Overhead })
                foreach (Transform prop in root)
                    foreach (var t in prop.GetComponentsInChildren<Transform>(true))
                    {
                        GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
                        if (PrefabUtility.IsPartOfPrefabInstance(t)) PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
                    }
        }

        // ---- Wall services -------------------------------------------------------------------------------------

        void BuildServices()
        {
            // Pilasters first, so everything else fits around them. One inside a gate approach is simply left out.
            foreach (Wall wall in Enum.GetValues(typeof(Wall)))
                for (int i = 0; i < ColumnsPerWall; i++)
                {
                    var column = Group(m_Services, $"Frame_{Letter(wall)}_{i + 1}");
                    var support = Instance(column, WallSupport, "Support");
                    support.transform.localScale = Vector3.Scale(support.transform.localScale, new Vector3(1, m_Height / B(support).size.y, 1));
                    Fit(support, 0, 0, 0);
                    Place(column, Section.WallServices, Kind.Services, wall, Column(wall, i), 0, optional: true);
                }

            // Heavy-pipe risers in the west corners, floor to ceiling, the middle pipe with a hand valve.
            Place(RiserBank("Riser_Bank_SW", m_Steel, m_Red, m_Steel), Section.WallServices, Kind.Services, Wall.West, 1.6f, 0);
            Place(RiserBank("Riser_Bank_NW", m_Red, m_Steel, m_Steel), Section.WallServices, Kind.Services, Wall.West, Length(Wall.West) - 1.6f, 0);
            // One red header loop on the east wall, rising near each corner and passing high over the terminal.
            Place(Header("Header_East", Wall.East), Section.WallServices, Kind.Services, Wall.East, Length(Wall.East) * .5f, 0);

            // Thin kit conduit in the bays between pilasters. It is wall-flush and needs no collider.
            Conduit("Conduit_S_A", PipesCorner, Wall.South, Bay(Wall.South, 1), 0);
            Conduit("Conduit_S_B", PipesHangB, Wall.South, Bay(Wall.South, 2), -1);
            Conduit("Conduit_N_A", PipesHangA, Wall.North, Bay(Wall.North, 0), -1);
            Conduit("Conduit_N_B", PipesCorner, Wall.North, Bay(Wall.North, 2), 0);
            Conduit("Conduit_W_A", PipesHangB, Wall.West, Bay(Wall.West, 3), -1);
            Conduit("Conduit_W_B", PipesLowRun, Wall.West, Bay(Wall.West, 4), 8f);
            Conduit("Conduit_E_A", PipesCorner, Wall.East, Bay(Wall.East, 1), 0);
            Conduit("Conduit_E_B", PipesHangA, Wall.East, Bay(Wall.East, 3), -1);

            foreach (Wall wall in new[] { Wall.South, Wall.North })
            {
                var panel = Group(m_Services, $"Fusebox_Panel_{Letter(wall)}");
                Fit(Instance(panel, FuseboxPost, "Fusebox_Post"), 0, -.4f, 0);
                Fit(Instance(panel, FuseboxLow, "Fusebox_Low"), 0, .4f, 1f);
                Place(panel, Section.WallServices, Kind.Services, wall, Length(wall) - 2f, 1f);
            }

            Camera("Camera_S", Wall.South, Column(Wall.South, 3) + .9f);
            Camera("Camera_N", Wall.North, Column(Wall.North, 3) + .9f);
            Camera("Camera_W", Wall.West, Column(Wall.West, 2) + 1f);
        }

        Transform RiserBank(string name, params Material[] paints)
        {
            var bank = Group(m_Services, name);
            float spacing = .7f, first = -spacing * (paints.Length - 1) * .5f;
            for (int i = 0; i < paints.Length; i++)
            {
                var riser = Group(bank, $"Riser_{i + 1}");
                var chain = new Chain(this, riser, new Vector3(RiserStandoff, 0, first + i * spacing), Vector3.up, paints[i]);
                chain.Flange(Vector3.right);
                if (i == paints.Length / 2)
                {
                    chain.StraightTo(ValveHeight - m_ValveLength * .5f, Vector3.right);
                    chain.Valve(Vector3.right);
                }
                chain.StraightTo(m_Height - m_FlangeLength, Vector3.right);
                chain.Flange(Vector3.right);
            }
            float width = spacing * (paints.Length - 1) + .62f;
            foreach (float y in new[] { 3f, 7f })
                Box(bank, y < 5 ? "Strap_Low" : "Strap_High", new Vector3((RiserStandoff + .1f) * .5f, y, 0),
                    new Vector3(RiserStandoff + .1f, .08f, width), m_Steel);
            Obstacle(bank.gameObject, B(bank.gameObject));
            return bank;
        }

        /// <summary>Up from the floor near one corner, along the wall at <see cref="HeaderHeight"/>, and down to the
        /// floor near the other, with a valve on each leg. Arms reach it from the pilasters it passes.</summary>
        Transform Header(string name, Wall wall)
        {
            var header = Group(m_Services, name);
            float half = Length(wall) * .5f - HeaderInset;
            var chain = new Chain(this, header, new Vector3(HeaderStandoff, 0, -half), Vector3.up, m_Red);
            chain.Flange(Vector3.right);
            chain.StraightTo(ValveHeight - m_ValveLength * .5f, Vector3.right);
            chain.Valve(Vector3.right);
            chain.StraightTo(HeaderHeight - m_Bend, Vector3.right);
            chain.Elbow(Vector3.forward);
            chain.StraightTo(half - m_Bend, Vector3.up);
            chain.Elbow(Vector3.down);
            chain.StraightTo(ValveHeight + m_ValveLength * .5f, Vector3.right);
            chain.Valve(Vector3.right);
            chain.StraightTo(m_FlangeLength, Vector3.right);
            chain.Flange(Vector3.right);

            float radius = B(header.Find("Straight_2")?.gameObject ?? header.gameObject).size.x * .5f;
            for (int i = 0; i < ColumnsPerWall; i++)
            {
                float z = Column(wall, i) - Length(wall) * .5f;
                // An arm from the pilaster's face to just past the pipe, under it.
                Box(header, $"Arm_{i + 1}", new Vector3((.4f + HeaderStandoff + .2f) * .5f, HeaderHeight - radius - .06f, z),
                    new Vector3(HeaderStandoff + .2f - .4f, .12f, .12f), m_Steel, collider: false);
            }
            foreach (float side in new[] { -1f, 1f })
            {
                var leg = Group(header, side < 0 ? "Leg_Obstacle_A" : "Leg_Obstacle_B");
                Obstacle(leg.gameObject, new Bounds(new Vector3(HeaderStandoff, 1f, side * half), new Vector3(.7f, 2f, .7f)));
            }
            return header;
        }

        void Conduit(string name, string prefab, Wall wall, float along, float bottom)
        {
            var conduit = Group(m_Services, name);
            var pipes = Instance(conduit, prefab, "Pipes");
            // A negative bottom hangs the piece from the ceiling.
            Fit(pipes, 0, 0, bottom < 0 ? m_Height - B(pipes).size.y : bottom);
            Place(conduit, Section.WallServices, Kind.Services, wall, along, 1f);
        }

        void Camera(string name, Wall wall, float along)
        {
            var mount = Group(m_Services, name);
            Fit(Instance(mount, SecurityCam, "Security_Cam"), 0, 0, 4.3f);
            Place(mount, Section.WallServices, Kind.Services, wall, along, 1f);
        }

        // ---- Perimeter machinery -------------------------------------------------------------------------------

        void BuildMachinery()
        {
            // West wall: consoles beside the gate (low enough to keep the corner pocket open), then power plant.
            Machine(ConsoleBank("Console_Bank_W"), Wall.West, 4.3f);
            Machine(Plinthed("Battery_Bay_W", BatteryBig, ZFront, .5f, m_Accent), Wall.West, 15.6f);
            Machine(Plinthed("Generator_W", Generator, 0, .75f, m_Dark), Wall.West, 19.6f);
            Machine(RotorCradle("Rotor_Cradle_W"), Wall.West, 23.6f);

            // North wall.
            Machine(Barrels("Barrel_Row_NW", 3), Wall.North, 4.4f);
            Machine(Plinthed("Capacitor_N", Capacitor, ZFront, .5f, m_Dark), Wall.North, 8.2f);
            Machine(Plinthed("Container_N", ContainerBig, ZFront, .6f, m_Dark), Wall.North, 11.7f);
            Machine(Stack("Pallet_Stack_N", PalletFlat, CrateLong), Wall.North, 16.2f);
            Machine(Crates("Crate_Stack_NE"), Wall.North, 27.1f);

            // East wall, either side of the terminal reservation.
            Machine(RotorCradle("Rotor_Cradle_E"), Wall.East, 7.1f);
            Machine(Plinthed("Generator_E", Generator, 0, .75f, m_Dark), Wall.East, 10.9f);
            Machine(Plinthed("Capacitor_E", Capacitor, ZFront, .5f, m_Dark), Wall.East, 20.3f);
            Machine(Plinthed("Battery_Bay_E", BatteryBig, ZFront, .5f, m_Dark), Wall.East, 24.2f);

            // South wall: storage rather than machines, so the entry side reads differently.
            Machine(Crates("Crate_Stack_SW"), Wall.South, 4.1f);
            Machine(Shelving("Shelving_S"), Wall.South, 9.8f);
            Machine(PalletRow("Pallet_Row_S"), Wall.South, 16f);
            Machine(Barrels("Barrel_Pair_SE", 2), Wall.South, 27.4f);
        }

        void Machine(Transform group, Wall wall, float along)
        {
            Obstacle(group.gameObject, B(group.gameObject));
            Place(group, Section.PerimeterMachinery, Kind.Machinery, wall, along, 3f);
        }

        Transform ConsoleBank(string name)
        {
            var bank = Group(m_Machinery, name);
            Fit(Pack(bank, ComputerStation, "Station_A", ZFront, 1f, m_Dark), MachineGap, -.74f, 0);
            Fit(Pack(bank, ComputerStation, "Station_B", ZFront, 1f, m_Dark), MachineGap, .74f, 0);
            return bank;
        }

        /// <summary>A Modular Pack machine on a first-principles steel base plate.</summary>
        Transform Plinthed(string name, string prefab, float yaw, float scale, Material palette)
        {
            const float plate = .08f;
            var group = Group(m_Machinery, name);
            var machine = Pack(group, prefab, "Machine", yaw, scale, palette);
            Fit(machine, MachineGap, 0, plate);
            Bounds b = B(machine);
            Box(group, "Base_Plate", new Vector3(b.center.x, plate * .5f, 0), new Vector3(b.size.x + .16f, plate, b.size.z + .16f), m_Steel);
            return group;
        }

        /// <summary>A rotor drum lying along the wall on two steel saddles.</summary>
        Transform RotorCradle(string name)
        {
            const float saddle = .35f;
            var group = Group(m_Machinery, name);
            var rotor = Pack(group, Rotor, "Rotor", ZFront, .9f, m_Dark);
            Fit(rotor, MachineGap, 0, saddle - .1f);
            Bounds b = B(rotor);
            Box(group, "Saddle_A", new Vector3(b.center.x, saddle * .5f, -b.size.z * .3f), new Vector3(b.size.x * .85f, saddle, .3f), m_Steel);
            Box(group, "Saddle_B", new Vector3(b.center.x, saddle * .5f, b.size.z * .3f), new Vector3(b.size.x * .85f, saddle, .3f), m_Steel);
            return group;
        }

        /// <summary>The kit's empty shelving, loaded by hand so it does not repeat a workshop shelf variant: pallets
        /// and barrels on the floor under it, crates on its two grated decks.</summary>
        Transform Shelving(string name)
        {
            // Deck tops above the floor, from the prefab's two "Shelf Grate" colliders.
            const float deckLow = 1.6f, deckHigh = 3.61f;
            var group = Group(m_Machinery, name);
            Fit(Instance(group, ShelvesEmpty, "Shelves", 90), MachineGap, 0, 0);
            float back = MachineGap + .15f;
            Fit(Instance(group, PalletFlat, "Floor_Pallets"), back, -1.5f, 0);
            Fit(Instance(group, Barrel, "Floor_Barrel_A", 25), back + .1f, .15f, 0);
            Fit(Instance(group, Barrel, "Floor_Barrel_B", 140), back + .35f, 1.3f, 0);
            var crate = Instance(group, CrateLong, "Deck_Crate_Long_A");
            Fit(crate, back, -1.5f, deckLow);
            Fit(Instance(group, CrateShort, "Deck_Crate_Short_A"), back + .2f, -1.7f, B(crate).max.y);
            Fit(Instance(group, CrateLong, "Deck_Crate_Long_B"), back + .45f, .2f, deckLow);
            Fit(Instance(group, CrateShort, "Deck_Crate_Short_B"), back, 1.7f, deckLow);
            Fit(Instance(group, CrateShort, "Top_Crate_Short_A"), back + .3f, -.9f, deckHigh);
            Fit(Instance(group, CrateLong, "Top_Crate_Long"), back + .1f, 1.1f, deckHigh);
            return group;
        }

        Transform Barrels(string name, int count)
        {
            var group = Group(m_Machinery, name);
            for (int i = 0; i < count; i++)
                Fit(Instance(group, Barrel, $"Barrel_{i + 1}", 40 * i), MachineGap + (i % 2) * .12f, (i - (count - 1) * .5f) * 1.02f, 0);
            return group;
        }

        Transform Crates(string name)
        {
            var group = Group(m_Machinery, name);
            var low = Instance(group, CrateLong, "Crate_Long");
            Fit(low, MachineGap, -.35f, 0);
            Fit(Instance(group, CrateShort, "Crate_Short_Top"), MachineGap, -.55f, B(low).max.y);
            Fit(Instance(group, CrateShort, "Crate_Short_Side"), MachineGap + .05f, .75f, 0);
            return group;
        }

        Transform Stack(string name, string pallet, string load)
        {
            var group = Group(m_Machinery, name);
            var stack = Instance(group, pallet, "Pallets");
            Fit(stack, MachineGap, 0, 0);
            Bounds b = B(stack);
            var crate = Instance(group, load, "Load");
            Fit(crate, b.center.x - B(crate).size.x * .5f, 0, b.max.y);
            return group;
        }

        Transform PalletRow(string name)
        {
            var group = Group(m_Machinery, name);
            Fit(Instance(group, PalletBarrels, "Pallet_Barrels"), MachineGap, -.9f, 0);
            Fit(Instance(group, PalletLeaning, "Pallet_Leaning"), MachineGap, .9f, 0);
            return group;
        }

        // ---- Overhead ------------------------------------------------------------------------------------------

        void BuildOverhead()
        {
            // A truss square under the ceiling around the reactor zone, tied back to the west and east walls. It is
            // built in world space: the ring centre is measured, not a wall offset.
            var ring = Group(m_Overhead, "Ring_Truss");
            Vector3 c = m_Layout.RingCenter;
            float top = m_Floor + m_Height, h = TrussHalfSide;
            Beams(ring, "South", new Vector3(c.x - h, top, c.z - h), 0, 2 * h, 3);
            Beams(ring, "East", new Vector3(c.x + h, top, c.z - h), -90, 2 * h, 3);
            Beams(ring, "North", new Vector3(c.x + h, top, c.z + h), 180, 2 * h, 3);
            Beams(ring, "West", new Vector3(c.x - h, top, c.z + h), 90, 2 * h, 3);
            Beams(ring, "Tie_West", new Vector3(c.x - h, top, c.z), 180, c.x - h - m_West, 1);
            Beams(ring, "Tie_East", new Vector3(c.x + h, top, c.z), 0, m_East - (c.x + h), 2);
            Accept(ring, Section.Overhead, Kind.Overhead, "world");

            foreach (Wall wall in new[] { Wall.South, Wall.North })
            {
                var trunk = Group(m_Overhead, $"Duct_Trunk_{Letter(wall)}");
                float span = Length(wall) - 2 * DuctInset;
                const int pieces = 4;
                float hang = 0;
                for (int i = 0; i < pieces; i++)
                {
                    var duct = Instance(trunk, DuctStraight, $"Duct_{i + 1}");
                    Bounds b = B(duct);
                    // The piece's hangers reach above its body; they meet the ceiling.
                    hang = m_Height - b.max.y;
                    var scale = duct.transform.localScale;
                    scale.z *= span / pieces / b.size.z;
                    duct.transform.localScale = scale;
                    duct.transform.position = new Vector3(DuctStandoff, hang, -span * .5f + i * span / pieces);
                }
                Instance(trunk, DuctVent, "Vent_A", 180).transform.position = new Vector3(DuctStandoff, hang, -span * .5f);
                Instance(trunk, DuctVent, "Vent_B").transform.position = new Vector3(DuctStandoff, hang, span * .5f);
                Place(trunk, Section.Overhead, Kind.Overhead, wall, Length(wall) * .5f, 0);
            }
        }

        /// <summary>Kit ceiling beams end to end from <paramref name="start"/> (their top edge) along the yawed +X.</summary>
        void Beams(Transform parent, string name, Vector3 start, float yaw, float length, int count)
        {
            Quaternion rotation = Quaternion.Euler(0, yaw, 0);
            for (int i = 0; i < count; i++)
            {
                var beam = Instance(parent, CeilingSupport, $"{name}_{i + 1}", yaw);
                Bounds b = B(beam);
                float authored = Mathf.Max(b.size.x, b.size.z);
                var scale = beam.transform.localScale;
                scale.x *= length / count / authored;
                beam.transform.localScale = scale;
                beam.transform.position = start + rotation * Vector3.right * (i * length / count) + Vector3.down * (b.max.y - beam.transform.position.y);
            }
        }

        // ---- Placement -----------------------------------------------------------------------------------------

        float Length(Wall wall) => wall == Wall.West || wall == Wall.East ? m_North - m_South : m_East - m_West;
        float Column(Wall wall, int index) => ColumnInset + index * (Length(wall) - 2 * ColumnInset) / (ColumnsPerWall - 1);
        float Bay(Wall wall, int index) => (Column(wall, index) + Column(wall, index + 1)) * .5f;
        static string Letter(Wall wall) => wall.ToString().Substring(0, 1);
        // Wall-local +X (into the room) turned to each wall's inward direction.
        static float Yaw(Wall wall) => wall switch { Wall.West => 0, Wall.East => 180, Wall.South => -90, _ => 90 };

        Vector3 WallPoint(Wall wall, float along) => wall switch
        {
            Wall.West => new Vector3(m_West, m_Floor, m_South + along),
            Wall.East => new Vector3(m_East, m_Floor, m_South + along),
            Wall.South => new Vector3(m_West + along, m_Floor, m_South),
            _ => new Vector3(m_West + along, m_Floor, m_North),
        };

        /// <summary>Moves a wall-local group to its wall, sliding along it in 0.25 m steps (up to
        /// <paramref name="slide"/>) until it is clear of the keep-outs and every placed prop.</summary>
        void Place(Transform group, Section section, Kind kind, Wall wall, float along, float slide, bool optional = false)
        {
            string problem = null;
            for (int step = 0; ; step++)
            {
                float offset = (step + 1) / 2 * .25f * (step % 2 == 1 ? -1 : 1);
                if (Mathf.Abs(offset) > slide + .001f) break;
                group.SetPositionAndRotation(WallPoint(wall, along + offset), Quaternion.Euler(0, Yaw(wall), 0));
                problem = Problem(group, section, kind);
                if (problem == null)
                {
                    Accept(group, section, kind, FormattableString.Invariant($"{wall} wall at {along + offset:F2} m (slid {offset:+0.00;-0.00;0})"), check: false);
                    return;
                }
            }
            if (!optional)
                throw new InvalidOperationException(FormattableString.Invariant(
                    $"No clear position for {group.name} on the {wall} wall within {slide:F2} m of {along:F2} m: {problem}."));
            m_Report.AppendLine($"  skipped {group.name}: {problem}");
            Object.DestroyImmediate(group.gameObject);
            m_Pending = null;
        }

        void Accept(Transform group, Section section, Kind kind, string where, bool check = true)
        {
            string problem = check ? Problem(group, section, kind) : null;
            if (problem != null) throw new InvalidOperationException($"{group.name} cannot be built: {problem}.");
            Bounds b = B(group.gameObject);
            m_Placed.Add(new Placed { prop = group, kind = kind, parts = Parts(group) });
            m_Report.AppendLine(FormattableString.Invariant(
                $"  {CentreRoomFramework.Spec(section).name}/{group.name}: {where}; bounds {b.min:F2} to {b.max:F2}"));
            Undo.RegisterCreatedObjectUndo(group.gameObject, UndoName);
            m_Pending = null;
        }

        string Problem(Transform group, Section section, Kind kind)
        {
            Physics.SyncTransforms();
            Bounds[] parts = Parts(group);
            foreach (Bounds part in parts)
            {
                string violation = m_Layout.KeepOutViolation(section, part);
                if (violation != null) return violation;
            }
            foreach (var collider in group.GetComponentsInChildren<Collider>())
            {
                string violation = collider.enabled && !collider.isTrigger ? m_Layout.KeepOutViolation(section, collider.bounds) : null;
                if (violation != null) return violation;
            }
            foreach (var other in m_Placed)
            {
                float margin = Margin(kind, other.kind);
                foreach (Bounds a in parts)
                    foreach (Bounds b in other.parts)
                        if (Overlaps(a, b, margin))
                            return margin > 0 ? $"within {margin:F1} m of {other.prop.name}" : $"intersects {other.prop.name}";
            }
            return null;
        }

        // ---- Construction helpers ------------------------------------------------------------------------------

        Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            // Groups are assembled at the world origin, so wall-local and world coordinates coincide until placed.
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            if (parent == m_Services || parent == m_Machinery || parent == m_Overhead) m_Pending = go;
            return go.transform;
        }

        /// <summary>A linked prefab instance at the group's origin, yawed from its authored rotation.</summary>
        internal GameObject Instance(Transform parent, string path, string name, float yaw = 0, float scale = 1f)
        {
            var asset = Load<GameObject>(path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            go.name = name;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, yaw, 0) * asset.transform.rotation);
            go.transform.localScale = asset.transform.localScale * scale;
            return go;
        }

        /// <summary>A Modular Pack prop repainted from the pack's pastel default to one of its darker palettes.</summary>
        GameObject Pack(Transform parent, string path, string name, float yaw, float scale, Material palette)
        {
            var go = Instance(parent, path, name, yaw, scale);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && materials[i].name == PackPalette) materials[i] = palette;
                r.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            }
            return go;
        }

        /// <summary>Shifts a piece so its back face is <paramref name="back"/> from the wall, its centre at
        /// <paramref name="along"/> and its underside at <paramref name="bottom"/> (wall-local metres).</summary>
        static void Fit(GameObject go, float back, float along, float bottom)
        {
            Bounds b = B(go);
            go.transform.position += new Vector3(back - b.min.x, bottom - b.min.y, along - b.center.z);
        }

        static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material material, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        /// <summary>A carving box over <paramref name="footprint"/> from the floor to 2 m, on a wall-local object that
        /// is still at the origin. The scene NavMesh is not rebaked, so enemies path around the prop this way.</summary>
        static void Obstacle(GameObject go, Bounds footprint)
        {
            float height = Mathf.Min(footprint.max.y, 2f);
            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = go.transform.InverseTransformPoint(new Vector3(footprint.center.x, height * .5f, footprint.center.z));
            obstacle.size = new Vector3(footprint.size.x, height, footprint.size.z);
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
        }

        /// <summary>Places heavy-kit pieces port to port: each piece's origin (Port_A) goes on the current end, and
        /// its Port_B becomes the new end.</summary>
        sealed class Chain
        {
            readonly Builder m_Builder;
            readonly Transform m_Root;
            readonly Material m_Paint;
            Vector3 m_Position, m_Direction;
            int m_Pieces;

            public Chain(Builder builder, Transform root, Vector3 start, Vector3 direction, Material paint)
            {
                m_Builder = builder; m_Root = root; m_Position = start; m_Direction = direction; m_Paint = paint;
            }

            /// <summary>Straight pipe until the end reaches <paramref name="target"/> on the current direction's axis.</summary>
            public void StraightTo(float target, Vector3 side)
            {
                float length = target * (m_Direction.x + m_Direction.y + m_Direction.z) - Vector3.Dot(m_Position, m_Direction);
                if (length < -.001f)
                    throw new InvalidOperationException(FormattableString.Invariant(
                        $"{m_Root.name} cannot reach {target:F2}: its end at {m_Position:F2} is already {-length:F2} m past it."));
                if (length < .01f) return;
                int pieces = Mathf.Max(1, Mathf.CeilToInt(length / m_Builder.m_StraightLength - .01f));
                for (int i = 0; i < pieces; i++)
                {
                    var piece = Add(StraightPrefab, "Straight", side);
                    var renderer = piece.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = m_Paint;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    var scale = piece.transform.localScale;
                    scale.z *= length / pieces / m_Builder.m_StraightLength;
                    piece.transform.localScale = scale;
                    Follow(piece);
                }
            }

            public void Elbow(Vector3 turn) => Follow(Add(ElbowPrefab, "Elbow", turn));
            public void Valve(Vector3 wheel) => Follow(Add(ValvePrefab, "Valve", wheel));
            public void Flange(Vector3 side) => Follow(Add(FlangePrefab, "Flange", side));

            GameObject Add(string path, string name, Vector3 side)
            {
                var go = m_Builder.Instance(m_Root, path, $"{name}_{++m_Pieces}");
                // Local +Z is the pipe's direction and local +X the bend or valve-wheel side.
                go.transform.SetPositionAndRotation(m_Position, Quaternion.LookRotation(m_Direction, Vector3.Cross(m_Direction, side)));
                return go;
            }

            void Follow(GameObject piece)
            {
                var end = piece.transform.Find("Port_B");
                m_Position = end.position;
                Vector3 f = end.forward, a = new Vector3(Mathf.Abs(f.x), Mathf.Abs(f.y), Mathf.Abs(f.z));
                m_Direction = a.x >= a.y && a.x >= a.z ? new Vector3(Mathf.Sign(f.x), 0, 0)
                    : a.y >= a.z ? new Vector3(0, Mathf.Sign(f.y), 0) : new Vector3(0, 0, Mathf.Sign(f.z));
            }
        }
    }

    // ---- Validation ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The corebuster_env_025 acceptance rule, on top of <see cref="CentreRoomFramework.Validate"/>: the three sections
    /// hold their required props; nothing intersects; the overhead stays above the movement band; machinery is grounded,
    /// solid, carved out of navigation, near a wall and clear of any cover piece.
    /// </summary>
    public static bool Validate(Scene scene, CentreRoomFramework.Layout layout, bool log)
    {
        var errors = new List<string>();
        if (!CentreRoomFramework.Validate(scene, layout, log)) errors.Add("Centre Room framework validation failed (see its log entry).");
        Transform services = CentreRoomFramework.FindSection(scene, Section.WallServices);
        Transform machinery = CentreRoomFramework.FindSection(scene, Section.PerimeterMachinery);
        Transform overhead = CentreRoomFramework.FindSection(scene, Section.Overhead);
        Transform cover = CentreRoomFramework.FindSection(scene, Section.Cover);
        if (!services || !machinery || !overhead || !cover)
        {
            Debug.LogError("CENTRE ROOM PERIMETER CHECKS FAILED: run Tools/CoreBuster/Build Centre Room Framework first.");
            return false;
        }
        Require(services, RequiredServices, errors);
        Require(machinery, RequiredMachinery, errors);
        Require(overhead, RequiredOverhead, errors);
        int columns = services.Cast<Transform>().Count(t => t.name.StartsWith("Frame_", StringComparison.Ordinal));
        if (columns < 16) errors.Add($"Wall_Services has {columns} pilasters; at least 16 are expected around the gates.");

        float floor = layout.Floor, band = floor + CentreRoomFramework.MovementHeight;
        var level = services.parent.parent.gameObject;
        var flags = GameObjectUtility.GetStaticEditorFlags(level);
        var props = new List<Placed>();
        foreach (Section section in Enum.GetValues(typeof(Section)))
        {
            Transform root = CentreRoomFramework.FindSection(scene, section);
            Kind kind = root == machinery ? Kind.Machinery : root == services ? Kind.Services : root == overhead ? Kind.Overhead
                : root == cover ? Kind.Cover : Kind.Other;
            foreach (Transform prop in root) props.Add(new Placed { prop = prop, kind = kind, parts = PlacementParts(prop, layout) });
        }

        for (int i = 0; i < props.Count; i++)
            for (int j = i + 1; j < props.Count; j++)
            {
                if (props[i].kind >= Kind.Cover && props[j].kind >= Kind.Cover) continue;
                bool lane = (props[i].kind == Kind.Machinery && props[j].kind == Kind.Cover) || (props[i].kind == Kind.Cover && props[j].kind == Kind.Machinery);
                float margin = lane ? CoverLane : 0;
                if (props[i].parts.Any(a => props[j].parts.Any(b => Overlaps(a, b, margin))))
                    errors.Add(lane ? $"{props[i].prop.name} leaves less than a {CoverLane:F1} m lane to {props[j].prop.name}."
                        : $"{props[i].prop.name} intersects {props[j].prop.name}.");
            }

        foreach (var placed in props.Where(p => p.kind < Kind.Cover))
        {
            Transform prop = placed.prop;
            string label = prop.parent.name + "/" + prop.name;
            if (placed.parts.Length == 0) { errors.Add(label + " has no visible mesh."); continue; }
            Bounds b = B(prop.gameObject);
            foreach (var t in prop.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) != flags) { errors.Add($"{label}: {t.name} does not match Level's static flags."); break; }
            foreach (var r in prop.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(m))) { errors.Add($"{label}: {r.name} has a missing or scene-only material."); break; }
            if (prop.GetComponentsInChildren<Rigidbody>(true).Length > 0) errors.Add(label + " carries a Rigidbody.");
            if (prop.GetComponentsInChildren<Light>(true).Length > 0) errors.Add(label + " carries a Light; lighting belongs to corebuster_env_026.");

            if (placed.kind == Kind.Overhead)
            {
                if (b.min.y < band) errors.Add(FormattableString.Invariant($"{label} drops to {b.min.y - floor:F2} m, inside the {CentreRoomFramework.MovementHeight:F1} m movement band."));
                if (b.max.y < layout.Ceiling - .05f) errors.Add(label + " does not reach the ceiling it hangs from.");
                continue;
            }
            if (placed.kind != Kind.Machinery) continue;
            if (Mathf.Abs(b.min.y - floor) > .02f) errors.Add(FormattableString.Invariant($"{label} does not rest on the floor (underside {b.min.y - floor:+0.00;-0.00} m)."));
            if (!prop.GetComponentsInChildren<Collider>().Any(c => c.enabled && !c.isTrigger && c.bounds.min.y < band))
                errors.Add(label + " has no collider at body height.");
            var obstacle = prop.GetComponent<NavMeshObstacle>();
            if (!obstacle || !obstacle.enabled || !obstacle.carving || obstacle.shape != NavMeshObstacleShape.Box)
                errors.Add(label + " needs an enabled carving box NavMeshObstacle on its root.");
            else
            {
                Vector3 c = prop.TransformPoint(obstacle.center), h = prop.TransformVector(obstacle.size) * .5f;
                h = new Vector3(Mathf.Abs(h.x), Mathf.Abs(h.y), Mathf.Abs(h.z));
                if (c.x - h.x > b.min.x + .05f || c.x + h.x < b.max.x - .05f || c.z - h.z > b.min.z + .05f || c.z + h.z < b.max.z - .05f)
                    errors.Add(label + "'s NavMeshObstacle does not cover its footprint.");
            }
            float depth = Mathf.Min(Mathf.Min(b.max.x - layout.interior.min.x, layout.interior.max.x - b.min.x),
                Mathf.Min(b.max.z - layout.interior.min.z, layout.interior.max.z - b.min.z));
            if (depth > MaxMachineryDepth)
                errors.Add(FormattableString.Invariant($"{label} reaches {depth:F2} m from its wall; perimeter machinery stays within {MaxMachineryDepth:F1} m."));
        }

        var report = new StringBuilder();
        report.AppendLine($"Wall_Services {services.childCount} props ({columns} pilasters), Perimeter_Machinery {machinery.childCount}, Overhead {overhead.childCount}; cover pieces present: {cover.childCount}.");
        foreach (string error in errors) report.AppendLine("  ERROR: " + error);
        if (errors.Count == 0)
        {
            if (log) Debug.Log("CENTRE ROOM PERIMETER CHECKS PASSED: required props, measured keep-outs, no intersections, clear overhead band, " +
                FormattableString.Invariant($"grounded and carved machinery within {MaxMachineryDepth:F1} m of the walls.\n") + report);
            return true;
        }
        if (log) Debug.LogError($"CENTRE ROOM PERIMETER CHECKS FAILED ({errors.Count} error(s)).\n{report}");
        return false;
    }

    static void Require(Transform section, string[] names, List<string> errors)
    {
        foreach (string name in names)
            if (!section.Find(name)) errors.Add($"{section.name} is missing {name}; run Tools/CoreBuster/Build Centre Room Perimeter.");
    }

    // ---- Review captures -----------------------------------------------------------------------------------------

    public static void CaptureBatch()
    {
        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        CaptureMenu();
    }

    /// <summary>Fixed perimeter views through a temporary URP camera. Nothing is saved to the scene.</summary>
    [MenuItem("Tools/CoreBuster/Capture Centre Room Perimeter")]
    public static void CaptureMenu()
    {
        var layout = CentreRoomFramework.MeasureLayout();
        var report = layout.report;
        Bounds room = layout.interior;
        Vector3 c = layout.RingCenter, eye = Vector3.up * 1.7f;
        var views = new List<(string name, Vector3 eye, Vector3 target)>
        {
            ("Wall_North", c + eye + Vector3.back * 6, new Vector3(c.x, c.y + 3, room.max.z)),
            ("Wall_South", c + eye + Vector3.forward * 6, new Vector3(c.x, c.y + 3, room.min.z)),
            ("Wall_West", c + eye + Vector3.right * 6, new Vector3(room.min.x, c.y + 3, c.z)),
            ("Wall_East", c + eye + Vector3.left * 4, new Vector3(room.max.x, c.y + 3, c.z)),
            ("Corner_NW", c + eye + new Vector3(5, .5f, -5), new Vector3(room.min.x, c.y + 3, room.max.z)),
            ("Corner_NE", c + eye + new Vector3(-5, .5f, -5), new Vector3(room.max.x, c.y + 3, room.max.z)),
            ("Corner_SW", c + eye + new Vector3(5, .5f, 5), new Vector3(room.min.x, c.y + 3, room.min.z)),
            ("Corner_SE", c + eye + new Vector3(-5, .5f, 5), new Vector3(room.max.x, c.y + 3, room.min.z)),
            ("Overhead", c + eye + new Vector3(8, 0, -8), c + new Vector3(-3, 10.5f, 4)),
            ("Overview_From_South", new Vector3(c.x + 6, c.y + 4.5f, room.min.z + 1.2f), c + Vector3.up * 2.5f),
            ("Terminal", report.terminalFocus.position + report.terminalForward * 6 + Vector3.up * .3f, report.terminalFocus.position),
        };
        foreach (var gate in report.gates)
            views.Add(("Gate_" + gate.name.Substring(gate.name.Length - 2, 1),
                gate.position - gate.outward * 7 + Vector3.up * 1.7f, gate.position + Vector3.up * 1.5f));

        Directory.CreateDirectory(Output);
        var go = new GameObject("Centre Room perimeter camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.enabled = false; camera.fieldOfView = 72; camera.nearClipPlane = .05f; camera.farClipPlane = 200;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        camera.GetUniversalAdditionalCameraData().volumeLayerMask = ~0;
        try
        {
            foreach (var view in views)
            {
                camera.transform.SetPositionAndRotation(view.eye, Quaternion.LookRotation(view.target - view.eye));
                var rt = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                try
                {
                    camera.targetTexture = rt;
                    // The first post-processed frame of a batch session can come out wrong; render it twice.
                    camera.Render(); camera.Render();
                    RenderTexture.active = rt;
                    texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
                    File.WriteAllBytes(Output + "/" + view.name + ".png", texture.EncodeToPNG());
                }
                finally
                {
                    camera.targetTexture = null; RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(texture);
                }
            }
        }
        finally { Object.DestroyImmediate(go); }
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Scene changed during capture; do not save it.");
        Debug.Log($"Centre Room perimeter captures written to {Output} ({views.Count} views); no scene saved.");
    }

    // ---- Shared helpers ------------------------------------------------------------------------------------------

    // Machinery keeps a gap to other machinery and a lane to cover; everything else only must not intersect.
    static float Margin(Kind a, Kind b)
    {
        if (a != Kind.Machinery && b != Kind.Machinery) return 0;
        if (a == Kind.Cover || b == Kind.Cover) return CoverLane;
        return a == b ? MachinerySpacing : 0;
    }

    /// <summary>True when the boxes interpenetrate by more than a centimetre once <paramref name="a"/> is widened
    /// horizontally by <paramref name="margin"/>.</summary>
    static bool Overlaps(Bounds a, Bounds b, float margin) =>
        Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x) > Tolerance - margin &&
        Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y) > Tolerance &&
        Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z) > Tolerance - margin;

    static Bounds[] Parts(Transform prop) => prop.GetComponentsInChildren<Renderer>()
        .Where(r => r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)).Select(r => r.bounds).ToArray();

    // A validated collider-free boundary finish occupies the original shell surface, not a prop footprint.
    static Bounds[] PlacementParts(Transform prop, CentreRoomFramework.Layout layout) => prop.GetComponentsInChildren<Renderer>()
        .Where(r => r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer) && !CentreRoomLighting.IsBoundarySurface(r, layout))
        .Select(r => r.bounds).ToArray();

    static Bounds B(GameObject go)
    {
        Bounds[] parts = Parts(go.transform);
        if (parts.Length == 0) throw new InvalidOperationException($"'{go.name}' has no mesh renderers to place by.");
        Bounds bounds = parts[0];
        foreach (Bounds part in parts) bounds.Encapsulate(part);
        return bounds;
    }

    static T Load<T>(string path) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (!asset) throw new InvalidOperationException($"Missing {typeof(T).Name} {path}.");
        return asset;
    }

    static Vector3 Port(string prefab)
    {
        var port = Load<GameObject>(prefab).transform.Find("Port_B");
        if (!port) throw new InvalidOperationException($"Missing Port_B on {prefab}; run Tools/CoreBuster/Build Heavy Industrial Kit.");
        return port.localPosition;
    }

    /// <summary>Every transform outside the three owned sections, with its parent, local pose, active state and
    /// renderer materials, keyed by its persistent scene id.</summary>
    static Dictionary<string, string> Snapshot(Scene scene)
    {
        var owned = new[] { Section.PerimeterMachinery, Section.WallServices, Section.Overhead }
            .Select(s => CentreRoomFramework.FindSection(scene, s)).ToArray();
        var transforms = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Where(t => !owned.Any(o => t != o && t.IsChildOf(o))).ToArray();
        var ids = new GlobalObjectId[transforms.Length];
        GlobalObjectId.GetGlobalObjectIdsSlow(transforms.Cast<Object>().ToArray(), ids);
        var snapshot = new Dictionary<string, string>(transforms.Length);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform t = transforms[i];
            var r = t.GetComponent<Renderer>();
            string materials = r ? string.Join(",", r.sharedMaterials.Select(m => m ? AssetDatabase.GetAssetPath(m) + ":" + m.name : "none")) + "|" + r.enabled : "";
            snapshot[ids[i].ToString()] = FormattableString.Invariant(
                $"{t.name} parent={(t.parent ? t.parent.name : "")} p={t.localPosition:R} r={t.localRotation:R} s={t.localScale:R} active={t.gameObject.activeSelf} {materials}");
        }
        return snapshot;
    }
}
