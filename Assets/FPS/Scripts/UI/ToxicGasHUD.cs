using TMPro;
using Unity.FPS.AI;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>
    /// Screen cues for core gas: red smoke creeping in from the edges while the player stands in a cloud
    /// (flashing on each damage tick) and a "CORE CRITICAL" warning while they're inside a pending blast.
    /// Builds its own overlay canvas in any scene with a player, so no HUD prefab needs editing.
    /// </summary>
    public class ToxicGasHUD : MonoBehaviour
    {
        const string OverlayMaterialPath = "CoreBuster/M_ToxicGasOverlay";

        public RawImage Overlay;
        public TextMeshProUGUI GasLabel;
        public TextMeshProUGUI CriticalLabel;
        [Tooltip("How fast the smoke thickens and clears on screen.")]
        public float ExposureRiseSpeed = 4f;
        public float ExposureFallSpeed = 1.5f;
        [Tooltip("Extra distance beyond the blast radius at which the critical warning shows.")]
        public float CriticalWarningMargin = 1f;

        Transform m_Player;
        Material m_Material;
        float m_Exposure;

        static readonly int k_Exposure = Shader.PropertyToID("_Exposure");
        static readonly int k_Hit = Shader.PropertyToID("_Hit");

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
            var player = FindAnyObjectByType<PlayerCharacterController>();
            if (!player || FindAnyObjectByType<ToxicGasHUD>()) return;

            var root = new GameObject("Toxic Gas HUD", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;

            var hud = root.AddComponent<ToxicGasHUD>();
            hud.m_Player = player.transform;

            var overlayObject = new GameObject("Gas Overlay", typeof(RectTransform));
            overlayObject.transform.SetParent(root.transform, false);
            var rect = (RectTransform)overlayObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            hud.Overlay = overlayObject.AddComponent<RawImage>();
            hud.Overlay.raycastTarget = false;
            var source = Resources.Load<Material>(OverlayMaterialPath);
            if (source)
            {
                hud.m_Material = new Material(source);
                hud.Overlay.material = hud.m_Material;
            }
            else
            {
                Debug.LogWarning("ToxicGasHUD: missing Resources/" + OverlayMaterialPath + "; using a flat tint.");
                hud.Overlay.color = new Color(.6f, 0f, .05f, 0f);
            }
            hud.Overlay.enabled = false;

            hud.GasLabel = CreateText(root.transform, "Gas Label", 34f, 380f);
            hud.GasLabel.color = new Color(1f, .35f, .3f, 0f);
            hud.CriticalLabel = CreateText(root.transform, "Critical Label", 40f, 260f);
            hud.CriticalLabel.color = new Color(1f, .15f, .1f, 0f);
            hud.CriticalLabel.text = "CORE CRITICAL - GET CLEAR";
        }

        static TextMeshProUGUI CreateText(Transform parent, string name, float size, float y)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(1200f, 70f);
            rect.anchoredPosition = new Vector2(0f, y);

            var text = go.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset) text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.characterSpacing = 8f;
            text.raycastTarget = false;
            text.outlineWidth = .2f;
            text.outlineColor = new Color32(40, 0, 6, 255);
            return text;
        }

        void Update()
        {
            float target = CoreGasCloud.PlayerExposure;
            float speed = target > m_Exposure ? ExposureRiseSpeed : ExposureFallSpeed;
            m_Exposure = Mathf.MoveTowards(m_Exposure, target, speed * Time.deltaTime);
            float hit = Mathf.Clamp01(1f - (Time.time - CoreGasCloud.LastPlayerDamageTime) / .35f);

            bool visible = m_Exposure > .001f || hit > 0f;
            if (Overlay)
            {
                Overlay.enabled = visible;
                if (m_Material)
                {
                    m_Material.SetFloat(k_Exposure, m_Exposure);
                    m_Material.SetFloat(k_Hit, hit);
                }
                else
                {
                    Overlay.color = new Color(.6f, 0f, .05f, m_Exposure * .5f + hit * .3f);
                }
            }

            float blink = .65f + .35f * Mathf.Sin(Time.time * 9f);
            if (GasLabel)
            {
                GasLabel.text = "TOXIC CORE GAS";
                SetAlpha(GasLabel, Mathf.Clamp01(m_Exposure * 3f) * blink);
            }
            if (CriticalLabel) SetAlpha(CriticalLabel, CriticalWarning() * blink);
        }

        // 0..1: how urgent it is to get out of a corpse's blast radius (0 when outside every one).
        float CriticalWarning()
        {
            if (!m_Player) return 0f;
            float warning = 0f;
            foreach (var corpse in ExecutedCorpse.Pending)
            {
                if (!corpse) continue;
                Vector3 offset = m_Player.position - corpse.BlastCenter;
                offset.y = 0f;
                if (offset.magnitude > corpse.BlastRadius + CriticalWarningMargin) continue;
                warning = Mathf.Max(warning, .5f + .5f * corpse.Progress);
            }
            return warning;
        }

        static void SetAlpha(Graphic graphic, float alpha)
        {
            Color color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }

        void OnDestroy()
        {
            if (m_Material) Destroy(m_Material);
        }
    }
}
