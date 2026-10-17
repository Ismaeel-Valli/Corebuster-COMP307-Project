using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;

/// <summary>
/// corebuster_render_001. The workshop packs' materials are Built-in Standard materials (their saved properties are still
/// _MainTex, _Color, _BumpMap, _Metallic, _Glossiness and _Mode) whose shader was swapped to URP's 2D
/// Mesh2D-Lit-Default, or, in HorrorPuzzleItems, to built-in unlit particle/legacy shaders. In the 3D forward renderer
/// those draw unlit, in the transparent queue and without shadows. This converts them in place to URP/Lit: it restores
/// Standard so every saved property is readable again, then runs URP's own StandardUpgrader. Shared assets are edited,
/// so every scene using them changes; the audit lists the build scenes affected.
/// </summary>
public static class WorkshopMaterialUpgrade
{
    public static readonly string[] PackRoots =
        { "Assets/SciFi Warehouse Kit", "Assets/Sci-Fi Styled Modular Pack", "Assets/HorrorPuzzleItems" };

    // Exactly the misassigned shaders found by the audit. Skyboxes, particles and foliage in the packs are left alone.
    static readonly string[] UpgradeSources =
    {
        "Universal Render Pipeline/2D/Mesh2D-Lit-Default",
        "Particles/Standard Unlit",
        "Legacy Shaders/Transparent/Cutout/Soft Edge Unlit",
    };

    static readonly string[] AuditedMaps = { "_BaseMap", "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap" };

    /// <summary>
    /// Shaders the URP forward renderer draws unlit (URP 2D) or not at all (Built-in pipeline shaders). URP/Unlit is not
    /// included: project overlays use it deliberately.
    /// </summary>
    public static bool RendersFlat(Shader shader) =>
        shader == null ||
        shader.name.StartsWith("Universal Render Pipeline/2D/", StringComparison.Ordinal) ||
        shader.name.StartsWith("Legacy Shaders/", StringComparison.Ordinal) ||
        shader.name.StartsWith("Particles/", StringComparison.Ordinal) ||
        shader.name == "Standard" || shader.name == "Standard (Specular setup)" ||
        shader.name == "Hidden/InternalErrorShader";

    [MenuItem("Tools/CoreBuster/Audit Workshop Materials")]
    public static int Audit()
    {
        var usedBy = BuildSceneUsage();
        var report = new StringBuilder();
        int pending = 0;
        foreach (var (path, material) in PackMaterials())
        {
            bool upgrade = NeedsUpgrade(material);
            if (upgrade) pending++;
            var maps = AuditedMaps.Where(p => material.HasProperty(p) && material.GetTexture(p) != null);
            usedBy.TryGetValue(path, out var scenes);
            report.AppendLine($"  {(upgrade ? "UPGRADE " : "        ")}{path}: {material.shader.name} " +
                              $"[{string.Join(", ", maps)}] build scenes: {(scenes == null ? "none" : string.Join(", ", scenes))}");
        }
        string summary = pending == 0
            ? "WORKSHOP MATERIAL AUDIT PASSED: no pack material uses a 2D or built-in unlit shader."
            : $"WORKSHOP MATERIAL AUDIT: {pending} material(s) still need Tools/CoreBuster/Upgrade Workshop Materials to URP Lit.";
        if (pending == 0) Debug.Log($"{summary}\n{report}");
        else Debug.LogWarning($"{summary}\n{report}");
        return pending;
    }

    /// <summary>
    /// The Standard -> URP/Lit conversion routine, kept for corebuster_render_005 to run on project-owned copies of
    /// pack materials. Do not run this on the shared pack materials themselves; corebuster_render_004 removed the
    /// in-place menu item and batch entry point that did that, because it changed every scene that uses the packs.
    /// </summary>
    public static int Upgrade(List<(string path, Material material)> targets)
    {
        var standard = Shader.Find("Standard");
        if (standard == null || Shader.Find("Universal Render Pipeline/Lit") == null)
        {
            Debug.LogError("Workshop material upgrade aborted: the built-in Standard or URP/Lit shader is unavailable.");
            return targets.Count;
        }

        Undo.RecordObjects(targets.Select(t => (UnityEngine.Object)t.material).ToArray(), "Upgrade Workshop Materials to URP Lit");
        var upgrader = new StandardUpgrader(standard.name);
        var log = new StringBuilder();
        int failed = 0;
        foreach (var (path, material) in targets)
        {
            string from = material.shader.name;
            // Standard declares every property these materials saved, so the upgrader can read them; keywords that the
            // 2D shader marked invalid (_NORMALMAP, _ALPHATEST_ON, _EMISSION, ...) become valid again.
            material.shader = standard;
            float mode = material.GetFloat("_Mode");
            if (mode > 3f)
            {
                // Particle blend modes (additive, subtractive, modulate) have no Lit equivalent.
                material.shader = Shader.Find(from);
                log.AppendLine($"  SKIPPED {path}: {from} _Mode {mode} has no URP/Lit equivalent.");
                failed++;
                continue;
            }
            upgrader.Upgrade(material, MaterialUpgrader.UpgradeFlags.None);
            DropSpeckleNormalMap(material);
            // The keyword/render-queue pass the URP/Lit inspector runs on validation.
            BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(material);
            log.AppendLine($"  {path}: {from} -> {material.shader.name} (surface {material.GetFloat("_Surface")}, " +
                           $"alpha clip {material.GetFloat("_AlphaClip")}, queue {material.renderQueue})");
        }
        AssetDatabase.SaveAssets();
        string summary = $"Workshop material upgrade: {targets.Count - failed} converted to URP/Lit, {failed} skipped.";
        if (failed == 0) Debug.Log($"{summary}\n{log}");
        else Debug.LogError($"{summary}\n{log}");
        return failed;
    }

    // Saved as a normal map but not one: a 2048 grayscale field of random white specks. The pack's 2D shader never
    // read it; under URP/Lit each speck tilts the normal and glints in lamp light, riddling lit surfaces with dots.
    const string SpeckleNormalMap = "Assets/Sci-Fi Styled Modular Pack/Textures/base-color-normal.png";

    /// <summary>
    /// Clears the Sci-Fi Styled Modular Pack's speckle "normal map" from a URP/Lit material (and its _NORMALMAP keyword).
    /// Returns whether it was there.
    /// </summary>
    public static bool DropSpeckleNormalMap(Material material)
    {
        if (!material || !material.HasProperty("_BumpMap")) return false;
        var bump = material.GetTexture("_BumpMap");
        if (!bump || AssetDatabase.GetAssetPath(bump) != SpeckleNormalMap) return false;
        material.SetTexture("_BumpMap", null);
        if (material.shader && material.shader.name == "Universal Render Pipeline/Lit")
            BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
        EditorUtility.SetDirty(material);
        return true;
    }

    static bool NeedsUpgrade(Material material) => material.shader != null && UpgradeSources.Contains(material.shader.name);

    static IEnumerable<(string path, Material material)> PackMaterials()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Material", PackRoots))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) yield return (path, material);
        }
    }

    static Dictionary<string, List<string>> BuildSceneUsage()
    {
        var usage = new Dictionary<string, List<string>>();
        foreach (var scene in EditorBuildSettings.scenes)
            foreach (var dependency in AssetDatabase.GetDependencies(scene.path, true))
                if (dependency.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                {
                    if (!usage.TryGetValue(dependency, out var scenes)) usage[dependency] = scenes = new List<string>();
                    scenes.Add(System.IO.Path.GetFileNameWithoutExtension(scene.path));
                }
        return usage;
    }
}
