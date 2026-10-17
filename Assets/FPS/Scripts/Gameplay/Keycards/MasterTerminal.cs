using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// The Master terminal in the room before the boss. The player slots every keycard into it, one per slot,
    /// and it unlocks the way to the final boss.
    /// </summary>
    public class MasterTerminal : KeycardInteractable
    {
        public KeycardData[] RequiredCards;
        [Tooltip("One per card; each slot's forward points into the terminal.")]
        public Transform[] Slots;
        [Tooltip("Where the player looks while slotting the cards in.")]
        public Transform LookAt;
        public SecurityDoor[] Doors;

        [Header("Status")]
        [Tooltip("One lamp per slot; it takes the card's colour once that card is in.")]
        public Renderer[] SlotLamps;
        public Renderer[] Lamps;
        public Light LampLight;
        public Color WaitingColor = new Color(1f, .12f, .08f);
        public Color GrantedColor = new Color(.15f, 1f, .35f);

        [Header("Sound")]
        public AudioClip GrantedSfx;

        public bool IsUnlocked { get; private set; }
        public UnityAction OnUnlocked;

        /// <summary>
        /// Set at runtime by the finale: accepting the keycards leaves the doors shut, because the player first
        /// chooses between escaping and fighting. The finale calls OpenDoors if they choose to fight.
        /// </summary>
        public bool DeferDoors { get; set; }

        bool m_Inserting;
        float m_DeniedTime = -10f;
        readonly Renderer[] m_OneLamp = new Renderer[1];
        bool[] m_Filled;

        public override bool IsAvailable => !IsUnlocked && !m_Inserting;

        public override string PromptAction(KeycardInteractor user) =>
            user.Inventory.HasAll(RequiredCards) ? "INSERT KEYCARDS" : "MASTER TERMINAL";

        public override string PromptDetail(KeycardInteractor user)
        {
            var text = new StringBuilder("KEYCARDS ");
            foreach (var card in RequiredCards)
                text.Append(user.Inventory.Has(card) ? card.RichName : "<color=#5a5f6a>" + card.DisplayName + "</color>")
                    .Append("  ");
            return text.ToString().TrimEnd();
        }

        void Awake() => m_Filled = new bool[RequiredCards.Length];

        public override void Interact(KeycardInteractor user)
        {
            if (!user.Inventory.HasAll(RequiredCards))
            {
                m_DeniedTime = Time.time;
                PlayAt(DeniedSfx, FocusPosition);
                int held = 0;
                foreach (var card in RequiredCards)
                    if (user.Inventory.Has(card)) held++;
                ShowMessage($"ALL {RequiredCards.Length} KEYCARDS REQUIRED ({held}/{RequiredCards.Length} FOUND)");
                return;
            }

            m_Inserting = user.Insert(RequiredCards, Slots, LookAt,
                (i, disk) =>
                {
                    user.Inventory.Remove(RequiredCards[i]);
                    m_Filled[i] = true;
                },
                Unlock);
        }

        void Unlock()
        {
            m_Inserting = false;
            IsUnlocked = true;
            PlayAt(GrantedSfx, FocusPosition);
            if (DeferDoors)
                ShowMessage("MASTER OVERRIDE ACCEPTED");
            else
                OpenDoors("MASTER OVERRIDE ACCEPTED - CORE VAULT OPEN");
            OnUnlocked?.Invoke();
        }

        /// <summary>Opens the vault after a short "processing" pause that readies the Boss Hall behind it.</summary>
        public void OpenDoors(string message = "CORE VAULT OPEN") => StartCoroutine(OpenWhenReady(message));

        IEnumerator OpenWhenReady(string message)
        {
            ShowMessage("PROCESSING OVERRIDE...");
            yield return ZoneStreamer.OpenWhenReady(Doors);
            ShowMessage(message);
        }

        void Update()
        {
            // An empty slot glows dim in its card's colour, showing which disk goes where; a filled one burns bright.
            for (int i = 0; SlotLamps != null && i < m_Filled.Length && i < SlotLamps.Length; i++)
            {
                m_OneLamp[0] = SlotLamps[i];
                StatusLamps.Set(m_OneLamp, null, RequiredCards[i].Tint, m_Filled[i] ? 5f : .6f * StatusLamps.Pulse(2f));
            }
            if (Time.time - m_DeniedTime < .6f)
                StatusLamps.Set(Lamps, LampLight, WaitingColor, 6f * StatusLamps.Pulse(18f));
            else if (IsUnlocked)
                StatusLamps.Set(Lamps, LampLight, GrantedColor, 3f);
            else
                StatusLamps.Set(Lamps, LampLight, WaitingColor, 2.5f * StatusLamps.Pulse(1.5f));
        }
    }
}
