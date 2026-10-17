using System.Collections;
using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    public enum ArmourStyle { Steel, Ceramic, Lead }

    /// <summary>
    /// One plate, tile or panel of the giant core's armour. Shots reach it through its own Damageable and Health
    /// (whose health is effectively endless; GiantCore keeps each layer's real total), and it shows its damage:
    /// steel buckles off its seat, ceramic cracks (CoreBuster/Core Armour's _Damage) and lead dents. When it comes
    /// away it leaves the way its layer does: steel plates fall, ceramic shatters into shards, and lead peels back
    /// on its top edge before it drops.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class ArmourPiece : MonoBehaviour
    {
        public ArmourStyle Style;
        [Tooltip("Midpoint of the edge a lead panel peels back about, in the piece's local space.")]
        public Vector3 HingePoint;
        [Tooltip("Along that edge, in the piece's local space. The peel direction is worked out at runtime.")]
        public Vector3 HingeAxis = Vector3.right;
        [Tooltip("Away from the core's centre, in the piece's local space.")]
        public Vector3 Outward = Vector3.forward;

        [Header("Coming Away")]
        [Min(.01f)] public float Mass = 40f;
        [Min(0f)] public float Lifetime = 6f;

        /// <summary>Damage this piece has soaked; the most-stressed pieces of a layer come away first.</summary>
        public float Stress { get; set; }
        public bool Detached { get; private set; }
        // Resolved on first use: GiantCore subscribes in its own Awake, which can run before this piece's.
        public Health Health => m_Health ? m_Health : m_Health = GetComponent<Health>();

        static readonly int k_Damage = Shader.PropertyToID("_Damage");
        static readonly int k_Seed = Shader.PropertyToID("_Seed");

        Health m_Health;
        Renderer m_Renderer;
        MaterialPropertyBlock m_Block;
        Vector3 m_RestPosition;
        Quaternion m_RestRotation;
        Vector3 m_BuckleAxis;
        float m_Seed;
        float m_Blast = 1f;

        void Awake()
        {
            m_Renderer = GetComponent<Renderer>();
            m_Block = new MaterialPropertyBlock();
            m_RestPosition = transform.localPosition;
            m_RestRotation = transform.localRotation;
            // A stable per-piece seed, so the same plate always buckles and cracks the same way.
            m_Seed = Mathf.Repeat(transform.localPosition.x * 12.9898f + transform.localPosition.y * 78.233f +
                                   transform.localPosition.z * 37.719f, 97f);
            Vector3 side = Vector3.Cross(Outward, new Vector3(Mathf.Sin(m_Seed), Mathf.Cos(m_Seed * 1.7f), .3f));
            m_BuckleAxis = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.up;
            SetDamage(0f);
        }

        /// <summary>0..1: how damaged this piece looks.</summary>
        public void SetDamage(float damage)
        {
            if (Detached) return;
            damage = Mathf.Clamp01(damage);
            if (m_Renderer)
            {
                m_Renderer.GetPropertyBlock(m_Block);
                m_Block.SetFloat(k_Damage, damage);
                m_Block.SetFloat(k_Seed, m_Seed);
                m_Renderer.SetPropertyBlock(m_Block);
            }
            if (Style == ArmourStyle.Steel)
            {
                // The plate springs off its seat and twists as its fixings give way.
                transform.localRotation = m_RestRotation * Quaternion.AngleAxis(9f * damage, m_BuckleAxis);
                transform.localPosition = m_RestPosition + m_RestRotation * Outward.normalized * (.12f * damage);
            }
        }

        /// <summary>
        /// Leaves the core, in this piece's style. Ceramic needs the shard meshes and material. A blast above 1 throws
        /// the piece off that many times harder (the reactor's overcharge), and lead is torn away rather than peeled.
        /// </summary>
        public void Detach(Mesh[] shardMeshes, Material shardMaterial, float blast = 1f)
        {
            if (Detached) return;
            SetDamage(1f);
            Detached = true;
            m_Blast = Mathf.Max(1f, blast);
            switch (Style)
            {
                case ArmourStyle.Ceramic:
                    Shatter(shardMeshes, shardMaterial);
                    break;
                case ArmourStyle.Lead when m_Blast < 1.5f:
                    StartCoroutine(PeelAndDrop());
                    break;
                case ArmourStyle.Lead:
                    Drop(1.5f * m_Blast);
                    break;
                default:
                    Drop(2.5f * m_Blast);
                    break;
            }
        }

        void Drop(float outwardSpeed)
        {
            transform.SetParent(null, true);
            foreach (var mesh in GetComponents<MeshCollider>()) mesh.convex = true;
            var body = gameObject.AddComponent<Rigidbody>();
            body.mass = Mass;
            body.linearVelocity = transform.TransformDirection(Outward).normalized * outwardSpeed + Vector3.up * .5f;
            body.angularVelocity = Random.onUnitSphere * (1.5f * m_Blast);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            Destroy(gameObject, Lifetime);
        }

        void Shatter(Mesh[] shardMeshes, Material shardMaterial)
        {
            if (shardMeshes != null && shardMeshes.Length > 0 && m_Renderer)
            {
                Bounds bounds = m_Renderer.bounds;
                Vector3 outward = transform.TransformDirection(Outward).normalized;
                var block = new MaterialPropertyBlock();
                block.SetFloat(k_Damage, 1f);
                block.SetFloat(k_Seed, m_Seed);
                for (int i = 0; i < 6; i++)
                {
                    var shard = new GameObject("Ceramic Shard");
                    shard.transform.position = bounds.center + Vector3.Scale(Random.insideUnitSphere, bounds.extents * .8f);
                    shard.transform.rotation = Random.rotation;
                    shard.transform.localScale = Vector3.one * Random.Range(.22f, .42f);
                    shard.AddComponent<MeshFilter>().sharedMesh = shardMeshes[Random.Range(0, shardMeshes.Length)];
                    var renderer = shard.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = shardMaterial;
                    renderer.SetPropertyBlock(block);
                    shard.AddComponent<BoxCollider>();
                    var body = shard.AddComponent<Rigidbody>();
                    body.mass = 2f;
                    body.linearVelocity = (outward * Random.Range(2.5f, 5f) + Random.insideUnitSphere * 1.5f) * m_Blast;
                    body.angularVelocity = Random.onUnitSphere * 8f;
                    Destroy(shard, Random.Range(3.5f, 5f));
                }
            }
            Destroy(gameObject);
        }

        IEnumerator PeelAndDrop()
        {
            Vector3 hinge = transform.TransformPoint(HingePoint);
            Vector3 axis = transform.TransformDirection(HingeAxis).normalized;
            Vector3 outward = transform.TransformDirection(Outward).normalized;
            // Peel the free edge outward: pick the rotation sense that moves the panel's middle away from the core.
            Vector3 centre = m_Renderer ? m_Renderer.bounds.center : transform.position;
            Vector3 swung = Quaternion.AngleAxis(5f, axis) * (centre - hinge) + hinge;
            if (Vector3.Dot(swung - centre, outward) < 0f) axis = -axis;

            const float peelAngle = 105f;
            const float duration = .75f;
            float turned = 0f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                float target = peelAngle * k * k;
                transform.RotateAround(hinge, axis, target - turned);
                turned = target;
                yield return null;
            }
            Drop(1.5f);
        }
    }
}
