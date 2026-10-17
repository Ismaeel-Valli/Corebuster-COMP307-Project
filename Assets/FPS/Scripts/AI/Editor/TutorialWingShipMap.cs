using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The ship map on the officer quarters' north wall, beside the ship status screen: a top-down schematic of the whole
/// level, surveyed from the scene itself so it matches what was built. The floor plan is found by probing the physics
/// scene on a half-metre grid (a floor below, nothing tall in the way), coloured by room, outlined, and marked with
/// the keycard doors, the security terminals and where the player stands. The schematic is drawn into a texture in
/// code; the labels are world text over it.
/// </summary>
public static partial class TutorialWingBuilder
{
    const string ShipMapTexturePath = TextureFolder + "/T_TW_ShipMap.png";
    const string ShipMapName = "Ship Map";

    // The texture: the map on the left, the key and route on the right, a heading band across the top.
    const int MapTexWidth = 1600, MapTexHeight = 1200;
    static readonly RectInt k_MapView = new RectInt(36, 36, 968, 1020);
    const int KeyLeft = 1048;

    // The surveyed world area (X/Z) and its grid.
    static readonly Rect k_MapWorld = Rect.MinMaxRect(-338f, -46f, -150f, 153f);
    const float MapCell = .5f;

    // The panel on the wall, and the room it faces.
    const float MapPanelWidth = 4.6f;
    const float MapPanelHeight = MapPanelWidth * MapTexHeight / MapTexWidth;

    struct MapRoom
    {
        public string Name;
        public Rect Area;
        public Color Tint;
        public Vector2 Label;
    }

    // As ZoneStreamer's zones; labels sit in open floor.
    static readonly MapRoom[] k_MapRooms =
    {
        new MapRoom { Name = "TUTORIAL WING", Area = Rect.MinMaxRect(-262f, -43f, -231.3f, .5f), Tint = new Color(.2f, .62f, .68f), Label = new Vector2(-246f, -5f) },
        new MapRoom { Name = "WORKSHOP", Area = Rect.MinMaxRect(-187f, 1f, -154f, 34.7f), Tint = new Color(.26f, .66f, .46f), Label = new Vector2(-170.5f, 25f) },
        new MapRoom { Name = "CENTRE ROOM", Area = Rect.MinMaxRect(-215f, 50.6f, -182f, 83.4f), Tint = new Color(.82f, .58f, .2f), Label = new Vector2(-198.5f, 76f) },
        new MapRoom { Name = "NORTH-WEST ROOM", Area = Rect.MinMaxRect(-252f, 99f, -218.3f, 132f), Tint = new Color(.5f, .38f, .82f), Label = new Vector2(-235f, 136.5f) },
        new MapRoom { Name = "BOSS HALL", Area = Rect.MinMaxRect(-336f, 80f, -251.5f, 152f), Tint = new Color(.78f, .24f, .38f), Label = new Vector2(-294f, 145f) },
    };
    static readonly Color k_MapCorridor = new Color(.3f, .42f, .54f);
    static readonly Color k_MapBackground = new Color(.02f, .035f, .055f);
    static readonly Color k_MapGrid = new Color(.07f, .12f, .16f);
    static readonly Color k_MapWall = new Color(.62f, .92f, 1f);
    static readonly Color k_MapRed = new Color(1f, .27f, .22f);

    [MenuItem("Tools/CoreBuster/Rebuild Ship Map")]
    public static void RebuildShipMap()
    {
        var quarters = GameObject.Find(RootName + "/Officer Quarters");
        if (!quarters)
        {
            Debug.LogError("Ship map: no " + RootName + "/Officer Quarters; run Build Tutorial Wing.");
            return;
        }
        s_F = new Fonts
        {
            Title = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath),
            Body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath),
        };
        Material Load(string name) => AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + name + ".mat");
        s_M = new Mats { PlaqueFace = Load("M_TW_PlaqueFace"), HoloFrame = Load("M_TW_HoloFrame") };
        var old = quarters.transform.Find(ShipMapName);
        if (old) Object.DestroyImmediate(old.gameObject);
        Physics.SyncTransforms();
        var frame = Measure();
        BuildShipMap(quarters.transform, frame);
        EditorSceneManager.MarkSceneDirty(quarters.scene);
    }

    // On the north wall west of the sidearm rack's shelves, its foot at 0.8 m.
    static void BuildShipMap(Transform parent, Frame f)
    {
        float spawnX = f.QuartersEast - 3.55f;
        var position = new Vector3(spawnX - 5.6f, f.Floor + .8f + MapPanelHeight * .5f, f.QuartersNorth - .06f);
        var standHere = new Vector2(position.x, f.QuartersNorth - 1.4f);

        var labels = new List<(Vector2 pixel, string text, bool title, float cap, Color color, TextAlignmentOptions align, float width)>();
        var texture = DrawShipMap(standHere, labels);
        var group = new GameObject(ShipMapName).transform;
        group.SetParent(parent, false);
        group.SetPositionAndRotation(position, Quaternion.identity);

        var backing = GameObject.CreatePrimitive(PrimitiveType.Cube);
        backing.name = "Panel";
        Object.DestroyImmediate(backing.GetComponent<Collider>());
        backing.transform.SetParent(group, false);
        backing.transform.localPosition = new Vector3(0f, 0f, .03f);
        backing.transform.localScale = new Vector3(MapPanelWidth + .04f, MapPanelHeight + .04f, .05f);
        backing.GetComponent<Renderer>().sharedMaterial = s_M.PlaqueFace;

        var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.name = "Schematic";
        Object.DestroyImmediate(screen.GetComponent<Collider>());
        screen.transform.SetParent(group, false);
        screen.transform.localPosition = new Vector3(0f, 0f, -.002f);
        screen.transform.localScale = new Vector3(MapPanelWidth, MapPanelHeight, 1f);
        var renderer = screen.GetComponent<Renderer>();
        var material = Emissive("M_TW_ShipMap", Color.white, 1f, texture);
        // A matte screen: a glossy one caught the quarters' hanging light as a broad glare across the plan.
        material.SetFloat("_Smoothness", .15f);
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;

        float w = MapPanelWidth, h = MapPanelHeight;
        MeshObject("Frame", group, SaveMesh("SM_TW_ScreenFrame_" + Mathf.RoundToInt(w * 10) + "x" + Mathf.RoundToInt(h * 10), m =>
        {
            m.Box(new Vector3(0f, h * .5f, 0f), new Vector3(w + .08f, .04f, .02f));
            m.Box(new Vector3(0f, -h * .5f, 0f), new Vector3(w + .08f, .04f, .02f));
            m.Box(new Vector3(w * .5f, 0f, 0f), new Vector3(.04f, h, .02f));
            m.Box(new Vector3(-w * .5f, 0f, 0f), new Vector3(.04f, h, .02f));
        }), new[] { s_M.HoloFrame }, Vector3.zero, Quaternion.identity, local: true);

        var face = new GameObject("Text").transform;
        face.SetParent(group, false);
        face.localPosition = new Vector3(0f, 0f, -.01f);
        Vector2 Local(Vector2 pixel) => new Vector2((pixel.x / MapTexWidth - .5f) * w, (pixel.y / MapTexHeight - .5f) * h);
        float metresPerPixel = w / MapTexWidth;
        int n = 0;
        foreach (var (pixel, text, title, cap, color, align, width) in labels)
        {
            var size = new Vector2(width * metresPerPixel, cap * 2.6f);
            var local = Local(pixel);
            // Left- and right-aligned labels are anchored at their start or end; centred ones at their middle.
            if (align == TextAlignmentOptions.Left) local.x += size.x * .5f;
            else if (align == TextAlignmentOptions.Right) local.x -= size.x * .5f;
            var tmp = WorldText(face, "Label " + ++n + " - " + text.Split('\n')[0], text, title ? s_F.Title : s_F.Body, cap, local, size, color, align);
            if (title) tmp.characterSpacing = 4f;
        }
    }

    // ------------------------------------------------------------------ survey

    // Walkable floor on the grid: a floor surface below and nothing standing tall (walls, machines, shelving) at body
    // height. The space-ground plane under the corridors does not count, and doors count as open.
    static bool[,] SurveyFloor(out int cols, out int rows)
    {
        cols = Mathf.CeilToInt(k_MapWorld.width / MapCell);
        rows = Mathf.CeilToInt(k_MapWorld.height / MapCell);
        var walkable = new bool[cols, rows];
        var overlaps = new Collider[16];
        var boss = k_MapRooms[k_MapRooms.Length - 1].Area;
        Physics.SyncTransforms();
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
            {
                float x = k_MapWorld.xMin + (i + .5f) * MapCell, z = k_MapWorld.yMin + (j + .5f) * MapCell;
                // The boss hall stands 6 m up; everywhere else the floor is at ground level, under 4 m corridor roofs.
                float top = boss.Contains(new Vector2(x, z)) ? 8.6f : 2.6f;
                if (!Physics.Raycast(new Vector3(x, top, z), Vector3.down, out var hit, top + 1f, ~0, QueryTriggerInteraction.Ignore)) continue;
                // The ground plane is the floor the rooms' kit tiles stand on (the tiles have no colliders), but outside
                // the rooms it is only the space under the corridors.
                if (hit.normal.y < .6f || (IsSpaceGround(hit.collider) && !k_MapRooms.Any(r => r.Area.Contains(new Vector2(x, z))))) continue;
                int count = Physics.OverlapBoxNonAlloc(new Vector3(x, hit.point.y + 1.4f, z), new Vector3(.2f, .7f, .2f), overlaps,
                    Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                bool blocked = false;
                for (int k = 0; k < count && !blocked; k++) blocked = Blocks(overlaps[k]);
                walkable[i, j] = !blocked;
            }

        // One pass of a majority filter clears single-cell specks (a chair leg, a pipe) without moving the walls.
        var smoothed = (bool[,])walkable.Clone();
        for (int i = 1; i < cols - 1; i++)
            for (int j = 1; j < rows - 1; j++)
            {
                int open = 0;
                for (int di = -1; di <= 1; di++)
                    for (int dj = -1; dj <= 1; dj++)
                        if (walkable[i + di, j + dj]) open++;
                if (walkable[i, j] && open <= 2) smoothed[i, j] = false;
                else if (!walkable[i, j] && open >= 7) smoothed[i, j] = true;
            }
        return smoothed;
    }

    static bool IsSpaceGround(Collider c) => c.transform.parent == null && c.name == "Plane (1)";

    static bool Blocks(Collider c)
    {
        if (c.bounds.size.y < 2f) return false;
        if (c is CharacterController || c.GetComponentInParent<SecurityDoor>()) return false;
        var root = c.transform.root.name;
        return root != "===== ENEMIES =====" && root != "===== PLAYER =====";
    }

    // ------------------------------------------------------------------ drawing

    static Texture2D DrawShipMap(Vector2 standHere,
        List<(Vector2 pixel, string text, bool title, float cap, Color color, TextAlignmentOptions align, float width)> labels)
    {
        var walkable = SurveyFloor(out int cols, out int rows);
        var pixels = new Color32[MapTexWidth * MapTexHeight];
        Color32 background = k_MapBackground;
        for (int p = 0; p < pixels.Length; p++) pixels[p] = background;

        float scale = Mathf.Min(k_MapView.width / k_MapWorld.width, k_MapView.height / k_MapWorld.height);
        float offsetX = k_MapView.x + (k_MapView.width - k_MapWorld.width * scale) * .5f;
        float offsetY = k_MapView.y + (k_MapView.height - k_MapWorld.height * scale) * .5f;
        Vector2 ToPixel(Vector2 world) => new Vector2(offsetX + (world.x - k_MapWorld.xMin) * scale, offsetY + (world.y - k_MapWorld.yMin) * scale);
        int mapX0 = Mathf.FloorToInt(offsetX), mapY0 = Mathf.FloorToInt(offsetY);
        int mapX1 = Mathf.CeilToInt(offsetX + k_MapWorld.width * scale), mapY1 = Mathf.CeilToInt(offsetY + k_MapWorld.height * scale);

        bool Open(int px, int py)
        {
            float x = k_MapWorld.xMin + (px + .5f - offsetX) / scale, z = k_MapWorld.yMin + (py + .5f - offsetY) / scale;
            int i = Mathf.FloorToInt((x - k_MapWorld.xMin) / MapCell), j = Mathf.FloorToInt((z - k_MapWorld.yMin) / MapCell);
            return i >= 0 && j >= 0 && i < cols && j < rows && walkable[i, j];
        }

        // The 4 m grid, faint under everything.
        for (int px = mapX0; px < mapX1; px++)
            for (int py = mapY0; py < mapY1; py++)
            {
                float x = k_MapWorld.xMin + (px + .5f - offsetX) / scale, z = k_MapWorld.yMin + (py + .5f - offsetY) / scale;
                float gx = Mathf.Abs(Mathf.Repeat(x + 2f, 4f) - 2f) * scale, gz = Mathf.Abs(Mathf.Repeat(z + 2f, 4f) - 2f) * scale;
                if (Mathf.Min(gx, gz) > 2f * scale - .6f) pixels[py * MapTexWidth + px] = k_MapGrid;
            }

        // Floor, tinted by room, with a bright edge where floor meets wall.
        var open = new bool[MapTexWidth, MapTexHeight];
        for (int px = mapX0; px < mapX1; px++)
            for (int py = mapY0; py < mapY1; py++)
                open[px, py] = Open(px, py);
        for (int px = mapX0 + 2; px < mapX1 - 2; px++)
            for (int py = mapY0 + 2; py < mapY1 - 2; py++)
            {
                if (!open[px, py]) continue;
                bool edge = false;
                for (int dx = -2; dx <= 2 && !edge; dx++)
                    for (int dy = -2; dy <= 2 && !edge; dy++)
                        if (dx * dx + dy * dy <= 5 && !open[px + dx, py + dy]) edge = true;
                var world = new Vector2(k_MapWorld.xMin + (px + .5f - offsetX) / scale, k_MapWorld.yMin + (py + .5f - offsetY) / scale);
                Color tint = k_MapCorridor;
                foreach (var room in k_MapRooms)
                    if (room.Area.Contains(world)) tint = room.Tint;
                Color under = pixels[py * MapTexWidth + px];
                pixels[py * MapTexWidth + px] = edge ? k_MapWall : Color.Lerp(tint * .42f, under * 1.6f, .25f);
            }

        // Doors: a bar across each doorway in its keycard's colour (sealed doors red, the quarters door plain).
        var cardOf = new Dictionary<SecurityDoor, Color>();
        foreach (var point in Object.FindObjectsByType<KeycardAccessPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (point.RequiredCard)
                foreach (var door in point.Doors)
                    if (door) cardOf[door] = point.RequiredCard.Tint;
        foreach (var master in Object.FindObjectsByType<MasterTerminal>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            foreach (var door in master.Doors)
                if (door) cardOf[door] = k_MapRed;
        foreach (var door in Object.FindObjectsByType<SecurityDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var frame = door.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == "Frame");
            Bounds b = frame ? frame.bounds : new Bounds(door.transform.position, new Vector3(3.7f, 3f, .3f));
            bool alongX = b.size.x >= b.size.z;
            var centre = new Vector2(b.center.x, b.center.z);
            var half = alongX ? new Vector2(b.size.x * .5f, .75f) : new Vector2(.75f, b.size.z * .5f);
            Color color = cardOf.TryGetValue(door, out var c) ? c : new Color(.75f, .8f, .86f);
            FillRect(pixels, ToPixel(centre - half - Vector2.one * .45f), ToPixel(centre + half + Vector2.one * .45f), k_MapBackground);
            FillRect(pixels, ToPixel(centre - half), ToPixel(centre + half), color);
        }

        // Security terminals: a diamond in the colour of the card each one gives (the master terminal white).
        foreach (var terminal in Object.FindObjectsByType<KeycardTerminal>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (terminal.Reward)
                Diamond(pixels, ToPixel(new Vector2(terminal.transform.position.x, terminal.transform.position.z)), 2.2f * scale, terminal.Reward.Tint);
        foreach (var master in Object.FindObjectsByType<MasterTerminal>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Diamond(pixels, ToPixel(new Vector2(master.transform.position.x, master.transform.position.z)), 2.2f * scale, Color.white);

        // You are here.
        var here = ToPixel(standHere);
        Ring(pixels, here, 3.4f * scale, 2.2f, k_Amber);
        Disc(pixels, here, 1.2f * scale, k_Amber);
        labels.Add((here + new Vector2(4.8f * scale, 0f), "YOU ARE HERE", true, .07f, k_Amber, TextAlignmentOptions.Left, 260f));

        // Room names on darkened plates (the floor plan still shows through), underlined in the room's colour.
        foreach (var room in k_MapRooms)
        {
            var at = ToPixel(room.Label);
            float plate = room.Name.Length * 13f + 34f;
            Shade(pixels, at - new Vector2(plate * .5f, 18f), at + new Vector2(plate * .5f, 18f), .6f);
            FillRect(pixels, at - new Vector2(plate * .5f, 18f), at + new Vector2(plate * .5f, -15f), room.Tint);
            labels.Add((at, room.Name, true, .068f, Color.Lerp(room.Tint, Color.white, .55f), TextAlignmentOptions.Center, plate));
        }

        // Heading band.
        FillRect(pixels, new Vector2(0f, 1072f), new Vector2(MapTexWidth, 1076f), k_MapWall * .7f);
        labels.Add((new Vector2(40f, 1150f), "SHIP LAYOUT  /  DECK SCHEMATIC", true, .14f, Color.white, TextAlignmentOptions.Left, 1100f));
        labels.Add((new Vector2(42f, 1100f), "Top view  •  north is straight ahead of you  •  grid squares are 4 × 4 m", false, .05f,
            new Color(.7f, .82f, .9f), TextAlignmentOptions.Left, 1100f));

        // The key.
        float y = 1010f;
        labels.Add((new Vector2(KeyLeft, y), "MAP KEY", true, .085f, k_Cyan, TextAlignmentOptions.Left, 500f));
        y -= 62f;
        void Key(System.Action<Vector2> swatch, string text)
        {
            swatch(new Vector2(KeyLeft + 22f, y));
            labels.Add((new Vector2(KeyLeft + 60f, y), text, false, .052f, new Color(.88f, .93f, 1f), TextAlignmentOptions.Left, 480f));
            y -= 46f;
        }
        void Swatch(Vector2 at, Color fill, bool outlined)
        {
            if (outlined) FillRect(pixels, at - new Vector2(20f, 14f), at + new Vector2(20f, 14f), k_MapWall);
            FillRect(pixels, at - new Vector2(17f, 11f), at + new Vector2(17f, 11f), fill);
        }
        Key(at => Swatch(at, k_MapCorridor * .42f, true), "Corridors (walkable floor, outlined by its walls)");
        Key(at => Swatch(at, k_MapRooms[1].Tint * .42f, true), "Rooms, each in its own colour");
        Key(at => Swatch(at, new Color(.82f, .86f, .95f), false), "Door: BLACK keycard");
        Key(at => Swatch(at, new Color(.25f, .62f, 1f), false), "Doors: BLUE keycard");
        Key(at => Swatch(at, new Color(1f, .82f, .2f), false), "Door: YELLOW keycard");
        Key(at => Swatch(at, k_MapRed, false), "Boss Hall door: all three cards at the master terminal");
        Key(at => Diamond(pixels, at, 15f, k_Cyan), "Security terminal (diamond in the colour of the card it gives)");
        Key(at => { Ring(pixels, at, 15f, 2.2f, k_Amber); Disc(pixels, at, 5f, k_Amber); }, "You are here");

        y -= 30f;
        labels.Add((new Vector2(KeyLeft, y), "ROUTE", true, .085f, k_Cyan, TextAlignmentOptions.Left, 500f));
        y -= 58f;
        string[] route =
        {
            "1   Revived here in the officer quarters.",
            "2   Train in the tutorial wing; earn the BLACK keycard.",
            "3   Workshop: hold its terminal against the waves for the BLUE keycard.",
            "4   Centre Room: its terminal gives the YELLOW keycard.",
            "5   North-West Room: its terminal gives the RED keycard.",
            "6   All three at the master terminal open the BOSS HALL.",
        };
        foreach (var line in route)
        {
            labels.Add((new Vector2(KeyLeft, y), line, false, .052f, new Color(.88f, .93f, 1f), TextAlignmentOptions.Left, 520f));
            y -= 56f;
        }

        var texture = new Texture2D(MapTexWidth, MapTexHeight, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        texture.Apply();
        File.WriteAllBytes(ShipMapTexturePath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(ShipMapTexturePath, ImportAssetOptions.ForceUpdate);
        if (AssetImporter.GetAtPath(ShipMapTexturePath) is TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(ShipMapTexturePath);
    }

    static void Put(Color32[] pixels, int x, int y, Color color)
    {
        if (x < 0 || y < 0 || x >= MapTexWidth || y >= MapTexHeight) return;
        pixels[y * MapTexWidth + x] = color;
    }

    static void FillRect(Color32[] pixels, Vector2 a, Vector2 b, Color color)
    {
        int x0 = Mathf.RoundToInt(Mathf.Min(a.x, b.x)), x1 = Mathf.RoundToInt(Mathf.Max(a.x, b.x));
        int y0 = Mathf.RoundToInt(Mathf.Min(a.y, b.y)), y1 = Mathf.RoundToInt(Mathf.Max(a.y, b.y));
        for (int x = x0; x < x1; x++)
            for (int y = y0; y < y1; y++)
                Put(pixels, x, y, color);
    }

    // Darkens what is already drawn, towards the background, by the given amount.
    static void Shade(Color32[] pixels, Vector2 a, Vector2 b, float amount)
    {
        int x0 = Mathf.RoundToInt(Mathf.Min(a.x, b.x)), x1 = Mathf.RoundToInt(Mathf.Max(a.x, b.x));
        int y0 = Mathf.RoundToInt(Mathf.Min(a.y, b.y)), y1 = Mathf.RoundToInt(Mathf.Max(a.y, b.y));
        for (int x = Mathf.Max(x0, 0); x < Mathf.Min(x1, MapTexWidth); x++)
            for (int y = Mathf.Max(y0, 0); y < Mathf.Min(y1, MapTexHeight); y++)
                pixels[y * MapTexWidth + x] = Color.Lerp(pixels[y * MapTexWidth + x], k_MapBackground, amount);
    }

    static void Disc(Color32[] pixels, Vector2 centre, float radius, Color color)
    {
        for (int x = Mathf.FloorToInt(centre.x - radius); x <= Mathf.CeilToInt(centre.x + radius); x++)
            for (int y = Mathf.FloorToInt(centre.y - radius); y <= Mathf.CeilToInt(centre.y + radius); y++)
                if ((new Vector2(x + .5f, y + .5f) - centre).sqrMagnitude <= radius * radius) Put(pixels, x, y, color);
    }

    static void Ring(Color32[] pixels, Vector2 centre, float radius, float width, Color color)
    {
        for (int x = Mathf.FloorToInt(centre.x - radius - width); x <= Mathf.CeilToInt(centre.x + radius + width); x++)
            for (int y = Mathf.FloorToInt(centre.y - radius - width); y <= Mathf.CeilToInt(centre.y + radius + width); y++)
                if (Mathf.Abs((new Vector2(x + .5f, y + .5f) - centre).magnitude - radius) <= width * .5f) Put(pixels, x, y, color);
    }

    static void Diamond(Color32[] pixels, Vector2 centre, float radius, Color color)
    {
        for (int x = Mathf.FloorToInt(centre.x - radius - 3f); x <= Mathf.CeilToInt(centre.x + radius + 3f); x++)
            for (int y = Mathf.FloorToInt(centre.y - radius - 3f); y <= Mathf.CeilToInt(centre.y + radius + 3f); y++)
            {
                float d = Mathf.Abs(x + .5f - centre.x) + Mathf.Abs(y + .5f - centre.y);
                if (d <= radius) Put(pixels, x, y, color);
                else if (d <= radius + 3f) Put(pixels, x, y, k_MapBackground);
            }
    }
}
