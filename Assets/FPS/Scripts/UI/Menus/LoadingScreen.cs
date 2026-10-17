using System.Collections;
using TMPro;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>
    /// A full-screen loading screen for heavy scenes (the ship). It survives the scene change, loads the scene
    /// asynchronously behind a progress bar, then stays up through scene activation and the first frames until
    /// every <see cref="LoadingGate"/> holder is ready (or <see cref="MaxHoldSeconds"/> passes), and fades out.
    /// It builds itself, borrowing the menu's fonts, so no scene needs rebuilding to use it.
    /// </summary>
    public class LoadingScreen : MonoBehaviour
    {
        const float FadeDuration = .45f;
        const int WarmupFrames = 8;
        const float MaxHoldSeconds = 20f;

        static readonly Color k_Gold = new Color(1f, .78f, .38f);
        static readonly Color k_Parchment = new Color(.93f, .86f, .72f);

        static readonly string[] k_Status =
        {
            "RESTORING SHIP POWER",
            "SEALING BULKHEADS",
            "WAKING DORMANT SYSTEMS",
            "CALIBRATING CORE CONTAINMENT",
            "SPINNING UP THE REACTOR",
        };

        public static bool IsLoading { get; private set; }

        CanvasGroup m_Group;
        RectTransform m_Fill;
        RectTransform m_Spinner;
        TextMeshProUGUI m_Status;
        TextMeshProUGUI m_Percent;
        float m_Shown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => IsLoading = false;

        /// <summary>Shows the loading screen and loads the scene behind it.</summary>
        public static void Load(string sceneName)
        {
            if (IsLoading) return;
            IsLoading = true;
            FindFonts(out var title, out var titleMaterial, out var body);
            var root = new GameObject("Loading Screen", typeof(RectTransform));
            DontDestroyOnLoad(root);
            var screen = root.AddComponent<LoadingScreen>();
            screen.Build((RectTransform)root.transform, title, titleMaterial, body);
            screen.StartCoroutine(screen.Run(sceneName));
        }

        IEnumerator Run(string sceneName)
        {
            yield return Fade(0f, 1f);
            // A frame with the screen fully up before the load starts hogging the main thread.
            yield return null;

            var operation = SceneManager.LoadSceneAsync(sceneName);
            operation.allowSceneActivation = false;
            while (operation.progress < .9f)
            {
                SetProgress(Mathf.Lerp(.05f, .75f, operation.progress / .9f));
                yield return null;
            }
            SetProgress(.8f, "BOARDING THE SHIP");
            yield return null;
            operation.allowSceneActivation = true;
            while (!operation.isDone) yield return null;

            // The scene is live: let it render a few frames (shader warm-up) and let its systems finish preparing.
            float start = Time.realtimeSinceStartup;
            for (int i = 0; i < WarmupFrames; i++)
            {
                SetProgress(Mathf.Lerp(.85f, .95f, i / (float)WarmupFrames), "SYSTEMS ONLINE");
                yield return null;
            }
            while (LoadingGate.IsBusy && Time.realtimeSinceStartup - start < MaxHoldSeconds)
            {
                SetProgress(.97f, "PREPARING THE SHIP");
                yield return null;
            }
            SetProgress(1f, "READY");
            yield return null;
            yield return Fade(1f, 0f);
            IsLoading = false;
            Destroy(gameObject);
        }

        void Update()
        {
            // Unscaled, so it keeps turning while a scene sets the time scale.
            if (m_Spinner) m_Spinner.Rotate(0f, 0f, -220f * Mathf.Min(Time.unscaledDeltaTime, .05f));
            if (m_Fill)
            {
                var size = m_Fill.anchorMax;
                size.x = Mathf.MoveTowards(size.x, m_Shown, Time.unscaledDeltaTime * 1.5f);
                m_Fill.anchorMax = size;
            }
        }

        void SetProgress(float value, string status = null)
        {
            m_Shown = Mathf.Max(m_Shown, value);
            if (m_Percent) m_Percent.text = Mathf.RoundToInt(m_Shown * 100f) + "%";
            if (!m_Status) return;
            m_Status.text = status ?? k_Status[Mathf.Min(k_Status.Length - 1, (int)(m_Shown * k_Status.Length))];
        }

        IEnumerator Fade(float from, float to)
        {
            for (float t = 0f; t < FadeDuration; t += Time.unscaledDeltaTime)
            {
                m_Group.alpha = Mathf.Lerp(from, to, t / FadeDuration);
                yield return null;
            }
            m_Group.alpha = to;
        }

        // ------------------------------------------------------------------ layout

        void Build(RectTransform root, TMP_FontAsset title, Material titleMaterial, TMP_FontAsset body)
        {
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Over everything, including the game's HUD and the tutorial's eyelids.
            canvas.sortingOrder = 1000;
            var scaler = root.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;
            m_Group = root.gameObject.AddComponent<CanvasGroup>();
            m_Group.alpha = 0f;
            // Swallows clicks so nothing behind it reacts while loading.
            m_Group.blocksRaycasts = true;
            root.gameObject.AddComponent<GraphicRaycaster>();

            var back = Box("Background", root, new Color(.012f, .014f, .02f, 1f));
            Stretch(back.rectTransform);

            var heading = Text("Title", root, title, 110f, Color.white);
            if (titleMaterial) heading.fontSharedMaterial = titleMaterial;
            heading.text = "COREBUSTER";
            heading.characterSpacing = 10f;
            Place(heading.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, 150f), new Vector2(1200f, 140f));

            // A spinning broken ring, so a frozen frame never reads as a hang for long.
            m_Spinner = Ring(root, new Vector2(0f, 10f), 64f);

            m_Status = Text("Status", root, body, 26f, k_Parchment);
            m_Status.characterSpacing = 6f;
            Place(m_Status.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -78f), new Vector2(1200f, 40f));

            var track = Box("Track", root, new Color(1f, 1f, 1f, .12f));
            Place(track.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -122f), new Vector2(720f, 6f));
            var fill = Box("Fill", track.rectTransform, k_Gold);
            m_Fill = fill.rectTransform;
            m_Fill.anchorMin = Vector2.zero;
            m_Fill.anchorMax = new Vector2(0f, 1f);
            m_Fill.offsetMin = m_Fill.offsetMax = Vector2.zero;

            m_Percent = Text("Percent", root, body, 20f, new Color(k_Parchment.r, k_Parchment.g, k_Parchment.b, .7f));
            Place(m_Percent.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -150f), new Vector2(400f, 30f));

            var hint = Text("Hint", root, body, 22f, new Color(k_Parchment.r, k_Parchment.g, k_Parchment.b, .55f));
            hint.text = "The ship is in lockdown. Follow the coloured lamps to the room your keycard opens.";
            Place(hint.rectTransform, new Vector2(.5f, 0f), new Vector2(0f, 90f), new Vector2(1400f, 40f));

            SetProgress(0f);
        }

        RectTransform Ring(RectTransform parent, Vector2 position, float radius)
        {
            var ring = new GameObject("Spinner", typeof(RectTransform)).GetComponent<RectTransform>();
            ring.SetParent(parent, false);
            Place(ring, new Vector2(.5f, .5f), position, new Vector2(radius * 2f, radius * 2f));
            const int segments = 14;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * 360f / segments;
                var dash = Box("Dash", ring, new Color(k_Gold.r, k_Gold.g, k_Gold.b, .15f + .85f * i / (segments - 1f)));
                var rect = dash.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                rect.sizeDelta = new Vector2(6f, 18f);
                rect.localRotation = Quaternion.Euler(0f, 0f, -angle);
                rect.anchoredPosition = Quaternion.Euler(0f, 0f, -angle) * new Vector3(0f, radius, 0f);
            }
            return ring;
        }

        static Image Box(string name, Transform parent, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            image.raycastTarget = true;
            return image;
        }

        static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font, float size, Color color)
        {
            var text = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(parent, false);
            if (font) text.font = font;
            else if (TMP_Settings.defaultFontAsset) text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        // The menus use Bebas Neue (gold material) for titles and Roboto Slab for text; borrow them from the
        // scene being left so the loading screen matches without asset references of its own.
        static void FindFonts(out TMP_FontAsset title, out Material titleMaterial, out TMP_FontAsset body)
        {
            title = null;
            titleMaterial = null;
            body = null;
            foreach (var text in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!text.font) continue;
                string name = text.font.name;
                if (!title && name.Contains("Bebas"))
                {
                    title = text.font;
                    if (text.fontSharedMaterial && text.fontSharedMaterial.name.Contains("Gold"))
                        titleMaterial = text.fontSharedMaterial;
                }
                else if (!titleMaterial && name.Contains("Bebas") && text.fontSharedMaterial &&
                         text.fontSharedMaterial.name.Contains("Gold"))
                    titleMaterial = text.fontSharedMaterial;
                else if (!body && name.Contains("Roboto")) body = text.font;
            }
        }
    }
}
