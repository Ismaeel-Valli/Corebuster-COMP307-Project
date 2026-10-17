using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// One blue laser bolt of the Disassembler's spread. It deals no damage: any enemy it strikes, and any other
    /// enemy within SplashRadius of the strike, is electrocuted (frozen with its core exposed) for ShockDuration.
    /// Flies from the camera so it lands where the crosshair points, but is drawn leaving the gun's muzzle
    /// (the offset closes over CorrectionDistance).
    /// </summary>
    public class DisassemblerBolt : MonoBehaviour
    {
        public float Speed = 120f;
        public float MaxDistance = 45f;
        [Tooltip("Thickness of the bolt for hit tests.")]
        public float Radius = .12f;
        public float ShockDuration = 3f;
        [Tooltip("Other enemies this close to the strike are caught by the discharge too.")]
        public float SplashRadius = 1.3f;
        public LayerMask HitMask = ~0;

        [Header("Look")]
        public LineRenderer Beam;
        [Tooltip("Length of the visible beam.")]
        public float BeamLength = 2.2f;
        [Tooltip("Distance over which the bolt's drawn path joins its real path from the muzzle.")]
        public float CorrectionDistance = 5f;
        public Light BoltLight;
        [Tooltip("Spawned where the bolt strikes.")]
        public GameObject ImpactVfx;
        public float ImpactVfxLifetime = 1.5f;
        public AudioClip ImpactSfx;

        static readonly RaycastHit[] s_Hits = new RaycastHit[16];
        static readonly Collider[] s_Splash = new Collider[32];
        // A spread lands many bolts in the same instant; one impact sound per moment is plenty.
        static float s_LastImpactSfx = -1f;

        GameObject m_Owner;
        Vector3 m_Origin;
        Vector3 m_Position;
        Vector3 m_Direction;
        Vector3 m_VisualOffset;
        float m_Travelled;
        float m_FadeTime = -1f;
        float m_LightIntensity;
        const float k_FadeDuration = .07f;

        /// <summary>Fires the bolt from origin (the eye) along direction, drawn leaving the muzzle.</summary>
        public void Launch(GameObject owner, Vector3 origin, Vector3 direction, Vector3 muzzle)
        {
            m_Owner = owner;
            m_Origin = m_Position = origin;
            m_Direction = direction.normalized;
            m_VisualOffset = muzzle - origin;
            if (BoltLight) m_LightIntensity = BoltLight.intensity;
            transform.SetPositionAndRotation(muzzle, Quaternion.LookRotation(m_Direction));
            if (Beam)
            {
                Beam.useWorldSpace = true;
                Beam.positionCount = 2;
            }
            UpdateVisuals(0f);
        }

        void Update()
        {
            float deltaTime = Time.deltaTime;
            if (m_FadeTime >= 0f)
            {
                m_FadeTime += deltaTime;
                float fade = 1f - m_FadeTime / k_FadeDuration;
                UpdateVisuals(Mathf.Clamp01(1f - fade));
                if (fade <= 0f) Destroy(gameObject);
                return;
            }

            float step = Mathf.Min(Speed * deltaTime, MaxDistance - m_Travelled);
            if (TryHit(step, out RaycastHit hit))
            {
                m_Position = hit.point;
                m_Travelled += hit.distance;
                Strike(hit);
                m_FadeTime = 0f;
                UpdateVisuals(0f);
                return;
            }

            m_Position += m_Direction * step;
            m_Travelled += step;
            UpdateVisuals(0f);
            if (m_Travelled >= MaxDistance) m_FadeTime = 0f;
        }

        bool TryHit(float distance, out RaycastHit best)
        {
            best = default;
            if (distance <= 0f) return false;
            int count = Physics.SphereCastNonAlloc(m_Position, Radius, m_Direction, s_Hits, distance, HitMask,
                QueryTriggerInteraction.Ignore);
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = s_Hits[i];
                if (m_Owner && hit.collider.transform.IsChildOf(m_Owner.transform)) continue;
                if (hit.collider.GetComponentInParent<IgnoreHitDetection>()) continue;
                // Starting inside something reports distance 0 and no point: take it at the bolt's position.
                if (hit.distance <= 0f) hit.point = m_Position;
                if (hit.distance >= bestDistance) continue;
                bestDistance = hit.distance;
                best = hit;
            }
            return !float.IsPositiveInfinity(bestDistance);
        }

        void Strike(RaycastHit hit)
        {
            var target = Electrocutable.Find(hit.collider);
            if (target) target.Electrocute(ShockDuration, hit.point, m_Owner);

            // The discharge leaps to anyone standing close by.
            if (SplashRadius > 0f)
            {
                int count = Physics.OverlapSphereNonAlloc(hit.point, SplashRadius, s_Splash, HitMask,
                    QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    if (m_Owner && s_Splash[i].transform.IsChildOf(m_Owner.transform)) continue;
                    var nearby = Electrocutable.Find(s_Splash[i]);
                    if (nearby && nearby != target && !nearby.IsElectrocuted)
                        nearby.Electrocute(ShockDuration, s_Splash[i].ClosestPoint(hit.point), m_Owner);
                }
            }

            if (ImpactVfx)
            {
                Vector3 normal = hit.normal.sqrMagnitude > .01f ? hit.normal : -m_Direction;
                var impact = Instantiate(ImpactVfx, hit.point + normal * .02f, Quaternion.LookRotation(normal));
                Destroy(impact, ImpactVfxLifetime);
            }
            if (ImpactSfx && Mathf.Abs(Time.time - s_LastImpactSfx) > .04f)
            {
                s_LastImpactSfx = Time.time;
                AudioUtility.CreateSFX(ImpactSfx, hit.point, AudioUtility.AudioGroups.Impact, 1f, 2f);
            }
        }

        // fade 0..1 shrinks the beam's tail into its head once it has struck.
        void UpdateVisuals(float fade)
        {
            // Both ends follow the drawn path, so the beam never trails back past the muzzle toward the eye.
            float tailDistance = Mathf.Lerp(Mathf.Max(0f, m_Travelled - BeamLength), m_Travelled, fade);
            Vector3 head = m_Position + m_VisualOffset * Correction(m_Travelled);
            Vector3 tail = m_Origin + m_Direction * tailDistance + m_VisualOffset * Correction(tailDistance);
            transform.position = head;
            if (Beam)
            {
                Beam.SetPosition(0, tail);
                Beam.SetPosition(1, head);
            }
            if (BoltLight) BoltLight.intensity = m_LightIntensity * (1f - fade);
        }

        // How much of the muzzle offset is still applied at this distance along the real path (1 at the start).
        float Correction(float distance) =>
            CorrectionDistance > 0f ? Mathf.Clamp01(1f - distance / CorrectionDistance) : 0f;
    }
}
