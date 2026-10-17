using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    /// <summary>
    /// Main menu flow. Start opens the difficulty choice (Invincible for testing, Easy, Hard), Options opens
    /// the settings, and Quit asks for confirmation first. Escape steps back out of any sub-screen.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        [Tooltip("Logo, glowing core and title; hidden while the Options screen is open")]
        public GameObject Header;
        public GameObject MainPanel;
        public GameObject DifficultyPanel;
        public GameObject OptionsPanel;
        public GameObject QuitDialog;

        [Header("Keyboard/gamepad focus when each screen opens")]
        public Selectable MainFirst;
        public Selectable DifficultyFirst;
        public Selectable OptionsFirst;
        public Selectable QuitFirst;

        public SceneFader Fader;
        public string GameSceneName = "MainScene";

        GameObject m_Current;
        Selectable m_CurrentFirst;

        void Start()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1f;
            ShowMain();
        }

        void Update()
        {
            bool back = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                        (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);
            if (back && !KeyRebindRow.IsRebinding) Back();

            // Keyboard or gamepad input with nothing focused picks the screen's first button.
            if (EventSystem.current && !EventSystem.current.currentSelectedGameObject && m_CurrentFirst &&
                m_CurrentFirst.gameObject.activeInHierarchy && NavigationPressed())
                m_CurrentFirst.Select();
        }

        static bool NavigationPressed()
        {
            var k = Keyboard.current;
            if (k != null && (k.upArrowKey.wasPressedThisFrame || k.downArrowKey.wasPressedThisFrame ||
                              k.wKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame || k.tabKey.wasPressedThisFrame))
                return true;
            var g = Gamepad.current;
            return g != null && (g.dpad.up.wasPressedThisFrame || g.dpad.down.wasPressedThisFrame ||
                                 g.leftStick.ReadValue().sqrMagnitude > .25f);
        }

        void Back()
        {
            if (m_Current == QuitDialog) CancelQuit();
            else if (m_Current != MainPanel) ShowMain();
        }

        void Show(GameObject panel, Selectable first, bool showHeader)
        {
            if (MainPanel) MainPanel.SetActive(panel == MainPanel);
            if (DifficultyPanel) DifficultyPanel.SetActive(panel == DifficultyPanel);
            if (OptionsPanel) OptionsPanel.SetActive(panel == OptionsPanel);
            if (QuitDialog) QuitDialog.SetActive(false);
            if (Header) Header.SetActive(showHeader);
            m_Current = panel;
            m_CurrentFirst = first;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }

        public void ShowMain() => Show(MainPanel, MainFirst, true);
        public void ShowDifficulty() => Show(DifficultyPanel, DifficultyFirst, true);
        public void ShowOptions() => Show(OptionsPanel, OptionsFirst, false);

        /// <summary>Opens the "are you sure" dialog over the main buttons.</summary>
        public void ShowQuitConfirm()
        {
            if (QuitDialog) QuitDialog.SetActive(true);
            if (MainPanel) MainPanel.SetActive(false);
            m_Current = QuitDialog;
            m_CurrentFirst = QuitFirst;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }

        public void CancelQuit() => ShowMain();

        public void ConfirmQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Starts the game at a difficulty (the GameDifficulty value, as an int for button events).</summary>
        public void StartGame(int difficulty)
        {
            GameSettings.Difficulty = (GameDifficulty)difficulty;
            if (Fader) Fader.LoadScene(GameSceneName);
            else UnityEngine.SceneManagement.SceneManager.LoadScene(GameSceneName);
        }
    }
}
