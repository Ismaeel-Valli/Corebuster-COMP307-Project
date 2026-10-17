using UnityEngine;

namespace Unity.FPS.Game
{
    /// <summary>
    /// Carries a scripted ending's line from the game scene to the win or lose screen, and whether that screen
    /// should open out of a white flash instead of black. The end screen reads it once and clears it.
    /// </summary>
    public static class GameEnding
    {
        public static string Message { get; private set; }
        public static bool FadeFromWhite { get; private set; }

        public static void Set(string message, bool fadeFromWhite)
        {
            Message = message;
            FadeFromWhite = fadeFromWhite;
        }

        public static void Clear()
        {
            Message = null;
            FadeFromWhite = false;
        }

        // The project enters play mode without a domain reload, so statics outlive each session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Clear();
    }
}
