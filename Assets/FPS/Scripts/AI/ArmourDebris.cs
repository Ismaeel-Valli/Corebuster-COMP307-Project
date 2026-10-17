using UnityEngine;

namespace Unity.FPS.AI
{
    /// <summary>A chunk of shed armour: lies around for a while, shrinks away, then frees its mesh.</summary>
    public class ArmourDebris : MonoBehaviour
    {
        public float Lifetime = 5f;
        [Min(.01f)] public float ShrinkDuration = .6f;

        float m_Age;
        Vector3 m_StartScale;

        void Start()
        {
            m_StartScale = transform.localScale;
        }

        void Update()
        {
            m_Age += Time.deltaTime;
            float shrink = Mathf.InverseLerp(Lifetime - ShrinkDuration, Lifetime, m_Age);
            transform.localScale = m_StartScale * (1f - shrink);
            if (m_Age >= Lifetime) Destroy(gameObject);
        }

        void OnDestroy()
        {
            var filter = GetComponent<MeshFilter>();
            if (filter && filter.sharedMesh) Destroy(filter.sharedMesh);
        }
    }
}
