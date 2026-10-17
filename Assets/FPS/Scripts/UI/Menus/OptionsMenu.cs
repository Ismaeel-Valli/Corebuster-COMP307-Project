using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>
    /// The Options screen: graphics quality, screen resolution, display mode, VSync, mouse sensitivity
    /// and key bindings. Every change applies and saves immediately.
    /// </summary>
    public class OptionsMenu : MonoBehaviour
    {
        [Header("Graphics")]
        public UIStepper Quality;
        public UIStepper Resolution;
        public UIStepper DisplayMode;
        public UIStepper VSync;
        public UIStepper FrameLimit;

        [Header("Controls")]
        public Slider Sensitivity;
        public TMP_Text SensitivityValue;
        public Button ResetBindingsButton;

        static readonly (string label, FullScreenMode mode)[] k_DisplayModes =
        {
            ("Borderless Fullscreen", FullScreenMode.FullScreenWindow),
            ("Exclusive Fullscreen", FullScreenMode.ExclusiveFullScreen),
            ("Windowed", FullScreenMode.Windowed),
        };

        readonly List<Vector2Int> m_Resolutions = new List<Vector2Int>();

        /// <summary>Built at runtime from the VSync row, under Quality (the menu scenes need no rebuild).</summary>
        UIStepper m_RayTracing;

        void Awake()
        {
            m_RayTracing = BuildRayTracingRow();
            if (m_RayTracing) m_RayTracing.OnValueChanged += i => GameSettings.RayTracing = i == 1;
            if (Quality)
                Quality.OnValueChanged += i =>
                {
                    GameSettings.QualityLevel = i;
                    RefreshRayTracing();
                };
            if (VSync) VSync.OnValueChanged += i => GameSettings.VSync = i == 1;
            if (Resolution) Resolution.OnValueChanged += ApplyResolution;
            if (DisplayMode) DisplayMode.OnValueChanged += i => Screen.fullScreenMode = k_DisplayModes[i].mode;
            if (FrameLimit) FrameLimit.OnValueChanged += i => GameSettings.FrameLimit = GameSettings.FrameLimits[i];

            if (Sensitivity)
            {
                Sensitivity.minValue = GameSettings.MinSensitivity;
                Sensitivity.maxValue = GameSettings.MaxSensitivity;
                Sensitivity.onValueChanged.AddListener(OnSensitivityChanged);
            }
            if (ResetBindingsButton) ResetBindingsButton.onClick.AddListener(ResetBindings);
        }

        void OnEnable()
        {
            if (Quality) Quality.SetOptions(QualitySettings.names, QualitySettings.GetQualityLevel());
            RefreshRayTracing();
            if (VSync) VSync.SetOptions(new[] { "Off", "On" }, QualitySettings.vSyncCount > 0 ? 1 : 0);
            if (FrameLimit)
                FrameLimit.SetOptions(GameSettings.FrameLimits.Select(f => f > 0 ? f + " FPS" : "Unlimited").ToArray(),
                    System.Array.IndexOf(GameSettings.FrameLimits, GameSettings.FrameLimit));

            // One entry per size; the highest refresh rate is used automatically.
            m_Resolutions.Clear();
            foreach (var r in Screen.resolutions.Select(r => new Vector2Int(r.width, r.height)).Distinct())
                m_Resolutions.Add(r);
            var current = new Vector2Int(Screen.width, Screen.height);
            if (!m_Resolutions.Contains(current)) m_Resolutions.Add(current);
            m_Resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            if (Resolution)
                Resolution.SetOptions(m_Resolutions.Select(r => r.x + " x " + r.y).ToArray(), m_Resolutions.IndexOf(current));

            if (DisplayMode)
            {
                int mode = System.Array.FindIndex(k_DisplayModes, d => d.mode == Screen.fullScreenMode);
                DisplayMode.SetOptions(k_DisplayModes.Select(d => d.label).ToArray(), Mathf.Max(0, mode));
            }

            if (Sensitivity) Sensitivity.SetValueWithoutNotify(GameSettings.MouseSensitivity);
            UpdateSensitivityLabel(GameSettings.MouseSensitivity);

            // Open the key bindings list at the top.
            foreach (var scroll in GetComponentsInChildren<ScrollRect>()) scroll.verticalNormalizedPosition = 1f;
        }

        void RefreshRayTracing()
        {
            if (!m_RayTracing) return;
            if (GameSettings.RayTracingSupported)
                m_RayTracing.SetOptions(new[] { "Off", "On" }, GameSettings.RayTracing ? 1 : 0);
            else
                m_RayTracing.SetOptions(new[] { "Unsupported" }, 0);
        }

        // Clones the VSync row as "Ray Tracing", places it under Quality and moves the rows below down one step.
        UIStepper BuildRayTracingRow()
        {
            if (!Quality || !VSync) return null;
            var qualityRow = RowOf(Quality, out var column);
            var vsyncRow = RowOf(VSync, out _);
            if (!qualityRow || !vsyncRow || !column) return null;

            float step = Mathf.Abs(RowY(qualityRow) - RowY(RowOf(Resolution, out _) ?? vsyncRow));
            if (step < 1f) step = 60f;
            float qualityY = RowY(qualityRow);
            foreach (RectTransform child in column)
                // Every row below Quality, but not the Back button at the bottom of the column.
                if (child.anchoredPosition.y < qualityY - 1f && child.anchoredPosition.y > -300f)
                    child.anchoredPosition -= new Vector2(0f, step);

            var row = Instantiate(vsyncRow.gameObject, column).GetComponent<RectTransform>();
            row.name = "Ray Tracing";
            row.anchoredPosition = new Vector2(qualityRow.anchoredPosition.x, qualityY - step);
            var stepper = row.GetComponentInChildren<UIStepper>(true);
            foreach (var text in row.GetComponentsInChildren<TMP_Text>(true))
                if (!stepper || text != stepper.ValueLabel)
                    text.text = "Ray Tracing";
            return stepper;
        }

        static RectTransform RowOf(UIStepper stepper, out RectTransform column)
        {
            column = null;
            if (!stepper) return null;
            // The row is the stepper's ancestor that sits directly in the column holding every graphics row.
            Transform t = stepper.transform;
            while (t.parent && t.parent.GetComponentsInChildren<UIStepper>(true).Length < 3) t = t.parent;
            column = t.parent as RectTransform;
            return t as RectTransform;
        }

        static float RowY(RectTransform row) => row ? row.anchoredPosition.y : 0f;

        void ApplyResolution(int index)
        {
            Vector2Int size = m_Resolutions[index];
            Screen.SetResolution(size.x, size.y, Screen.fullScreenMode);
        }

        void OnSensitivityChanged(float value)
        {
            GameSettings.MouseSensitivity = value;
            UpdateSensitivityLabel(value);
        }

        void UpdateSensitivityLabel(float value)
        {
            if (SensitivityValue) SensitivityValue.text = value.ToString("0.00");
        }

        void ResetBindings()
        {
            KeyBindings.ResetToDefaults();
            KeyRebindRow.RefreshAll();
        }
    }
}
