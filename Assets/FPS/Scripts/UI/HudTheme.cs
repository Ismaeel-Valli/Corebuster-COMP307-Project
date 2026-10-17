using TMPro;
using UnityEngine;

namespace Unity.FPS.UI
{
    /// <summary>
    /// Shares the HUD's fonts with UI that builds itself at runtime (the core execution prompt), so it
    /// matches the Steampunk HUD without referencing assets of its own.
    /// </summary>
    public class HudTheme : MonoBehaviour
    {
        public TMP_FontAsset TitleFont;
        public Material TitleMaterial;
        public TMP_FontAsset BodyFont;

        public static HudTheme Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = null;

        void Awake() => Current = this;

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }
    }
}
