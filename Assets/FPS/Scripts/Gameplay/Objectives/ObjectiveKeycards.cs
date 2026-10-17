using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Recover every keycard from the major rooms' terminals, then present them at the Master terminal to open
    /// the way to the boss. Counts cards as they are taken and completes when the Master terminal unlocks.
    /// </summary>
    public class ObjectiveKeycards : Objective
    {
        public MasterTerminal Terminal;

        [Tooltip("Shown once every keycard is in hand.")]
        public string PresentDescription = "Present the keycards at the Master terminal";

        KeycardInventory m_Inventory;
        int m_Found;
        int CardCount => Terminal && Terminal.RequiredCards != null ? Terminal.RequiredCards.Length : 3;

        protected override void Start()
        {
            base.Start();
            UpdateObjective(Description, Counter(), string.Empty);

            m_Inventory = FindAnyObjectByType<KeycardInventory>();
            if (m_Inventory) m_Inventory.OnAdded += OnCardAdded;
            if (Terminal) Terminal.OnUnlocked += OnUnlocked;
        }

        void OnDestroy()
        {
            if (m_Inventory) m_Inventory.OnAdded -= OnCardAdded;
            if (Terminal) Terminal.OnUnlocked -= OnUnlocked;
        }

        void OnCardAdded(KeycardData card)
        {
            if (IsCompleted) return;
            // Only the Master terminal's cards count, not the tutorial's black clearance card.
            if (Terminal && Terminal.RequiredCards != null && System.Array.IndexOf(Terminal.RequiredCards, card) < 0) return;
            m_Found = Mathf.Min(CardCount, m_Found + 1);
            bool all = m_Found >= CardCount;
            UpdateObjective(all ? PresentDescription : Description, Counter(),
                all ? "All keycards recovered" : $"Keycard recovered ({m_Found}/{CardCount})");
        }

        void OnUnlocked()
        {
            if (!IsCompleted) CompleteObjective(string.Empty, Counter(), "Objective complete : " + Title);
        }

        string Counter() => m_Found + " / " + CardCount;
    }
}
