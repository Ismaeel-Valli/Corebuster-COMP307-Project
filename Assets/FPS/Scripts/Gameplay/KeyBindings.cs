using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Saves and restores the player's key rebinds. They are stored as binding overrides on the project-wide
    /// input actions, so every script reading InputSystem.actions (movement, weapons, core execution)
    /// uses the new keys straight away.
    /// </summary>
    public static class KeyBindings
    {
        const string k_Key = "CoreBuster.KeyBindings";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Load()
        {
            var actions = InputSystem.actions;
            if (actions == null) return;
            actions.RemoveAllBindingOverrides();
            string json = PlayerPrefs.GetString(k_Key, "");
            if (!string.IsNullOrEmpty(json)) actions.LoadBindingOverridesFromJson(json);
        }

        public static void Save()
        {
            var actions = InputSystem.actions;
            if (actions == null) return;
            PlayerPrefs.SetString(k_Key, actions.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        public static void ResetToDefaults()
        {
            InputSystem.actions?.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(k_Key);
            PlayerPrefs.Save();
        }
    }
}
