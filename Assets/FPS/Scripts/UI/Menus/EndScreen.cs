using TMPro;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>
    /// Win/lose screen: play again at the same difficulty, or go back to the main menu. A scripted ending can
    /// replace the subtitle with its own line and have the screen open out of a white flash (see GameEnding).
    /// </summary>
    public class EndScreen : MonoBehaviour
    {
        public SceneFader Fader;
        public TMP_Text DifficultyLabel;
        [Tooltip("Replaced by the ending's line when the game ended on a scripted ending")]
        public TMP_Text Subtitle;
        public Selectable FirstSelection;
        public string GameSceneName = "MainScene";
        public string MenuSceneName = "IntroMenu";
        [Tooltip("How long the screen takes to emerge from the white flash of a scripted ending")]
        [Min(0f)] public float WhiteFadeInDuration = 2.5f;

        void Awake()
        {
            // Read before SceneFader.Start begins its fade-in.
            if (Subtitle && !string.IsNullOrEmpty(GameEnding.Message)) Subtitle.text = GameEnding.Message;
            if (GameEnding.FadeFromWhite && Fader && Fader.Overlay)
            {
                var overlay = Fader.Overlay.GetComponent<Image>();
                if (overlay) overlay.color = Color.white;
                Fader.FadeInDuration = WhiteFadeInDuration;
            }
            GameEnding.Clear();
        }

        void Start()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (DifficultyLabel && GameSettings.Difficulty != GameDifficulty.Standard)
                DifficultyLabel.text = "Difficulty: " + GameSettings.DifficultyLabel(GameSettings.Difficulty);
            else if (DifficultyLabel) DifficultyLabel.text = "";
        }

        void Update()
        {
            var k = Keyboard.current;
            if (EventSystem.current && !EventSystem.current.currentSelectedGameObject && FirstSelection && k != null &&
                (k.upArrowKey.wasPressedThisFrame || k.downArrowKey.wasPressedThisFrame || k.tabKey.wasPressedThisFrame))
                FirstSelection.Select();
        }

        public void PlayAgain() => Load(GameSceneName);
        public void MainMenu() => Load(MenuSceneName);

        void Load(string scene)
        {
            if (Fader) Fader.LoadScene(scene);
            else UnityEngine.SceneManagement.SceneManager.LoadScene(scene);
        }
    }
}
