using System;
using System.Collections;
using TMPro;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>
    /// The finale's screen work for FinaleDirector:
    /// <list type="bullet">
    /// <item>the choice between escaping and fighting (click, keys 1/2, or gamepad X/Y);</item>
    /// <item>the meltdown: the player's own core rises from the bottom of the screen, swells to fill it and
    /// bursts into white (CoreBuster/UI Core Meltdown drives the look through _Expand);</item>
    /// <item>the slow white flash that carries the final bust into the win screen.</item>
    /// </list>
    /// Times are unscaled, so a pause menu or slow-motion cannot stall an ending.
    /// </summary>
    public class FinaleOverlay : MonoBehaviour
    {
        public FinaleDirector Director;

        [Header("Choice")]
        public CanvasGroup ChoicePanel;
        public Button EscapeButton;
        public Button FightButton;
        [Min(0f)] public float ChoiceFadeDuration = .35f;

        [Header("Meltdown")]
        public CanvasGroup MeltdownGroup;
        [Tooltip("Shaken during the meltdown; parent of the core.")]
        public RectTransform ShakeRoot;
        public RectTransform MeltdownCore;
        [Tooltip("Uses CoreBuster/UI Core Meltdown; a copy is animated.")]
        public Graphic MeltdownGraphic;
        [Min(0f)] public float RiseDuration = 2.4f;
        [Min(0f)] public float ExpandDuration = 1.8f;
        [Min(0f)] public float BurstDuration = .5f;
        [Min(0f)] public float HoldWhite = .6f;
        [Min(1f)] public float ExpandScale = 7f;
        [Min(0f)] public float ShakeAmount = 18f;
        public AudioClip RumbleSfx;
        public AudioClip BurstSfx;

        [Header("Flash")]
        public CanvasGroup WhiteFlash;

        [Header("Escape Countdown")]
        [Tooltip("Seconds left at which the countdown turns red and pulses.")]
        [Min(0f)] public float CountdownWarning = 15f;

        static readonly int k_Expand = Shader.PropertyToID("_Expand");
        static readonly Color k_CountdownColor = new Color(1f, .86f, .55f);
        static readonly Color k_CountdownWarningColor = new Color(1f, .25f, .15f);

        Material m_CoreMaterial;
        AudioSource m_Audio;
        bool m_Choosing;
        CanvasGroup m_Countdown;
        TextMeshProUGUI m_CountdownTime;
        int m_CountdownShown = -1;

        void Awake()
        {
            SetGroup(ChoicePanel, 0f, false);
            SetGroup(MeltdownGroup, 0f, false);
            SetGroup(WhiteFlash, 0f, false);
            if (MeltdownGraphic && MeltdownGraphic.material)
            {
                m_CoreMaterial = new Material(MeltdownGraphic.material);
                MeltdownGraphic.material = m_CoreMaterial;
            }
            m_Audio = gameObject.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;
            m_Audio.spatialBlend = 0f;
            if (EscapeButton) EscapeButton.onClick.AddListener(() => Pick(FinaleDirector.Choice.Escape));
            if (FightButton) FightButton.onClick.AddListener(() => Pick(FinaleDirector.Choice.Fight));
            BuildCountdown();
        }

        void OnEnable()
        {
            if (!Director) return;
            Director.ChoiceRequested += OnChoiceRequested;
            Director.MeltdownRequested += OnMeltdownRequested;
            Director.WhiteFlashRequested += OnWhiteFlashRequested;
        }

        void OnDisable()
        {
            if (!Director) return;
            Director.ChoiceRequested -= OnChoiceRequested;
            Director.MeltdownRequested -= OnMeltdownRequested;
            Director.WhiteFlashRequested -= OnWhiteFlashRequested;
        }

        void OnDestroy()
        {
            if (m_CoreMaterial) Destroy(m_CoreMaterial);
        }

        // ------------------------------------------------------------------ choice

        void OnChoiceRequested()
        {
            m_Choosing = true;
            StartCoroutine(Fade(ChoicePanel, 0f, 1f, ChoiceFadeDuration, true));
        }

        void Update()
        {
            UpdateCountdown();
            if (!m_Choosing) return;
            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;
            if ((keyboard != null && (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)) ||
                (gamepad != null && gamepad.buttonWest.wasPressedThisFrame))
                Pick(FinaleDirector.Choice.Escape);
            else if ((keyboard != null && (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)) ||
                     (gamepad != null && gamepad.buttonNorth.wasPressedThisFrame))
                Pick(FinaleDirector.Choice.Fight);
        }

        void Pick(FinaleDirector.Choice choice)
        {
            if (!m_Choosing) return;
            m_Choosing = false;
            SetGroup(ChoicePanel, 0f, false);
            if (Director) Director.Choose(choice);
        }

        // ------------------------------------------------------------------ escape countdown

        // A plate at the top centre of the screen, built here so the scene's overlay needs no rebuild.
        void BuildCountdown()
        {
            var parent = transform as RectTransform;
            if (!parent) return;
            var theme = HudTheme.Current ? HudTheme.Current : FindAnyObjectByType<HudTheme>();

            var plate = new GameObject("Escape Countdown", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            var rect = (RectTransform)plate.transform;
            rect.SetParent(parent, false);
            // Behind the choice, meltdown and flash, which are full-screen siblings.
            rect.SetAsFirstSibling();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -24f);
            rect.sizeDelta = new Vector2(420f, 112f);
            var image = plate.GetComponent<Image>();
            image.color = new Color(.02f, .03f, .05f, .72f);
            image.raycastTarget = false;
            m_Countdown = plate.GetComponent<CanvasGroup>();
            SetGroup(m_Countdown, 0f, false);

            var label = CountdownText("Label", rect, theme ? theme.BodyFont : null, 20f, new Color(.8f, .85f, .9f, .9f));
            label.text = "ESCAPE POD PRIMING";
            label.characterSpacing = 6f;
            PlaceCountdownText(label.rectTransform, -8f, 26f);
            m_CountdownTime = CountdownText("Time", rect, theme ? theme.TitleFont : null, 58f, k_CountdownColor);
            if (theme && theme.TitleMaterial) m_CountdownTime.fontSharedMaterial = theme.TitleMaterial;
            PlaceCountdownText(m_CountdownTime.rectTransform, -34f, 70f);
        }

        void UpdateCountdown()
        {
            if (!m_Countdown || !Director) return;
            bool show = Director.Current == FinaleDirector.Phase.Escape;
            m_Countdown.alpha = Mathf.MoveTowards(m_Countdown.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            if (!show) return;

            float remaining = Director.SurviveRemaining;
            int seconds = Mathf.CeilToInt(remaining);
            if (seconds != m_CountdownShown)
            {
                m_CountdownShown = seconds;
                m_CountdownTime.text = $"{seconds / 60}:{seconds % 60:00}";
            }
            bool warning = remaining <= CountdownWarning;
            float pulse = warning ? .6f + .4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f)) : 1f;
            m_CountdownTime.color = (warning ? k_CountdownWarningColor : k_CountdownColor) * new Color(1f, 1f, 1f, pulse);
        }

        static TextMeshProUGUI CountdownText(string name, Transform parent, TMP_FontAsset font, float size, Color color)
        {
            var text = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(parent, false);
            if (font) text.font = font;
            else if (TMP_Settings.defaultFontAsset) text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Top;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        static void PlaceCountdownText(RectTransform rect, float y, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = new Vector2(0f, y);
        }

        // ------------------------------------------------------------------ meltdown

        void OnMeltdownRequested(Action done) => StartCoroutine(Meltdown(done));

        IEnumerator Meltdown(Action done)
        {
            SetGroup(MeltdownGroup, 1f, false);
            Play(RumbleSfx);
            float height = ShakeRoot ? ShakeRoot.rect.height : 1080f;
            float size = MeltdownCore ? MeltdownCore.rect.height : 520f;
            Vector2 below = new Vector2(0f, -(height + size) * .5f);
            SetCore(below, 1f, 0f);

            // Rises from below the screen, throbbing.
            for (float t = 0f; t < RiseDuration; t += Time.unscaledDeltaTime)
            {
                float k = t / RiseDuration;
                float ease = 1f - Mathf.Pow(1f - k, 3f);
                SetCore(Vector2.Lerp(below, Vector2.zero, ease), 1f, .15f * k);
                ShakeBy(ShakeAmount * .25f * k);
                yield return null;
            }

            // Swells to fill the screen, its surface tearing open.
            for (float t = 0f; t < ExpandDuration; t += Time.unscaledDeltaTime)
            {
                float k = t / ExpandDuration;
                SetCore(Vector2.zero, Mathf.Lerp(1f, ExpandScale, k * k), Mathf.Lerp(.15f, 1f, k));
                ShakeBy(ShakeAmount * Mathf.Lerp(.25f, 1f, k));
                yield return null;
            }

            // Bursts into white.
            Play(BurstSfx);
            yield return Fade(WhiteFlash, 0f, 1f, BurstDuration, false);
            ShakeBy(0f);
            yield return new WaitForSecondsRealtime(HoldWhite);
            done?.Invoke();
        }

        void SetCore(Vector2 position, float scale, float expand)
        {
            if (MeltdownCore)
            {
                MeltdownCore.anchoredPosition = position;
                MeltdownCore.localScale = Vector3.one * scale;
            }
            if (m_CoreMaterial) m_CoreMaterial.SetFloat(k_Expand, expand);
        }

        void ShakeBy(float amount)
        {
            if (ShakeRoot) ShakeRoot.anchoredPosition = UnityEngine.Random.insideUnitCircle * amount;
        }

        // ------------------------------------------------------------------ flash

        void OnWhiteFlashRequested(float duration, Action done) => StartCoroutine(SlowFlash(duration, done));

        IEnumerator SlowFlash(float duration, Action done)
        {
            // Emerges slowly at first, then floods in.
            SetGroup(WhiteFlash, 0f, false);
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = t / duration;
                WhiteFlash.alpha = k * k * k;
                yield return null;
            }
            WhiteFlash.alpha = 1f;
            yield return new WaitForSecondsRealtime(HoldWhite);
            done?.Invoke();
        }

        // ------------------------------------------------------------------ helpers

        IEnumerator Fade(CanvasGroup group, float from, float to, float duration, bool interactive)
        {
            if (!group) yield break;
            SetGroup(group, from, interactive);
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Lerp(from, to, t / duration);
                yield return null;
            }
            group.alpha = to;
        }

        static void SetGroup(CanvasGroup group, float alpha, bool interactive)
        {
            if (!group) return;
            group.alpha = alpha;
            group.interactable = interactive;
            group.blocksRaycasts = interactive;
        }

        void Play(AudioClip clip)
        {
            if (clip && m_Audio) m_Audio.PlayOneShot(clip);
        }
    }
}
