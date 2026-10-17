namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// The final execution, offered on the platform in front of the giant core once its armour is gone. Uses the
    /// keycard loop's Interact prompt; parent it under the core so the core's own colliders never hide it.
    /// </summary>
    public class CoreBustPoint : KeycardInteractable
    {
        public GiantCore Core;
        public FinaleDirector Director;

        public override bool IsAvailable => Core && Core.IsExposed && Director && Director.CanBustCore;

        public override string PromptAction(KeycardInteractor user) => "BUST THE CORE";

        public override string PromptDetail(KeycardInteractor user) => "FINAL EXECUTION";

        public override void Interact(KeycardInteractor user) => Director.BeginFinalBust();
    }
}
