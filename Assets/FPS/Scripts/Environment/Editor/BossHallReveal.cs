using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Unity.FPS.Gameplay;
using Object = UnityEngine.Object;
using Section = BossHallFramework.Section;

/// <summary>
/// corebuster_env_056: reveal and entrance composition review. Measures five actual-eye views on the real
/// door/incline/threshold route and the upper reverse, counts what each view shows of the turret, assembly,
/// upper tiers, open floor and emergency condition, captures an uninterrupted entry sequence, and resolves the
/// approved sign reservations for corebuster_env_057. Read-only: transient cameras, materials and sign proxies
/// are destroyed, and the scene, protected state and shared assets are proven unchanged.
/// </summary>
public static class BossHallReveal
{
    public const string Owner = "corebuster_env_056", Output = "ReviewCaptures/BossHall/056";
    const int W = BossHallRenderingReview.Width, H = BossHallRenderingReview.Height;

    public sealed class View
    {
        public string name, surface;
        public Vector3 eye;
        public float yaw, pitch, doorDistance;
        public Quaternion Rotation => Quaternion.Euler(-pitch, yaw, 0);
    }

    /// <summary>A reserved, empty sign envelope. env_057 places graphics only inside one of these, facing <c>normal</c>.</summary>
    public sealed class SignReservation
    {
        public string name, purpose, surface;
        public Vector3 centre, normal;
        public Vector2 size;
        public string[] approach;
        public Bounds Box
        {
            get
            {
                var e = new Vector3(Mathf.Abs(normal.x) > .5f ? .02f : size.x * .5f, size.y * .5f, Mathf.Abs(normal.z) > .5f ? .02f : size.x * .5f);
                return new Bounds(centre, e * 2);
            }
        }
    }

    // ---- Measured views -------------------------------------------------------------------------------------------

    static Transform Door()
    {
        var all = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true));
        return BossHallFramework.Unique(all, BossHallSurvey.BossDoorName, true);
    }
    static Transform[] actorCache; static string actorScene;
    // GlobalObjectId resolution is slow; resolve the 11 actors once per loaded scene.
    static Transform[] Actors(BossHallFramework.Layout layout)
    {
        if (actorCache != null && actorScene == layout.sceneSha && actorCache.All(t => t)) return actorCache;
        actorScene = layout.sceneSha;
        return actorCache = layout.Report.actors.Select(a =>
        {
            if (!GlobalObjectId.TryParse(a.id, out var id)) throw new InvalidOperationException("Invalid actor id " + a.name);
            return ((GameObject)GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id)).transform;
        }).ToArray();
    }

    // First original walking surface below a point; actors and dressing never count as ground.
    static Vector3 Ground(BossHallFramework.Layout layout, Vector3 p, out string surface)
    {
        var actors = Actors(layout); var root = BossHallFramework.FindRoot();
        var hit = Physics.RaycastAll(p, Vector3.down, 12, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance)
            .FirstOrDefault(h => !h.collider.transform.IsChildOf(root) && !actors.Any(a => h.collider.transform.IsChildOf(a)));
        if (!hit.collider) throw new InvalidOperationException("No measured walking surface below " + p.ToString("F2"));
        surface = BossHallFramework.PathOf(hit.collider.transform); return hit.point;
    }

    // The trimmed contract keeps no incline profile, so the incline is measured live on its own colliders.
    public const float LandingDistance = 11f;
    static float MiddleInclineDistance(BossHallFramework.Layout layout)
    {
        float low = BossHallRenderingReview.InclineEye(1, layout).y, high = BossHallRenderingReview.InclineEye(LandingDistance, layout).y, mid = (low + high) * .5f;
        for (float d = 1; d <= LandingDistance; d += .05f)
            if (BossHallRenderingReview.InclineEye(d, layout).y >= mid) return d;
        throw new InvalidOperationException("Incline does not rise to its landing");
    }
    // Deck undersides and the structure top come from the contract's deck footprints (the scalar fields are trimmed).
    internal static (float tier1Under, float tier2Under, float structureTop) Decks(BossHallFramework.Layout layout)
    {
        var r = layout.Report; var f = r.deckFootprints;
        float Under(float top) => f.Where(x => Mathf.Abs(x.max.y - top) < .15f).Min(x => x.min.y);
        return (Under(r.tier1TopY), Under(r.tier2TopY), r.stairs.Max(st => st.bounds.max.y) > r.tier2TopY + 1 ? r.stairs.Max(st => st.bounds.max.y) : r.tier2TopY + 1.6f);
    }
    public static Vector3 EntryInside(BossHallFramework.Layout layout) => layout.Report.entry.centre + layout.Report.entry.inward * 3;

    static View Incline(string name, float distance, BossHallFramework.Layout layout)
    {
        var eye = BossHallRenderingReview.InclineEye(distance, layout);
        return new View { name = name, eye = eye, yaw = 270, pitch = 0, doorDistance = distance, surface = BossHallSurvey.InclineName };
    }
    static View Floor(string name, Vector3 at, BossHallFramework.Layout layout, float yaw = 270, float pitch = 0)
    {
        var g = Ground(layout, at + Vector3.up * 3, out string surface);
        return new View { name = name, eye = g + Vector3.up * BossHallRenderingReview.EyeHeight(), yaw = yaw, pitch = pitch,
            doorDistance = Door().position.x - at.x, surface = surface };
    }

    /// <summary>The five fixed actual-eye views. Forward views look west along the measured route, level, as a walking player does.</summary>
    public static List<View> Views(BossHallFramework.Layout layout)
    {
        var r = layout.Report; var room = layout.interior;
        return new List<View> {
            Incline("Door_Side", 1, layout),
            Incline("Middle_Incline", (float)Math.Round(MiddleInclineDistance(layout), 2), layout),
            Floor("Arena_Threshold", new Vector3(r.entry.centre.x, r.floorTopY, r.entry.centre.z), layout),
            Floor("Just_Inside", EntryInside(layout), layout),
            // Upper bridge, west of the turret void and outside the west bay-door approach; looking back at the entry.
            Floor("Upper_Reverse", new Vector3(room.min.x + 15, r.tier2TopY, r.entry.centre.z), layout, 90, -8),
        };
    }

    /// <summary>Uninterrupted walk from the door threshold, up the incline and 10 m into the arena, at 1 m steps.</summary>
    public static List<View> EntrySequence(BossHallFramework.Layout layout)
    {
        var r = layout.Report; var door = Door().position; var list = new List<View>();
        float wall = door.x - r.entry.centre.x;
        for (float d = 1; d <= wall + 10.01f; d += 1)
        {
            string name = "Entry_" + d.ToString("00", CultureInfo.InvariantCulture) + "m";
            // The incline helper measures only incline colliders; beyond its landing use the hall's own floor.
            View v = null;
            if (d < wall - .2f) try { v = Incline(name, d, layout); } catch (InvalidOperationException) { }
            list.Add(v ?? Floor(name, new Vector3(door.x - d, r.floorTopY, r.entry.centre.z), layout));
        }
        return list;
    }

    // ---- Groups ---------------------------------------------------------------------------------------------------

    static Renderer[] Live(IEnumerable<Renderer> rs) => rs.Where(x => x && x.enabled && x.gameObject.activeInHierarchy).Distinct().ToArray();
    static Renderer[] SceneRenderers() => Live(SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)));

    public static Dictionary<string, Renderer[]> Groups(BossHallFramework.Layout layout)
    {
        var r = layout.Report; var room = layout.interior; var root = BossHallFramework.FindRoot();
        var skins = BossHallFramework.FindSection(Section.Rendering); var all = SceneRenderers();
        var actors = Actors(layout); var turret = actors[r.actors.FindIndex(a => a.kind == "turret")];
        bool InActor(Renderer x) => actors.Any(a => x.transform.IsChildOf(a));
        bool Shell(Renderer x) => !x.transform.IsChildOf(root) || x.transform.IsChildOf(skins);
        var decks = Decks(layout);
        bool Inside(Bounds b) => b.center.x > room.min.x && b.center.x < room.max.x && b.center.z > room.min.z && b.center.z < room.max.z;
        Renderer[] Of(params Section[] s) => Live(s.SelectMany(x => BossHallFramework.FindSection(x).GetComponentsInChildren<Renderer>(true)));
        return new Dictionary<string, Renderer[]> {
            ["Turret"] = Live(turret.GetComponentsInChildren<Renderer>(true)),
            ["Hall_Enemies"] = Live(actors.Where(a => a != turret).SelectMany(a => a.GetComponentsInChildren<Renderer>(true))),
            ["Assembly"] = Of(BossHallFramework.Section.AssemblyBackdrop, BossHallFramework.Section.MechanicalArms),
            ["Services"] = Of(BossHallFramework.Section.PowerServices, BossHallFramework.Section.CoolingServices, BossHallFramework.Section.Overhead),
            ["Upper_Tiers"] = all.Where(x => Shell(x) && !InActor(x) && Inside(x.bounds) && !(x is ParticleSystemRenderer) &&
                x.bounds.min.y >= decks.tier1Under - .6f && x.bounds.max.y <= decks.structureTop + .1f).ToArray(),
            ["Open_Floor"] = all.Where(x => Shell(x) && !InActor(x) && Inside(x.bounds) &&
                x.bounds.max.y <= r.floorTopY + .1f && x.bounds.min.y >= r.floorTopY - .35f).ToArray(),
            ["Meltdown_VFX"] = Of(BossHallFramework.Section.MeltdownVfx),
            ["Giant_Core"] = Finale("Giant Core"),
            ["Core_Platform"] = Finale("Core Platform"),
        };
    }
    // The merged giant-core finale (FinaleBuilder, accepted 2026-10-07) is a second, owner-approved focal object.
    public const string FinaleRoot = "===== FINALE =====";
    static Renderer[] Finale(string child)
    {
        var root = SceneManager.GetActiveScene().GetRootGameObjects().SingleOrDefault(g => g.name == FinaleRoot);
        var t = root ? root.transform.Find(child) : null;
        if (!t) throw new InvalidOperationException("Missing finale object " + child);
        return Live(t.GetComponentsInChildren<Renderer>(true));
    }
    static Renderer[] Focal(Dictionary<string, Renderer[]> g) => g["Turret"].Concat(g["Giant_Core"]).ToArray();
    static Light[] EmergencyLights() => BossHallLighting.Lights()
        .Where(l => l.name.StartsWith("Upper_Warning_", StringComparison.Ordinal) || l.name.StartsWith("Warning_Flicker_", StringComparison.Ordinal)).ToArray();

    // ---- Rendering helpers ------------------------------------------------------------------------------------------

    sealed class Rig : IDisposable
    {
        public readonly Camera camera; public readonly Material a, b, c; readonly BossHallRenderingReview.MaterialState[] materials;
        public Rig()
        {
            materials = BossHallRenderingReview.PreserveMaterials(); camera = BossHallRenderingReview.CameraForReview();
            var weapons = Object.FindAnyObjectByType<PlayerWeaponsManager>();
            if (!weapons) throw new InvalidOperationException("Missing saved player weapons manager");
            camera.fieldOfView = weapons.DefaultFov; // The player's actual vertical field of view.
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? throw new InvalidOperationException("Missing URP Unlit");
            Material M(Color colour) { var m = new Material(shader) { hideFlags = HideFlags.HideAndDontSave }; m.SetColor("_BaseColor", colour); return m; }
            a = M(new Color(1, 0, 1)); b = M(new Color(0, 1, 0)); c = M(new Color(.95f, .55f, .05f));
        }
        public void Dispose()
        {
            Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(c);
            BossHallRenderingReview.DestroyCamera(camera); BossHallRenderingReview.RestoreMaterials(materials);
        }
        public Color32[] Pixels(View v, bool post)
        {
            camera.transform.SetPositionAndRotation(v.eye, v.Rotation); camera.GetUniversalAdditionalCameraData().renderPostProcessing = post;
            var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32); var previous = RenderTexture.active;
            var t = new Texture2D(W, H, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt; camera.Render(); camera.Render(); RenderTexture.active = rt;
                t.ReadPixels(new Rect(0, 0, W, H), 0, 0); t.Apply(); return t.GetPixels32();
            }
            finally { camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(t); }
        }
        // Visible screen area of a group: repaint it with two flat colours; every pixel that changes is the group.
        // Shadows/lighting are identical in both renders, so only the unoccluded silhouette is counted.
        public int Visible(View v, Renderer[] group) => Mask(v, group).Count(x => x);
        public bool[] Mask(View v, Renderer[] group)
        {
            var mask = new bool[W * H];
            if (group.Length == 0) return mask;
            var saved = group.Select(x => x.sharedMaterials).ToArray();
            try
            {
                Color32[] Paint(Material m)
                {
                    for (int i = 0; i < group.Length; i++) group[i].sharedMaterials = Enumerable.Repeat(m, Mathf.Max(1, saved[i].Length)).ToArray();
                    return Pixels(v, false);
                }
                var p = Paint(a); var q = Paint(b);
                for (int i = 0; i < p.Length; i++) mask[i] = Delta(p[i], q[i]) > 24;
                return mask;
            }
            finally { for (int i = 0; i < group.Length; i++) group[i].sharedMaterials = saved[i]; }
        }
    }
    static int Delta(Color32 p, Color32 q) => Mathf.Max(Mathf.Abs(p.r - q.r), Mathf.Abs(p.g - q.g), Mathf.Abs(p.b - q.b));
    static byte[] Png(Color32[] pixels)
    {
        var t = new Texture2D(W, H, TextureFormat.RGB24, false);
        try { t.SetPixels32(pixels); t.Apply(); return t.EncodeToPNG(); } finally { Object.DestroyImmediate(t); }
    }
    static void Sheet(string path, IList<Color32[]> frames, int columns)
    {
        int w = W / 4, h = H / 4, rows = (frames.Count + columns - 1) / columns;
        var sheet = new Texture2D(w * columns, h * rows, TextureFormat.RGB24, false);
        try
        {
            var fill = Enumerable.Repeat(new Color32(12, 12, 12, 255), w * columns * h * rows).ToArray();
            for (int f = 0; f < frames.Count; f++)
            {
                int ox = f % columns * w, oy = (rows - 1 - f / columns) * h;
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    int r = 0, g = 0, b = 0;
                    for (int j = 0; j < 4; j++) for (int i = 0; i < 4; i++) { var p = frames[f][(y * 4 + j) * W + x * 4 + i]; r += p.r; g += p.g; b += p.b; }
                    fill[(oy + y) * w * columns + ox + x] = new Color32((byte)(r / 16), (byte)(g / 16), (byte)(b / 16), 255);
                }
            }
            sheet.SetPixels32(fill); sheet.Apply(); File.WriteAllBytes(path, sheet.EncodeToPNG());
        }
        finally { Object.DestroyImmediate(sheet); }
    }
    static void SimulateVfx() { foreach (var ps in BossHallMeltdown.Emitters()) ps.Simulate(2.5f, false, true, true); }

    // ---- Analysis ---------------------------------------------------------------------------------------------------

    static string F(FormattableString s) => FormattableString.Invariant(s);

    static int FloorCells(BossHallFramework.Layout layout, Vector3 eye, out int total)
    {
        var r = layout.Report; var room = layout.interior;
        var tier1 = r.deckFootprints.Where(d => Mathf.Abs(d.max.y - r.tier1TopY) < .15f).ToArray();
        float z0 = tier1.Where(d => d.max.z < room.center.z).Max(d => d.max.z), z1 = tier1.Where(d => d.min.z > room.center.z).Min(d => d.min.z);
        int seen = 0; total = 0;
        for (float x = room.min.x + 5.5f; x < room.max.x - 5; x += 1)
            for (float z = Mathf.Ceil(z0) + .5f; z < z1; z += 1)
            {
                var p = new Vector3(x, r.floorTopY + .05f, z); total++;
                if (!Physics.Linecast(eye, p, out var hit, ~0, QueryTriggerInteraction.Ignore) || (hit.point - p).sqrMagnitude < .04f) seen++;
            }
        return seen;
    }

    sealed class Metrics
    {
        public string view; public Vector3 eye; public float yaw, pitch, doorDistance; public string surface;
        public float turretLuma, coreLuma; public int turret, turretBare, core, coreBare, platform, enemies, assembly, services, upper, floor, vfx, emergencyLight, floorCells, floorTotal;
        public Vector2 turretScreen; public bool turretInFrame;
    }

    static Metrics Measure(Rig rig, BossHallFramework.Layout layout, View v, Dictionary<string, Renderer[]> groups, Renderer[] dressing, Light[] emergency)
    {
        var m = new Metrics { view = v.name, eye = v.eye, yaw = v.yaw, pitch = v.pitch, doorDistance = v.doorDistance, surface = v.surface };
        // Mean displayed luminance (0-255, post-processed) of each focal object's visible pixels.
        var shown = rig.Pixels(v, true);
        float Luma(bool[] mask)
        {
            int n = 0; double sum = 0;
            for (int i = 0; i < mask.Length; i++) if (mask[i]) { n++; sum += .2126 * shown[i].r + .7152 * shown[i].g + .0722 * shown[i].b; }
            return n == 0 ? 0 : (float)(sum / n);
        }
        var turretMask = rig.Mask(v, groups["Turret"]); var coreMask = rig.Mask(v, groups["Giant_Core"]);
        m.turretLuma = Luma(turretMask); m.coreLuma = Luma(coreMask);
        m.turret = turretMask.Count(x => x); m.core = coreMask.Count(x => x); m.platform = rig.Visible(v, groups["Core_Platform"]);
        m.enemies = rig.Visible(v, groups["Hall_Enemies"]);
        m.assembly = rig.Visible(v, groups["Assembly"]); m.services = rig.Visible(v, groups["Services"]);
        m.upper = rig.Visible(v, groups["Upper_Tiers"]); m.floor = rig.Visible(v, groups["Open_Floor"]); m.vfx = rig.Visible(v, groups["Meltdown_VFX"]);
        foreach (var x in dressing) x.enabled = false;
        try { m.turretBare = rig.Visible(v, groups["Turret"]); m.coreBare = rig.Visible(v, groups["Giant_Core"]); } finally { foreach (var x in dressing) x.enabled = true; }
        var on = rig.Pixels(v, false); foreach (var l in emergency) l.enabled = false;
        try { var off = rig.Pixels(v, false); m.emergencyLight = on.Where((p, i) => Delta(p, off[i]) > 6).Count(); }
        finally { foreach (var l in emergency) l.enabled = true; }
        m.floorCells = FloorCells(layout, v.eye, out m.floorTotal);
        var head = (layout.Report.turret.head.min + layout.Report.turret.head.max) * .5f;
        rig.camera.transform.SetPositionAndRotation(v.eye, v.Rotation); var s = rig.camera.WorldToViewportPoint(head);
        m.turretScreen = new Vector2(s.x, s.y); m.turretInFrame = s.z > 0 && s.x > 0 && s.x < 1 && s.y > 0 && s.y < 1;
        return m;
    }
    static string Line(Metrics m) => F($"{m.view}: eye {m.eye:F2}, yaw {m.yaw:F0}, pitch {m.pitch:F0}, {m.doorDistance:F2} m from door on {m.surface}. Visible px of {W * H}: turret {m.turret} (no dressing {m.turretBare}, luma {m.turretLuma:F1}), giant core {m.core} (no dressing {m.coreBare}, luma {m.coreLuma:F1}), core platform {m.platform}, enemies {m.enemies}, assembly {m.assembly}, services {m.services}, upper tiers {m.upper}, floor {m.floor}, VFX {m.vfx}; emergency-light lit px {m.emergencyLight}; central floor cells in sight {m.floorCells}/{m.floorTotal}; turret head viewport ({m.turretScreen.x:F2}, {m.turretScreen.y:F2}) in frame {m.turretInFrame}.");

    // ---- Sign reservations ------------------------------------------------------------------------------------------

    // Descriptors are measured relative to the contract frame and resolved against real collision on every run.
    // Selected 2026-10-07 by SearchSigns (Docs/BossHall/RevealSigns_056.txt); no sign geometry is created here.
    sealed class Descriptor { public string name, purpose, surface; public float along, height; public Vector2 size; public string[] approach; }
    // Side-wall gallery bands had no candidate inside the forward frame from either approach eye, so env_057 must judge
    // gallery signage from floor/gallery approaches instead of the reveal.
    static readonly Descriptor[] Descriptors = {
        new Descriptor { name = "Sign_Final_Assembly", surface = "West_Assembly", along = 6.5f, height = 1.95f, size = new Vector2(4, 1),
            approach = new[] { "Arena_Threshold", "Just_Inside" },
            purpose = "Final-assembly identification on the cradle front, north of the turret and below the firing volume; read along the entry axis." },
    };

    static (Vector3 origin, Vector3 normal, Vector3 tangent) Surface(string surface, BossHallFramework.Layout layout, float along, float height)
    {
        var r = layout.Report; var room = layout.interior; float y = r.floorTopY + height;
        switch (surface)
        {
            case "West_Assembly": return (new Vector3(room.min.x + 7, y, r.entry.centre.z + along), Vector3.right, Vector3.forward);
            case "South_Gallery": return (new Vector3(room.max.x - along, y, room.min.z + 3), Vector3.forward, Vector3.right);
            case "North_Gallery": return (new Vector3(room.max.x - along, y, room.max.z - 3), Vector3.back, Vector3.right);
        }
        throw new ArgumentException(surface);
    }
    static SignReservation Resolve(BossHallFramework.Layout layout, string name, string purpose, string surface, float along, float height, Vector2 size, string[] approach, out string reject)
    {
        reject = null; var (origin, normal, tangent) = Surface(surface, layout, along, height);
        var root = BossHallFramework.FindRoot(); var actors = Actors(layout);
        RaycastHit Back(Vector3 p) => Physics.RaycastAll(p, -normal, 8, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance)
            .FirstOrDefault(h => !actors.Any(a => h.collider.transform.IsChildOf(a)));
        var centreHit = Back(origin);
        if (!centreHit.collider || Vector3.Angle(centreHit.normal, normal) > 10) { reject = "no facing backing surface"; return null; }
        var sign = new SignReservation { name = name, purpose = purpose, surface = surface, normal = normal, size = size, approach = approach,
            centre = centreHit.point + normal * .06f };
        // Planar backing at every corner: no floating plate over a gap or a stepped face.
        foreach (var u in new[] { -.5f, .5f }) foreach (var w in new[] { -.5f, .5f })
        {
            var corner = sign.centre + tangent * u * size.x + Vector3.up * w * size.y;
            var h = Back(corner + normal * 1);
            if (!h.collider || Mathf.Abs(h.distance - 1.06f) > .05f || Vector3.Angle(h.normal, normal) > 10) { reject = "uneven or missing backing at a corner"; return null; }
        }
        var box = sign.Box; string keep = layout.KeepOutViolation(box);
        if (keep != null) { reject = "enters " + keep; return null; }
        var front = new Bounds(sign.centre + normal * .2f, sign.Box.size + new Vector3(Mathf.Abs(normal.x), 0, Mathf.Abs(normal.z)) * .36f);
        if (Physics.CheckBox(front.center, front.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) { reject = "collision in front of the plate"; return null; }
        var hitDressing = DressingBounds().FirstOrDefault(x => x.b.Intersects(front)).name;
        if (hitDressing != null) { reject = "occupied by " + hitDressing; return null; }
        return sign;
    }

    static (string name, Bounds b)[] dressingCache; static int dressingFrame = -1;
    static (string name, Bounds b)[] DressingBounds()
    {
        if (dressingCache != null && dressingFrame == Time.frameCount) return dressingCache;
        var skins = BossHallFramework.FindSection(Section.Rendering); dressingFrame = Time.frameCount;
        return dressingCache = Live(BossHallFramework.FindRoot().GetComponentsInChildren<Renderer>(true))
            // env_057 occupies this reservation. Its exact bindings are checked by the framework;
            // the installed plate must not invalidate its own empty-envelope survey.
            .Where(x => !x.transform.IsChildOf(skins) && !x.transform.IsChildOf(BossHallFramework.FindSection(Section.Wayfinding)))
            .Select(x => (x.name, x.bounds)).ToArray();
    }

    /// <summary>The approved env_056 sign envelopes for env_057, resolved and validated against the current scene.</summary>
    public static List<SignReservation> SignReservations(BossHallFramework.Layout layout)
    {
        var list = new List<SignReservation>();
        foreach (var d in Descriptors)
        {
            var s = Resolve(layout, d.name, d.purpose, d.surface, d.along, d.height, d.size, d.approach, out string reject);
            if (s == null) throw new InvalidOperationException("Sign reservation " + d.name + " is no longer valid: " + reject);
            list.Add(s);
        }
        return list;
    }

    static (int px, int focalWith) SignVisible(Rig rig, View v, SignReservation s, Renderer[] focal)
    {
        var go = new GameObject("Temporary env_056 sign proxy") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            go.transform.SetPositionAndRotation(s.centre, Quaternion.LookRotation(-s.normal)); go.transform.localScale = new Vector3(s.size.x, s.size.y, 1);
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var mr = go.AddComponent<MeshRenderer>(); mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.sharedMaterial = rig.c;
            int px = rig.Visible(v, new Renderer[] { mr });
            return (px, rig.Visible(v, focal));
        }
        finally { Object.DestroyImmediate(go); }
    }

    static string SearchSigns(Rig rig, BossHallFramework.Layout layout, List<View> views, Dictionary<string, Renderer[]> groups)
    {
        var lines = new List<string> { "SIGN RESERVATION SEARCH env_056 (transient proxies only; nothing saved)" };
        var r = layout.Report; var room = layout.interior;
        var approach = new[] { "Arena_Threshold", "Just_Inside" }; var eyes = views.Where(v => approach.Contains(v.name)).ToArray();
        var surfaces = new[] {
            (surface: "West_Assembly", purpose: "Final-assembly identification behind the turret, read along the entry axis", size: new Vector2(4, 1),
                along: Enumerable.Range(0, 61).Select(i => -15 + i * .5f), height: Enumerable.Range(0, 40).Select(i => .7f + i * .25f)),
            (surface: "South_Gallery", purpose: "Containment/load warning on the south gallery band, read obliquely from the entry", size: new Vector2(2.4f, .6f),
                along: Enumerable.Range(0, 85).Select(i => 3 + i * .5f), height: Enumerable.Range(0, 19).Select(i => 7.4f + i * .1f)),
            (surface: "North_Gallery", purpose: "Containment/load warning on the north gallery band, read obliquely from the entry", size: new Vector2(2.4f, .6f),
                along: Enumerable.Range(0, 85).Select(i => 3 + i * .5f), height: Enumerable.Range(0, 19).Select(i => 7.4f + i * .1f)) };
        var focalBase = eyes.ToDictionary(v => v.name, v => rig.Visible(v, Focal(groups)));
        foreach (var s in surfaces)
        {
            var valid = new List<(SignReservation sign, float along, float height, int clear, float score)>(); var rejects = new Dictionary<string, int>();
            foreach (float a in s.along) foreach (float h in s.height)
            {
                if (h + s.size.y * .5f > Decks(layout).tier2Under - r.floorTopY - .1f) continue;
                var sign = Resolve(layout, "candidate", s.purpose, s.surface, a, h, s.size, approach, out string reject);
                if (sign == null) { string k = reject.Split(':')[0]; rejects[k] = rejects.TryGetValue(k, out int n) ? n + 1 : 1; continue; }
                var (_, _, tangent) = Surface(s.surface, layout, a, h); int clear = int.MaxValue; float score = float.MaxValue;
                foreach (var v in eyes)
                {
                    int c = 0;
                    for (int i = 0; i < 5; i++) for (int j = 0; j < 3; j++)
                    {
                        var p = sign.centre + sign.normal * .01f + tangent * (i / 4f - .5f) * s.size.x * .9f + Vector3.up * (j / 2f - .5f) * s.size.y * .9f;
                        if (!Physics.Linecast(v.eye, p, ~0, QueryTriggerInteraction.Ignore)) c++;
                    }
                    var to = v.eye - sign.centre; float facing = Vector3.Dot(sign.normal, to.normalized);
                    // Legible only inside the forward frame a walking player actually sees.
                    rig.camera.transform.SetPositionAndRotation(v.eye, v.Rotation); var vp = rig.camera.WorldToViewportPoint(sign.centre);
                    bool framed = vp.z > 0 && vp.x > .06f && vp.x < .94f && vp.y > .06f && vp.y < .94f;
                    clear = Mathf.Min(clear, facing < .3f || !framed ? 0 : c); score = Mathf.Min(score, facing * s.size.x * s.size.y / to.sqrMagnitude);
                }
                if (clear >= 12) valid.Add((sign, a, h, clear, score));
            }
            lines.Add(F($"{s.surface}: {valid.Count} collision/keep-out-valid candidates with >=12/15 clear sample rays from both approach eyes; rejected: ") +
                string.Join(", ", rejects.OrderBy(p => p.Key).Select(p => p.Key + " " + p.Value)));
            // Render the best few by projected solid angle; the winner maximises the worst approach view's visible pixels.
            (SignReservation sign, float along, float height, int min)? best = null;
            foreach (var c in valid.OrderByDescending(c => c.score).ThenBy(c => c.along).ThenBy(c => c.height).Take(8))
            {
                int min = int.MaxValue; var parts = new List<string>(); bool turretSafe = true;
                foreach (var v in eyes)
                {
                    var (px, focalWith) = SignVisible(rig, v, c.sign, Focal(groups));
                    min = Mathf.Min(min, px); turretSafe &= focalWith == focalBase[v.name]; parts.Add(v.name + " " + px + " px");
                }
                lines.Add(F($"  candidate along {c.along:F2}, height {c.height:F2}, centre {c.sign.centre:F2}: ") + string.Join(", ", parts) + "; turret/core pixels unchanged " + turretSafe);
                if (turretSafe && (best == null || min > best.Value.min)) best = (c.sign, c.along, c.height, min);
            }
            lines.Add(best == null ? "  NO VALID RESERVATION" : F($"  SELECT {s.surface}: along {best.Value.along:F2}, height {best.Value.height:F2}, worst approach {best.Value.min} px"));
        }
        return string.Join("\n", lines);
    }

    // ---- Batch entry points -----------------------------------------------------------------------------------------

    static Dictionary<string, string> Protected() => BossHallSnapshot.Capture(SceneManager.GetActiveScene(), null);

    public static string Review(BossHallFramework.Layout layout, string label, bool search)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Reveal review renders; omit -nographics");
        var culture = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string folder = Output + "/" + label; Directory.CreateDirectory(folder + "/Views"); Directory.CreateDirectory(folder + "/Entry_Sequence");
        var lines = new List<string> { "BOSS HALL REVEAL env_056 — " + label, "Scene SHA-256 " + layout.sceneSha };
        try
        {
            SimulateVfx();
            using (var rig = new Rig())
            {
                var groups = Groups(layout); var root = BossHallFramework.FindRoot(); var skins = BossHallFramework.FindSection(Section.Rendering);
                var dressing = Live(root.GetComponentsInChildren<Renderer>(true)).Where(x => !x.transform.IsChildOf(skins)).ToArray();
                var emergency = EmergencyLights();
                lines.Add(F($"Player eye {BossHallRenderingReview.EyeHeight():F2} m, vertical FOV {rig.camera.fieldOfView:F0} deg, {W}x{H}. Groups: ") +
                    string.Join(", ", groups.Select(g => g.Key + " " + g.Value.Length)) + $", emergency lights {emergency.Length}, non-skin dressing renderers {dressing.Length}.");
                var views = Views(layout); var metrics = new List<Metrics>();
                lines.Add("FIVE FIXED ACTUAL-EYE VIEWS");
                foreach (var v in views)
                {
                    metrics.Add(Measure(rig, layout, v, groups, dressing, emergency)); lines.Add(Line(metrics.Last()));
                    File.WriteAllBytes(folder + "/Views/" + v.name + ".png", Png(rig.Pixels(v, true)));
                }
                // Door status is protected: its lamps must stay visible from the incline and Room 3.
                var door = Door().GetComponent<SecurityDoor>(); var lamps = Live(door.Lamps);
                var back = new View { name = "Door_Status_From_Incline", eye = views[1].eye, yaw = 90, pitch = 0, surface = views[1].surface, doorDistance = views[1].doorDistance };
                var room3 = BossHallRenderingReview.Neighbours(layout).Single(n => n.Item1 == "Room3_BossDoor");
                var front = new View { name = "Door_Status_From_Room3", eye = room3.Item2, yaw = 270, pitch = 0, surface = "Room 3" };
                foreach (var v in new[] { back, front })
                {
                    lines.Add(F($"{v.name}: {lamps.Length} status lamp renderers, {rig.Visible(v, lamps)} visible px; door StartLocked {door.StartLocked}."));
                    File.WriteAllBytes(folder + "/Views/" + v.name + ".png", Png(rig.Pixels(v, true)));
                }
                lines.Add("ENTRY SEQUENCE (uninterrupted, 1 m steps, level gaze along the route)");
                var frames = new List<Color32[]>(); string firstTurret = null, firstCore = null, firstAssembly = null, firstUpper = null;
                foreach (var v in EntrySequence(layout))
                {
                    int t = rig.Visible(v, groups["Turret"]), k = rig.Visible(v, groups["Giant_Core"]), a = rig.Visible(v, groups["Assembly"]), u = rig.Visible(v, groups["Upper_Tiers"]), f = rig.Visible(v, groups["Open_Floor"]);
                    if (t > 0 && firstTurret == null) firstTurret = v.name; if (k > 0 && firstCore == null) firstCore = v.name; if (a > 0 && firstAssembly == null) firstAssembly = v.name; if (u > 0 && firstUpper == null) firstUpper = v.name;
                    lines.Add(F($"{v.name}: eye {v.eye:F2}; turret {t}, giant core {k}, assembly {a}, upper tiers {u}, floor {f} px"));
                    var pixels = rig.Pixels(v, true); frames.Add(pixels); File.WriteAllBytes(folder + "/Entry_Sequence/" + v.name + ".png", Png(pixels));
                }
                Sheet(folder + "/Entry_Sequence.png", frames, 5);
                lines.Add($"First visible along the walk: turret {firstTurret}, giant core {firstCore}, assembly {firstAssembly}, upper tiers {firstUpper}.");
                if (search) { string s = SearchSigns(rig, layout, views, groups); File.WriteAllText("Docs/BossHall/RevealSigns_056.txt", s + "\n"); lines.Add(s); }
                var signs = SignReservations(layout);
                lines.Add("SIGN RESERVATIONS (" + signs.Count + ")");
                foreach (var s in signs)
                {
                    var parts = s.approach.Select(n => views.Single(v => v.name == n)).Select(v => { var (px, fw) = SignVisible(rig, v, s, Focal(groups)); return v.name + " " + px + " px, turret+core " + fw; });
                    lines.Add(F($"{s.name} [{s.surface}] centre {s.centre:F2}, normal {s.normal:F0}, {s.size.x:F1}x{s.size.y:F1} m: ") + string.Join("; ", parts) + ". " + s.purpose);
                }
            }
        }
        finally { CultureInfo.CurrentCulture = culture; foreach (var ps in BossHallMeltdown.Emitters()) ps.Clear(true); }
        return string.Join("\n", lines);
    }

    static void Run(string label, string file, bool search)
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); BossHallFramework.Validate(layout);
        var before = Protected(); var shared = BossHallSnapshot.CaptureAssets();
        string report = Review(layout, label, search);
        BossHallSnapshot.Compare(before, Protected(), "Reveal review scene state");
        BossHallSnapshot.Compare(shared, BossHallSnapshot.CaptureAssets(), "Reveal review shared assets");
        if (SceneManager.GetActiveScene().isDirty || BossHallSurvey.Hash(BossHallSurvey.ScenePath) != layout.sceneSha)
            throw new InvalidOperationException("Reveal review changed MainScene");
        report += $"\nRead-only: {before.Count} scene states, {shared.Count} shared entries and scene bytes unchanged.\n";
        BossHallLighting.Write(file, report);
    }
    /// <summary>Transient proposal: rebuild Lighting through its owner (env_055) recipe, review, discard; scene unchanged.</summary>
    public static void ProposalBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); BossHallFramework.Validate(layout);
        var before = Protected(); var shared = BossHallSnapshot.CaptureAssets(); string report;
        try
        {
            BossHallFramework.RebuildSection(layout, Section.Lighting, BossHallLighting.Owner, root => BossHallLighting.Populate(root, layout));
            report = Review(layout, "Proposed", false) + "\n" + BossHallLightingReview.Analyse(layout, out int errors) +
                "\nPROPOSAL ONLY; lighting analysis errors " + errors + ". Scene discarded.\n";
        }
        finally { EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); }
        BossHallSnapshot.Compare(before, Protected(), "Proposal scene state");
        BossHallSnapshot.Compare(shared, BossHallSnapshot.CaptureAssets(), "Proposal shared assets");
        if (BossHallSurvey.Hash(BossHallSurvey.ScenePath) != layout.sceneSha) throw new InvalidOperationException("Proposal changed scene bytes");
        BossHallLighting.Write("RevealProposed_056.txt", report);
    }
    /// <summary>Baseline/before review including the sign search. Graphics required: omit -nographics.</summary>
    public static void BaselineBatch() => Run("Before", "RevealBefore_056.txt", true);
    /// <summary>Saved-scene review after any refinement. Graphics required.</summary>
    public static void CaptureBatch() => Run("Saved", "Reveal_056.txt", false);
    [MenuItem("Tools/CoreBuster/Review Boss Hall Reveal")]
    public static void ReviewMenu() => CaptureBatch();
}
