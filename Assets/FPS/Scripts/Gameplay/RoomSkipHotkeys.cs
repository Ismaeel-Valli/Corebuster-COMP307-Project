using System;
using System.Collections;
using System.Collections.Generic;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Shortcuts that skip ahead to each keycard room, for testing and demos: Ctrl+P the Workshop (blue card's room),
    /// Ctrl+I the Centre Room (yellow), Ctrl+O the North-West Room (red). The player arrives just inside the room's
    /// entrance, holding every keycard earned before it, with every door those cards open already open and the room
    /// drawn. A skip also ends the tutorial if it is still running (the sidearm is handed over).
    /// Everything is found in the scene by name when a skip is used, so the component needs no wiring.
    /// </summary>
    public class RoomSkipHotkeys : MonoBehaviour
    {
        [Serializable]
        public class RoomSkip
        {
            public string Title;
            [Tooltip("Pressed with Ctrl.")]
            public Key Key;
            [Tooltip("The ZoneStreamer room it lands in.")]
            public string Room;
            [Tooltip("The SecurityDoor the player arrives through.")]
            public string EntranceDoor;
            [Tooltip("Keycards held on arrival, by display name.")]
            public string[] Cards;
            [Tooltip("How far inside the entrance the player lands.")]
            public float Inward = 3.5f;
        }

        public RoomSkip[] Skips =
        {
            new RoomSkip { Title = "WORKSHOP", Key = Key.P, Room = "Workshop", EntranceDoor = "Spawn Room Door (N)",
                Cards = new[] { "BLACK KEYCARD" } },
            new RoomSkip { Title = "CENTRE ROOM", Key = Key.I, Room = "Centre Room", EntranceDoor = "Centre Room Door (S)",
                Cards = new[] { "BLACK KEYCARD", "BLUE KEYCARD" } },
            new RoomSkip { Title = "NORTH-WEST ROOM", Key = Key.O, Room = "North-West Room", EntranceDoor = "North-West Room Door (E)",
                Cards = new[] { "BLACK KEYCARD", "BLUE KEYCARD", "YELLOW KEYCARD" } },
        };

        [Tooltip("Frames the destination room is drawn (behind the player) before the jump, so it arrives loaded.")]
        [Min(1)] public int PrewarmFrames = 4;

        PlayerCharacterController m_Character;
        bool m_Skipping;

        void Awake() => m_Character = GetComponent<PlayerCharacterController>();

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.ctrlKey.isPressed || m_Skipping) return;
            foreach (var skip in Skips)
                if (skip != null && keyboard[skip.Key].wasPressedThisFrame)
                {
                    if (CanSkip(out string reason)) StartCoroutine(SkipTo(skip));
                    else Message(reason);
                    return;
                }
        }

        bool CanSkip(out string reason)
        {
            reason = null;
            if (!m_Character || m_Character.IsDead || Time.timeScale == 0f) reason = "";
            else if (GetComponent<CoreExecutioner>() is CoreExecutioner executioner && executioner.IsExecuting)
                reason = "ROOM SKIP UNAVAILABLE DURING A CORE BUST";
            else if (GetComponent<KeycardInteractor>() is KeycardInteractor interactor && interactor.IsBusy)
                reason = "ROOM SKIP UNAVAILABLE WHILE USING A PANEL";
            else if (FindAnyObjectByType<FinaleDirector>() is FinaleDirector finale && finale.Current != FinaleDirector.Phase.Waiting)
                reason = "ROOM SKIP UNAVAILABLE DURING THE FINALE";
            return reason == null;
        }

        IEnumerator SkipTo(RoomSkip skip)
        {
            m_Skipping = true;
            var tutorial = FindAnyObjectByType<TutorialDirector>();
            if (tutorial) tutorial.Skip();

            // The keycards earned before this room.
            var cards = FindCards();
            var inventory = GetComponent<KeycardInventory>();
            if (!inventory && GetComponent<KeycardInteractor>() is KeycardInteractor user) inventory = user.Inventory;
            var granted = new List<KeycardData>();
            foreach (var name in skip.Cards)
                if (cards.TryGetValue(name, out var card))
                {
                    granted.Add(card);
                    if (inventory) inventory.Add(card);
                }

            // Every door those cards open, and the way in.
            var doors = new List<SecurityDoor>();
            foreach (var point in FindObjectsByType<KeycardAccessPoint>(FindObjectsSortMode.None))
                if (point.RequiredCard && granted.Contains(point.RequiredCard) && point.Doors != null)
                    foreach (var door in point.Doors)
                        if (door && !doors.Contains(door)) doors.Add(door);
            SecurityDoor entrance = null;
            foreach (var door in FindObjectsByType<SecurityDoor>(FindObjectsSortMode.None))
                if (door.name == skip.EntranceDoor) entrance = door;
            if (entrance && !doors.Contains(entrance)) doors.Add(entrance);

            if (!entrance || !Destination(skip, entrance, out Vector3 position, out Quaternion facing))
            {
                Message("ROOM SKIP: " + skip.Title + " NOT FOUND");
                m_Skipping = false;
                yield break;
            }

            // Draw the room behind the doors for a few frames before arriving, then jump and open up.
            if (ZoneStreamer.Instance) ZoneStreamer.Instance.Prewarm(doors, 4f);
            for (int i = 0; i < PrewarmFrames; i++) yield return null;
            Teleport(position, facing);
            foreach (var door in doors) door.Unlock();

            Message($"SKIPPED TO THE {skip.Title}" + (granted.Count > 0 ? "  -  KEYCARDS ISSUED" : ""));
            m_Skipping = false;
        }

        // Just inside the room from its entrance, on the floor, facing in.
        bool Destination(RoomSkip skip, SecurityDoor entrance, out Vector3 position, out Quaternion facing)
        {
            position = default;
            facing = Quaternion.identity;
            Vector3 door = entrance.transform.position;
            Vector3 inward = Vector3.zero;
            if (ZoneStreamer.Instance)
                foreach (var zone in ZoneStreamer.Instance.Rooms)
                    if (zone != null && zone.Name == skip.Room)
                    {
                        Vector2 towards = zone.Area.center - new Vector2(door.x, door.z);
                        inward = Mathf.Abs(towards.x) > Mathf.Abs(towards.y)
                            ? new Vector3(Mathf.Sign(towards.x), 0f, 0f)
                            : new Vector3(0f, 0f, Mathf.Sign(towards.y));
                    }
            if (inward == Vector3.zero) return false;

            Vector3 target = door + inward * skip.Inward;
            if (Physics.Raycast(target + Vector3.up * 2.5f, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                target.y = hit.point.y;
            position = target + Vector3.up * .05f;
            facing = Quaternion.LookRotation(inward, Vector3.up);
            return true;
        }

        void Teleport(Vector3 position, Quaternion facing)
        {
            var controller = GetComponent<CharacterController>();
            if (controller) controller.enabled = false;
            transform.SetPositionAndRotation(position, facing);
            m_Character.CharacterVelocity = Vector3.zero;
            if (controller) controller.enabled = true;
            Physics.SyncTransforms();
        }

        // Every keycard the level uses, by display name: the access machines' cards and the terminals' rewards.
        static Dictionary<string, KeycardData> FindCards()
        {
            var cards = new Dictionary<string, KeycardData>();
            void Add(KeycardData card)
            {
                if (card && !cards.ContainsKey(card.DisplayName)) cards.Add(card.DisplayName, card);
            }
            foreach (var point in FindObjectsByType<KeycardAccessPoint>(FindObjectsSortMode.None)) Add(point.RequiredCard);
            foreach (var terminal in FindObjectsByType<KeycardTerminal>(FindObjectsSortMode.None)) Add(terminal.Reward);
            foreach (var master in FindObjectsByType<MasterTerminal>(FindObjectsSortMode.None))
                if (master.RequiredCards != null)
                    foreach (var card in master.RequiredCards) Add(card);
            return cards;
        }

        static void Message(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            DisplayMessageEvent evt = Events.DisplayMessageEvent;
            evt.Message = text;
            evt.DelayBeforeDisplay = 0f;
            EventManager.Broadcast(evt);
        }
    }
}
