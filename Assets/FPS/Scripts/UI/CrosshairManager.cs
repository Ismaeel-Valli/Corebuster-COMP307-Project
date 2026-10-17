using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    public class CrosshairManager : MonoBehaviour
    {
        public Image CrosshairImage;
        public Sprite NullCrosshairSprite;
        public float CrosshairUpdateshrpness = 5f;

        [Header("Theme (overrides each weapon's crosshair look)")]
        [Tooltip("When set, every weapon uses this reticle; the weapon still decides the size")]
        public Sprite ThemeSprite;
        public Color ThemeColor = new Color(1f, .85f, .55f, .85f);
        [Tooltip("Colour while the crosshair is over an enemy")]
        public Color ThemeTargetColor = new Color(1f, .35f, .2f, 1f);
        [Min(.1f)] public float ThemeSizeMultiplier = .8f;
        [Tooltip("Size used when a weapon defines no crosshair")]
        [Min(1f)] public float ThemeFallbackSize = 44f;

        PlayerWeaponsManager m_WeaponsManager;
        bool m_WasPointingAtEnemy;
        RectTransform m_CrosshairRectTransform;
        CrosshairData m_CrosshairDataDefault;
        CrosshairData m_CrosshairDataTarget;
        CrosshairData m_CurrentCrosshair;

        void Start()
        {
            m_WeaponsManager = FindAnyObjectByType<PlayerWeaponsManager>();
            DebugUtility.HandleErrorIfNullFindObject<PlayerWeaponsManager, CrosshairManager>(m_WeaponsManager, this);

            OnWeaponChanged(m_WeaponsManager.GetActiveWeapon());

            m_WeaponsManager.OnSwitchedToWeapon += OnWeaponChanged;
        }

        void Update()
        {
            UpdateCrosshairPointingAtEnemy(false);
            m_WasPointingAtEnemy = m_WeaponsManager.IsPointingAtEnemy;
        }

        void UpdateCrosshairPointingAtEnemy(bool force)
        {
            if (m_CrosshairDataDefault.CrosshairSprite == null)
                return;

            if ((force || !m_WasPointingAtEnemy) && m_WeaponsManager.IsPointingAtEnemy)
            {
                m_CurrentCrosshair = m_CrosshairDataTarget;
                CrosshairImage.sprite = m_CurrentCrosshair.CrosshairSprite;
                m_CrosshairRectTransform.sizeDelta = m_CurrentCrosshair.CrosshairSize * Vector2.one;
            }
            else if ((force || m_WasPointingAtEnemy) && !m_WeaponsManager.IsPointingAtEnemy)
            {
                m_CurrentCrosshair = m_CrosshairDataDefault;
                CrosshairImage.sprite = m_CurrentCrosshair.CrosshairSprite;
                m_CrosshairRectTransform.sizeDelta = m_CurrentCrosshair.CrosshairSize * Vector2.one;
            }

            CrosshairImage.color = Color.Lerp(CrosshairImage.color, m_CurrentCrosshair.CrosshairColor,
                Time.deltaTime * CrosshairUpdateshrpness);

            m_CrosshairRectTransform.sizeDelta = Mathf.Lerp(m_CrosshairRectTransform.sizeDelta.x,
                m_CurrentCrosshair.CrosshairSize,
                Time.deltaTime * CrosshairUpdateshrpness) * Vector2.one;
        }

        CrosshairData Themed(CrosshairData data, Color color)
        {
            if (!ThemeSprite) return data;
            float size = data.CrosshairSize > 0f ? data.CrosshairSize : ThemeFallbackSize;
            data.CrosshairSprite = ThemeSprite;
            data.CrosshairSize = Mathf.RoundToInt(size * ThemeSizeMultiplier);
            data.CrosshairColor = color;
            return data;
        }

        void OnWeaponChanged(WeaponController newWeapon)
        {
            if (newWeapon)
            {
                CrosshairImage.enabled = true;
                m_CrosshairDataDefault = Themed(newWeapon.CrosshairDataDefault, ThemeColor);
                m_CrosshairDataTarget = Themed(newWeapon.CrosshairDataTargetInSight, ThemeTargetColor);
                m_CrosshairRectTransform = CrosshairImage.GetComponent<RectTransform>();
                DebugUtility.HandleErrorIfNullGetComponent<RectTransform, CrosshairManager>(m_CrosshairRectTransform,
                    this, CrosshairImage.gameObject);
            }
            else
            {
                if (NullCrosshairSprite)
                {
                    CrosshairImage.sprite = NullCrosshairSprite;
                }
                else
                {
                    CrosshairImage.enabled = false;
                }
            }

            UpdateCrosshairPointingAtEnemy(true);
        }
    }
}