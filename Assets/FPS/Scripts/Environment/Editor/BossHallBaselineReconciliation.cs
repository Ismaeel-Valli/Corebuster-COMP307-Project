using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Explicit, one-time acceptance of the reviewed env_046 revision before art_006. Never called by a builder.</summary>
public static class BossHallBaselineReconciliation
{
    /// <summary>Explicit env_057 prerequisite: accept only the audited corridor/quality merge at 3eb1892.
    /// Never called automatically by a graphics builder. Existing hall objects and survey/skin data are retained.</summary>
    public static void AcceptGraphicsBaselineBatch()
    {
        const string sceneSha = "d76359d411fb4e1b537de4505f5328ff23f2ec1528e5d5577ed8755d476cbcdf";
        const string contractSha = "1cbc61bbb57fc352dc37bb7e719b1f5cdf02a2999cbd10e505bef4d2ad9c5206";
        const string manifestSha = "1253e839e5e25a78a2403cb3d877c222d429669869665cfd042e720a74ee272c";
        const string audit = "Docs/BossHall/BaselineAudit_057.txt";
        if (!Application.isBatchMode || BossHallSurvey.Hash(BossHallSurvey.ScenePath) != sceneSha ||
            BossHallSurvey.Hash(BossHallFramework.ContractPath) != contractSha || BossHallSurvey.Hash(BossHallRendering.ManifestPath) != manifestSha ||
            BossHallSurvey.Hash(audit) != "97c73d3461983a0d6eb34b67a13d5b2ff412f552ca995a2899ae033fde0db63c")
            throw new InvalidOperationException("env_057 reconciliation requires the exact reviewed 3eb1892 baseline and prior bindings.");
        foreach (var line in File.ReadAllLines(audit).Where(l => l.StartsWith("Assets/", StringComparison.Ordinal) || l.StartsWith("ProjectSettings/", StringComparison.Ordinal)))
        {
            int split = line.LastIndexOf(' ');
            if (BossHallSurvey.Hash(line.Substring(0, split)) != line.Substring(split + 1))
                throw new InvalidOperationException("Audited merge asset changed: " + line.Substring(0, split));
        }
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        string oldContract = File.ReadAllText(BossHallFramework.ContractPath), oldManifest = File.ReadAllText(BossHallRendering.ManifestPath);
        var contract = JsonUtility.FromJson<BossHallFramework.Contract>(oldContract);
        var manifest = JsonUtility.FromJson<BossHallRendering.Manifest>(oldManifest);
        if (manifest.contractSha != contractSha || manifest.protectedSceneDigest != contract.protectedSceneDigest)
            throw new InvalidOperationException("Prior skin/contract bindings disagree.");
        var states = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), BossHallFramework.FindRoot());
        var assets = BossHallSnapshot.CaptureAssets();
        string prior = contract.protectedSceneDigest, priorAssets = contract.protectedAssetDigest;
        contract.protectedSceneDigest = BossHallSnapshot.Digest(states); contract.protectedStateCount = states.Count;
        contract.protectedAssetDigest = BossHallSnapshot.Digest(assets); contract.protectedAssetCount = assets.Count;
        try
        {
            File.WriteAllText(BossHallFramework.ContractPath, JsonUtility.ToJson(contract, true) + "\n");
            manifest.contractSha = BossHallSurvey.Hash(BossHallFramework.ContractPath); manifest.protectedSceneDigest = contract.protectedSceneDigest;
            File.WriteAllText(BossHallRendering.ManifestPath, JsonUtility.ToJson(manifest, true) + "\n");
            AssetDatabase.ImportAsset(BossHallFramework.ContractPath); AssetDatabase.ImportAsset(BossHallRendering.ManifestPath);
            var layout = BossHallFramework.MeasureLayout(); BossHallFramework.Validate(layout); BossHallRendering.Validate(layout);
            BossHallRenderingReview.ValidateNeighbours(); BossHallGraphics.Reservation(layout);
            if (SceneManager.GetActiveScene().isDirty || BossHallSurvey.Hash(BossHallSurvey.ScenePath) != sceneSha)
                throw new InvalidOperationException("Graphics reconciliation changed MainScene.");
            File.WriteAllText("Docs/BossHall/Reconciliation_057.txt", "Explicitly accepted owner-approved 3eb1892 corridor/quality baseline for env_057.\n" +
                "MainScene unchanged: " + sceneSha + "\nPrior protected scene: " + prior + "\nCurrent: " + contract.protectedSceneDigest + " (" + states.Count + ")\n" +
                "Prior shared digest: " + priorAssets + "\nCurrent: " + contract.protectedAssetDigest + " (" + assets.Count + ")\n" +
                "Only protected fingerprints/counts and manifest bindings changed; measured survey, skin list and all hall sections retained.\n" +
                "Framework, rendering, neighbour-framework and sign reservation validation passed. No gameplay or all-quality rendering acceptance.\n");
        }
        catch
        {
            File.WriteAllText(BossHallFramework.ContractPath, oldContract); File.WriteAllText(BossHallRendering.ManifestPath, oldManifest);
            AssetDatabase.ImportAsset(BossHallFramework.ContractPath); AssetDatabase.ImportAsset(BossHallRendering.ManifestPath); throw;
        }
    }
    /// <summary>
    /// env_056 prerequisite, approved by the owner on 2026-10-07: explicit acceptance of merge 389eff4 (tutorial wing,
    /// corridor lamp and the giant-core finale). MergeAudit_056.txt proves the Boss_Hall_Dressing subtree is unchanged
    /// apart from YAML line wrapping. Unlike earlier reconciliations, the merge also changed protected shared assets
    /// (three enemy prefabs plus added materials/prefabs), so both digests are re-accepted. Survey data and surface
    /// bindings are retained.
    /// </summary>
    public static void AcceptFinaleMergeBatch()
    {
        const string sceneSha = "61deb08049452ba7e3dd403b7d6e5cf27f8495653ec908401271bac7dfbc2cb9";
        const string contractSha = "1009e5c7d3f58d2a275216a698ad22ab169d576cca8f9ae400f998b275f3aaae";
        const string manifestSha = "a521e470940665d16809f389919f24ade522d462deee5c1f5a82d3f0c285a8e6";
        const string auditPath = "Docs/BossHall/MergeAudit_056.txt", auditSha = "087cbf47444714aee40320b77a6a4ee480faa5ee53be2185ddfc933f2e2faf32";
        if (!Application.isBatchMode) throw new InvalidOperationException("Explicit reconciliation requires exclusive batch access.");
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        if (BossHallSurvey.Hash(BossHallSurvey.ScenePath) != sceneSha || BossHallSurvey.Hash(auditPath) != auditSha ||
            BossHallSurvey.Hash(BossHallFramework.ContractPath) != contractSha || BossHallSurvey.Hash(BossHallRendering.ManifestPath) != manifestSha)
            throw new InvalidOperationException("Finale reconciliation accepts only the audited merge 389eff4 and its unchanged prior bindings.");
        string oldContract = File.ReadAllText(BossHallFramework.ContractPath), oldManifest = File.ReadAllText(BossHallRendering.ManifestPath);
        var contract = JsonUtility.FromJson<BossHallFramework.Contract>(oldContract);
        var manifest = JsonUtility.FromJson<BossHallRendering.Manifest>(oldManifest);
        if (manifest.contractSha != contractSha || manifest.protectedSceneDigest != contract.protectedSceneDigest)
            throw new InvalidOperationException("Prior rendering/contract bindings disagree.");
        string priorScene = contract.protectedSceneDigest, priorAssets = contract.protectedAssetDigest;
        int priorStates = contract.protectedStateCount, priorAssetCount = contract.protectedAssetCount;
        var assets = BossHallSnapshot.CaptureAssets();
        var states = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), BossHallFramework.FindRoot());
        contract.protectedSceneDigest = BossHallSnapshot.Digest(states); contract.protectedStateCount = states.Count;
        contract.protectedAssetDigest = BossHallSnapshot.Digest(assets); contract.protectedAssetCount = assets.Count;
        try
        {
            File.WriteAllText(BossHallFramework.ContractPath, JsonUtility.ToJson(contract, true) + "\n");
            manifest.contractSha = BossHallSurvey.Hash(BossHallFramework.ContractPath);
            manifest.protectedSceneDigest = contract.protectedSceneDigest;
            File.WriteAllText(BossHallRendering.ManifestPath, JsonUtility.ToJson(manifest, true) + "\n");
            AssetDatabase.ImportAsset(BossHallFramework.ContractPath); AssetDatabase.ImportAsset(BossHallRendering.ManifestPath);
            // Every saved section, keep-out, material rule and installed energy/VFX/lighting recipe must still validate.
            var layout = BossHallFramework.MeasureLayout(); BossHallFramework.Validate(layout); BossHallRendering.Validate(layout);
            BossHallRenderingReview.ValidateNeighbours();
            string finale = FinaleReport(layout);
            if (SceneManager.GetActiveScene().isDirty || BossHallSurvey.Hash(BossHallSurvey.ScenePath) != sceneSha)
                throw new InvalidOperationException("Reconciliation changed MainScene.");
            File.WriteAllText("Docs/BossHall/Reconciliation_056.txt",
                "PASS: explicitly accepted the owner-approved merge 389eff4 (tutorial wing, corridor lamp, giant-core finale) for corebuster_env_056.\n" +
                "Scene unchanged: " + sceneSha + "\nAudit: " + auditPath + " " + auditSha + "\nPrior contract: " + contractSha + "\nCurrent contract: " + manifest.contractSha +
                "\nProtected state: " + priorScene + " (" + priorStates + ") -> " + contract.protectedSceneDigest + " (" + states.Count + " entries)\n" +
                "Shared assets: " + priorAssets + " (" + priorAssetCount + ") -> " + contract.protectedAssetDigest + " (" + assets.Count + " entries); changes listed in the audit.\n" +
                "Framework, all installed section recipes, rendering skins, Centre and Room 3 validators pass. Survey/reservation data and surface bindings retained;\n" +
                "only fingerprint/count fields and the manifest binding changed. This does not verify finale gameplay, containment or win ordering.\n" + finale);
            Debug.Log("BOSS HALL FINALE MERGE EXPLICITLY RECONCILED");
        }
        catch
        {
            File.WriteAllText(BossHallFramework.ContractPath, oldContract); File.WriteAllText(BossHallRendering.ManifestPath, oldManifest);
            AssetDatabase.ImportAsset(BossHallFramework.ContractPath); AssetDatabase.ImportAsset(BossHallRendering.ManifestPath); throw;
        }
    }

    // Finale objects inside the hall, and any overlap with dressing bounds or the measured reservations (reported, not repaired).
    static string FinaleReport(BossHallFramework.Layout layout)
    {
        var root = SceneManager.GetActiveScene().GetRootGameObjects().SingleOrDefault(g => g.name == "===== FINALE =====");
        if (!root) return "Finale root absent.\n";
        var skins = BossHallFramework.FindSection(BossHallFramework.Section.Rendering);
        var dressing = BossHallFramework.FindRoot().GetComponentsInChildren<Renderer>(true).Where(r => !r.transform.IsChildOf(skins)).ToArray();
        var lines = new System.Collections.Generic.List<string>();
        var parts = root.GetComponentsInChildren<Renderer>(true).Select(r => (kind: "renderer", path: BossHallFramework.PathOf(r.transform), b: r.bounds))
            .Concat(root.GetComponentsInChildren<Collider>(true).Select(c => (kind: "collider", path: BossHallFramework.PathOf(c.transform), b: c.bounds)))
            .Where(x => x.b.Intersects(layout.interior)).ToArray();
        lines.Add($"Finale in hall: {parts.Count(x => x.kind == "renderer")} renderers, {parts.Count(x => x.kind == "collider")} colliders (reported, not repaired; finale-owned).");
        foreach (var group in parts.GroupBy(x => x.path.Split('/').Skip(1).FirstOrDefault() ?? x.path))
        {
            Bounds g = group.First().b; foreach (var x in group) g.Encapsulate(x.b);
            var touching = dressing.Where(r => r.bounds.Intersects(g)).Select(r => r.name).Distinct().Take(6).ToArray();
            lines.Add($"  {group.Key}: {group.Count()} parts, bounds {g.min:F2}..{g.max:F2}; contract reservation: {layout.KeepOutViolation(g) ?? "none"}; dressing bounds touched: " +
                (touching.Length == 0 ? "none" : string.Join(", ", touching)));
        }
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>Explicit acceptance of the user's synchronized spawn-room merge; never invoked by a builder.</summary>
    public static void AcceptSpawnRoomMergeBatch()
    {
        const string sceneSha = "398f28174fd740665d66bb1a4cf2e0cd0b5898052d10c4d35c5b1495844803b1";
        const string contractSha = "11dfb8aa3c6eef2fe1a5bcb0bab68ca3023b75e264a57fb94d46de316ffaf4b6";
        const string manifestSha = "78c2aa613d75be3a03e9b7b61abfff85f90e11ae62f73a24954ff50b19b2a0bf";
        const string auditSha = "953c68bd3bc1793aa16b1ca3240263ede12784f3503e4640c61db4613d419954";
        if (!Application.isBatchMode) throw new InvalidOperationException("Explicit reconciliation requires exclusive batch access.");
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        if (BossHallSurvey.Hash(BossHallSurvey.ScenePath) != sceneSha ||
            BossHallSurvey.Hash("Docs/BossHall/MergePreservation_051.txt") != auditSha ||
            BossHallSurvey.Hash(BossHallFramework.ContractPath) != contractSha ||
            BossHallSurvey.Hash(BossHallRendering.ManifestPath) != manifestSha)
            throw new InvalidOperationException("Spawn-room reconciliation accepts only the audited merge and its unchanged prior bindings.");
        string oldContract = File.ReadAllText(BossHallFramework.ContractPath), oldManifest = File.ReadAllText(BossHallRendering.ManifestPath);
        var contract = JsonUtility.FromJson<BossHallFramework.Contract>(oldContract);
        var manifest = JsonUtility.FromJson<BossHallRendering.Manifest>(oldManifest);
        if (manifest.contractSha != contractSha || manifest.protectedSceneDigest != contract.protectedSceneDigest)
            throw new InvalidOperationException("Prior rendering/contract bindings disagree.");
        var assets = BossHallSnapshot.CaptureAssets();
        if (assets.Count != contract.protectedAssetCount || BossHallSnapshot.Digest(assets) != contract.protectedAssetDigest)
            throw new InvalidOperationException("Shared assets/settings changed; the approved merge changes scene objects only.");
        var states = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), BossHallFramework.FindRoot());
        contract.protectedSceneDigest = BossHallSnapshot.Digest(states); contract.protectedStateCount = states.Count;
        try
        {
            File.WriteAllText(BossHallFramework.ContractPath, JsonUtility.ToJson(contract, true) + "\n");
            manifest.contractSha = BossHallSurvey.Hash(BossHallFramework.ContractPath);
            manifest.protectedSceneDigest = contract.protectedSceneDigest;
            File.WriteAllText(BossHallRendering.ManifestPath, JsonUtility.ToJson(manifest, true) + "\n");
            AssetDatabase.ImportAsset(BossHallFramework.ContractPath); AssetDatabase.ImportAsset(BossHallRendering.ManifestPath);
            var layout = BossHallFramework.MeasureLayout(); BossHallServices.Validate(layout);
            BossHallRenderingReview.ValidateNeighbours();
            if (SceneManager.GetActiveScene().isDirty || BossHallSurvey.Hash(BossHallSurvey.ScenePath) != sceneSha)
                throw new InvalidOperationException("Reconciliation changed MainScene.");
            File.WriteAllText("Docs/BossHall/Reconciliation_051.txt",
                "PASS: explicitly accepted the user-authorized spawn-room merge after exact two-parent YAML audit.\n" +
                "Scene unchanged: " + sceneSha + "\nPrior contract: " + contractSha + "\nCurrent contract: " + manifest.contractSha +
                "\nProtected state: " + contract.protectedSceneDigest + " (" + states.Count + " entries)\n" +
                "Shared assets unchanged: " + contract.protectedAssetDigest + " (" + assets.Count + " entries)\n" +
                "Existing Boss Hall services, arms, backdrop, rendering, Centre and Room 3 validators pass.\n" +
                "All survey/reservation data and surface bindings retained; only fingerprint/count and manifest binding fields updated.\n" +
                "Remote spawn-room and Player start retained as requested; this does not verify their gameplay.\n");
            Debug.Log("BOSS HALL SPAWN-ROOM MERGE EXPLICITLY RECONCILED");
        }
        catch
        {
            File.WriteAllText(BossHallFramework.ContractPath, oldContract); File.WriteAllText(BossHallRendering.ManifestPath, oldManifest);
            AssetDatabase.ImportAsset(BossHallFramework.ContractPath); AssetDatabase.ImportAsset(BossHallRendering.ManifestPath); throw;
        }
    }

    const string ApprovedScene = "37aa4910a9d2d9cb1f89caae9956b5f1b65899cefad3eab53f334bc6a6e859d7";
    const string PriorContract = "2b2d602edf5df9435d50a52969de39f6a169bc1d592f0a70d34edb6951802c4f";
    const string PriorManifest = "0eda0d54d9a5011ccf01af61046c377b78e4e9daf091db0eeb14035d3251b242";
    const string AuditPath = "Docs/BossHall/Reconciliation_006.txt";
    const string AuditSha = "2aa0a97f08152231b97bb3ec7fe1e52e0e573715034485c92c97607bc655c014";

    public static void AcceptEnv046Batch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Explicit reconciliation requires exclusive batch access.");
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        if (BossHallSurvey.Hash(BossHallSurvey.ScenePath) != ApprovedScene || BossHallSurvey.Hash(AuditPath) != AuditSha ||
            BossHallSurvey.Hash(BossHallFramework.ContractPath) != PriorContract || BossHallSurvey.Hash(BossHallRendering.ManifestPath) != PriorManifest)
            throw new InvalidOperationException("Reconciliation accepts only the independently audited env_046 revision and its unchanged prior contracts.");
        var contract = JsonUtility.FromJson<BossHallFramework.Contract>(File.ReadAllText(BossHallFramework.ContractPath));
        var manifest = JsonUtility.FromJson<BossHallRendering.Manifest>(File.ReadAllText(BossHallRendering.ManifestPath));
        if (manifest.contractSha != PriorContract || manifest.protectedSceneDigest != contract.protectedSceneDigest)
            throw new InvalidOperationException("Prior rendering/contract bindings do not agree.");
        var assets = BossHallSnapshot.CaptureAssets();
        if (assets.Count != contract.protectedAssetCount || BossHallSnapshot.Digest(assets) != contract.protectedAssetDigest)
            throw new InvalidOperationException("Protected assets/settings changed; env_046 authorized scene and generator changes only.");
        var states = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), BossHallFramework.FindRoot());
        contract.protectedSceneDigest = BossHallSnapshot.Digest(states); contract.protectedStateCount = states.Count;
        string oldContract = File.ReadAllText(BossHallFramework.ContractPath), oldManifest = File.ReadAllText(BossHallRendering.ManifestPath);
        try
        {
            File.WriteAllText(BossHallFramework.ContractPath, JsonUtility.ToJson(contract, true) + "\n");
            // Only these two binding fields change; the surface list and each saved mesh hash remain untouched.
            manifest.contractSha = BossHallSurvey.Hash(BossHallFramework.ContractPath); manifest.protectedSceneDigest = contract.protectedSceneDigest;
            File.WriteAllText(BossHallRendering.ManifestPath, JsonUtility.ToJson(manifest, true) + "\n");
            AssetDatabase.ImportAsset(BossHallFramework.ContractPath); AssetDatabase.ImportAsset(BossHallRendering.ManifestPath);
            var layout = BossHallFramework.MeasureLayout(); BossHallBackdrop.Validate(layout); BossHallRenderingReview.ValidateNeighbours();
            if (SceneManager.GetActiveScene().isDirty || BossHallSurvey.Hash(BossHallSurvey.ScenePath) != ApprovedScene)
                throw new InvalidOperationException("Reconciliation changed MainScene.");
            File.WriteAllText("Docs/BossHall/ReconciliationBindings_006.txt", "corebuster_art_006 prerequisite: explicitly reconciled verified corebuster_env_046 only.\n" +
                "Scene: " + ApprovedScene + "\nAudit: " + AuditSha + "\nPrior contract: " + PriorContract + "\nCurrent contract: " + manifest.contractSha +
                "\nProtected scene: " + contract.protectedSceneDigest + " (" + states.Count + " states)\nShared asset digest unchanged: " + contract.protectedAssetDigest +
                " (" + assets.Count + " entries)\nPASS: backdrop, exact unchanged rendering skins, measured reservations, Centre and Room 3 validators. MainScene unchanged.\n");
            Debug.Log("BOSS HALL ENV046 BASELINE EXPLICITLY RECONCILED");
        }
        catch
        {
            File.WriteAllText(BossHallFramework.ContractPath, oldContract); File.WriteAllText(BossHallRendering.ManifestPath, oldManifest);
            AssetDatabase.ImportAsset(BossHallFramework.ContractPath); AssetDatabase.ImportAsset(BossHallRendering.ManifestPath); throw;
        }
    }
}
