using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>
    /// Holds the menu's core logo still under a steady halo. The halo shader can pulse and spin its rays;
    /// the menus keep it fixed so the logo neither bounces nor flashes.
    /// </summary>
    public class CoreLogoGlow : MonoBehaviour
    {
        [Tooltip("Image using the CoreBuster/UI Core Glow material")]
        public Graphic Glow;
        [Tooltip("The logo image; held at its authored position")]
        public RectTransform Logo;

        [Min(0f)] public float BaseIntensity = 1.1f;

        static readonly int k_Intensity = Shader.PropertyToID("_Intensity");
        static readonly int k_RaySpin = Shader.PropertyToID("_RaySpin");

        Material m_Material;

        void Awake()
        {
            if (Glow && Glow.material)
            {
                // Per-screen copy so setting it never touches the material asset.
                m_Material = new Material(Glow.material);
                m_Material.SetFloat(k_Intensity, BaseIntensity);
                m_Material.SetFloat(k_RaySpin, 0f);
                Glow.material = m_Material;
            }
        }

        void OnDestroy()
        {
            if (m_Material) Destroy(m_Material);
        }
    }
}
