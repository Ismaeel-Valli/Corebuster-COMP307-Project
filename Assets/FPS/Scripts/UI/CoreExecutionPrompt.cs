using TMPro;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>
    /// "[E] CORE BUST" prompt under the crosshair while an exposed core is close enough to execute.
    /// Builds its own overlay canvas in any scene with a CoreExecutioner, so no HUD prefab needs editing.
    /// </summary>
    public class CoreExecutionPrompt : MonoBehaviour
    {
        public CoreExecutioner Executioner;
        public TextMeshProUGUI Label;
        public TextMeshProUGUI Detail;
        public CanvasGroup Group;
        [Min(.1f)] public float FadeSpeed = 10f;
        public Color KeyColor = new Color(.5f, .9f, 1f);

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
            var executioner = FindAnyObjectByType<CoreExecutioner>();
            if (!executioner || FindAnyObjectByType<CoreExecutionPrompt>()) return;

            var root = new GameObject("Core Execution Prompt", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;

            var prompt = root.AddComponent<CoreExecutionPrompt>();
            prompt.Executioner = executioner;
            prompt.Group = root.AddComponent<CanvasGroup>();
            prompt.Group.alpha = 0f;
            prompt.Group.interactable = false;
            prompt.Group.blocksRaycasts = false;
            var theme = HudTheme.Current;
            prompt.Label = CreateText(root.transform, "Label", 46f, -150f, FontStyles.Bold,
                theme ? theme.TitleFont : null, theme ? theme.TitleMaterial : null);
            prompt.Detail = CreateText(root.transform, "Detail", 22f, -196f, FontStyles.Normal,
                theme ? theme.BodyFont : null, null);
            prompt.Detail.color = new Color(.75f, .85f, 1f, .85f);
        }

        internal static TextMeshProUGUI CreateText(Transform parent, string name, float size, float y, FontStyles style,
            TMP_FontAsset font, Material material)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(900f, 60f);
            rect.anchoredPosition = new Vector2(0f, y);

            var text = go.AddComponent<TextMeshProUGUI>();
            if (font) text.font = font;
            else if (TMP_Settings.defaultFontAsset) text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.characterSpacing = 6f;
            text.raycastTarget = false;
            if (material)
            {
                // The themed material brings its own bevel and shadow.
                text.fontSharedMaterial = material;
                return text;
            }
            text.fontStyle = style;
            text.outlineWidth = .18f;
            text.outlineColor = new Color32(10, 12, 60, 255);
            return text;
        }

        void Update()
        {
            bool show = Executioner && Executioner.Candidate && !Executioner.IsExecuting;
            if (Group) Group.alpha = Mathf.MoveTowards(Group.alpha, show ? 1f : 0f, FadeSpeed * Time.deltaTime);
            if (!show) return;

            string key = ColorUtility.ToHtmlStringRGB(KeyColor);
            if (Label)
            {
                Label.text = $"<color=#{key}>[{Executioner.PromptKey}]</color>  CORE BUST";
                Label.transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.time * 7f) * .035f);
            }
            if (Detail) Detail.text = $"ABSORB CORE   +{Mathf.RoundToInt(Executioner.Candidate.HealthReward)} HP";
        }
    }
}
