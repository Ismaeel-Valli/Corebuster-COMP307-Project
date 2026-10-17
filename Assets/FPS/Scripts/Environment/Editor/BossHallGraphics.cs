using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Section = BossHallFramework.Section;

/// <summary>corebuster_env_057: inert identity plates on the reserved cradle and measured gallery walls.</summary>
public static class BossHallGraphics
{
    public const string Owner = "corebuster_env_057", Folder = BossHallFramework.AssetRoot + "/Wayfinding";
    public const string Output = "ReviewCaptures/BossHall/057", TexturePath = Folder + "/T_Final_Assembly.png";
    const string MaterialPath = Folder + "/M_Final_Assembly.mat";
    const string TextureSha = "33328addf006ef85f546d49135ce33d011303f0c4df296a7619d41829cffa8c2";
    static bool upgradingLegacyPlate;
    static Transform SectionRoot => BossHallFramework.FindSection(Section.Wayfinding);
    public static BossHallReveal.SignReservation Reservation(BossHallFramework.Layout layout) =>
        BossHallReveal.SignReservations(layout).Single(s => s.name == "Sign_Final_Assembly");
    public static List<BossHallReveal.SignReservation> Plan(BossHallFramework.Layout layout)
    {
        var result = new List<BossHallReveal.SignReservation> { Reservation(layout) };
        var root = BossHallFramework.FindRoot(); var owned = SectionRoot;
        var obstacles = root.GetComponentsInChildren<Renderer>(true).Where(r =>
            !r.transform.IsChildOf(owned) && !r.transform.IsChildOf(BossHallFramework.FindSection(Section.Rendering))).ToArray();
        foreach (bool north in new[] { true, false })
        {
            var normal = north ? Vector3.back : Vector3.forward;
            var tangent = Vector3.Cross(normal, Vector3.up);
            BossHallReveal.SignReservation selected = null;
            // Prefer the western gallery approach, then search east in measured one-metre steps.
            for (float x = layout.interior.min.x + 14; x < layout.interior.max.x - 10 && selected == null; x += 1)
                for (float y = layout.Report.tier1TopY + 3.4f; y <= layout.Report.tier1TopY + 4.4f && selected == null; y += .25f)
                {
                    var origin = new Vector3(x, y, north ? layout.interior.max.z - 3 : layout.interior.min.z + 3);
                    var hit = Physics.RaycastAll(origin, -normal, 4, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance)
                        .FirstOrDefault(h => Vector3.Angle(h.normal, normal) < 10);
                    if (!hit.collider) continue;
                    var sign = new BossHallReveal.SignReservation { name = "Sign_Gallery_" + (north ? "North" : "South"),
                        centre = hit.point + normal * .06f, normal = normal, size = new Vector2(4, 1),
                        purpose = "Printed final-assembly room identity from the original first-tier gallery, above walking headroom." };
                    if (layout.KeepOutViolation(sign.Box) != null) continue;
                    var front = sign.Box; front.center += normal * .2f; front.Expand(new Vector3(Mathf.Abs(normal.x), 0, Mathf.Abs(normal.z)) * .36f);
                    if (Physics.CheckBox(front.center, front.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore) || obstacles.Any(r => r.bounds.Intersects(front))) continue;
                    bool backed = true;
                    foreach (float u in new[] { -.5f, .5f }) foreach (float v in new[] { -.5f, .5f })
                    {
                        var corner = sign.centre + tangent * sign.size.x * u + Vector3.up * sign.size.y * v + normal;
                        var h = Physics.RaycastAll(corner, -normal, 2, ~0, QueryTriggerInteraction.Ignore).OrderBy(a => a.distance).FirstOrDefault();
                        backed &= h.collider && Mathf.Abs(h.distance - 1.06f) < .05f && Vector3.Angle(h.normal, normal) < 10;
                    }
                    if (backed) selected = sign;
                }
            if (selected == null) throw new InvalidOperationException("No backed, keep-out-valid gallery graphic patch: " + (north ? "North" : "South"));
            result.Add(selected);
        }
        return result;
    }

    static void Assets()
    {
        if (BossHallSurvey.Hash(TexturePath) != TextureSha) throw new InvalidOperationException("Regenerate the approved FINAL ASSEMBLY texture first.");
        AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
        importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true;
        importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = 8;
        importer.maxTextureSize = 2048; importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (!shader) throw new InvalidOperationException("Missing URP Unlit shader.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, MaterialPath); }
        if (material.parent) throw new InvalidOperationException("Graphic material must be flattened.");
        material.shader = shader; material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
        material.SetColor("_BaseColor", Color.white); material.SetTextureScale("_BaseMap", Vector2.one);
        material.SetTextureOffset("_BaseMap", Vector2.zero); material.SetFloat("_Surface", 0); material.SetFloat("_Cull", 2);
        material.SetFloat("_AlphaClip", 0); material.renderQueue = 2000;
        EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
    }

    static void Populate(Transform root, BossHallReveal.SignReservation sign)
    {
        var go = new GameObject(sign.name); Undo.RegisterCreatedObjectUndo(go, "Create Boss Hall assembly plate");
        go.transform.SetParent(root, false); go.transform.SetPositionAndRotation(sign.centre, Quaternion.LookRotation(-sign.normal));
        go.transform.localScale = new Vector3(sign.size.x, sign.size.y, 1);
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    // Empty Wayfinding remains valid for framework initialization and isolated clear/Undo.
    public static void ValidateInstalled(BossHallFramework.Layout layout)
    {
        var root = SectionRoot; if (root.childCount == 0) return;
        var signs = Plan(layout);
        // Scoped migration from the first saved cradle-only review. The final validator requires three.
        if (upgradingLegacyPlate && root.childCount == 1) signs = signs.Take(1).ToList();
        if (root.childCount != signs.Count) throw new InvalidOperationException("Wayfinding must contain only the three measured identity plates.");
        foreach (var sign in signs)
        {
        var t = root.Find(sign.name);
        if (!t) throw new InvalidOperationException("Missing measured identity plate: " + sign.name);
        if (t.name != sign.name || t.childCount != 0 || !t.gameObject.activeSelf || t.gameObject.layer != 0 || t.tag != "Untagged" ||
            Vector3.Distance(t.position, sign.centre) > .001f || Quaternion.Angle(t.rotation, Quaternion.LookRotation(-sign.normal)) > .01f ||
            Vector3.Distance(t.localScale, new Vector3(sign.size.x, sign.size.y, 1)) > .001f)
            throw new InvalidOperationException("Assembly plate name, facing or reserved transform changed.");
        var components = t.GetComponents<Component>(); var renderer = t.GetComponent<MeshRenderer>(); var filter = t.GetComponent<MeshFilter>();
        if (components.Length != 3 || !renderer || !filter || !renderer.enabled || renderer.HasPropertyBlock() ||
            filter.sharedMesh != Resources.GetBuiltinResource<Mesh>("Quad.fbx") || renderer.shadowCastingMode != ShadowCastingMode.Off ||
            renderer.receiveShadows || renderer.lightProbeUsage != LightProbeUsage.Off || renderer.reflectionProbeUsage != ReflectionProbeUsage.Off)
            throw new InvalidOperationException("Assembly plate must be an inert, shadow-free Quad with saved UVs.");
        var material = renderer.sharedMaterial;
        if (renderer.sharedMaterials.Length != 1 || !material || AssetDatabase.GetAssetPath(material) != MaterialPath || material.parent ||
            material.shader.name != "Universal Render Pipeline/Unlit" || material.GetColor("_BaseColor") != Color.white ||
            AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")) != TexturePath || material.GetTextureScale("_BaseMap") != Vector2.one ||
            material.GetTextureOffset("_BaseMap") != Vector2.zero || material.GetFloat("_Cull") != 2 || material.GetFloat("_Surface") != 0 ||
            material.GetFloat("_AlphaClip") != 0 || material.renderQueue != 2000 || BossHallSurvey.Hash(TexturePath) != TextureSha)
            throw new InvalidOperationException("Assembly plate material, lettering or persistent tiling changed.");
        var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
        if (!importer.mipmapEnabled || !importer.sRGBTexture || importer.wrapMode != TextureWrapMode.Clamp ||
            importer.filterMode != FilterMode.Trilinear || importer.anisoLevel != 8 || importer.maxTextureSize != 2048 ||
            importer.textureCompression != TextureImporterCompression.Uncompressed)
            throw new InvalidOperationException("Assembly plate import settings changed.");
        if (layout.KeepOutViolation(renderer.bounds) is string violation) throw new InvalidOperationException("Plate enters " + violation);
        }
    }
    public static void Validate(BossHallFramework.Layout layout)
    {
        if (SectionRoot.childCount != 3) throw new InvalidOperationException("Missing identity plates.");
        BossHallFramework.Validate(layout); ValidateInstalled(layout);
    }

    static Dictionary<string, string> Protected()
    {
        var result = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), BossHallFramework.FindRoot());
        foreach (var spec in BossHallFramework.Sections.Where(s => s.section != Section.Wayfinding))
            foreach (var p in BossHallSnapshot.Capture(SceneManager.GetActiveScene(), null, BossHallFramework.FindSection(spec.section)))
                result[spec.name + "|" + p.Key] = p.Value;
        return result;
    }
    static void Guards(BossHallFramework.Layout layout)
    {
        var t = SectionRoot.GetChild(0); var rotation = t.rotation; var position = t.position;
        void Reject(Action action, string label)
        {
            try { action(); } catch (InvalidOperationException) { Debug.Log("PASS: graphics guard " + label); return; }
            throw new InvalidOperationException("Graphics accepted " + label);
        }
        try { t.Rotate(0, 180, 0); Reject(() => Validate(layout), "reversed face"); } finally { t.rotation = rotation; }
        try { t.position += Vector3.up; Reject(() => Validate(layout), "outside reservation"); } finally { t.position = position; }
        var material = t.GetComponent<Renderer>().sharedMaterial;
        try { material.SetTextureScale("_BaseMap", new Vector2(2, 1)); Reject(() => Validate(layout), "repeated lettering"); }
        finally { material.SetTextureScale("_BaseMap", Vector2.one); EditorUtility.ClearDirty(material); }
        var collider = t.gameObject.AddComponent<BoxCollider>();
        try { Reject(() => Validate(layout), "interaction collision"); } finally { Object.DestroyImmediate(collider); }
        Reject(() => BossHallFramework.RebuildSection(layout, Section.Wayfinding, "corebuster_env_056", _ => { }), "wrong owner");
        Physics.SyncTransforms(); Validate(layout);
    }
    [MenuItem("Tools/CoreBuster/Build Boss Hall Graphics")]
    public static void BuildMenu()
    {
        bool previous = upgradingLegacyPlate; upgradingLegacyPlate = true;
        try {
        var layout = BossHallFramework.MeasureLayout(); var signs = Plan(layout); Assets();
        BossHallFramework.RebuildSection(layout, Section.Wayfinding, Owner, root => { foreach (var sign in signs) Populate(root, sign); }); Validate(layout);
        } finally { upgradingLegacyPlate = previous; }
    }
    public static void BuildBatch()
    {
        upgradingLegacyPlate = true;
        try { BuildAndVerify(); } finally { upgradingLegacyPlate = false; }
    }
    static void BuildAndVerify()
    {
        if (!Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("Use exclusive batch Editor access with graphics.");
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); BossHallFramework.Validate(layout);
        var before = Protected(); var shared = BossHallSnapshot.CaptureAssets();
        BossHallGraphicsReview.Neighbours(layout, false);
        BuildMenu(); Guards(layout);
        BossHallFramework.RebuildSection(layout, Section.Wayfinding, Owner, _ => { }); Undo.PerformUndo(); Validate(layout);
        layout = BossHallFramework.SaveAndReload(layout); Validate(layout);
        BossHallSnapshot.Compare(before, Protected(), "Graphics protected scene/other sections after reload");
        BossHallSnapshot.Compare(shared, BossHallSnapshot.CaptureAssets(), "Graphics protected assets after reload");
        BossHallGraphicsReview.Capture(layout); BossHallGraphicsReview.Neighbours(layout, true);
        Directory.CreateDirectory("Docs/BossHall");
        File.WriteAllText("Docs/BossHall/Graphics_057.txt", "FINAL ASSEMBLY: printed identity and safety border; no interaction or route arrow.\n" +
            "Three measured backed identity plates: cradle and both first-tier gallery walls; above walking headroom.\n" +
            "Five negative guards, isolated clear/Undo, saved reload and framework validation passed.\n" +
            before.Count + " protected states and " + shared.Count + " shared assets unchanged.\n" +
            "Scene SHA-256 " + BossHallSurvey.Hash(BossHallSurvey.ScenePath) + "\n" + BossHallFramework.BudgetReport() + "\n");
        Debug.Log("BOSS HALL GRAPHICS BUILD PASSED");
    }
    public static void ValidateBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        Validate(BossHallFramework.MeasureLayout()); BossHallRenderingReview.ValidateNeighbours();
        Debug.Log("BOSS HALL GRAPHICS SAVED VALIDATION PASSED");
    }
    // Deliberately read-only when the protected baseline is stale: measures the reservation,
    // records current digests, and never adopts them or writes assets/scene content.
    public static void PreflightBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = new BossHallFramework.Layout(JsonUtility.FromJson<BossHallFramework.Contract>(File.ReadAllText(BossHallFramework.ContractPath)));
        var sign = Reservation(layout); var before = Protected(); var shared = BossHallSnapshot.CaptureAssets();
        if (BossHallSurvey.Hash(TexturePath) != TextureSha) throw new InvalidOperationException("Unapproved graphics texture.");
        var states = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), BossHallFramework.FindRoot());
        Directory.CreateDirectory("Docs/BossHall");
        File.WriteAllText("Docs/BossHall/GraphicsPreflight_057.txt", "READ-ONLY graphics preflight; no baseline adoption or scene authoring.\n" +
            "Scene: " + layout.sceneSha + "\nProtected state: " + BossHallSnapshot.Digest(states) + " (" + states.Count + ")\n" +
            "Shared assets: " + BossHallSnapshot.Digest(shared) + " (" + shared.Count + ")\n" +
            "Contract matches current state/assets: " + (BossHallSnapshot.Digest(states) == layout.contract.protectedSceneDigest &&
                BossHallSnapshot.Digest(shared) == layout.contract.protectedAssetDigest) + "\n" +
            "Resolved " + sign.name + " centre " + sign.centre.ToString("F2") + " normal " + sign.normal + " size " + sign.size + "\n" +
            "Texture hash: " + TextureSha + "\n");
        BossHallSnapshot.Compare(before, Protected(), "Read-only graphics preflight scene");
        BossHallSnapshot.Compare(shared, BossHallSnapshot.CaptureAssets(), "Read-only graphics preflight assets");
        if (SceneManager.GetActiveScene().isDirty || BossHallSurvey.Hash(BossHallSurvey.ScenePath) != layout.sceneSha)
            throw new InvalidOperationException("Preflight changed MainScene.");
        Debug.Log("BOSS HALL GRAPHICS READ-ONLY PREFLIGHT PASSED");
    }
}
