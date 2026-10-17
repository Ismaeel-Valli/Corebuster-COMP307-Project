using System.Collections.Generic;
using UnityEngine;

namespace Unity.FPS.Game
{
    /// <summary>
    /// The look and sound of an enemy being electrocuted: jagged lightning arcing from its limbs to the ground
    /// (sparks spraying where each bolt lands), arcs crawling between its limbs, sparks spitting off the whole
    /// body mesh, a strobing blue light and a crackling buzz. Play() binds it to a body; Stop() lets it fade
    /// and then destroys it.
    /// </summary>
    public class ElectrocutionVfx : MonoBehaviour
    {
        [Tooltip("Line renderers reused for the arcs (CoreBuster/Electric Arc material, Stretch texture mode).")]
        public LineRenderer[] Arcs;
        [Tooltip("Sparks emitted from the body's mesh.")]
        public ParticleSystem BodySparks;
        [Tooltip("Sparks sprayed where an arc hits the ground (emitted at the strike point).")]
        public ParticleSystem GroundSparks;
        public Light Glow;
        public float GlowIntensity = 6f;
        public AudioSource CrackleLoop;
        public AudioClip ZapSfx;
        [Range(0f, 1f)] public float ZapVolume = 1f;

        [Header("Arcs")]
        [Tooltip("Seconds each arc holds its shape before it re-strikes (min, max).")]
        public Vector2 StrikeInterval = new Vector2(.04f, .11f);
        [Tooltip("Chance that a strike goes to the ground rather than jumping between two limbs.")]
        [Range(0f, 1f)] public float GroundChance = .65f;
        [Min(2)] public int ArcSegments = 12;
        [Tooltip("How far the arc zig-zags off the straight line, as a fraction of its length.")]
        public float Jaggedness = .16f;
        public Vector2 ArcWidth = new Vector2(.018f, .05f);
        [Tooltip("Furthest below a limb the ground can be for an arc to reach it.")]
        public float MaxGroundDistance = 3.5f;
        [Min(.05f)] public float FadeOutDuration = .35f;

        public bool IsPlaying { get; private set; }

        readonly List<Transform> m_Anchors = new List<Transform>();
        readonly RaycastHit[] m_Hits = new RaycastHit[8];
        Transform m_Body;
        Bounds m_FallbackBounds;
        Renderer m_BodyRenderer;
        float[] m_NextStrike;
        Vector3[] m_Points;
        float m_Fade;
        float m_Seed;

        void Awake()
        {
            m_Seed = Random.Range(0f, 100f);
            m_Points = new Vector3[Mathf.Max(2, ArcSegments + 1)];
            m_NextStrike = new float[Arcs != null ? Arcs.Length : 0];
            foreach (var arc in Arcs)
            {
                arc.useWorldSpace = true;
                arc.positionCount = 0;
            }
            if (Glow) Glow.intensity = 0f;
        }

        /// <summary>Starts electrocuting the body: arcs jump from the anchors (limbs), sparks fly off bodyRenderer.</summary>
        public void Play(Transform body, IList<Transform> anchors, Renderer bodyRenderer)
        {
            m_Body = body;
            m_BodyRenderer = bodyRenderer;
            m_Anchors.Clear();
            if (anchors != null)
                foreach (var anchor in anchors)
                    if (anchor) m_Anchors.Add(anchor);
            IsPlaying = true;
            m_Fade = 1f;

            if (BodySparks)
            {
                var shape = BodySparks.shape;
                if (bodyRenderer is SkinnedMeshRenderer skinned)
                {
                    shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
                    shape.skinnedMeshRenderer = skinned;
                }
                else if (bodyRenderer is MeshRenderer meshRenderer)
                {
                    shape.shapeType = ParticleSystemShapeType.MeshRenderer;
                    shape.meshRenderer = meshRenderer;
                }
                BodySparks.Play(true);
            }
            if (GroundSparks) GroundSparks.Play(true);
            if (CrackleLoop)
            {
                CrackleLoop.volume = 1f;
                if (CrackleLoop.clip) CrackleLoop.Play();
                if (ZapSfx) CrackleLoop.PlayOneShot(ZapSfx, ZapVolume);
            }
            for (int i = 0; i < m_NextStrike.Length; i++) m_NextStrike[i] = 0f;
        }

        /// <summary>Fades the arcs, light and sound out, then destroys the effect.</summary>
        public void Stop()
        {
            if (!IsPlaying) return;
            IsPlaying = false;
            if (BodySparks) BodySparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            // Leave the effect where it is if the body is about to be destroyed.
            transform.SetParent(null, true);
            Destroy(gameObject, FadeOutDuration + 1f);
        }

        void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            if (!IsPlaying) m_Fade = Mathf.MoveTowards(m_Fade, 0f, deltaTime / FadeOutDuration);
            if (IsPlaying && !m_Body)
            {
                Stop();
                return;
            }

            for (int i = 0; i < Arcs.Length; i++)
            {
                LineRenderer arc = Arcs[i];
                if (m_Fade <= 0f || m_Anchors.Count == 0)
                {
                    arc.positionCount = 0;
                    continue;
                }
                if (Time.time >= m_NextStrike[i])
                {
                    m_NextStrike[i] = Time.time + Random.Range(StrikeInterval.x, StrikeInterval.y) / Mathf.Max(.3f, m_Fade);
                    Strike(arc, i);
                }
                else if (Random.value < deltaTime * 20f)
                {
                    // Held arcs still twitch.
                    Jitter(arc);
                }
                Color color = arc.startColor;
                color.a = m_Fade * (Random.value < .12f ? .3f : 1f);
                arc.startColor = arc.endColor = color;
            }

            if (Glow)
            {
                float strobe = .45f + .9f * Mathf.PerlinNoise(Time.time * 40f, m_Seed);
                Glow.intensity = GlowIntensity * m_Fade * strobe;
                if (m_BodyRenderer) Glow.transform.position = m_BodyRenderer.bounds.center;
            }
            if (CrackleLoop && !IsPlaying) CrackleLoop.volume = m_Fade;
        }

        void Strike(LineRenderer arc, int index)
        {
            Vector3 start = AnchorPoint();
            Vector3 end;
            bool toGround = Random.value < GroundChance || m_Anchors.Count < 2;
            if (toGround && GroundBelow(start, out Vector3 ground))
            {
                // Strike somewhere around the feet rather than straight down, so the arcs splay outward.
                Vector2 splay = Random.insideUnitCircle * .6f;
                Vector3 target = ground + new Vector3(splay.x, 0f, splay.y);
                end = GroundBelow(target + Vector3.up * .5f, out Vector3 splayed) ? splayed : ground;
                if (GroundSparks)
                {
                    var emit = new ParticleSystem.EmitParams { position = end, applyShapeToPosition = false };
                    GroundSparks.Emit(emit, Random.Range(4, 9));
                }
            }
            else
            {
                end = AnchorPoint();
                if ((end - start).sqrMagnitude < .01f) end = start + Random.onUnitSphere * .35f;
            }

            BuildBolt(start, end);
            arc.positionCount = m_Points.Length;
            arc.SetPositions(m_Points);
            float width = Random.Range(ArcWidth.x, ArcWidth.y) * (toGround ? 1f : .7f);
            arc.widthMultiplier = width;
        }

        void Jitter(LineRenderer arc)
        {
            int count = arc.positionCount;
            if (count < 3) return;
            for (int i = 1; i < count - 1; i++)
                arc.SetPosition(i, arc.GetPosition(i) + Random.insideUnitSphere * .025f);
        }

        // Midpoint-displaced lightning: big kinks in the middle, small ones near the ends.
        void BuildBolt(Vector3 start, Vector3 end)
        {
            int last = m_Points.Length - 1;
            Vector3 along = end - start;
            float length = along.magnitude;
            Vector3 side = Vector3.Cross(along, Random.onUnitSphere).normalized;
            Vector3 side2 = Vector3.Cross(along, side).normalized;
            float bow = Random.Range(-.25f, .25f) * length;
            for (int i = 0; i <= last; i++)
            {
                float t = i / (float)last;
                float envelope = Mathf.Sin(t * Mathf.PI);
                Vector3 kink = (side * Random.Range(-1f, 1f) + side2 * Random.Range(-1f, 1f)) * (Jaggedness * length);
                m_Points[i] = start + along * t + side * (bow * envelope) + kink * (i == 0 || i == last ? 0f : envelope);
            }
        }

        Vector3 AnchorPoint()
        {
            if (m_Anchors.Count > 0)
            {
                Transform anchor = m_Anchors[Random.Range(0, m_Anchors.Count)];
                if (anchor) return anchor.position + Random.insideUnitSphere * .06f;
            }
            Bounds bounds = m_BodyRenderer ? m_BodyRenderer.bounds : new Bounds(transform.position, Vector3.one);
            return bounds.center + Vector3.Scale(Random.insideUnitSphere, bounds.extents);
        }

        bool GroundBelow(Vector3 from, out Vector3 point)
        {
            point = from;
            int count = Physics.RaycastNonAlloc(from, Vector3.down, m_Hits, MaxGroundDistance, ~0, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                // Skip the electrocuted body itself and any other actor standing in the way.
                if (m_Body && m_Hits[i].collider.transform.IsChildOf(m_Body)) continue;
                if (m_Hits[i].collider.GetComponentInParent<Actor>()) continue;
                if (m_Hits[i].distance < best)
                {
                    best = m_Hits[i].distance;
                    point = m_Hits[i].point + Vector3.up * .01f;
                }
            }
            return !float.IsPositiveInfinity(best);
        }
    }
}
