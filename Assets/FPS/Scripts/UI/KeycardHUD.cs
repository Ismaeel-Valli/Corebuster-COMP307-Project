using System.Collections.Generic;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>
    /// A row of floppy disk icons just above the health bar, one per keycard the player is carrying. It starts
    /// empty (and takes no space); each card pops in as it is pocketed and drops out when it goes into the
    /// Master terminal. Slots itself into the HUD's bottom-left column at runtime, so no HUD prefab needs editing.
    /// </summary>
    public class KeycardHUD : MonoBehaviour
    {
        public KeycardInventory Inventory;
        public RectTransform Tray;
        [Tooltip("The notification feed stacked above the column; lifted while the tray takes up room.")]
        public RectTransform Notifications;
        public float IconSize = 30f;
        public float Spacing = 8f;
        public float PopDuration = .4f;
        public float SlideSharpness = 14f;

        class Icon
        {
            public KeycardData Card;
            public RectTransform Rect;
            public Image Glow;
            public float Born;
            public float Leaving = -1f;
        }

        readonly List<Icon> m_Icons = new List<Icon>();
        LayoutElement m_Layout;
        Vector2 m_NotificationsRest;
        float m_ColumnSpacing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Create();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Create();

        static void Create()
        {
            var inventory = FindAnyObjectByType<KeycardInventory>();
            if (!inventory || FindAnyObjectByType<KeycardHUD>()) return;

            var tray = new GameObject("Keycard Tray", typeof(RectTransform));
            var rect = (RectTransform)tray.transform;
            var hud = tray.AddComponent<KeycardHUD>();
            hud.Inventory = inventory;
            hud.Tray = rect;

            // Into the HUD's bottom-left column, directly above the health bar's row.
            Transform row = FindHealthRow();
            if (row)
            {
                rect.SetParent(row.parent, false);
                rect.SetSiblingIndex(row.GetSiblingIndex());
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                var notifications = FindAnyObjectByType<NotificationHUDManager>();
                if (notifications) hud.Notifications = notifications.NotificationPanel;
            }
            else
            {
                var canvas = new GameObject("Keycard HUD", typeof(RectTransform)).AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 5;
                var scaler = canvas.gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(960f, 540f);
                rect.SetParent(canvas.transform, false);
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.anchoredPosition = new Vector2(25f, 70f);
            }
            rect.pivot = Vector2.zero;
            rect.sizeDelta = new Vector2(250f, hud.IconSize);
            var layout = tray.AddComponent<LayoutElement>();
            layout.preferredHeight = layout.minHeight = hud.IconSize;
            layout.preferredWidth = 250f;
            layout.ignoreLayout = true;
        }

        // The health bar's ancestor that sits directly in a vertical layout column.
        static Transform FindHealthRow()
        {
            var healthBar = FindAnyObjectByType<PlayerHealthBar>();
            if (!healthBar || !healthBar.HealthFillImage) return null;
            for (Transform t = healthBar.HealthFillImage.transform; t && t.parent; t = t.parent)
                if (t.parent.GetComponent<VerticalLayoutGroup>()) return t;
            return null;
        }

        void Start()
        {
            m_Layout = GetComponent<LayoutElement>();
            if (Notifications) m_NotificationsRest = Notifications.anchoredPosition;
            var column = Tray ? Tray.parent.GetComponent<VerticalLayoutGroup>() : null;
            m_ColumnSpacing = column ? column.spacing : 0f;
            if (!Inventory) return;
            Inventory.OnAdded += Add;
            Inventory.OnRemoved += Remove;
            foreach (var card in Inventory.Cards) Add(card);
            Refresh();
        }

        void OnDestroy()
        {
            if (!Inventory) return;
            Inventory.OnAdded -= Add;
            Inventory.OnRemoved -= Remove;
        }

        void Add(KeycardData card)
        {
            if (m_Icons.Exists(i => i.Card == card && i.Leaving < 0f)) return;

            var go = new GameObject(card.DisplayName, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(Tray, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = Vector2.one * IconSize;
            rect.anchoredPosition = new Vector2(Slot(m_Icons.Count), 0f);

            var glowObject = new GameObject("Glow", typeof(RectTransform));
            var glowRect = (RectTransform)glowObject.transform;
            glowRect.SetParent(rect, false);
            glowRect.anchorMin = Vector2.zero;
            glowRect.anchorMax = Vector2.one;
            glowRect.sizeDelta = Vector2.one * 10f;
            var glow = glowObject.AddComponent<Image>();
            glow.color = new Color(card.Tint.r, card.Tint.g, card.Tint.b, .35f);
            glow.raycastTarget = false;

            var imageObject = new GameObject("Disk", typeof(RectTransform));
            var imageRect = (RectTransform)imageObject.transform;
            imageRect.SetParent(rect, false);
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.sizeDelta = Vector2.zero;
            var image = imageObject.AddComponent<Image>();
            image.sprite = card.Icon;
            image.preserveAspect = true;
            image.raycastTarget = false;
            if (!card.Icon) image.color = card.Tint;
            var outline = imageObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, .7f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            m_Icons.Add(new Icon { Card = card, Rect = rect, Glow = glow, Born = Time.unscaledTime });
            Refresh();
        }

        void Remove(KeycardData card)
        {
            var icon = m_Icons.Find(i => i.Card == card && i.Leaving < 0f);
            if (icon != null) icon.Leaving = Time.unscaledTime;
        }

        float Slot(int index) => IconSize * .5f + index * (IconSize + Spacing);

        void Refresh()
        {
            // Takes no room in the column until the first card arrives.
            bool empty = m_Icons.Count == 0;
            if (m_Layout && m_Layout.ignoreLayout != empty) m_Layout.ignoreLayout = empty;
        }

        void Update()
        {
            float now = Time.unscaledTime;
            int slot = 0;
            for (int i = 0; i < m_Icons.Count; i++)
            {
                var icon = m_Icons[i];
                if (icon.Leaving >= 0f)
                {
                    float k = (now - icon.Leaving) / (PopDuration * .75f);
                    if (k >= 1f)
                    {
                        Destroy(icon.Rect.gameObject);
                        m_Icons.RemoveAt(i--);
                        continue;
                    }
                    icon.Rect.localScale = Vector3.one * (1f - k * k);
                    icon.Rect.anchoredPosition += Vector2.up * (Time.unscaledDeltaTime * 60f);
                    continue;
                }

                // Pops in with an overshoot and a bright flash, then glows softly.
                float age = (now - icon.Born) / PopDuration;
                float scale = age < 1f ? BackOut(Mathf.Clamp01(age)) : 1f;
                icon.Rect.localScale = Vector3.one * scale;
                Color tint = icon.Card.Tint;
                float flash = Mathf.Clamp01(1f - (now - icon.Born) / 1.2f);
                float breathe = .25f + .1f * Mathf.Sin(now * 2.2f + i);
                icon.Glow.color = new Color(tint.r, tint.g, tint.b, Mathf.Max(breathe, flash * .9f));

                Vector2 target = new Vector2(Slot(slot++), 0f);
                icon.Rect.anchoredPosition = Vector2.Lerp(icon.Rect.anchoredPosition, target,
                    1f - Mathf.Exp(-SlideSharpness * Time.unscaledDeltaTime));
            }
            Refresh();

            // The column grows by a row while the tray shows; keep the notification feed clear of it.
            if (Notifications)
            {
                float lift = m_Layout && !m_Layout.ignoreLayout ? IconSize + m_ColumnSpacing : 0f;
                Notifications.anchoredPosition = m_NotificationsRest + Vector2.up * lift;
            }
        }

        static float BackOut(float t)
        {
            const float s = 2.2f;
            t -= 1f;
            return t * t * ((s + 1f) * t + s) + 1f;
        }
    }
}
