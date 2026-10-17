using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.FPS.UI
{
    /// <summary>Fades the screen from black when a menu scene opens, and to black before loading another.</summary>
    public class SceneFader : MonoBehaviour
    {
        [Tooltip("Full-screen black overlay")]
        public CanvasGroup Overlay;

        [Min(0f)] public float FadeInDuration = .5f;
        [Min(0f)] public float FadeOutDuration = .35f;

        [Tooltip("Scenes heavy enough to load behind the loading screen instead of a plain fade")]
        public string[] LoadingScreenScenes = { "MainScene" };

        bool m_Loading;

        void Start()
        {
            // End screens are reached from a paused or slowed game.
            Time.timeScale = 1f;
            if (Overlay) StartCoroutine(Fade(1f, 0f, FadeInDuration));
        }

        public void LoadScene(string sceneName)
        {
            if (m_Loading) return;
            m_Loading = true;
            StartCoroutine(FadeAndLoad(sceneName));
        }

        IEnumerator FadeAndLoad(string sceneName)
        {
            if (Overlay) yield return Fade(Overlay.alpha, 1f, FadeOutDuration);
            if (System.Array.IndexOf(LoadingScreenScenes, sceneName) >= 0) LoadingScreen.Load(sceneName);
            else SceneManager.LoadScene(sceneName);
        }

        IEnumerator Fade(float from, float to, float duration)
        {
            Overlay.blocksRaycasts = true;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                Overlay.alpha = Mathf.Lerp(from, to, t / duration);
                yield return null;
            }
            Overlay.alpha = to;
            Overlay.blocksRaycasts = to > .01f;
        }
    }
}
