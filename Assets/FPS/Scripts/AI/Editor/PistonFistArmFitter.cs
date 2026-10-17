using System;
using System.Collections.Generic;
using System.Linq;
using Unity.FPS.AI;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Seats every Robot1-style piston assembly on its forearm. The housing is centred on the elbow-to-hand
/// line with the punch axis (+Z) pointing at the hand, so the fist rests over the wrist. Where the
/// forearm armour would hide the housing on every side (the Elite's broad gauntlets), the assembly rides
/// on top of the armour instead. The fit is computed from the bones and skinned mesh alone, so it is
/// idempotent and safe to run after any builder.
/// </summary>
public static class PistonFistArmFitter
{
    const string PivotPrefix = "Robot1 Piston Fist Arm";
    const string HousingName = "Piston forearm housing";
    const float HousingAlongForearm = .5f;
    const float ArmourOverlap = .015f;

    static readonly string[] k_Prefabs =
    {
        "Assets/FPS/Prefabs/Enemies/Enemy_Robot1_PistonFist.prefab",
        "Assets/FPS/Prefabs/Enemies/Enemy_CyberSoldier_PistonFist.prefab",
        "Assets/FPS/Prefabs/Enemies/Enemy_Elite_PistonFist.prefab",
    };

    [MenuItem("Tools/CoreBuster/Fit Piston Fist Arms")]
    public static void FitAll()
    {
        foreach (string path in k_Prefabs)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int fitted = Fit(contents);
                if (fitted > 0) PrefabUtility.SaveAsPrefabAsset(contents, path);
                Debug.Log($"Piston fist arms fitted: {fitted} in {path}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
    }

    /// <summary>Fits every piston arm under <paramref name="root"/> and returns how many were moved.</summary>
    public static int Fit(GameObject root)
    {
        var piston = root.GetComponent<PistonFist>();
        if (!piston) return 0;

        int fitted = 0;
        foreach (Transform pivot in root.GetComponentsInChildren<Transform>(true)
                     .Where(t => t.name.StartsWith(PivotPrefix)).ToList())
        {
            Transform elbow = pivot.parent;
            Transform hand = elbow ? elbow.Cast<Transform>().FirstOrDefault(
                c => c != pivot && c.name.ToLowerInvariant().Contains("hand")) : null;
            Transform housing = pivot.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == HousingName);
            if (!hand || !housing)
            {
                Debug.LogWarning($"{root.name}: {pivot.name} has no forearm hand bone or housing; left unchanged.");
                continue;
            }

            Vector3 forearm = hand.position - elbow.position;
            float length = forearm.magnitude;
            if (length < .001f) continue;
            Vector3 along = forearm / length;
            Vector3 up = Vector3.ProjectOnPlane(root.transform.up, along);
            if (up.sqrMagnitude < .01f) up = Vector3.ProjectOnPlane(root.transform.forward, along);
            up.Normalize();
            pivot.rotation = Quaternion.LookRotation(along, up);

            // The housing is a unit cube primitive, so its world size is its lossy scale.
            float housingHalfWidth = Mathf.Abs(housing.lossyScale.x) * .5f;
            float housingHalfHeight = Mathf.Abs(housing.lossyScale.y) * .5f;
            Vector2 armour = ArmourExtent(root, elbow, length, along, up);
            bool hidden = armour.x > housingHalfWidth && armour.y > housingHalfHeight;
            float lift = hidden ? armour.y + housingHalfHeight - ArmourOverlap : 0f;
            Vector3 target = elbow.position + along * (length * HousingAlongForearm) + up * lift;
            pivot.position += target - housing.position;
            fitted++;
        }

        // The runtime bone alignment assumes the builder's original layout and would undo this fit.
        if (fitted > 0) piston.AlignArmToBoneDirection = false;
        return fitted;
    }

    /// <summary>
    /// How far the skinned forearm surface reaches from the elbow-to-hand axis to the side (x) and along
    /// <paramref name="up"/> (y), over the middle of the forearm where the housing sits, measured on
    /// vertices weighted mainly to the forearm bone.
    /// </summary>
    static Vector2 ArmourExtent(GameObject root, Transform elbow, float length, Vector3 along, Vector3 up)
    {
        Vector3 side = Vector3.Cross(along, up);
        var extent = Vector2.zero;
        foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            int bone = Array.IndexOf(skin.bones, elbow);
            if (bone < 0 || !skin.sharedMesh) continue;
            BoneWeight[] weights = skin.sharedMesh.boneWeights;
            var baked = new Mesh();
            try
            {
                // Baked with the renderer's scale divided out, so TransformPoint returns world positions even
                // on imported rigs with a 0.01 renderer scale.
                skin.BakeMesh(baked, true);
                Vector3[] vertices = baked.vertices;
                for (int i = 0; i < vertices.Length && i < weights.Length; i++)
                {
                    if (weights[i].boneIndex0 != bone || weights[i].weight0 < .5f) continue;
                    Vector3 offset = skin.transform.TransformPoint(vertices[i]) - elbow.position;
                    float t = Vector3.Dot(offset, along) / length;
                    if (t < .3f || t > .7f) continue;
                    Vector3 radial = Vector3.ProjectOnPlane(offset, along);
                    extent.x = Mathf.Max(extent.x, Mathf.Abs(Vector3.Dot(radial, side)));
                    extent.y = Mathf.Max(extent.y, Vector3.Dot(radial, up));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(baked);
            }
        }
        return extent;
    }
}
