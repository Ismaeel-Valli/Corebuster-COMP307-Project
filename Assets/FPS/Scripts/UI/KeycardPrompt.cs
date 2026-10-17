using TMPro;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>
    /// "[E] ACCESS TERMINAL" prompt under the crosshair for the keycard loop's terminals and access machines,
    /// in the same spot and style as the core execution prompt (the two never show at once).
    /// Builds its own overlay canvas in any scene with a KeycardInteractor.
    /// </summary>
    public class KeycardPrompt : MonoBehaviour
    {
        public KeycardInteractor Interactor;
        public TextMeshProUGUI Label;
        public TextMeshProUGUI Detail;
        public CanvasGroup Group;
        [Tooltip("Fills while Interact is held on a hold-to-use focus.")]
        public Image HoldBar;
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
            var interactor = FindAnyObjectByType<KeycardInteractor>();
            if (!interactor || FindAnyObjectByType<KeycardPrompt>()) return;

            var root = new GameObject("Keycard Prompt", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;

            var prompt = root.AddComponent<KeycardPrompt>();
            prompt.Interactor = interactor;
            prompt.Group = root.AddComponent<CanvasGroup>();
            prompt.Group.alpha = 0f;
            prompt.Group.interactable = false;
            prompt.Group.blocksRaycasts = false;
            var theme = HudTheme.Current;
            prompt.Label = CoreExecutionPrompt.CreateText(root.transform, "Label", 40f, -150f, FontStyles.Bold,
                theme ? theme.TitleFont : null, theme ? theme.TitleMaterial : null);
            prompt.Detail = CoreExecutionPrompt.CreateText(root.transform, "Detail", 22f, -194f, FontStyles.Normal,
                theme ? theme.BodyFont : null, null);
            prompt.Detail.color = new Color(.75f, .85f, 1f, .85f);
            prompt.HoldBar = CreateHoldBar(root.transform, prompt.KeyColor);
        }

        // A thin bar under the prompt, filled from the left: a dim track with a bright fill.
        static Image CreateHoldBar(Transform parent, Color color)
        {
            var track = new GameObject("Hold Track", typeof(RectTransform)).AddComponent<Image>();
            track.transform.SetParent(parent, false);
            var trackRect = track.rectTransform;
            trackRect.anchorMin = trackRect.anchorMax = new Vector2(.5f, .5f);
            trackRect.sizeDelta = new Vector2(320f, 6f);
            trackRect.anchoredPosition = new Vector2(0f, -226f);
            track.color = new Color(color.r, color.g, color.b, .18f);
            track.raycastTarget = false;

            var fill = new GameObject("Hold Fill", typeof(RectTransform)).AddComponent<Image>();
            fill.transform.SetParent(track.transform, false);
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            fill.color = color;
            fill.raycastTarget = false;
            return fill;
        }

        void Update()
        {
            var focus = Interactor ? Interactor.Focus : null;
            if (Group) Group.alpha = Mathf.MoveTowards(Group.alpha, focus ? 1f : 0f, FadeSpeed * Time.deltaTime);
            if (!focus) return;

            bool hold = focus.HoldDuration > 0f;
            string key = ColorUtility.ToHtmlStringRGB(KeyColor);
            if (Label)
            {
                // Interact's binding may already read "Hold E".
                bool named = Interactor.PromptKey.StartsWith("Hold", System.StringComparison.OrdinalIgnoreCase);
                Label.text = $"<color=#{key}>[{(hold && !named ? "HOLD " : "")}{Interactor.PromptKey}]</color>  {focus.PromptAction(Interactor)}";
            }
            if (Detail) Detail.text = focus.PromptDetail(Interactor);
            if (HoldBar)
            {
                HoldBar.transform.parent.gameObject.SetActive(hold);
                HoldBar.rectTransform.anchorMax = new Vector2(Interactor.HoldProgress, 1f);
            }
        }
    }
}
