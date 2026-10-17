using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Section = NorthWestRoomFramework.Section;

/// <summary>
/// Room 3 materials and shell rendering (corebuster_env_036). It owns Level/NorthWest_Room/Rendering and
/// Assets/FPS/Art/NorthWestRoom/Rendering/. One build:
///  - generates three first-principles weathering textures (metal, wall plating, floor plate);
///  - applies the corebuster_art_005 palette to every existing Room 3 material, compensating each tint for its texture's
///    mean so the effective albedo equals the palette swatch (the kit's own albedo textures average 0.07 and 0.04);
///  - creates the missing red-warning and electric-emissive materials, and owned lit copies of the Modular Pack materials;
///  - builds collider-free, inward-facing lit skins of the original shell (the Centre Room Room_Surfaces technique), split
///    into patches so no skin collects more than a few lights, with world-space planar UVs.
/// The original shell, the shared pack materials and everything outside Level/NorthWest_Room are never edited.
/// </summary>
public static class NorthWestRoomRendering
{
    const string Owner = "corebuster_env_036";
    const string Folder = NorthWestRoomFramework.AssetRoot + "/Rendering";
    const string PalettePath = NorthWestRoomFramework.AssetRoot + "/NorthWestRoomPalette.json";
    const string Output = "ReviewCaptures/NorthWestRoom/036";
    const string SkinGroup = "Room_Surfaces";
    const float SkinOffset = .008f, FloorSkinOffset = .006f, LuminanceFloor = .22f, MetallicCeiling = .4f;
    const float WallTile = 3.2f, FloorTile = 2.05f;
    // Review of the first build: tan walls turned vivid orange under warm light, and a floor as bright as the walls lost the
    // Start Workshop's dark steel. Walls use the dominant warm-grey swatch; the floor is that swatch one step darker.
    const float FloorShade = .72f;
    const string SrcFloor = "Assets/SciFi Warehouse Kit/Art/Materials/Floor Tile Mat.mat";
    const string SrcWalls = "Assets/SciFi Warehouse Kit/Art/Materials/Walls.mat";
    const string SrcStructure = "Assets/SciFi Warehouse Kit/Art/Materials/Structure Mat.mat";
    const string GraphitePath = NorthWestRoomFramework.AssetRoot + "/Electrical_Systems/Materials/M_Electrical_Graphite.mat";
    const string PackFolder = "Assets/Sci-Fi Styled Modular Pack/";

    // ---- palette data --------------------------------------------------------------------------------------------

    [Serializable] class Swatch { public string id; public float[] baseColor, emission; public float metallic, smoothness; }
    [Serializable] class Audit { public string path, classification; public float[] targetBaseColor, targetColour; public float targetMetallic, targetSmoothness, targetIntensity, targetFlicker; }
    [Serializable] class Palette { public Swatch[] swatches; public Audit[] existingMaterialAudit; }

    enum Weave { Metal, Wall, Floor }

    sealed class Finish
    {
        public string path; public Color target; public float metallic, smoothness; public Weave weave;
    }

    static Color C(float[] v) => new Color(v[0], v[1], v[2], 1);

    static Palette LoadPalette()
    {
        var palette = JsonUtility.FromJson<Palette>(File.ReadAllText(PalettePath));
        if (palette == null || palette.swatches == null || palette.existingMaterialAudit == null) throw new InvalidOperationException("Unreadable " + PalettePath);
        return palette;
    }

    static Swatch SwatchOf(Palette p, string id) => p.swatches.SingleOrDefault(s => s.id == id) ?? throw new InvalidOperationException("Palette has no swatch " + id);

    /// <summary>Every textured room material: the nine palette-audit rows plus the built-after-the-palette finishes.</summary>
    static List<Finish> Finishes(Palette palette)
    {
        var list = new List<Finish>();
        foreach (var a in palette.existingMaterialAudit.Where(a => a.classification == "change" && a.targetBaseColor != null))
            list.Add(new Finish { path = a.path, target = C(a.targetBaseColor), metallic = a.targetMetallic, smoothness = a.targetSmoothness, weave = Weave.Metal });
        string r = NorthWestRoomFramework.AssetRoot + "/";
        // Built from palette swatches by env_041 and env_033; they only gain the weathering texture.
        list.Add(new Finish { path = r + "Perimeter_Machinery/Materials/M_Perimeter_WarmGrey.mat", target = C(SwatchOf(palette, "warm_grey_plating").baseColor), metallic = .32f, smoothness = .34f, weave = Weave.Metal });
        list.Add(new Finish { path = r + "Perimeter_Machinery/Materials/M_Perimeter_Tan.mat", target = C(SwatchOf(palette, "tan_plating").baseColor), metallic = .25f, smoothness = .30f, weave = Weave.Metal });
        list.Add(new Finish { path = r + "Perimeter_Machinery/Materials/M_Perimeter_Ochre.mat", target = C(SwatchOf(palette, "ochre").baseColor), metallic = .20f, smoothness = .28f, weave = Weave.Metal });
        list.Add(new Finish { path = r + "Pressure_Systems/Materials/M_Pressure_Rust.mat", target = C(SwatchOf(palette, "rust_red_pipework").baseColor), metallic = .22f, smoothness = .24f, weave = Weave.Metal });
        list.Add(new Finish { path = r + "Pressure_Systems/Materials/M_Pressure_Brass.mat", target = new Color(.62f, .42f, .16f), metallic = .38f, smoothness = .40f, weave = Weave.Metal });
        list.Add(new Finish { path = r + "Pressure_Systems/Materials/M_Pressure_Enamel.mat", target = new Color(.72f, .54f, .14f), metallic = .18f, smoothness = .28f, weave = Weave.Metal });
        return list;
    }

    // ---- textures ------------------------------------------------------------------------------------------------

    sealed class Weaves { public Texture2D metal, wall, floor; public Color metalMean, wallMean, floorMean; }

    static string TexturePath(Weave w) => Folder + "/Textures/T_Room3_" + (w == Weave.Metal ? "WeatheredMetal" : w == Weave.Wall ? "WallPlating" : "FloorPlate") + "_Albedo.png";

    static Weaves EnsureTextures()
    {
        EnsureFolder(Folder + "/Textures");
        MeanCache.Clear();
        var w = new Weaves();
        foreach (Weave kind in Enum.GetValues(typeof(Weave)))
        {
            string path = TexturePath(kind);
            int n = kind == Weave.Metal ? 512 : 1024;
            byte[] png = EncodePng(kind == Weave.Metal ? PaintMetal(n) : kind == Weave.Wall ? PaintWall(n) : PaintFloor(n), n);
            bool exists = File.Exists(path);
            if (!exists || !File.ReadAllBytes(path).SequenceEqual(png))
            {
                File.WriteAllBytes(path, png);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); importer = (TextureImporter)AssetImporter.GetAtPath(path); }
            if (!importer.isReadable || !importer.sRGBTexture || !importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Repeat ||
                importer.textureCompression != TextureImporterCompression.Uncompressed || importer.anisoLevel != 8)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true; importer.isReadable = true; importer.mipmapEnabled = true; importer.anisoLevel = 8;
                importer.wrapMode = TextureWrapMode.Repeat; importer.filterMode = FilterMode.Trilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path) ?? throw new InvalidOperationException("Could not import " + path);
            NorthWestRoomFramework.RequireOwnedAsset(tex, Section.Rendering);
            var mean = MeanLinear(tex);
            if (kind == Weave.Metal) { w.metal = tex; w.metalMean = mean; }
            else if (kind == Weave.Wall) { w.wall = tex; w.wallMean = mean; }
            else { w.floor = tex; w.floorMean = mean; }
        }
        return w;
    }

    static byte[] EncodePng(float[] linear, int n)
    {
        var pixels = new Color32[n * n];
        for (int i = 0; i < pixels.Length; i++)
        {
            byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(Mathf.LinearToGammaSpace(linear[i])) * 255f);
            pixels[i] = new Color32(v, v, v, 255);
        }
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false, false);
        tex.SetPixels32(pixels);
        byte[] bytes = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        return bytes;
    }

    /// <summary>Exact mean of a readable texture in linear space, or an approximate GPU mean when it is not readable.</summary>
    static readonly Dictionary<Texture2D, Color> MeanCache = new Dictionary<Texture2D, Color>();
    static float[] s_Linear;

    static Color MeanLinear(Texture2D t)
    {
        if (MeanCache.TryGetValue(t, out var cached)) return cached;
        var mean = ComputeMean(t);
        MeanCache[t] = mean;
        return mean;
    }

    static Color ComputeMean(Texture2D t)
    {
        if (t.isReadable)
        {
            if (s_Linear == null) { s_Linear = new float[256]; for (int i = 0; i < 256; i++) s_Linear[i] = Mathf.GammaToLinearSpace(i / 255f); }
            double r = 0, g = 0, b = 0;
            var px = t.GetPixels32();
            foreach (var p in px) { r += s_Linear[p.r]; g += s_Linear[p.g]; b += s_Linear[p.b]; }
            return new Color((float)(r / px.Length), (float)(g / px.Length), (float)(b / px.Length), 1);
        }
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return Color.white;
        var rt = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;
        Graphics.Blit(t, rt);
        RenderTexture.active = rt;
        var read = new Texture2D(32, 32, TextureFormat.RGBA32, false, true);
        read.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
        var all = read.GetPixels();
        RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(read);
        Color sum = Color.black;
        foreach (var c in all) sum += c;
        sum /= all.Length; sum.a = 1;
        return sum;
    }

    // Tileable value noise (wraps at its period) for first-principles surface wear.
    static int Mod(int a, int m) => ((a % m) + m) % m;
    static float Rand(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(seed * 83492791);
            h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
    static float Value(float x, float y, int px, int py, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0, sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
        int xa = Mod(x0, px), xb = Mod(x0 + 1, px), ya = Mod(y0, py), yb = Mod(y0 + 1, py);
        return Mathf.Lerp(Mathf.Lerp(Rand(xa, ya, seed), Rand(xb, ya, seed), sx), Mathf.Lerp(Rand(xa, yb, seed), Rand(xb, yb, seed), sx), sy);
    }
    static float Fbm(float u, float v, int cells, int octaves, int seed)
    {
        float sum = 0, amp = .5f, norm = 0; int p = cells;
        for (int o = 0; o < octaves; o++) { sum += amp * Value(u * p, v * p, p, p, seed + o * 17); norm += amp; amp *= .5f; p *= 2; }
        return sum / norm;
    }

    static void Scratches(float[] a, int n, int count, int seed, float strength)
    {
        var rng = new System.Random(seed);
        for (int i = 0; i < count; i++)
        {
            float x = (float)rng.NextDouble() * n, y = (float)rng.NextDouble() * n, ang = (float)rng.NextDouble() * Mathf.PI;
            int len = Mathf.RoundToInt(n * (.04f + .12f * (float)rng.NextDouble()));
            float s = ((float)rng.NextDouble() - .6f) * strength, dx = Mathf.Cos(ang), dy = Mathf.Sin(ang);
            for (int k = 0; k < len; k++)
            {
                int idx = Mod((int)(y + dy * k), n) * n + Mod((int)(x + dx * k), n);
                a[idx] = Mathf.Clamp(a[idx] * (1 + s), .45f, 1f);
            }
        }
    }

    static void Chips(float[] a, int n, int count, int seed, float depth)
    {
        var rng = new System.Random(seed);
        for (int i = 0; i < count; i++)
        {
            int cx = rng.Next(n), cy = rng.Next(n), r = 1 + rng.Next(3);
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                    if (x * x + y * y <= r * r) { int idx = Mod(cy + y, n) * n + Mod(cx + x, n); a[idx] *= 1 - depth; }
        }
    }

    // Weathered painted metal: soft mottling, fine grain, vertical grime streaks, scratches and chips. Mean about 0.84.
    static float[] PaintMetal(int n)
    {
        var a = new float[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float mottle = Fbm(u, v, 4, 4, 11), grain = Value(u * 128, v * 128, 128, 128, 23), streak = Value(u * 20, v * 2, 20, 2, 31);
                a[y * n + x] = Mathf.Clamp(.88f + .14f * (mottle - .5f) + .05f * (grain - .5f) - .07f * streak * streak, .55f, 1f);
            }
        Scratches(a, n, 70, 41, .10f);
        Chips(a, n, 45, 43, .22f);
        return a;
    }

    // One 3.2 m wall panel: bevelled seam groove, rivet rows, grime gathering toward the base, mottling and scratches.
    static float[] PaintWall(int n)
    {
        var a = new float[n * n];
        const int Inset = 28, Pitch = 64;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float val = .87f + .12f * (Fbm(u, v, 4, 4, 51) - .5f) + .03f * (Value(u * 160, v * 6, 160, 6, 53) - .5f);
                val -= .10f * Mathf.Clamp01((.22f - v) / .22f) * Value(u * 24, v * 3, 24, 3, 57);
                int d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(n - 1 - x, n - 1 - y));
                if (d < 5) val *= .62f; else if (d < 9) val *= 1.05f;
                // Rivets sit on a ring Inset px in from the border, every Pitch px.
                bool onRing = Mathf.Abs(Mathf.Min(Mathf.Min(x, y), Mathf.Min(n - 1 - x, n - 1 - y)) - Inset) <= 7;
                if (onRing)
                {
                    int cx = Mathf.RoundToInt((x - Pitch * .5f) / Pitch) * Pitch + Pitch / 2, cy = Mathf.RoundToInt((y - Pitch * .5f) / Pitch) * Pitch + Pitch / 2;
                    float best = float.MaxValue;
                    foreach (var p in new[] { new Vector2(cx, Inset), new Vector2(cx, n - 1 - Inset), new Vector2(Inset, cy), new Vector2(n - 1 - Inset, cy) })
                        best = Mathf.Min(best, Vector2.Distance(new Vector2(x, y), p));
                    if (best < 5) val *= 1.04f; else if (best < 7.5f) val *= .70f;
                }
                a[y * n + x] = Mathf.Clamp(val, .5f, 1f);
            }
        Scratches(a, n, 90, 59, .09f);
        Chips(a, n, 70, 61, .20f);
        return a;
    }

    // One 2.05 m floor plate: seam groove, corner bolts, a faint diamond tread, mottling and heavy scuffing.
    static float[] PaintFloor(int n)
    {
        var a = new float[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float tread = Mathf.Abs(Mathf.Sin((x + y) * Mathf.PI * 2f / 48f)) * Mathf.Abs(Mathf.Sin((x - y) * Mathf.PI * 2f / 48f));
                float val = .86f + .12f * (Fbm(u, v, 4, 4, 71) - .5f) + .04f * (tread - .5f) + .03f * (Value(u * 128, v * 128, 128, 128, 73) - .5f);
                int d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(n - 1 - x, n - 1 - y));
                if (d < 6) val *= .60f; else if (d < 10) val *= 1.04f;
                foreach (var p in new[] { new Vector2(40, 40), new Vector2(n - 41, 40), new Vector2(40, n - 41), new Vector2(n - 41, n - 41) })
                {
                    float dist = Vector2.Distance(new Vector2(x, y), p);
                    if (dist < 9) val *= 1.04f; else if (dist < 13) val *= .68f;
                }
                a[y * n + x] = Mathf.Clamp(val, .5f, 1f);
            }
        Scratches(a, n, 160, 79, .10f);
        Chips(a, n, 60, 83, .20f);
        return a;
    }

    // ---- materials -----------------------------------------------------------------------------------------------

    static Color Tint(Color target, Color mean) =>
        new Color(Mathf.Min(1f, target.r / Mathf.Max(.01f, mean.r)), Mathf.Min(1f, target.g / Mathf.Max(.01f, mean.g)), Mathf.Min(1f, target.b / Mathf.Max(.01f, mean.b)), 1);

    static void SetBaseMap(Material m, Texture2D tex)
    {
        m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        m.SetTextureScale("_BaseMap", Vector2.one);
        m.SetTextureOffset("_BaseMap", Vector2.zero);
    }

    static void ApplyFinish(Material m, Finish f, Weaves w)
    {
        if (m.shader.name != "Universal Render Pipeline/Lit") throw new InvalidOperationException(m.name + " is not URP/Lit.");
        if (m.parent) m.parent = null;
        var tex = f.weave == Weave.Metal ? w.metal : f.weave == Weave.Wall ? w.wall : w.floor;
        var mean = f.weave == Weave.Metal ? w.metalMean : f.weave == Weave.Wall ? w.wallMean : w.floorMean;
        SetBaseMap(m, tex);
        m.SetColor("_BaseColor", Tint(f.target, mean));
        m.SetFloat("_Metallic", f.metallic);
        m.SetFloat("_Smoothness", f.smoothness);
        EditorUtility.SetDirty(m);
    }

    static void ApplyPalette(Palette palette, Weaves w)
    {
        foreach (var f in Finishes(palette))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(f.path) ?? throw new InvalidOperationException("Missing Room 3 material " + f.path);
            ApplyFinish(m, f, w);
        }
        var arcRow = palette.existingMaterialAudit.Single(a => a.path.EndsWith("M_Room3_Electric_Arc.mat", StringComparison.Ordinal));
        var arc = AssetDatabase.LoadAssetAtPath<Material>(arcRow.path) ?? throw new InvalidOperationException("Missing " + arcRow.path);
        var blue = C(arcRow.targetColour);
        arc.SetColor("_Color", blue);
        arc.SetFloat("_Intensity", arcRow.targetIntensity);
        arc.SetFloat("_Flicker", arcRow.targetFlicker);
        EditorUtility.SetDirty(arc);
    }

    static Material Own(string relative, Func<Material> create)
    {
        string path = Folder + "/Materials/" + relative;
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        var generated = create();
        generated.name = Path.GetFileNameWithoutExtension(relative);
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing) { EditorUtility.CopySerialized(generated, existing); Object.DestroyImmediate(generated); EditorUtility.SetDirty(existing); }
        else { AssetDatabase.CreateAsset(generated, path); existing = generated; }
        if (existing.parent) existing.parent = null;
        NorthWestRoomFramework.RequireOwnedAsset(existing, Section.Rendering);
        return existing;
    }

    static Material NewLit() => new Material(Shader.Find("Universal Render Pipeline/Lit"));

    static Material Emissive(Swatch s, string name)
    {
        var m = NewLit();
        m.SetColor("_BaseColor", C(s.baseColor));
        m.SetFloat("_Metallic", s.metallic);
        m.SetFloat("_Smoothness", s.smoothness);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", new Color(s.emission[0], s.emission[1], s.emission[2], 1));
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive; // URP needs AnyEmissive to preserve _EMISSION on import.
        return m;
    }

    sealed class Owned { public Material floor, wall, packBase, electric, warning, graphite; }

    static Owned BuildMaterials(Palette palette, Weaves w)
    {
        var o = new Owned();
        string GuidOf(string path) => AssetDatabase.AssetPathToGUID(path);
        // Shell copies are keyed by their source material GUID. Large diffuse-leaning surfaces: metallic is held well under the 0.4 ceiling.
        o.floor = Own("Copies/" + GuidOf(SrcFloor) + ".mat", () =>
        {
            var m = NewLit();
            var grey = C(SwatchOf(palette, "warm_grey_plating").baseColor);
            ApplyFinish(m, new Finish { target = new Color(grey.r * FloorShade, grey.g * FloorShade, grey.b * FloorShade, 1), metallic = .15f, smoothness = .30f, weave = Weave.Floor }, w);
            return m;
        });
        o.wall = Own("Copies/" + GuidOf(SrcWalls) + ".mat", () =>
        {
            var m = NewLit();
            ApplyFinish(m, new Finish { target = C(SwatchOf(palette, "warm_grey_plating").baseColor), metallic = .15f, smoothness = .30f, weave = Weave.Wall }, w);
            return m;
        });
        // Modular Pack "base color" palette repaint -> owned, flattened, lit copy that keeps the pack texture.
        string packBase = PackFolder + "Materials/Palletes/base color.mat";
        var source = AssetDatabase.LoadAssetAtPath<Material>(packBase) ?? throw new InvalidOperationException("Missing " + packBase);
        o.packBase = Own("Copies/" + GuidOf(packBase) + ".mat", () =>
        {
            var m = NewLit();
            m.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Metallic", .2f); m.SetFloat("_Smoothness", .35f);
            return m;
        });
        o.electric = Own("M_Room3_Electric_Emissive.mat", () => Emissive(SwatchOf(palette, "electric_blue_white"), "M_Room3_Electric_Emissive"));
        o.warning = Own("M_Room3_Warning_Red.mat", () => Emissive(SwatchOf(palette, "warning_red"), "M_Room3_Warning_Red"));
        o.graphite = AssetDatabase.LoadAssetAtPath<Material>(GraphitePath) ?? throw new InvalidOperationException("Missing " + GraphitePath);
        return o;
    }

    /// <summary>Per-instance replacement of every shared pack material on Room 3 renderers by an owned copy.</summary>
    static int ReplacePackMaterials(Transform root, Owned o)
    {
        int changed = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (NorthWestRoomFramework.IsRenderingSkin(r.transform)) continue;
            var materials = r.sharedMaterials;
            bool dirty = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var m = materials[i];
                string path = m ? AssetDatabase.GetAssetPath(m).Replace('\\', '/') : "";
                if (!path.StartsWith(PackFolder, StringComparison.Ordinal)) continue;
                Material copy = m.name == "blue emission" ? o.electric : path.EndsWith("Palletes/base color.mat", StringComparison.Ordinal) ? o.packBase : null;
                if (!copy) throw new InvalidOperationException($"{r.name} uses pack material '{path}' with no owned Room 3 replacement; add one before building.");
                materials[i] = copy; dirty = true; changed++;
            }
            if (!dirty) continue;
            Undo.RecordObject(r, "Replace Room 3 pack material");
            r.sharedMaterials = materials;
            if (PrefabUtility.IsPartOfPrefabInstance(r)) PrefabUtility.RecordPrefabInstancePropertyModifications(r);
        }
        return changed;
    }

    // ---- shell skins ---------------------------------------------------------------------------------------------

    struct Vertex
    {
        public Vector3 p; public Vector2 uv;
        public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex { p = Vector3.Lerp(a.p, b.p, t), uv = Vector2.Lerp(a.uv, b.uv, t) };
    }

    static List<Vertex> Clip(List<Vertex> poly, int axis, float plane, bool keepGreater)
    {
        var result = new List<Vertex>();
        if (poly.Count == 0) return result;
        Vertex previous = poly[poly.Count - 1];
        float pd = (previous.p[axis] - plane) * (keepGreater ? 1 : -1);
        foreach (var current in poly)
        {
            float cd = (current.p[axis] - plane) * (keepGreater ? 1 : -1);
            if ((cd >= 0) != (pd >= 0)) result.Add(Vertex.Lerp(previous, current, pd / (pd - cd)));
            if (cd >= 0) result.Add(current);
            previous = current; pd = cd;
        }
        return result;
    }

    /// <summary>Patches per piece along the two in-plane axes. Floors and ceilings are quartered; walls halved each way.</summary>
    static Vector2Int Patches(Vector3 inward) => new Vector2Int(2, 2);

    static Vector3 InwardOf(Bounds b, NorthWestRoomFramework.Layout layout)
    {
        if (b.center.y < layout.Floor) return Vector3.up;
        if (b.center.y > layout.report.ceilingUndersideY) return Vector3.down;
        var distances = new[] { Mathf.Abs(b.center.x - layout.interior.min.x), Mathf.Abs(b.center.x - layout.interior.max.x),
            Mathf.Abs(b.center.z - layout.interior.min.z), Mathf.Abs(b.center.z - layout.interior.max.z) };
        return new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back }[Array.IndexOf(distances, distances.Min())];
    }

    // In-plane axes (a, b) of a skin whose inward normal is given; planar UV = world (a, b) over the tile size.
    static void PlaneAxes(Vector3 inward, out int a, out int b)
    {
        if (Mathf.Abs(inward.y) > .5f) { a = 0; b = 2; }
        else if (Mathf.Abs(inward.x) > .5f) { a = 2; b = 1; }
        else { a = 0; b = 1; }
    }

    static Material SkinMaterial(Material source, Vector3 inward, Owned o)
    {
        string path = AssetDatabase.GetAssetPath(source).Replace('\\', '/');
        if (path == SrcFloor) return inward.y > .5f ? o.floor : o.wall; // the ceiling reuses the wall plating copy
        if (path == SrcWalls) return o.wall;
        if (path == SrcStructure) return o.graphite;
        throw new InvalidOperationException($"Shell material '{path}' has no Room 3 skin mapping.");
    }

    static void BuildSurfaces(Transform section, NorthWestRoomFramework.Layout layout, Owned o)
    {
        EnsureFolder(Folder + "/Shell");
        var group = Group(section, SkinGroup);
        group.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var generated = new HashSet<string>();
        var interior = layout.interior;
        foreach (var shell in layout.report.shell)
        {
            if (!GlobalObjectId.TryParse(shell.id, out var id)) throw new InvalidOperationException("Invalid measured shell ID.");
            var r = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as MeshRenderer;
            var filter = r ? r.GetComponent<MeshFilter>() : null;
            if (!filter || !filter.sharedMesh) throw new InvalidOperationException("Missing measured shell mesh " + shell.name);
            Mesh src = filter.sharedMesh;
            Vector3[] positions = src.vertices, normals = src.normals;
            Bounds b = r.bounds;
            Vector3 inward = InwardOf(b, layout);
            float offset = inward.y > .5f ? FloorSkinOffset : SkinOffset;
            PlaneAxes(inward, out int axA, out int axB);
            float tile = Mathf.Abs(inward.y) > .5f && inward.y > 0 ? FloorTile : WallTile;
            var patches = Patches(inward);
            for (int sub = 0; sub < src.subMeshCount; sub++)
            {
                var triangles = src.GetTriangles(sub);
                Material source = r.sharedMaterials[Mathf.Min(sub, r.sharedMaterials.Length - 1)];
                Material material = SkinMaterial(source, inward, o);
                for (int pa = 0; pa < patches.x; pa++)
                    for (int pb = 0; pb < patches.y; pb++)
                    {
                        var vertices = new List<Vector3>(); var outNormals = new List<Vector3>(); var outUV = new List<Vector2>(); var indices = new List<int>();
                        for (int i = 0; i < triangles.Length; i += 3)
                        {
                            var poly = new List<Vertex>();
                            Vector3 faceNormal = Vector3.zero;
                            for (int j = 0; j < 3; j++)
                            {
                                int index = triangles[i + j];
                                poly.Add(new Vertex { p = filter.transform.TransformPoint(positions[index]) });
                                faceNormal += filter.transform.TransformDirection(normals[index]).normalized;
                            }
                            // Only the room-facing surface is copied, never the outer face or a floor tile's side edge.
                            if (Vector3.Dot(faceNormal.normalized, inward) < .55f) continue;
                            // Recessed room faces move to the interior plane.
                            for (int j = 0; j < poly.Count; j++)
                            {
                                var v = poly[j];
                                for (int axis = 0; axis < 3; axis++)
                                    if (Mathf.Abs(inward[axis]) > .5f) v.p[axis] = Mathf.Clamp(v.p[axis], interior.min[axis], interior.max[axis]);
                                v.p += inward * offset; poly[j] = v;
                            }
                            for (int axis = 0; axis < 3 && poly.Count >= 3; axis++)
                            {
                                poly = Clip(poly, axis, interior.min[axis] + .003f, true);
                                poly = Clip(poly, axis, interior.max[axis] - .003f, false);
                            }
                            float a0 = b.min[axA], a1 = b.max[axA], b0 = b.min[axB], b1 = b.max[axB];
                            if (patches.x > 1)
                            {
                                if (pa > 0) poly = Clip(poly, axA, Mathf.Lerp(a0, a1, pa / (float)patches.x), true);
                                if (pa < patches.x - 1) poly = Clip(poly, axA, Mathf.Lerp(a0, a1, (pa + 1) / (float)patches.x), false);
                            }
                            if (patches.y > 1)
                            {
                                if (pb > 0) poly = Clip(poly, axB, Mathf.Lerp(b0, b1, pb / (float)patches.y), true);
                                if (pb < patches.y - 1) poly = Clip(poly, axB, Mathf.Lerp(b0, b1, (pb + 1) / (float)patches.y), false);
                            }
                            for (int j = 1; j + 1 < poly.Count; j++)
                            {
                                if (Vector3.Cross(poly[j].p - poly[0].p, poly[j + 1].p - poly[0].p).sqrMagnitude < 1e-12f) continue;
                                foreach (var v in new[] { poly[0], poly[j], poly[j + 1] })
                                {
                                    indices.Add(vertices.Count); vertices.Add(v.p); outNormals.Add(inward);
                                    outUV.Add(new Vector2(v.p[axA] / tile, v.p[axB] / tile));
                                }
                            }
                        }
                        if (vertices.Count == 0) continue;
                        string name = "Surface_" + id.targetPrefabId + "_" + id.targetObjectId + "_" + sub + "_" + pa + pb;
                        var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                        mesh.SetVertices(vertices); mesh.SetNormals(outNormals); mesh.SetUVs(0, outUV); mesh.SetTriangles(indices, 0);
                        mesh.RecalculateBounds();
                        mesh = SaveAsset(mesh, "Shell/" + name + ".asset");
                        generated.Add(AssetDatabase.GetAssetPath(mesh));
                        var t = Group(group, name);
                        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var renderer = t.gameObject.AddComponent<MeshRenderer>();
                        renderer.sharedMaterial = material;
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        renderer.receiveShadows = true;
                    }
            }
        }
        foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { Folder + "/Shell" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!generated.Contains(path)) AssetDatabase.DeleteAsset(path);
        }
        Debug.Log("North-West Room shell skins: " + group.childCount);
    }

    // ---- entry points --------------------------------------------------------------------------------------------

    [MenuItem("Tools/CoreBuster/Build North-West Room Rendering")]
    public static void BuildMenu()
    {
        var layout = NorthWestRoomFramework.MeasureLayout();
        NorthWestRoomFramework.Build(layout);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Build North-West Room Rendering");
        try { Build(layout); RequireValid(layout); Undo.CollapseUndoOperations(group); }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    public static void BuildBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        var layout = NorthWestRoomFramework.MeasureLayout();
        bool graphics = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        // The first run keeps the pre-change neighbour views; later runs compare against them again.
        if (graphics && !Directory.Exists(Output + "/Neighbours/Before")) CaptureNeighbours("Before", layout);
        NorthWestRoomFramework.Build(layout);
        var before = SnapshotOutside();
        var protectedMaterials = NorthWestRoomFramework.CaptureProtectedMaterialHashes();
        Build(layout);
        VerifyIsolatedUndo(layout);
        RequireValid(layout);
        NorthWestRoomFramework.VerifyProtectedMaterialHashes(protectedMaterials);
        if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new InvalidOperationException("Could not save Room 3 rendering.");
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        layout = NorthWestRoomFramework.MeasureLayout();
        RequireValid(layout);
        var after = SnapshotOutside();
        if (before.Count != after.Count || before.Any(p => !after.TryGetValue(p.Key, out string value) || value != p.Value))
            throw new InvalidOperationException("A scene object outside Rendering changed.");
        NorthWestRoomFramework.VerifyProtectedMaterialHashes(protectedMaterials);
        string neighbours = "neighbour captures skipped (no graphics device)";
        if (graphics) neighbours = CompareNeighbours();
        string density = NorthWestRoomDensity.Check(SceneManager.GetActiveScene(), layout, false, true) ? "density check passed" : "density check reported errors";
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/Build.txt", $"corebuster_env_036: palette applied, shell skins built; isolation, ownership, darkness/metallic, material hashes and save/reload passed ({before.Count} outside transforms unchanged); {neighbours}; {density}.\n");
        Debug.Log($"NORTH-WEST RENDERING VERIFIED: {before.Count} outside transforms unchanged; {neighbours}.");
    }

    [MenuItem("Tools/CoreBuster/Validate North-West Room Rendering")]
    public static void ValidateMenu() => RequireValid(NorthWestRoomFramework.MeasureLayout());

    public static void ValidateBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        ValidateMenu();
    }

    /// <summary>Read-only re-verification of the saved scene: checks, neighbour views, density; nothing is rebuilt.</summary>
    public static void VerifyBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        var layout = NorthWestRoomFramework.MeasureLayout();
        RequireValid(layout);
        string neighbours = CompareNeighbours();
        string density = NorthWestRoomDensity.Check(SceneManager.GetActiveScene(), layout, false, true) ? "density check passed" : "density check reported errors";
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/Verify.txt", $"corebuster_env_036 saved-scene verification: checks passed; {neighbours}; {density}.\n");
        Debug.Log($"NORTH-WEST RENDERING SAVED SCENE VERIFIED: {neighbours}; {density}.");
    }

    /// <summary>Rewrites the textures and every material asset in place; the scene is neither opened nor changed.</summary>
    [MenuItem("Tools/CoreBuster/Refresh North-West Room Materials")]
    public static void RefreshMaterials()
    {
        var palette = LoadPalette();
        var weaves = EnsureTextures();
        ApplyPalette(palette, weaves);
        BuildMaterials(palette, weaves);
        AssetDatabase.SaveAssets();
        Debug.Log("North-West Room materials refreshed from the palette; scene untouched.");
    }

    public static void NeighboursBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        Debug.Log("NORTH-WEST NEIGHBOURS: " + CompareNeighbours());
    }

    static void Build(NorthWestRoomFramework.Layout layout)
    {
        var palette = LoadPalette();
        var weaves = EnsureTextures();
        ApplyPalette(palette, weaves);
        var owned = BuildMaterials(palette, weaves);
        Transform section = NorthWestRoomFramework.BeginSectionRebuild(layout, Section.Rendering, Owner);
        BuildSurfaces(section, layout, owned);
        int replaced = ReplacePackMaterials(section.parent, owned);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"North-West Room rendering built: {replaced} pack material slots replaced.");
    }

    // ---- validation ----------------------------------------------------------------------------------------------

    static float Lum(Color c) => .2126f * c.r + .7152f * c.g + .0722f * c.b;

    internal static void RequireValid(NorthWestRoomFramework.Layout layout)
    {
        var scene = SceneManager.GetActiveScene();
        if (!NorthWestRoomFramework.Validate(scene, layout, true) || !NorthWestRoomFramework.ValidateSection(scene, layout, Section.Rendering, true))
            throw new InvalidOperationException("North-West Room framework or rendering section validation failed.");
        var rendering = NorthWestRoomFramework.FindSection(scene, Section.Rendering);
        var root = rendering.parent;
        var palette = LoadPalette();
        MeanCache.Clear();
        var errors = new List<string>();
        var skins = rendering.Find(SkinGroup);
        if (!skins) errors.Add("Rendering has no " + SkinGroup + " group.");
        else
        {
            var all = skins.GetComponentsInChildren<MeshRenderer>(true);
            if (all.Length < 100) errors.Add($"Only {all.Length} shell skins exist.");
            float floorArea = 0, ceilingArea = 0, wallArea = 0;
            foreach (var skin in all)
            {
                var mesh = skin.GetComponent<MeshFilter>().sharedMesh;
                float area = MeshArea(mesh, skin.transform);
                var n = mesh.normals.Length > 0 ? mesh.normals[0] : Vector3.zero;
                if (n.y > .5f) floorArea += area; else if (n.y < -.5f) ceilingArea += area; else wallArea += area;
                if (skin.shadowCastingMode != ShadowCastingMode.Off) errors.Add(skin.name + " casts shadows; skins sit on the shell.");
                if (skin.sharedMaterials.Length != 1 || !skin.sharedMaterial) errors.Add(skin.name + " needs exactly one material.");
            }
            float room = layout.interior.size.x * layout.interior.size.z;
            if (floorArea < room * .97f || floorArea > room * 1.001f) errors.Add($"Floor skins cover {floorArea:F1} m2 of the {room:F1} m2 interior.");
            if (ceilingArea < room * .97f || ceilingArea > room * 1.001f) errors.Add($"Ceiling skins cover {ceilingArea:F1} m2 of the {room:F1} m2 interior.");
            float walls = 2 * (layout.interior.size.x + layout.interior.size.z) * layout.interior.size.y;
            if (wallArea < walls * .85f || wallArea > walls) errors.Add($"Wall skins cover {wallArea:F1} m2 of at most {walls:F1} m2.");
            Debug.Log($"Room 3 skin coverage: floor {floorArea:F1}/{room:F1} m2, ceiling {ceilingArea:F1}/{room:F1} m2, walls {wallArea:F1}/{walls:F1} m2, {all.Length} skins.");
        }
        // Material ownership, flattening, shader family, metallic ceiling and darkness floor.
        var materials = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().ToArray();
        foreach (var m in materials)
        {
            string path = AssetDatabase.GetAssetPath(m).Replace('\\', '/');
            if (!path.StartsWith(NorthWestRoomFramework.AssetRoot + "/", StringComparison.Ordinal)) { errors.Add($"Room 3 renderer uses non-room material '{path}'."); continue; }
            if (m.parent) errors.Add($"{m.name} still has a parent material.");
            if (m.shader.name.Contains("/2D/") || m.shader.name.StartsWith("Legacy", StringComparison.Ordinal) || m.shader.name == "Standard") errors.Add($"{m.name} uses {m.shader.name}.");
            if (m.shader.name != "Universal Render Pipeline/Lit") continue;
            if (m.GetFloat("_Metallic") > MetallicCeiling + .0005f) errors.Add($"{m.name} metallic {m.GetFloat("_Metallic"):F2} exceeds {MetallicCeiling:F1}.");
            bool emissive = m.IsKeywordEnabled("_EMISSION") && Lum(m.GetColor("_EmissionColor")) > .01f;
            if (emissive || m.GetFloat("_Surface") > .5f) continue; // lit accents and transparent floor overlays
            Color effective = m.GetColor("_BaseColor");
            if (m.GetTexture("_BaseMap") is Texture2D tex) { var mean = MeanLinear(tex); effective = new Color(effective.r * mean.r, effective.g * mean.g, effective.b * mean.b, 1); }
            if (Lum(effective) < LuminanceFloor - .006f) errors.Add($"{m.name} effective luminance {Lum(effective):F3} is below {LuminanceFloor}.");
        }
        foreach (var m in materials)
            if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") && AssetDatabase.GetAssetPath(m.GetTexture("_BaseMap")).Contains("HeavyIndustrialKit"))
                errors.Add($"{m.name} still uses a kit albedo texture.");
        // Palette targets on the nine audited materials.
        foreach (var a in palette.existingMaterialAudit.Where(a => a.classification == "change"))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(a.path);
            if (!m) { errors.Add("Missing " + a.path); continue; }
            if (a.targetBaseColor != null)
            {
                var mean = m.GetTexture("_BaseMap") is Texture2D t ? MeanLinear(t) : Color.white;
                var c = m.GetColor("_BaseColor"); var eff = new Color(c.r * mean.r, c.g * mean.g, c.b * mean.b);
                var target = C(a.targetBaseColor);
                if (Mathf.Abs(eff.r - target.r) > .04f || Mathf.Abs(eff.g - target.g) > .04f || Mathf.Abs(eff.b - target.b) > .04f)
                    errors.Add($"{m.name} effective colour {eff.r:F2}/{eff.g:F2}/{eff.b:F2} differs from the palette target {target.r:F2}/{target.g:F2}/{target.b:F2}.");
                if (Mathf.Abs(m.GetFloat("_Metallic") - a.targetMetallic) > .005f || Mathf.Abs(m.GetFloat("_Smoothness") - a.targetSmoothness) > .005f)
                    errors.Add(m.name + " metallic or smoothness differs from the palette.");
            }
            else
            {
                var c = m.GetColor("_Color"); var target = C(a.targetColour);
                if (Vector4.Distance(c, target) > .01f || Mathf.Abs(m.GetFloat("_Intensity") - a.targetIntensity) > .01f || Mathf.Abs(m.GetFloat("_Flicker") - a.targetFlicker) > .01f)
                    errors.Add(m.name + " differs from the electric blue-white arc target.");
            }
        }
        foreach (string required in new[] { "M_Room3_Electric_Emissive", "M_Room3_Warning_Red" })
            if (!AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Materials/" + required + ".mat")) errors.Add("Missing " + required);
        // Floor overlays must stay above the floor skin.
        var detail = NorthWestRoomFramework.FindSection(scene, Section.FloorDetail);
        foreach (var r in detail.GetComponentsInChildren<Renderer>(true))
            if (r.bounds.min.y < layout.Floor + FloorSkinOffset + .004f) errors.Add(r.name + " would be buried by the floor skin.");
        if (rendering.GetComponentsInChildren<Light>(true).Length != 0) errors.Add("Rendering must carry no lights (corebuster_env_037).");
        if (rendering.GetComponentsInChildren<MonoBehaviour>(true).Length != 0) errors.Add("Rendering must be script-free.");
        if (errors.Count > 0) throw new InvalidOperationException("North-West rendering checks failed (" + errors.Count + "):\n  " + string.Join("\n  ", errors.Take(40)));
        Debug.Log($"NORTH-WEST RENDERING CHECKS PASSED: {materials.Length} room materials, all room-owned, flattened, lit, metallic at most {MetallicCeiling:F1} and effective luminance at least {LuminanceFloor}; palette targets met; no kit albedo texture remains.");
    }

    static float MeshArea(Mesh mesh, Transform t)
    {
        var v = mesh.vertices; var tri = mesh.triangles; double sum = 0;
        for (int i = 0; i < tri.Length; i += 3)
            sum += Vector3.Cross(t.TransformPoint(v[tri[i + 1]]) - t.TransformPoint(v[tri[i]]), t.TransformPoint(v[tri[i + 2]]) - t.TransformPoint(v[tri[i]])).magnitude * .5;
        return (float)sum;
    }

    static void VerifyIsolatedUndo(NorthWestRoomFramework.Layout layout)
    {
        var scene = SceneManager.GetActiveScene();
        var rendering = NorthWestRoomFramework.FindSection(scene, Section.Rendering);
        var pressure = NorthWestRoomFramework.FindSection(scene, Section.PressureSystems);
        int r = rendering.GetComponentsInChildren<Transform>(true).Length, p = pressure.GetComponentsInChildren<Transform>(true).Length;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Verify isolated rendering section Undo");
        NorthWestRoomFramework.BeginSectionRebuild(layout, Section.Rendering, Owner);
        Undo.CollapseUndoOperations(group);
        if (rendering.childCount != 0 || pressure.GetComponentsInChildren<Transform>(true).Length != p) throw new InvalidOperationException("Rendering section clear changed another section.");
        Undo.PerformUndo();
        if (rendering.GetComponentsInChildren<Transform>(true).Length != r || pressure.GetComponentsInChildren<Transform>(true).Length != p) throw new InvalidOperationException("Rendering section Undo did not restore exactly the cleared section.");
    }

    // ---- captures ------------------------------------------------------------------------------------------------

    const int Width = 960, Height = 540;

    static List<(string name, Vector3 position, Vector3 target)> NeighbourViews(NorthWestRoomFramework.Layout layout)
    {
        var report = layout.report;
        var east = report.gates.Single(g => g.name == NorthWestRoomSurvey.EastDoorName);
        var boss = report.gates.Single(g => g.name == NorthWestRoomSurvey.BossDoorName);
        float eye = report.playerEyeHeight;
        Vector3 Steps(float d)
        {
            var s = report.bossApproach.OrderBy(p => Mathf.Abs(p.distance - d)).First();
            return boss.position + boss.outward * d + Vector3.up * (s.groundY + eye - report.floorTopY);
        }
        return new List<(string, Vector3, Vector3)>
        {
            ("East_Door_Outside", east.position + east.outward * 4f + Vector3.up * eye, east.position + Vector3.up * 1.6f),
            ("East_Door_Wide", east.position + east.outward * 8f + Vector3.up * eye, east.position + Vector3.up * 3f),
            ("East_Corridor_To_Centre", east.position + east.outward * 3f + Vector3.up * eye, new Vector3(-196.5f, 1.8f, 90.17f)),
            ("East_Corridor_Mid", new Vector3(-209f, eye, 96f), new Vector3(-218.8f, 3f, 108.8f)),
            ("Access_Machine_View", report.accessStanding.position + Vector3.up * eye, new Vector3(-218.5f, 3f, 111f)),
            ("Boss_Steps", Steps(6f), boss.position + Vector3.up * 2.2f),
            ("Boss_Landing_Back", Steps(11f), boss.position + Vector3.up * 3f),
        };
    }

    static Camera MakeCamera(string name)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.enabled = false; camera.fieldOfView = 70; camera.nearClipPlane = .05f; camera.farClipPlane = 300;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        return camera;
    }

    static byte[] Shoot(Camera camera, Vector3 position, Vector3 target)
    {
        camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
        var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = rt;
            camera.Render(); camera.Render();
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            return texture.EncodeToPNG();
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(texture);
        }
    }

    static void CaptureNeighbours(string set, NorthWestRoomFramework.Layout layout)
    {
        string dir = Output + "/Neighbours/" + set;
        Directory.CreateDirectory(dir);
        var camera = MakeCamera("Temporary Room 3 neighbour camera");
        try { foreach (var v in NeighbourViews(layout)) File.WriteAllBytes(dir + "/" + v.name + ".png", Shoot(camera, v.position, v.target)); }
        finally { Object.DestroyImmediate(camera.gameObject); }
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Neighbour capture dirtied MainScene.");
    }

    // The Boss Hall Door leaves stop short of the lintel, so a thin strip of the room's own interior shows above them from
    // the incline. That strip is the only part of a neighbour view that may legitimately change (pixel x, y from the top, width, height).
    // Outside those windows only edge aliasing is tolerated: a few pixels, each off by at most NoiseDelta channel levels.
    const int NoiseDelta = 32, NoiseBudget = 32;

    static readonly Dictionary<string, RectInt> InteriorWindows = new Dictionary<string, RectInt>
    {
        ["Boss_Steps"] = new RectInt(410, 205, 130, 35),
        ["Boss_Landing_Back"] = new RectInt(440, 255, 90, 30),
    };

    /// <summary>Captures the "After" set, diffs it with "Before" and fails when a view changed outside a room-interior window.</summary>
    static string CompareNeighbours()
    {
        CaptureNeighbours("After", NorthWestRoomFramework.MeasureLayout());
        string before = Output + "/Neighbours/Before", after = Output + "/Neighbours/After", diff = Output + "/Neighbours/Diff";
        Directory.CreateDirectory(diff);
        var lines = new List<string>(); int failures = 0;
        foreach (string file in Directory.GetFiles(before, "*.png").OrderBy(f => f, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            var a = new Texture2D(2, 2); var b = new Texture2D(2, 2);
            a.LoadImage(File.ReadAllBytes(file)); b.LoadImage(File.ReadAllBytes(after + "/" + name));
            var pa = a.GetPixels32(); var pb = b.GetPixels32();
            var mask = new Color32[pa.Length]; int changed = 0, outside = 0, bright = 0, largest = 0;
            string view = Path.GetFileNameWithoutExtension(name);
            bool windowed = InteriorWindows.TryGetValue(view, out var window);
            for (int i = 0; i < pa.Length; i++)
            {
                int d = Mathf.Max(Mathf.Abs(pa[i].r - pb[i].r), Mathf.Abs(pa[i].g - pb[i].g), Mathf.Abs(pa[i].b - pb[i].b));
                bool hit = d > 4; // above sampling noise
                if (hit)
                {
                    changed++; largest = Mathf.Max(largest, d);
                    if (!windowed || !window.Contains(new Vector2Int(i % a.width, a.height - 1 - i / a.width))) { outside++; if (d > NoiseDelta) bright++; }
                }
                mask[i] = hit ? new Color32(255, 0, 0, 255) : new Color32((byte)(pa[i].r / 3), (byte)(pa[i].g / 3), (byte)(pa[i].b / 3), 255);
            }
            var m = new Texture2D(a.width, a.height, TextureFormat.RGB24, false); m.SetPixels32(mask);
            File.WriteAllBytes(diff + "/" + name, m.EncodeToPNG());
            Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(m);
            lines.Add($"{view}: {changed} px changed, {outside} outside the room-interior window ({bright} above delta {NoiseDelta}), largest channel delta {largest}");
            if (bright > 0 || outside > NoiseBudget) failures++;
        }
        File.WriteAllText(Output + "/Neighbours/Report.txt", string.Join("\n", lines) + "\n");
        string summary = $"{lines.Count} neighbour views, {failures} changed outside a room-interior window";
        if (failures > 0) throw new InvalidOperationException("Neighbour views changed: " + summary + "\n" + string.Join("\n", lines));
        return summary;
    }

    public static void CaptureBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        CaptureMenu();
    }

    [MenuItem("Tools/CoreBuster/Capture North-West Room Rendering")]
    public static void CaptureMenu()
    {
        var layout = NorthWestRoomFramework.MeasureLayout();
        RequireValid(layout);
        var report = layout.report;
        float floor = report.floorTopY, eye = report.playerEyeHeight;
        var east = report.gates.Single(g => g.name == NorthWestRoomSurvey.EastDoorName);
        var views = new[]
        {
            ("East_Entry", east.position - east.outward * 4f + Vector3.up * eye, report.centre + Vector3.up * 1.6f),
            ("Room_From_SouthEast", new Vector3(report.interior.max.x - 4f, eye, report.interior.min.z + 5f), new Vector3(report.interior.min.x + 6f, floor + 3f, report.interior.max.z - 6f)),
            ("Room_From_NorthWest", new Vector3(report.interior.min.x + 5f, eye, report.interior.max.z - 6f), new Vector3(report.interior.max.x - 6f, floor + 3f, report.interior.min.z + 6f)),
            ("Red_Terminal", new Vector3(report.redTerminal.standing.position.x, report.redTerminal.eyeY, report.redTerminal.standing.position.z), report.redTerminal.focus.position),
            ("Master_Terminal", new Vector3(report.masterTerminal.standing.position.x, report.masterTerminal.eyeY, report.masterTerminal.standing.position.z), report.masterTerminal.lookAt.position),
            ("Ceiling_Overhead", report.centre + Vector3.up * 1.2f, report.centre + new Vector3(4f, 9f, 3f)),
            ("South_Machinery", new Vector3(report.centre.x, floor + eye - .18f, report.centre.z - 6f), new Vector3(report.centre.x, floor + 1.8f, report.interior.min.z + 1.7f)),
        };
        Directory.CreateDirectory(Output);
        var camera = MakeCamera("Temporary Room 3 rendering camera");
        // Unsaved neutral review lamps: the room has no lighting until corebuster_env_037.
        var lamps = new List<GameObject>();
        foreach (var offset in new[] { new Vector3(0, 0, 0), new Vector3(-8, 0, -8), new Vector3(8, 0, -8), new Vector3(-8, 0, 8), new Vector3(8, 0, 8) })
        {
            var lamp = new GameObject("Temporary neutral review lamp") { hideFlags = HideFlags.HideAndDontSave };
            lamp.transform.position = report.centre + new Vector3(offset.x, 7.5f, offset.z);
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Point; light.range = 22; light.intensity = 14; light.color = Color.white; light.shadows = LightShadows.None;
            lamps.Add(lamp);
        }
        try { foreach (var v in views) File.WriteAllBytes(Output + "/" + v.Item1 + ".png", Shoot(camera, v.Item2, v.Item3)); }
        finally { foreach (var l in lamps) Object.DestroyImmediate(l); Object.DestroyImmediate(camera.gameObject); }
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Rendering capture dirtied MainScene.");
    }

    // ---- assets and snapshots ------------------------------------------------------------------------------------

    static T SaveAsset<T>(T generated, string relative) where T : Object
    {
        string path = Folder + "/" + relative;
        generated.name = Path.GetFileNameWithoutExtension(relative);
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing) { EditorUtility.CopySerialized(generated, existing); Object.DestroyImmediate(generated); EditorUtility.SetDirty(existing); }
        else { AssetDatabase.CreateAsset(generated, path); existing = generated; }
        NorthWestRoomFramework.RequireOwnedAsset(existing, Section.Rendering);
        return existing;
    }

    static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Build North-West Room rendering");
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(parent.Length + 1));
    }

    /// <summary>Transforms outside Rendering (and renderer materials outside Room 3 entirely) before and after the build.</summary>
    static Dictionary<string, string> SnapshotOutside()
    {
        var scene = SceneManager.GetActiveScene();
        var rendering = NorthWestRoomFramework.FindSection(scene, Section.Rendering);
        var root = rendering.parent;
        return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .Where(t => t == rendering || !t.IsChildOf(rendering))
            .ToDictionary(t => GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(), t =>
            {
                var renderer = t.GetComponent<Renderer>();
                string mats = renderer && !t.IsChildOf(root) ? string.Join(",", renderer.sharedMaterials.Select(m => m ? AssetDatabase.GetAssetPath(m) : "null")) : "";
                return FormattableString.Invariant($"{t.name}|{(t.parent ? GlobalObjectId.GetGlobalObjectIdSlow(t.parent).ToString() : "root")}|{t.localPosition:R}|{t.localRotation:R}|{t.localScale:R}|{t.gameObject.activeSelf}|{mats}");
            });
    }
}
