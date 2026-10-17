using System.Collections.Generic;
using TMPro;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>
    /// The tutorial's screen furniture, read from the TutorialDirector: eyelids for the wake-up, the task card at the
    /// upper right (heading, a line under it, and key chips with tick boxes) and the ship systems subtitle along the
    /// bottom. Builds its own overlay canvas in any scene with an active TutorialDirector.
    /// </summary>
    public class TutorialHUD : MonoBehaviour
    {
        public TutorialDirector Director;
        public Color Accent = new Color(.5f, .9f, 1f);
        public Color DoneColor = new Color(.35f, 1f, .5f);
        [Tooltip("Characters per second the subtitles type out at.")]
        public float TypeSpeed = 70f;

        class Row
        {
            public RectTransform Rect;
            public TextMeshProUGUI Key;
            public TextMeshProUGUI Text;
            public Image Box;
            public Image Tick;
        }

        RectTransform m_Card;
        CanvasGroup m_CardGroup;
        TextMeshProUGUI m_Title, m_Subtitle;
        Image m_CardBar;
        readonly List<Row> m_Rows = new List<Row>();
        CanvasGroup m_SubtitleGroup;
        TextMeshProUGUI m_Speaker, m_Line;
        RectTransform m_TopLid, m_BottomLid;
        Image m_Dim;
        int m_ShownVersion = -1;
        string m_ShownTitle;
        float m_CardSince;
        string m_ShownLine;

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
            var director = FindAnyObjectByType<TutorialDirector>();
            if (!director || !director.IsActive || FindAnyObjectByType<TutorialHUD>()) return;

            var root = new GameObject("Tutorial HUD", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Over the HUD (the eyelids cover it while waking) but under the pause menu's fade.
            canvas.sortingOrder = 30;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;
            var hud = root.AddComponent<TutorialHUD>();
            hud.Director = director;
            hud.Build((RectTransform)root.transform);
        }

        void Build(RectTransform root)
        {
            var theme = HudTheme.Current;
            TMP_FontAsset title = theme ? theme.TitleFont : null, body = theme ? theme.BodyFont : null;

            // --- Task card, upper right ---
            m_Card = Rect("Card", root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -150f), new Vector2(470f, 120f));
            m_CardGroup = m_Card.gameObject.AddComponent<CanvasGroup>();
            m_CardGroup.alpha = 0f;
            var plate = Box("Plate", m_Card, new Color(.02f, .05f, .08f, .72f));
            Stretch(plate.rectTransform);
            m_CardBar = Box("Accent", m_Card, Accent);
            var bar = m_CardBar.rectTransform;
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(0f, 1f);
            bar.pivot = new Vector2(0f, .5f);
            bar.sizeDelta = new Vector2(5f, 0f);
            bar.anchoredPosition = Vector2.zero;
            m_Title = Text("Title", m_Card, title, 38f, TextAlignmentOptions.TopLeft, Color.white);
            Place(m_Title.rectTransform, 24f, -12f, 430f, 44f);
            m_Title.characterSpacing = 4f;
            m_Subtitle = Text("Subtitle", m_Card, body, 19f, TextAlignmentOptions.TopLeft, new Color(.75f, .87f, 1f, .85f));
            Place(m_Subtitle.rectTransform, 25f, -56f, 430f, 26f);

            // --- Subtitles, bottom centre, above the health and Disassembler gauges ---
            var strip = Rect("Transmission", root, new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(0f, 235f), new Vector2(1180f, 90f));
            m_SubtitleGroup = strip.gameObject.AddComponent<CanvasGroup>();
            m_SubtitleGroup.alpha = 0f;
            var back = Box("Plate", strip, new Color(0f, 0f, 0f, .55f));
            Stretch(back.rectTransform);
            m_Speaker = Text("Speaker", strip, title, 22f, TextAlignmentOptions.Top, Accent);
            m_Speaker.characterSpacing = 8f;
            m_Speaker.text = TutorialDirector.Speaker;
            Place(m_Speaker.rectTransform, 0f, -8f, 1240f, 28f, centred: true);
            m_Line = Text("Line", strip, body, 26f, TextAlignmentOptions.Top, Color.white);
            Place(m_Line.rectTransform, 0f, -38f, 1180f, 50f, centred: true);

            // --- Eyelids and dimming for the wake-up ---
            m_Dim = Box("Dim", root, Color.black);
            Stretch(m_Dim.rectTransform);
            m_TopLid = Box("Top Lid", root, Color.black).rectTransform;
            m_TopLid.anchorMin = new Vector2(0f, .5f);
            m_TopLid.anchorMax = new Vector2(1f, 1f);
            m_TopLid.offsetMin = m_TopLid.offsetMax = Vector2.zero;
            m_BottomLid = Box("Bottom Lid", root, Color.black).rectTransform;
            m_BottomLid.anchorMin = new Vector2(0f, 0f);
            m_BottomLid.anchorMax = new Vector2(1f, .5f);
            m_BottomLid.offsetMin = m_BottomLid.offsetMax = Vector2.zero;
        }

        void Update()
        {
            if (!Director)
            {
                Destroy(gameObject);
                return;
            }
            UpdateEyelids();
            UpdateCard();
            UpdateSubtitle();
        }

        void UpdateEyelids()
        {
            float closed = Director.EyesClosed;
            bool show = closed > .001f;
            m_TopLid.gameObject.SetActive(show);
            m_BottomLid.gameObject.SetActive(show);
            m_Dim.gameObject.SetActive(show);
            if (!show) return;
            // Each lid covers its half when shut, and a little less than nothing when open (eased).
            float k = closed * closed * (3f - 2f * closed);
            m_TopLid.anchorMin = new Vector2(0f, Mathf.Lerp(1f, .5f, k));
            m_BottomLid.anchorMax = new Vector2(1f, Mathf.Lerp(0f, .5f, k));
            m_Dim.color = new Color(0f, 0f, 0f, Mathf.Clamp01(closed * .85f));
        }

        void UpdateCard()
        {
            if (Director.CardVersion != m_ShownVersion)
            {
                m_ShownVersion = Director.CardVersion;
                if (Director.CardTitle != m_ShownTitle)
                {
                    m_ShownTitle = Director.CardTitle;
                    m_CardSince = Time.unscaledTime;
                }
                Rebuild();
            }

            // Slides in from the right whenever the heading changes; ticks brighten as they land.
            float since = Time.unscaledTime - m_CardSince;
            float slide = 1f - Mathf.Clamp01(since / .35f);
            slide = slide * slide;
            m_Card.anchoredPosition = new Vector2(-40f + slide * 120f, -150f);
            float target = Director.CardVisible ? 1f : 0f;
            m_CardGroup.alpha = Mathf.MoveTowards(m_CardGroup.alpha, target * (1f - slide * .8f), Time.unscaledDeltaTime * 4f);
            m_CardBar.color = Color.Lerp(Color.white, Accent, Mathf.Clamp01(since / .6f));
        }

        void Rebuild()
        {
            var theme = HudTheme.Current;
            TMP_FontAsset title = theme ? theme.TitleFont : null, body = theme ? theme.BodyFont : null;
            m_Title.text = Director.CardTitle;
            m_Subtitle.text = Director.CardSubtitle;

            var tasks = Director.Tasks;
            while (m_Rows.Count < tasks.Count)
            {
                var row = new Row { Rect = Rect("Task", m_Card, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(430f, 34f)) };
                row.Rect.pivot = new Vector2(0f, 1f);
                row.Box = Box("Box", row.Rect, new Color(1f, 1f, 1f, .5f));
                Place(row.Box.rectTransform, 0f, -9f, 16f, 16f);
                var hole = Box("Hole", row.Box.rectTransform, new Color(.02f, .05f, .08f, 1f));
                Stretch(hole.rectTransform, 2f);
                row.Tick = Box("Tick", row.Box.rectTransform, DoneColor);
                Stretch(row.Tick.rectTransform, 3f);
                row.Key = Text("Key", row.Rect, title, 25f, TextAlignmentOptions.Left, Accent);
                row.Key.characterSpacing = 3f;
                Place(row.Key.rectTransform, 28f, -2f, 200f, 32f);
                row.Text = Text("Text", row.Rect, body, 22f, TextAlignmentOptions.Left, Color.white);
                Place(row.Text.rectTransform, 28f, -2f, 400f, 32f);
                m_Rows.Add(row);
            }

            float y = -92f;
            for (int i = 0; i < m_Rows.Count; i++)
            {
                var row = m_Rows[i];
                bool used = i < tasks.Count;
                row.Rect.gameObject.SetActive(used);
                if (!used) continue;
                var task = tasks[i];
                row.Rect.anchoredPosition = new Vector2(24f, y);
                bool hasKey = !string.IsNullOrEmpty(task.Key);
                row.Key.text = hasKey ? $"[ {task.Key} ]" : "";
                float keyWidth = hasKey ? row.Key.GetPreferredValues(row.Key.text).x + 14f : 0f;
                Place(row.Text.rectTransform, 28f + keyWidth, -2f, 400f - keyWidth, 32f);
                row.Text.text = task.Text;
                row.Tick.enabled = task.Done;
                Color text = task.Done ? new Color(.7f, .8f, .85f, .6f) : Color.white;
                row.Text.color = text;
                row.Key.color = task.Done ? new Color(DoneColor.r, DoneColor.g, DoneColor.b, .7f) : Accent;
                y -= 38f;
            }
            m_Card.sizeDelta = new Vector2(470f, -y + 10f);
        }

        void UpdateSubtitle()
        {
            string line = Director.Transmission;
            bool on = line != null;
            m_SubtitleGroup.alpha = Mathf.MoveTowards(m_SubtitleGroup.alpha, on ? 1f : 0f, Time.unscaledDeltaTime * 5f);
            if (!on) return;
            if (line != m_ShownLine)
            {
                m_ShownLine = line;
                m_Line.text = line;
            }
            float age = Time.time - Director.TransmissionStart;
            m_Line.maxVisibleCharacters = Mathf.FloorToInt(age * TypeSpeed);
            // The speaker tag flickers while the line types, like an open channel.
            m_Speaker.alpha = m_Line.maxVisibleCharacters < line.Length ? .6f + .4f * Mathf.Abs(Mathf.Sin(age * 17f)) : 1f;
        }

        // ------------------------------------------------------------------ building blocks

        static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(anchorMax.x, anchorMax.y);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        static Image Box(string name, Transform parent, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        // Top-left placement inside the parent (or top-centre when centred).
        static void Place(RectTransform rect, float x, float y, float width, float height, bool centred = false)
        {
            rect.anchorMin = rect.anchorMax = centred ? new Vector2(.5f, 1f) : new Vector2(0f, 1f);
            rect.pivot = centred ? new Vector2(.5f, 1f) : new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, y);
        }

        static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font, float size, TextAlignmentOptions alignment, Color color)
        {
            var text = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(parent, false);
            if (font) text.font = font;
            else if (TMP_Settings.defaultFontAsset) text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }
    }
}
