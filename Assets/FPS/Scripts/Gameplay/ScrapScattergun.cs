using System;
using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Visual controller for the Scrap Scattergun. WeaponController handles the
    /// charge, pellet fan and projectile damage; this script communicates the
    /// cocking and recoil so the attack is readable to the player.
    /// </summary>
    [RequireComponent(typeof(WeaponController))]
    public class ScrapScattergun : MonoBehaviour
    {
        public Transform BarrelAssembly;
        public Transform WeaponRoot;
        public float CockOpenAngle = 18f;
        public float CockBackDistance = .08f;
        public float RecoilDistance = .28f;
        [Min(.01f)] public float RecoilDuration = .18f;
        public Color CockingColor = new Color(1f, .35f, .05f);

        WeaponController m_Weapon;
        Vector3 m_BarrelRestPosition;
        Quaternion m_BarrelRestRotation;
        Vector3 m_WeaponRestPosition;
        float m_RecoilTimer;
        Renderer[] m_CockingRenderers;
        MaterialPropertyBlock m_Properties;

        void Awake()
        {
            m_Weapon = GetComponent<WeaponController>();
            m_BarrelRestPosition = BarrelAssembly ? BarrelAssembly.localPosition : Vector3.zero;
            m_BarrelRestRotation = BarrelAssembly ? BarrelAssembly.localRotation : Quaternion.identity;
            m_WeaponRestPosition = WeaponRoot ? WeaponRoot.localPosition : Vector3.zero;
            m_CockingRenderers = GetComponentsInChildren<Renderer>(true);
            m_Properties = new MaterialPropertyBlock();
            m_Weapon.OnShootProcessed += OnShot;
        }

        void OnDestroy()
        {
            if (m_Weapon) m_Weapon.OnShootProcessed -= OnShot;
        }

        void Update()
        {
            float charge = m_Weapon && m_Weapon.IsCharging ? m_Weapon.CurrentCharge : 0f;
            ApplyCockingPose(charge);

            if (m_RecoilTimer > 0f)
                m_RecoilTimer = Mathf.Max(0f, m_RecoilTimer - Time.deltaTime);

            float recoil = m_RecoilTimer > 0f
                ? Mathf.Sin((m_RecoilTimer / Mathf.Max(.01f, RecoilDuration)) * Mathf.PI) 
                : 0f;
            if (WeaponRoot)
                WeaponRoot.localPosition = m_WeaponRestPosition + Vector3.back * (RecoilDistance * recoil);
        }

        void ApplyCockingPose(float charge)
        {
            if (BarrelAssembly)
            {
                float open = Mathf.SmoothStep(0f, 1f, charge);
                BarrelAssembly.localPosition = m_BarrelRestPosition + Vector3.back * (CockBackDistance * open);
                BarrelAssembly.localRotation = m_BarrelRestRotation * Quaternion.Euler(-CockOpenAngle * open, 0f, 0f);
            }

            Color lampColor = charge > 0f ? Color.Lerp(Color.black, CockingColor, charge) : Color.black;
            foreach (var renderer in m_CockingRenderers)
            {
                if (!renderer || !renderer.sharedMaterial || !renderer.sharedMaterial.IsKeywordEnabled("_EMISSION"))
                    continue;
                renderer.GetPropertyBlock(m_Properties);
                m_Properties.SetColor("_EmissionColor", lampColor * (charge > 0f ? 2f : 0f));
                renderer.SetPropertyBlock(m_Properties);
            }
        }

        void OnShot() => m_RecoilTimer = RecoilDuration;
    }
}
