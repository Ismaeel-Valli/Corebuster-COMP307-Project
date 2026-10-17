using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Focused integration checks for the destructive Editor authoring API, never a gameplay completion claim.</summary>
public static class BossHallFrameworkVerification
{
    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run fixture verification in an exclusive batch Editor; it clears only that session's Undo history.");
        // Batch has exclusive project access; no interactive scene can be discarded.
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        BossHallFramework.InitializeBaseline();
        var layout = BossHallFramework.MeasureLayout();
        var before = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), BossHallFramework.FindRoot());
        var assets = BossHallSnapshot.CaptureAssets();
        BossHallFramework.Build(layout);
        layout = BossHallFramework.SaveAndReload(layout);
        Verify(layout);
        // Idempotence with populated sections is also checked in Verify, not just on an empty root.
        var complete = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), null);
        BossHallFramework.Build(layout);
        BossHallSnapshot.Compare(complete, BossHallSnapshot.Capture(SceneManager.GetActiveScene(), null), "Idempotent framework rerun");
        layout = BossHallFramework.SaveAndReload(layout);
        BossHallSnapshot.Compare(before, BossHallSnapshot.Capture(SceneManager.GetActiveScene(), BossHallFramework.FindRoot()), "Protected scene after reload");
        BossHallSnapshot.Compare(assets, BossHallSnapshot.CaptureAssets(), "Protected assets after reload");
        string neighbours = ValidateNeighbours();
        Directory.CreateDirectory("Docs/BossHall");
        string evidence = "corebuster_env_048 | " + Application.unityVersion + " | 2026-10-04\n" +
            "Source scene: " + BossHallFramework.InitialSceneSha + "\nSaved scene: " + BossHallSurvey.Hash(BossHallSurvey.ScenePath) + "\n" +
            "Protected state: " + before.Count + " entries, " + BossHallSnapshot.Digest(before) + "\n" +
            "Protected assets/meta/settings: " + assets.Count + " entries, " + BossHallSnapshot.Digest(assets) + "\n" +
            "PASS: all 12 owner-locked sections, per-section rebuild/Undo, populated rerun, rollback, wrong owner, stale baseline, missing/ambiguous anchors, keep-outs, material isolation, component allowlist, save/reload, protected-state comparison.\n" + neighbours + "\n" +
            BossHallFramework.BudgetReport() + "\n";
        File.WriteAllText("Docs/BossHall/Framework_048.txt", evidence);
        Debug.Log("BOSS HALL FRAMEWORK BUILD VERIFIED\n" + evidence);
    }

    public static void ValidateBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an exclusive batch Editor.");
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        string before = BossHallSurvey.Hash(BossHallSurvey.ScenePath);
        BossHallFramework.Validate(BossHallFramework.MeasureLayout());
        Debug.Log(ValidateNeighbours());
        if (SceneManager.GetActiveScene().isDirty || before != BossHallSurvey.Hash(BossHallSurvey.ScenePath))
            throw new InvalidOperationException("Read-only validation changed the scene.");
        Debug.Log("BOSS HALL READ-ONLY RELOAD VALIDATION PASSED");
    }

    static string ValidateNeighbours()
    {
        // CentreRoom*.cs are deliberately local/ignored authoring tools. Never create a hard clone dependency on them.
        var centre = typeof(BossHallFramework).Assembly.GetType("CentreRoomFramework");
        if (centre != null)
        {
            var measure = centre.GetMethod("MeasureLayout"); var validate = centre.GetMethod("Validate");
            if (measure == null || validate == null || !(bool)validate.Invoke(null, new[] { (object)SceneManager.GetActiveScene(), measure.Invoke(null, null), true }))
                throw new InvalidOperationException("Centre Room framework regression check failed");
        }
        else Debug.Log("CENTRE ROOM REGRESSION TOOL NOT INSTALLED: local ignored generator; original scene/component/asset preservation remains checked.");
        if (!NorthWestRoomFramework.Validate(SceneManager.GetActiveScene(), NorthWestRoomFramework.MeasureLayout(), true))
            throw new InvalidOperationException("North-West Room framework regression check failed");
        return centre != null ? "PASS: Centre and North-West Room framework validators." :
            "PASS: North-West Room framework validator. SKIP: local Centre validator is not installed.";
    }

    static void Verify(BossHallFramework.Layout layout)
    {
        var protectedBefore = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), BossHallFramework.FindRoot());
        foreach (var spec in BossHallFramework.Sections)
        {
            Debug.Log("BOSS HALL: verify rebuild/Undo/rollback " + spec.name);
            var target = BossHallFramework.FindSection(spec.section);
            // Only this framework task's empty sections may be used as integration fixtures.
            if (target.childCount != 0) throw new InvalidOperationException("Verification needs empty sections; never overwrite real dressing.");
            var otherSpec = BossHallFramework.Sections.First(s => s.section != spec.section);
            var other = BossHallFramework.FindSection(otherSpec.section);
            var a = Probe(target, "__Owner_Probe", AllowedPoint(layout));
            var b = Probe(other, "__Sibling_Probe", AllowedPoint(layout));
            try
            {
                BossHallFramework.Build(layout);
                if (!a || !b) throw new InvalidOperationException("Framework rerun cleared populated sections");
                ExpectRejected(() => BossHallFramework.RebuildSection(layout, spec.section, "corebuster_env_048", _ => { }), "wrong owner");
                if (!a || !b) throw new InvalidOperationException("Wrong owner mutated sections");
                BossHallFramework.RebuildSection(layout, spec.section, spec.owner, _ => { });
                if (target.Find("__Owner_Probe") || !b) throw new InvalidOperationException("Section rebuild escaped ownership");
                Undo.PerformUndo();
                if (!target.Find("__Owner_Probe") || !b) throw new InvalidOperationException("Undo did not restore section");
                ExpectRejected(() => BossHallFramework.RebuildSection(layout, spec.section, spec.owner, t =>
                {
                    var g = Probe(t, "__Invalid_Placement", layout.Report.entryInside); Undo.RegisterCreatedObjectUndo(g, "Invalid probe");
                }), "invalid rebuild rollback");
                if (!target.Find("__Owner_Probe") || target.Find("__Invalid_Placement") || !b) throw new InvalidOperationException("Failed rebuild did not roll back");
            }
            finally
            {
                var restored = target.Find("__Owner_Probe"); if (restored) Object.DestroyImmediate(restored.gameObject);
                if (b) Object.DestroyImmediate(b);
            }
        }

        foreach (var zone in layout.Report.zones.Where(z => z.kind == "reserved-volume" || z.kind == "column"))
            if (layout.KeepOutViolation(new Bounds((zone.min + zone.max) * .5f, Vector3.one * .1f)) == null)
                throw new InvalidOperationException("Accepted reserved volume " + zone.name);
        foreach (var actor in layout.Report.actors.Where(a => a.kind != "turret"))
            if (layout.KeepOutViolation(new Bounds(actor.position + Vector3.up, Vector3.one * .2f)) == null)
                throw new InvalidOperationException("Accepted actor envelope " + actor.name);
        foreach (var deck in layout.Report.deckFootprints)
            if (layout.KeepOutViolation(new Bounds(new Vector3((deck.min.x + deck.max.x) * .5f, deck.max.y + .5f, (deck.min.z + deck.max.z) * .5f), Vector3.one * .1f)) == null)
                throw new InvalidOperationException("Accepted deck headroom " + deck.name);
        if (layout.KeepOutViolation(new Bounds(layout.interior.max + Vector3.one, Vector3.one)) == null)
            throw new InvalidOperationException("Accepted outside placement");
        if (layout.KeepOutViolation(new Bounds(AllowedPoint(layout), Vector3.one * .1f)) != null)
            throw new InvalidOperationException("Rejected measured overhead safe zone");

        var all = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        var door = all.Single(t => t.name == BossHallSurvey.BossDoorName);
        string oldName = door.name;
        try { door.name = "__Missing_Door"; ExpectRejected(() => BossHallFramework.Build(layout), "missing anchor"); }
        finally { door.name = oldName; }
        var duplicate = new GameObject(BossHallSurvey.BossDoorName);
        try { ExpectRejected(() => BossHallFramework.Build(layout), "ambiguous anchor"); }
        finally { Object.DestroyImmediate(duplicate); }
        var actorTransform = all.First(t => t.name == layout.Report.actors[0].name);
        Vector3 oldPosition = actorTransform.localPosition;
        try { actorTransform.localPosition += Vector3.up; ExpectRejected(() => BossHallFramework.Build(layout), "changed actor baseline"); }
        finally { actorTransform.localPosition = oldPosition; }
        var original = layout.contract.protectedSceneDigest;
        try { layout.contract.protectedSceneDigest = "stale"; ExpectRejected(() => BossHallFramework.Build(layout), "changed protected baseline"); }
        finally { layout.contract.protectedSceneDigest = original; }

        var materialProbe = Probe(BossHallFramework.FindSection(BossHallFramework.Section.Rendering), "__Material_Probe", AllowedPoint(layout));
        try
        {
            var r = materialProbe.AddComponent<MeshRenderer>();
            var borrowed = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true))
                .Where(x => x != r && BossHallFramework.PathOf(x.transform).StartsWith("Level/NorthWest_Room/", StringComparison.Ordinal))
                .SelectMany(x => x.sharedMaterials).First(m => m);
            r.sharedMaterial = borrowed;
            ExpectRejected(BossHallFramework.ValidateMaterials, "borrowed room material");
            ExpectRejected(() => BossHallFramework.RequireOwnedAsset(borrowed, BossHallFramework.Section.Rendering), "unowned generated asset");
        }
        finally { Object.DestroyImmediate(materialProbe); }
        var componentProbe = Probe(BossHallFramework.FindSection(BossHallFramework.Section.Rendering), "__Gameplay_Probe", AllowedPoint(layout));
        try
        {
            componentProbe.AddComponent<Unity.FPS.Game.Health>();
            ExpectRejected(() => BossHallFramework.Validate(layout), "gameplay component");
        }
        finally { Object.DestroyImmediate(componentProbe); }
        BossHallSnapshot.Compare(protectedBefore, BossHallSnapshot.Capture(SceneManager.GetActiveScene(), BossHallFramework.FindRoot()), "Protected state after negative checks");
        BossHallFramework.Validate(layout);
        Undo.ClearAll(); // Never leave verification fixtures in the user's Undo history.
    }

    static Vector3 AllowedPoint(BossHallFramework.Layout layout)
    {
        var zone = layout.Report.zones.Single(z => z.kind == "overhead"); return (zone.min + zone.max) * .5f;
    }
    static GameObject Probe(Transform parent, string name, Vector3 point)
    { var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = point; return go; }
    static void ExpectRejected(Action action, string label)
    {
        bool rejected = false;
        try { action(); } catch (InvalidOperationException) { rejected = true; }
        if (!rejected) throw new InvalidOperationException("Negative check accepted " + label);
    }
}
