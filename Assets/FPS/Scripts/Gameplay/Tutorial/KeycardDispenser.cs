using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// An officer's clearance terminal: once armed it ejects a keycard from its drive when accessed, and the player
    /// takes it (looking the disk over as from a security terminal). There is no lockdown; the tutorial uses it to
    /// hand out the starter card. Its screens show one state object each.
    /// </summary>
    public class KeycardDispenser : KeycardInteractable
    {
        public enum State { Standby, Ready, Ejected, Taken }

        public KeycardData Card;
        [Tooltip("Starts ready; otherwise it stays on standby until Arm is called.")]
        public bool StartArmed;

        [Header("Drive")]
        [Tooltip("The drive slot the disk pops out of; its forward points out of the terminal.")]
        public Transform EjectSlot;
        public Vector3 EjectEuler = new Vector3(0f, -90f, 0f);
        public float EjectDistance = .05f;
        public float EjectDuration = .45f;

        [Header("Wording")]
        public string StandbyAction = "CLEARANCE PENDING";
        public string StandbyDetail = "COMPLETE THE COMBAT DRILL FIRST";
        public string ReadyAction = "REQUEST CLEARANCE";
        public string ReadyDetail = "OFFICER CLEARANCE TERMINAL";
        public string StandbyMessage = "CLEARANCE PENDING - FINISH THE COMBAT DRILL";

        [Header("Screens (one shown per state: standby, ready, ejected, taken)")]
        public GameObject[] StateScreens = new GameObject[4];

        [Header("Status")]
        public Renderer[] Lamps;
        public Light LampLight;
        public Color StandbyColor = new Color(1f, .45f, .1f);
        public Color IssuedColor = new Color(.15f, 1f, .35f);

        [Header("Sound")]
        public AudioClip AcceptSfx;
        public AudioClip EjectSfx;

        public State Current { get; private set; }
        public UnityAction OnTaken;

        GameObject m_Disk;
        Light m_DiskLight;

        // Not while the disk is still popping out of the drive.
        public override bool IsAvailable =>
            Current == State.Standby || Current == State.Ready || (Current == State.Ejected && m_Disk != null);

        public override string PromptAction(KeycardInteractor user) => Current switch
        {
            State.Standby => StandbyAction,
            State.Ejected => "TAKE " + Card.RichName,
            _ => ReadyAction,
        };

        public override string PromptDetail(KeycardInteractor user) => Current switch
        {
            State.Standby => StandbyDetail,
            State.Ejected => "SECURITY KEYCARD",
            _ => ReadyDetail,
        };

        void Awake()
        {
            if (!FocusPoint) FocusPoint = EjectSlot;
            Current = StartArmed ? State.Ready : State.Standby;
            ShowScreen();
        }

        /// <summary>Brings it out of standby, ready to issue its card.</summary>
        public void Arm()
        {
            if (Current != State.Standby) return;
            Current = State.Ready;
            PlayAt(AcceptSfx, FocusPosition, .7f);
            ShowScreen();
        }

        public override void Interact(KeycardInteractor user)
        {
            switch (Current)
            {
                case State.Standby:
                    PlayAt(DeniedSfx, FocusPosition);
                    ShowMessage(StandbyMessage);
                    break;
                case State.Ready:
                    StartCoroutine(Eject());
                    break;
                case State.Ejected:
                    if (!m_Disk) return;
                    if (!user.Inspect(Card, () =>
                        {
                            user.Inventory.Add(Card);
                            ShowMessage(Card.DisplayName + " ACQUIRED");
                            OnTaken?.Invoke();
                        }))
                        return;
                    Current = State.Taken;
                    Destroy(m_Disk);
                    if (m_DiskLight) Destroy(m_DiskLight.gameObject);
                    ShowScreen();
                    break;
            }
        }

        IEnumerator Eject()
        {
            Current = State.Ejected;
            ShowScreen();
            PlayAt(AcceptSfx, FocusPosition);
            Transform slot = EjectSlot ? EjectSlot : transform;
            var disk = Card && Card.Model ? Instantiate(Card.Model, slot, false) : GameObject.CreatePrimitive(PrimitiveType.Cube);
            if (!Card || !Card.Model)
            {
                disk.transform.SetParent(slot, false);
                disk.transform.localScale = new Vector3(.09f, .003f, .09f);
            }
            disk.name = "Ejected Disk";
            if (!disk.GetComponentInChildren<Collider>())
            {
                var box = disk.AddComponent<BoxCollider>();
                box.size = new Vector3(.16f, .02f, .15f);
            }

            var glow = new GameObject("Disk Glow");
            glow.transform.SetParent(slot, false);
            glow.transform.localPosition = new Vector3(0f, .12f, .1f);
            m_DiskLight = glow.AddComponent<Light>();
            m_DiskLight.type = LightType.Point;
            m_DiskLight.range = 1.6f;
            m_DiskLight.shadows = LightShadows.None;
            m_DiskLight.color = Card ? Card.Tint : Color.white;

            yield return new WaitForSeconds(.25f);
            PlayAt(EjectSfx, slot.position);
            // Pops out of the slot like a disk from a drive, with a small overshoot (as KeycardTerminal).
            disk.transform.localRotation = Quaternion.Euler(EjectEuler);
            Vector3 inside = new Vector3(0f, 0f, -.12f), outside = new Vector3(0f, 0f, EjectDistance);
            for (float t = 0f; t < EjectDuration; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / EjectDuration) - 1f;
                k = k * k * (2.4f * k + 1.4f) + 1f;
                disk.transform.localPosition = Vector3.LerpUnclamped(inside, outside, k);
                yield return null;
            }
            disk.transform.localPosition = outside;
            m_Disk = disk;
            ShowMessage((Card ? Card.DisplayName : "KEYCARD") + " ISSUED");
        }

        void ShowScreen()
        {
            if (StateScreens == null) return;
            for (int i = 0; i < StateScreens.Length; i++)
                if (StateScreens[i]) StateScreens[i].SetActive(i == (int)Current);
        }

        void Update()
        {
            Color tint = Card ? Card.Tint : Color.white;
            switch (Current)
            {
                case State.Standby:
                    StatusLamps.Set(Lamps, LampLight, StandbyColor, 2f);
                    break;
                case State.Ready:
                    StatusLamps.Set(Lamps, LampLight, tint, 5f * StatusLamps.Pulse(2.5f), 2.5f);
                    break;
                case State.Ejected:
                    float pulse = StatusLamps.Pulse(4f);
                    StatusLamps.Set(Lamps, LampLight, tint, 5f * pulse, 2.5f);
                    if (m_DiskLight) m_DiskLight.intensity = 1.5f * pulse;
                    break;
                default:
                    StatusLamps.Set(Lamps, LampLight, IssuedColor, 2.5f);
                    break;
            }
        }
    }
}
