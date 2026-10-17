using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// One of the keycards (a coloured floppy disk) the player recovers from the major rooms. The same asset is
    /// shared by the terminal that ejects it, the access machine it opens, the Master terminal and the HUD.
    /// </summary>
    [CreateAssetMenu(menuName = "CoreBuster/Keycard", fileName = "Keycard")]
    public class KeycardData : ScriptableObject
    {
        [Tooltip("Shown in prompts and messages, e.g. \"BLUE KEYCARD\".")]
        public string DisplayName = "KEYCARD";
        [Tooltip("Colour of the disk: lights, prompts and the HUD use it.")]
        public Color Tint = Color.cyan;
        [Tooltip("HUD icon.")]
        public Sprite Icon;
        [Tooltip("The floppy disk model, used both in the world and in the player's hand.")]
        public GameObject Model;

        public string RichName => $"<color=#{ColorUtility.ToHtmlStringRGB(Tint)}>{DisplayName}</color>";
    }
}
