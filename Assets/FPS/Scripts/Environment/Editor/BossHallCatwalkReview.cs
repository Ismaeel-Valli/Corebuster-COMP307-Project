using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Read-only proposal and saved captures for env_051; review objects never become saved scene content.</summary>
public static class BossHallCatwalkReview
{
    public static void PreviewBatch()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Preview needs graphics.");
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); var plan = BossHallCatwalk.Plan(layout);
        BossHallCatwalk.PlanReport(layout, plan);
        string sha = BossHallSurvey.Hash(BossHallSurvey.ScenePath);
        var states = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), null);
        var materials = BossHallRenderingReview.PreserveMaterials();
        var previewObjects = new List<GameObject>(); var meshes = new List<Mesh>();
        try
        {
            foreach (var p in plan)
            {
                var go = new GameObject("Unsaved proposal " + p.name) { hideFlags = HideFlags.HideAndDontSave };
                previewObjects.Add(go); go.transform.SetPositionAndRotation(p.position, p.rotation);
                var mesh = BossHallCatwalk.Compose(p.variant); meshes.Add(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = BossHallRendering.GetFinish("Gunmetal");
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
            }
            Capture(layout, plan, previewObjects.Select(g => g.GetComponent<MeshRenderer>()).ToArray(), "Proposed");
        }
        finally
        {
            foreach (var go in previewObjects) Object.DestroyImmediate(go);
            foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
            BossHallRenderingReview.RestoreMaterials(materials);
        }
        BossHallSnapshot.Compare(states, BossHallSnapshot.Capture(SceneManager.GetActiveScene(), null), "Proposal preview");
        BossHallFramework.Validate(layout);
        if (SceneManager.GetActiveScene().isDirty || BossHallSurvey.Hash(BossHallSurvey.ScenePath) != sha) throw new InvalidOperationException("Proposal preview changed MainScene.");
        File.WriteAllText("Docs/BossHall/CatwalkPreview_051.txt",
            "PASS: six unsaved proposal meshes rendered at measured first-tier positions, then destroyed.\n" +
            "Full scene state, shared material state and scene bytes unchanged: " + sha + "\n" +
            "Captures: " + BossHallCatwalk.Output + "/Proposed. Neutral review lamps are unsaved and not final lighting.\n" +
            "No dressing asset or saved object created; route/gameplay decisions and saved-scene validation remain pending.\n");
    }

    public static void CaptureBatch()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Capture needs graphics.");
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); BossHallCatwalk.Validate(layout);
        var states = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), null); string sha = BossHallSurvey.Hash(BossHallSurvey.ScenePath);
        var materials = BossHallRenderingReview.PreserveMaterials();
        try { Capture(layout, BossHallCatwalk.Plan(layout), BossHallCatwalk.Renderers(), "Saved"); }
        finally { BossHallRenderingReview.RestoreMaterials(materials); }
        BossHallSnapshot.Compare(states, BossHallSnapshot.Capture(SceneManager.GetActiveScene(), null), "Saved maintenance capture");
        if (SceneManager.GetActiveScene().isDirty || BossHallSurvey.Hash(BossHallSurvey.ScenePath) != sha) throw new InvalidOperationException("Capture changed MainScene.");
        File.WriteAllText("Docs/BossHall/CatwalkCapture_051.txt",
            "PASS saved maintenance captures; full scene/material state and scene bytes unchanged.\n" +
            "Scene SHA-256: " + sha + "\nCaptures: " + BossHallCatwalk.Output + "/Saved\n" +
            "Neutral review lamps/camera destroyed; saved lighting unchanged.\n" +
            File.ReadAllText(BossHallCatwalk.Output + "/Saved/Neighbours.txt"));
    }

    static void Capture(BossHallFramework.Layout layout, List<BossHallCatwalk.Placement> plan, MeshRenderer[] renderers, string stage)
    {
        string directory = BossHallCatwalk.Output + "/" + stage; Directory.CreateDirectory(directory);
        var camera = BossHallRenderingReview.CameraForReview(); var lamps = new List<GameObject>();
        var eye = BossHallRenderingReview.EyeHeight(); var r = layout.Report; var focus = (r.turret.head.min + r.turret.head.max) * .5f;
        var views = new List<(string name, Vector3 position, Vector3 look)> { ("Entry", r.entryEye, focus) };
        foreach (string side in new[] { "South", "North" })
        {
            var p = plan.Single(x => x.name == "Maintenance_" + side + "_2");
            views.Add((side + "_Gallery", new Vector3(p.position.x + 6, r.tier1TopY + eye, p.position.z) + p.rotation * Vector3.forward * 4, p.bounds.center));
        }
        foreach (var p in plan) views.Add((p.name + "_Detail", p.bounds.center + p.rotation * new Vector3(1, -.5f, 4), p.bounds.center));
        try
        {
            foreach (var v in views) File.WriteAllBytes(directory + "/" + v.name + "_Lighting.png", BossHallRenderingReview.Shoot(camera, v.position, v.look));
            // Use one local white review lamp per rack, below the rack at a safe viewing distance; never persisted.
            foreach (var p in plan)
            {
                var go = new GameObject("Unsaved maintenance review lamp") { hideFlags = HideFlags.HideAndDontSave }; lamps.Add(go);
                go.transform.position = p.bounds.center + p.rotation * new Vector3(0, -.5f, 3);
                var light = go.AddComponent<Light>(); light.type = LightType.Point; light.range = 7; light.intensity = 8; light.color = Color.white; light.shadows = LightShadows.None;
            }
            foreach (var v in views) File.WriteAllBytes(directory + "/" + v.name + "_Neutral.png", BossHallRenderingReview.Shoot(camera, v.position, v.look));
            foreach (var go in lamps) Object.DestroyImmediate(go); lamps.Clear();
            // Compare before/after at identical cameras with no extra lights or shadows.
            var lines = new List<string>();
            foreach (var v in BossHallRenderingReview.Neighbours(layout))
            {
                foreach (var renderer in renderers) renderer.enabled = false;
                var before = BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3);
                foreach (var renderer in renderers) renderer.enabled = true;
                var after = BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3);
                File.WriteAllBytes(directory + "/" + v.Item1 + "_Before.png", before); File.WriteAllBytes(directory + "/" + v.Item1 + "_After.png", after);
                var a = new Texture2D(2, 2); var b = new Texture2D(2, 2);
                try
                {
                    a.LoadImage(before); b.LoadImage(after); var pa = a.GetPixels32(); var pb = b.GetPixels32(); int changed = 0, outside = 0;
                    for (int i = 0; i < pa.Length; i++)
                    {
                        if (Mathf.Max(Mathf.Abs(pa[i].r - pb[i].r), Mathf.Abs(pa[i].g - pb[i].g), Mathf.Abs(pa[i].b - pb[i].b)) <= 4) continue;
                        changed++;
                        var ray = camera.ViewportPointToRay(new Vector3((i % a.width + .5f) / a.width, (i / a.width + .5f) / a.height));
                        if (!layout.interior.IntersectRay(ray, out float distance) || distance < 0) outside++;
                    }
                    bool forward = v.Item1 == "Threshold_Forward" || v.Item1 == "Incline_Reveal" || v.Item1 == "Landing_Forward";
                    lines.Add(v.Item1 + ": " + changed + " changed pixels; " + outside + " outside hall projection; " + (forward ? "forward view" : "strict neighbour/backward view"));
                    if (!forward && outside != 0) throw new InvalidOperationException("Neighbour preview regression: " + lines.Last());
                }
                finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
            }
            File.WriteAllText(directory + "/Neighbours.txt", string.Join("\n", lines) + "\n"); Debug.Log(string.Join("\n", lines));
        }
        finally
        {
            foreach (var renderer in renderers) if (renderer) renderer.enabled = true;
            foreach (var go in lamps) Object.DestroyImmediate(go);
            BossHallRenderingReview.DestroyCamera(camera);
        }
    }
}
