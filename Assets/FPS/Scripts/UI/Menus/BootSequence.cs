using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>
    /// Boot screen: the generative AI and asset disclosure comes first, then the galaxy opens into a
    /// wormhole that pulls the viewer in, a flash carries them through, and "Cosmic Studios" appears with
    /// a shine across it before the main menu loads. During the disclosure any key or click continues to
    /// the intro once the text has had time to be read; after that it skips to the menu.
    /// </summary>
    public class BootSequence : MonoBehaviour
    {
        [Tooltip("Full-screen RawImage using the CoreBuster/UI Galaxy material")]
        public RawImage Galaxy;
        [Tooltip("White overlay for the flash through the wormhole")]
        public CanvasGroup Flash;
        [Tooltip("Black overlay for fading in and out")]
        public CanvasGroup Fade;
        [Tooltip("Generative AI and asset disclosure, shown before the studio intro")]
        public CanvasGroup Disclosure;
        [Tooltip("\"Press any key\" line under the disclosure; appears once it can be dismissed")]
        public CanvasGroup DisclosureHint;
        public CanvasGroup StudioGroup;
        public RectTransform StudioTitle;
        public TextShine Shine;
        public string NextScene = "IntroMenu";

        [Header("Timing (seconds)")]
        public float DisclosureFadeTime = .5f;
        [Tooltip("The disclosure moves on by itself after this long")]
        public float DisclosureHoldTime = 10f;
        [Tooltip("Input is ignored for this long, so the disclosure is never skipped by accident")]
        public float DisclosureMinTime = 2f;
        public float FadeInTime = .6f;
        public float PullTime = 2.8f;
        public float FlashTime = .35f;
        public float RevealTime = .7f;
        public float HoldTime = 1.8f;
        public float FadeOutTime = .6f;

        static readonly int k_Wormhole = Shader.PropertyToID("_Wormhole");
        static readonly int k_Warp = Shader.PropertyToID("_WarpOffset");
        static readonly int k_Swirl = Shader.PropertyToID("_Swirl");
        static readonly int k_HoleGlow = Shader.PropertyToID("_HoleGlow");
        static readonly int k_HoleSize = Shader.PropertyToID("_HoleSize");
        static readonly int k_Zoom = Shader.PropertyToID("_Zoom");
        static readonly int k_Brightness = Shader.PropertyToID("_Brightness");

        Material m_Material;
        float m_Warp;
        float m_BaseBrightness = 1f;
        bool m_Leaving;
        bool m_InDisclosure;
        bool m_DisclosureDismissed;
        float m_DisclosureShownAt;

        void Awake()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = false;
            if (Galaxy && Galaxy.material)
            {
                m_Material = new Material(Galaxy.material);
                Galaxy.material = m_Material;
                m_BaseBrightness = m_Material.GetFloat(k_Brightness);
            }
            SetAlpha(Fade, 1f);
            SetAlpha(Flash, 0f);
            SetAlpha(StudioGroup, 0f);
            SetAlpha(Disclosure, 0f);
            SetAlpha(DisclosureHint, 0f);
        }

        void OnDestroy()
        {
            if (m_Material) Destroy(m_Material);
        }

        void Start() => StartCoroutine(Run());

        void Update()
        {
            if (m_Leaving || Time.timeSinceLevelLoad < .4f) return;
            bool skip = (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) ||
                        (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                        (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
            if (!skip) return;
            if (m_InDisclosure)
            {
                if (Time.unscaledTime - m_DisclosureShownAt >= DisclosureMinTime) m_DisclosureDismissed = true;
                return;
            }
            StopAllCoroutines();
            StartCoroutine(Leave());
        }

        IEnumerator Run()
        {
            SetWormhole(0f, 0f);
            if (Disclosure) yield return ShowDisclosure();

            // Out of black onto a calm galaxy that is already starting to drift inward.
            yield return Tween(FadeInTime, k =>
            {
                SetAlpha(Fade, 1f - k);
                Travel(.05f);
            });

            // The wormhole opens and accelerates: the tunnel twists harder, the singularity swells and the
            // view zooms toward it.
            yield return Tween(PullTime, k =>
            {
                float open = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 1.6f));
                float rush = k * k * k;
                SetWormhole(open, rush);
                Travel(Mathf.Lerp(.15f, 3.5f, rush));
                if (k > .8f) SetAlpha(Flash, (k - .8f) / .2f * .6f);
            });

            // Through the throat: white-out.
            yield return Tween(FlashTime, k =>
            {
                SetAlpha(Flash, Mathf.Lerp(.6f, 1f, k));
                Travel(4f);
            });

            // Out the other side: a quiet star field, and the studio name.
            SetWormhole(0f, 0f);
            if (m_Material) m_Material.SetFloat(k_Brightness, .45f);
            yield return Tween(RevealTime, k =>
            {
                float e = 1f - (1f - k) * (1f - k);
                SetAlpha(Flash, 1f - e);
                SetAlpha(StudioGroup, e);
                if (StudioTitle) StudioTitle.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, e);
                Travel(.05f);
            });

            if (Shine) Shine.Play();
            yield return Tween(HoldTime, k => Travel(.05f));
            yield return Leave();
        }

        // The disclosure sits over a dimmed, slowly drifting sky, then fades back to black for the intro.
        IEnumerator ShowDisclosure()
        {
            m_InDisclosure = true;
            m_DisclosureShownAt = Time.unscaledTime;
            if (m_Material) m_Material.SetFloat(k_Brightness, m_BaseBrightness * .35f);

            yield return Tween(DisclosureFadeTime, k =>
            {
                SetAlpha(Fade, 1f - k);
                SetAlpha(Disclosure, k);
                Travel(.02f);
            });
            for (float t = DisclosureFadeTime; t < DisclosureHoldTime && !m_DisclosureDismissed; t += Time.unscaledDeltaTime)
            {
                SetAlpha(DisclosureHint, Mathf.Clamp01((t - DisclosureMinTime) / .4f));
                Travel(.02f);
                yield return null;
            }
            yield return Tween(DisclosureFadeTime, k =>
            {
                SetAlpha(Fade, k);
                SetAlpha(Disclosure, 1f - k);
            });

            m_InDisclosure = false;
            if (m_Material) m_Material.SetFloat(k_Brightness, m_BaseBrightness);
        }

        IEnumerator Leave()
        {
            m_Leaving = true;
            float from = Fade ? Fade.alpha : 0f;
            yield return Tween(FadeOutTime, k => SetAlpha(Fade, Mathf.Lerp(from, 1f, k)));
            Cursor.visible = true;
            SceneManager.LoadScene(NextScene);
        }

        void SetWormhole(float open, float rush)
        {
            if (!m_Material) return;
            m_Material.SetFloat(k_Wormhole, open);
            m_Material.SetFloat(k_Swirl, Mathf.Lerp(.4f, 2.2f, rush));
            m_Material.SetFloat(k_HoleGlow, Mathf.Lerp(.8f, 3.5f, rush));
            m_Material.SetFloat(k_HoleSize, Mathf.Lerp(.05f, .2f, rush));
            m_Material.SetFloat(k_Zoom, Mathf.Lerp(1f, 1.8f, rush));
        }

        // Slides the tunnel walls toward the viewer; speed in tunnel lengths per second.
        void Travel(float speed)
        {
            m_Warp += speed * Time.unscaledDeltaTime;
            if (m_Material) m_Material.SetFloat(k_Warp, m_Warp);
        }

        static IEnumerator Tween(float duration, System.Action<float> step)
        {
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                step(Mathf.Clamp01(t / duration));
                yield return null;
            }
            step(1f);
        }

        static void SetAlpha(CanvasGroup group, float alpha)
        {
            if (group) group.alpha = alpha;
        }
    }
}
