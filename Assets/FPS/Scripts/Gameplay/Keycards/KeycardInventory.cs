using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// The keycards the player is carrying. Access machines only check for a card; the Master terminal takes
    /// them all. The HUD tray above the health bar mirrors this list.
    /// </summary>
    public class KeycardInventory : MonoBehaviour
    {
        readonly List<KeycardData> m_Cards = new List<KeycardData>();

        public IReadOnlyList<KeycardData> Cards => m_Cards;

        public UnityAction<KeycardData> OnAdded;
        public UnityAction<KeycardData> OnRemoved;

        public bool Has(KeycardData card) => card && m_Cards.Contains(card);

        public bool HasAll(IEnumerable<KeycardData> cards)
        {
            foreach (var card in cards)
                if (!Has(card)) return false;
            return true;
        }

        public void Add(KeycardData card)
        {
            if (!card || m_Cards.Contains(card)) return;
            m_Cards.Add(card);
            OnAdded?.Invoke(card);
        }

        public void Remove(KeycardData card)
        {
            if (!m_Cards.Remove(card)) return;
            OnRemoved?.Invoke(card);
        }
    }
}
