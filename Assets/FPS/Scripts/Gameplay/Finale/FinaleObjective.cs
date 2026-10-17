using Unity.FPS.Game;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// A finale objective whose lines the FinaleDirector writes (the survival countdown, the core's armour).
    /// Kept inactive until its phase begins, so it only appears on the objective HUD then.
    /// </summary>
    public class FinaleObjective : Objective
    {
        public void Show(string description, string counter, string notification = "") =>
            UpdateObjective(description, counter, notification);

        public void Complete(string notification) => CompleteObjective(string.Empty, string.Empty, notification);
    }
}
