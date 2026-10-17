using System.Collections;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// The access machine outside a sealed room. Tapping the previous room's keycard on its reader opens the
    /// room's doors for good. Its lamp shows the colour of the card it wants.
    /// </summary>
    public class KeycardAccessPoint : KeycardInteractable
    {
        public KeycardData RequiredCard;
        [Tooltip("Every door into the room; any one access machine opens them all.")]
        public SecurityDoor[] Doors;
        [Tooltip("The reader face the disk is tapped on; its forward points out of the machine.")]
        public Transform Reader;

        [Header("Status")]
        public Renderer[] Lamps;
        public Light LampLight;
        public Color DeniedColor = new Color(1f, .12f, .08f);
        public Color GrantedColor = new Color(.15f, 1f, .35f);

        [Header("Sound")]
        public AudioClip AcceptSfx;

        bool m_Tapping;
        bool m_Processing;
        float m_DeniedTime = -10f;

        /// <summary>Whether it still has a sealed door to open.</summary>
        public bool IsSealed
        {
            get
            {
                foreach (var door in Doors)
                    if (door && door.IsLocked && !door.HasBeenOpened) return true;
                return false;
            }
        }

        public override bool IsAvailable => !m_Tapping && IsSealed;

        public override string PromptAction(KeycardInteractor user) =>
            user.Inventory.Has(RequiredCard) ? "USE " + RequiredCard.RichName : "LOCKED";

        public override string PromptDetail(KeycardInteractor user) =>
            user.Inventory.Has(RequiredCard) ? "ACCESS MACHINE" : "REQUIRES " + RequiredCard.RichName;

        void Awake()
        {
            if (!FocusPoint) FocusPoint = Reader;
        }

        public override void Interact(KeycardInteractor user)
        {
            if (!user.Inventory.Has(RequiredCard))
            {
                m_DeniedTime = Time.time;
                PlayAt(DeniedSfx, FocusPosition);
                ShowMessage("ACCESS DENIED - " + RequiredCard.DisplayName + " REQUIRED");
                return;
            }
            m_Tapping = user.Tap(RequiredCard, Reader ? Reader : transform, Grant);
        }

        void Grant() => StartCoroutine(GrantWhenReady());

        // "Processing credentials": the doors stay shut a moment while the room behind them is drawn early
        // (ZoneStreamer), so it is ready by the time it is revealed.
        IEnumerator GrantWhenReady()
        {
            m_Processing = true;
            ShowMessage("PROCESSING CREDENTIALS...");
            yield return ZoneStreamer.OpenWhenReady(Doors);
            m_Processing = false;
            m_Tapping = false;
            PlayAt(AcceptSfx, FocusPosition);
            ShowMessage("ACCESS GRANTED");
        }

        void Update()
        {
            if (Time.time - m_DeniedTime < .6f)
                StatusLamps.Set(Lamps, LampLight, DeniedColor, 6f * StatusLamps.Pulse(18f));
            else if (m_Processing)
                StatusLamps.Set(Lamps, LampLight, RequiredCard ? RequiredCard.Tint : GrantedColor, 5f * StatusLamps.Pulse(9f));
            else if (IsSealed)
                StatusLamps.Set(Lamps, LampLight, RequiredCard ? RequiredCard.Tint : DeniedColor, 3.5f * StatusLamps.Pulse(2f));
            else
                StatusLamps.Set(Lamps, LampLight, GrantedColor, 3f);
        }
    }
}
