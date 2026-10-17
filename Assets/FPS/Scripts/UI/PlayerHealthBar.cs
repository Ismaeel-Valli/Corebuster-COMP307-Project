using TMPro;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    public class PlayerHealthBar : MonoBehaviour
    {
        [Tooltip("Image component dispplaying current health")]
        public Image HealthFillImage;

        [Tooltip("Optional readout of the current health")]
        public TMP_Text HealthText;

        [Tooltip("Fill colour at full health")]
        public Color HealthyColor = Color.white;

        [Tooltip("Fill colour as health runs out; it pulses below the critical ratio")]
        public Color CriticalColor = new Color(1f, .3f, .22f);

        Health m_PlayerHealth;

        void Start()
        {
            PlayerCharacterController playerCharacterController =
                FindAnyObjectByType<PlayerCharacterController>();
            DebugUtility.HandleErrorIfNullFindObject<PlayerCharacterController, PlayerHealthBar>(
                playerCharacterController, this);

            m_PlayerHealth = playerCharacterController.GetComponent<Health>();
            DebugUtility.HandleErrorIfNullGetComponent<Health, PlayerHealthBar>(m_PlayerHealth, this,
                playerCharacterController.gameObject);
        }

        void Update()
        {
            // update health bar value
            float ratio = m_PlayerHealth.CurrentHealth / m_PlayerHealth.MaxHealth;
            HealthFillImage.fillAmount = ratio;

            // Shift toward the critical colour as health drops, pulsing once it is critical.
            float danger = 1f - Mathf.Clamp01(ratio / Mathf.Max(.01f, m_PlayerHealth.CriticalHealthRatio * 2f));
            if (m_PlayerHealth.IsCritical()) danger = .75f + .25f * Mathf.Sin(Time.time * 8f);
            HealthFillImage.color = Color.Lerp(HealthyColor, CriticalColor, danger);

            if (HealthText) HealthText.text = Mathf.CeilToInt(m_PlayerHealth.CurrentHealth).ToString();
        }
    }
}
