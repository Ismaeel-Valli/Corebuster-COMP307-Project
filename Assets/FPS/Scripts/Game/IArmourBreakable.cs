namespace Unity.FPS.Game
{
    /// <summary>
    /// Something whose armour can be stripped without touching its health, such as an armoured enemy caught in a
    /// rocket blast. Lets gameplay code break armour without depending on the AI assembly.
    /// </summary>
    public interface IArmourBreakable
    {
        bool IsArmourBroken { get; }

        /// <summary>Strips the armour immediately, regardless of health.</summary>
        void BreakArmour();
    }
}
