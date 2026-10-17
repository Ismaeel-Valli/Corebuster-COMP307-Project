using UnityEngine;

namespace Unity.FPS.Game
{
    public enum GameDifficulty
    {
        // Used when MainScene is played directly, without going through the main menu.
        Standard,
        // For testing: the player cannot be hurt.
        Invincible,
        Easy,
        Hard,
    }

    /// <summary>
    /// Player options chosen in the main menu, kept for the whole session. Graphics settings and mouse
    /// sensitivity are saved to PlayerPrefs; the difficulty lasts until the game is closed.
    /// </summary>
    public static class GameSettings
    {
        const string k_SensitivityKey = "CoreBuster.MouseSensitivity";
        const string k_QualityKey = "CoreBuster.Quality";
        const string k_VSyncKey = "CoreBuster.VSync";
        const string k_FrameLimitKey = "CoreBuster.FrameLimit";
        const string k_RayTracingKey = "CoreBuster.RayTracing";

        /// <summary>The quality level that turns ray tracing on when chosen.</summary>
        public const string UltraQualityName = "Ultra";

        /// <summary>Raised after a graphics setting changes (quality or ray tracing).</summary>
        public static event System.Action GraphicsChanged;

        /// <summary>Frame rate caps offered in Options; 0 means unlimited.</summary>
        public static readonly int[] FrameLimits = { 30, 60, 120, 144, 0 };

        public const float DefaultSensitivity = 1f;
        public const float MinSensitivity = .1f;
        public const float MaxSensitivity = 3f;

        public static GameDifficulty Difficulty { get; set; } = GameDifficulty.Standard;

        /// <summary>Damage the player takes is multiplied by this.</summary>
        public static float PlayerDamageTakenMultiplier
        {
            get
            {
                switch (Difficulty)
                {
                    case GameDifficulty.Easy: return .6f;
                    case GameDifficulty.Hard: return 1.5f;
                    default: return 1f;
                }
            }
        }

        /// <summary>Enemy max health is multiplied by this.</summary>
        public static float EnemyHealthMultiplier => Difficulty == GameDifficulty.Hard ? 1.3f : 1f;

        public static string DifficultyLabel(GameDifficulty difficulty)
        {
            switch (difficulty)
            {
                case GameDifficulty.Invincible: return "Invincible (Testing)";
                case GameDifficulty.Easy: return "Easy";
                case GameDifficulty.Hard: return "Hard";
                default: return "Standard";
            }
        }

        public static float MouseSensitivity
        {
            get => PlayerPrefs.GetFloat(k_SensitivityKey, DefaultSensitivity);
            set
            {
                PlayerPrefs.SetFloat(k_SensitivityKey, Mathf.Clamp(value, MinSensitivity, MaxSensitivity));
                PlayerPrefs.Save();
            }
        }

        public static int QualityLevel
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(k_QualityKey, QualitySettings.GetQualityLevel()), 0,
                QualitySettings.names.Length - 1);
            set
            {
                PlayerPrefs.SetInt(k_QualityKey, value);
                QualitySettings.SetQualityLevel(value, true);
                // Quality levels carry their own vSync; keep the player's choice on top.
                QualitySettings.vSyncCount = VSync ? 1 : 0;
                // Ultra turns ray tracing on by default, Normal turns it off; either can be toggled afterwards.
                PlayerPrefs.SetInt(k_RayTracingKey, IsUltra && RayTracingSupported ? 1 : 0);
                PlayerPrefs.Save();
                GraphicsChanged?.Invoke();
            }
        }

        public static bool IsUltra => QualitySettings.names.Length > 0 &&
                                      QualitySettings.names[QualitySettings.GetQualityLevel()] == UltraQualityName;

        /// <summary>
        /// Ray tracing runs on hardware ray tracing where the GPU has it and on compute shaders everywhere else.
        /// It is slow without hardware support, so it is off by default and only Ultra turns it on.
        /// </summary>
        public static bool RayTracingSupported => SystemInfo.supportsComputeShaders;

        public static bool RayTracing
        {
            get => RayTracingSupported && PlayerPrefs.GetInt(k_RayTracingKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(k_RayTracingKey, value && RayTracingSupported ? 1 : 0);
                PlayerPrefs.Save();
                GraphicsChanged?.Invoke();
            }
        }

        public static bool VSync
        {
            get => PlayerPrefs.GetInt(k_VSyncKey, QualitySettings.vSyncCount > 0 ? 1 : 0) == 1;
            set
            {
                PlayerPrefs.SetInt(k_VSyncKey, value ? 1 : 0);
                PlayerPrefs.Save();
                QualitySettings.vSyncCount = value ? 1 : 0;
            }
        }

        /// <summary>Frame rate cap (0 = unlimited). VSync, when on, takes precedence.</summary>
        public static int FrameLimit
        {
            get => PlayerPrefs.GetInt(k_FrameLimitKey, 0);
            set
            {
                PlayerPrefs.SetInt(k_FrameLimitKey, Mathf.Max(0, value));
                PlayerPrefs.Save();
                Application.targetFrameRate = value > 0 ? value : -1;
            }
        }

        // Domain reload is disabled in this project, so statics are reset by hand when play mode starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession()
        {
            Difficulty = GameDifficulty.Standard;
            GraphicsChanged = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplySaved()
        {
            if (PlayerPrefs.HasKey(k_QualityKey)) QualitySettings.SetQualityLevel(QualityLevel, true);
            if (PlayerPrefs.HasKey(k_VSyncKey)) QualitySettings.vSyncCount = VSync ? 1 : 0;
            if (PlayerPrefs.HasKey(k_FrameLimitKey)) Application.targetFrameRate = FrameLimit > 0 ? FrameLimit : -1;
        }
    }
}
