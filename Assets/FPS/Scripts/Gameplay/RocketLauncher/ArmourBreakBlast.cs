using System.Collections.Generic;
using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// The rocket's blast, on its explosion effect: the moment it spawns, the armoured enemies within Radius that the
    /// blast can reach (not behind a wall), nearest first and at most MaxTargets of them, have their armour torn off.
    /// It deals no damage: the enemy is left whole, its core exposed, for the main gun or a core execution to finish.
    /// Things without armour, such as the Boss Hall's giant core, are untouched.
    /// </summary>
    public class ArmourBreakBlast : MonoBehaviour
    {
        [Min(.5f)] public float Radius = 5f;
        [Tooltip("Most enemies one blast strips; the nearest go first.")]
        [Min(1)] public int MaxTargets = 3;
        [Tooltip("What the blast can be stopped by when reaching an enemy.")]
        public LayerMask Occluders = ~0;

        [Header("Shake")]
        [Tooltip("The player's view shakes when this close to the blast, fading out with distance.")]
        [Min(0f)] public float ShakeRadius = 16f;
        [Min(0f)] public float ShakeStrength = .35f;

        static readonly Collider[] s_Hits = new Collider[64];
        static readonly RaycastHit[] s_Blockers = new RaycastHit[16];
        static readonly List<IArmourBreakable> s_Broken = new List<IArmourBreakable>();
        static readonly List<(IArmourBreakable target, float distance)> s_Reached = new List<(IArmourBreakable, float)>();

        public int ArmourBroken { get; private set; }

        void Start()
        {
            Vector3 origin = transform.position;
            int count = Physics.OverlapSphereNonAlloc(origin, Radius, s_Hits, ~0, QueryTriggerInteraction.Collide);
            s_Broken.Clear();
            s_Reached.Clear();
            for (int i = 0; i < count; i++)
            {
                var target = s_Hits[i].GetComponentInParent<IArmourBreakable>();
                if (target == null || target.IsArmourBroken || s_Broken.Contains(target)) continue;
                if (!Reaches(origin, s_Hits[i], target)) continue;
                s_Broken.Add(target);
                s_Reached.Add((target, Vector3.Distance(origin, s_Hits[i].bounds.center)));
            }
            s_Reached.Sort((a, b) => a.distance.CompareTo(b.distance));
            ArmourBroken = Mathf.Min(MaxTargets, s_Reached.Count);
            for (int i = 0; i < ArmourBroken; i++) s_Reached[i].target.BreakArmour();
            s_Broken.Clear();
            s_Reached.Clear();
            BlastShake.Shake(origin, ShakeRadius, ShakeStrength);
        }

        // Clear if nothing but the target itself stands between the blast and the collider's middle.
        bool Reaches(Vector3 origin, Collider collider, IArmourBreakable target)
        {
            Vector3 to = collider.bounds.center;
            Vector3 offset = to - origin;
            float distance = offset.magnitude;
            if (distance < .05f) return true;
            int count = Physics.RaycastNonAlloc(origin, offset / distance, s_Blockers, distance, Occluders, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = s_Blockers[i].collider;
                if (hit.GetComponentInParent<IArmourBreakable>() == target) continue;
                if (hit.GetComponentInParent<Health>() || hit.attachedRigidbody) continue;
                return false;
            }
            return true;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, .5f, .1f, .35f);
            Gizmos.DrawWireSphere(transform.position, Radius);
        }
    }
}
