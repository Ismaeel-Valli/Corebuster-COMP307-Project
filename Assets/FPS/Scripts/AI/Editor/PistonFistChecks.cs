using System;
using System.Reflection;
using Unity.FPS.AI;
using Unity.FPS.Game;
using UnityEditor;
using UnityEngine;

public static class PistonFistChecks
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [MenuItem("Tools/CoreBuster/Validate Piston Fist Combat")]
    public static void Run()
    {
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PistonFistBuilder.PrefabPath));
        var target = new GameObject("Piston test target");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            root.transform.position = new Vector3(10000, 10000, 10000);
            var fist = root.GetComponent<PistonFist>();
            Invoke(fist, "Awake");
            var health = target.AddComponent<Health>();
            health.MaxHealth = health.CurrentHealth = 100f;
            // The player is affiliation 1; the piston-fist enemy is affiliation 0.
            target.AddComponent<Actor>().Affiliation = 1;
            target.AddComponent<BoxCollider>().size = Vector3.one * .4f;
            target.AddComponent<BoxCollider>().size = Vector3.one * .3f;
            Vector3 origin = root.transform.position;
            Vector3 from = origin + new Vector3(0, 1.2f, .45f);
            Vector3 to = origin + new Vector3(0, 1.2f, 3f);
            target.transform.position = origin + new Vector3(0, 1.2f, 2f);
            wall.transform.position = origin + Vector3.right * 20;
            Physics.SyncTransforms();
            foreach (PistonFist.AttackPhase phase in Enum.GetValues(typeof(PistonFist.AttackPhase)))
            {
                if (phase == PistonFist.AttackPhase.Punch) continue;
                Reset(fist, phase);
                Invoke(fist, "SweepDamage", from, to);
                Assert(health.CurrentHealth == 100f, "Non-punch phase damaged player: " + phase);
            }
            Reset(fist, PistonFist.AttackPhase.Punch);
            Invoke(fist, "SweepDamage", from, to);
            Invoke(fist, "SweepDamage", from, to);
            Assert(health.CurrentHealth == 75f, "Swept punch must hit exactly once with multiple colliders.");
            Reset(fist, PistonFist.AttackPhase.Punch);
            Invoke(fist, "SweepDamage", from, to);
            Assert(health.CurrentHealth == 50f, "Subsequent attack must hit again.");
            health.CurrentHealth = 100f;
            target.transform.position += Vector3.right * 2f;
            Physics.SyncTransforms();
            Reset(fist, PistonFist.AttackPhase.Punch);
            Invoke(fist, "SweepDamage", from, to);
            Assert(health.CurrentHealth == 100f, "Sidestepping must evade punch.");
            target.transform.position = origin + new Vector3(0, 1.2f, 5f);
            Physics.SyncTransforms();
            Reset(fist, PistonFist.AttackPhase.Punch);
            Invoke(fist, "SweepDamage", from, to);
            Assert(health.CurrentHealth == 100f, "Retreating must evade punch.");
            target.transform.position = origin + new Vector3(0, 1.2f, 2f);
            wall.transform.position = origin + new Vector3(0, 1.2f, 1.3f);
            wall.transform.localScale = new Vector3(3, 3, .15f);
            Physics.SyncTransforms();
            Reset(fist, PistonFist.AttackPhase.Punch);
            Invoke(fist, "SweepDamage", from, to);
            Assert(health.CurrentHealth == 100f, "Wall must block damage.");
            wall.SetActive(false);
            Physics.SyncTransforms();
            Reset(fist, PistonFist.AttackPhase.Punch);
            fist.SetFrozen(true);
            Invoke(fist, "SweepDamage", from, to);
            Assert(health.CurrentHealth == 100f, "Frozen fist must not damage.");
            fist.SetFrozen(false);
            Assert(fist.Phase == PistonFist.AttackPhase.Cooldown, "Thaw must not resume active punch.");
            Reset(fist, PistonFist.AttackPhase.Punch);
            Invoke(fist, "OnDeath");
            Invoke(fist, "SweepDamage", from, to);
            Assert(health.CurrentHealth == 100f, "Dead fist must not damage.");
            Debug.Log("PISTON CHECKS PASSED: harmless non-punch phases, swept hit, one hit per attack, next attack, sidestep, retreat, wall, freeze/thaw, death.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(wall);
        }
    }
    static void Reset(PistonFist fist, PistonFist.AttackPhase phase)
    {
        Invoke(fist, "Enter", phase);
        typeof(PistonFist).GetField("m_DidHit", Private).SetValue(fist, false);
    }
    static void Invoke(PistonFist fist, string method, params object[] args) => typeof(PistonFist).GetMethod(method, Private).Invoke(fist, args);
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
