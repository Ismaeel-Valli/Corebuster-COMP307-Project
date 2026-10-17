using System;
using System.Collections;
using System.Collections.Generic;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Lets the player use the keycard loop's terminals and access machines with Interact, and plays the
    /// first-person floppy disk moves: holding a new disk up to look it over before pocketing it, tapping one
    /// against an access machine, and slotting the whole set into the Master terminal. The gun is lowered out of
    /// view and the player is locked in place (and invincible) for each move, as in a core execution.
    /// Core executions take priority over it, as they share the Interact key.
    /// </summary>
    [RequireComponent(typeof(PlayerCharacterController), typeof(PlayerWeaponsManager), typeof(KeycardInventory))]
    public class KeycardInteractor : MonoBehaviour
    {
        [Header("Targeting")]
        [Tooltip("How far off the crosshair a terminal can be.")]
        [Range(1f, 60f)] public float MaxAimAngle = 30f;
        [Tooltip("Geometry that blocks the view of a terminal.")]
        public LayerMask OcclusionMask = ~0;
        public string InteractActionName = "Player/Interact";

        [Header("Held Disk (weapon socket space)")]
        [Tooltip("Where a disk is brought up from, below the view.")]
        public Vector3 OffscreenPosition = new Vector3(.16f, -.42f, .3f);
        [Tooltip("Held up in front of the face to be looked over.")]
        public Vector3 InspectPosition = new Vector3(0f, -.015f, .3f);
        [Tooltip("Ready in the right hand, low in the view.")]
        public Vector3 HandPosition = new Vector3(.1f, -.12f, .36f);
        [Tooltip("Where it leaves the view once put away (a left pocket).")]
        public Vector3 PocketPosition = new Vector3(-.24f, -.46f, .22f);
        [Tooltip("Rotation of a disk held up with its label to the camera, shutter at the top.")]
        public Vector3 FaceEuler = new Vector3(0f, 270f, 288f);

        [Header("Timing")]
        public float LowerDuration = .22f;
        public float RiseDuration = .35f;
        [Tooltip("How long a new disk is turned over in front of the face.")]
        public float InspectDuration = 1.35f;
        public float PocketDuration = .3f;
        [Tooltip("Turning to face (and stepping up to) a machine.")]
        public float TurnDuration = .35f;
        public float TapDuration = .13f;
        public float InsertDuration = .38f;
        [Tooltip("How close the player steps up to an access machine to tap it.")]
        public float TapReach = .85f;

        [Header("Sound")]
        public AudioClip PickupSfx;
        public AudioClip PocketSfx;
        public AudioClip TapSfx;
        public AudioClip InsertSfx;
        [Range(0f, 1f)] public float SfxVolume = 1f;

        /// <summary>The interactable Interact would use now; null when there is none or the player is busy.</summary>
        public KeycardInteractable Focus { get; private set; }
        /// <summary>Playing a disk move; the player can't move, shoot or interact.</summary>
        public bool IsBusy { get; private set; }
        public KeycardInventory Inventory { get; private set; }
        public string PromptKey { get; private set; } = "E";
        /// <summary>0..1 through holding Interact on a hold-to-use focus.</summary>
        public float HoldProgress { get; private set; }

        public UnityAction<KeycardData> OnInspected;

        PlayerCharacterController m_Character;
        PlayerWeaponsManager m_Weapons;
        PlayerInputHandler m_Input;
        CoreExecutioner m_Executioner;
        Disassembler m_Disassembler;
        CharacterController m_Controller;
        Health m_Health;
        Camera m_Camera;
        Transform m_Holder;
        InputAction m_Interact;
        AudioSource m_Audio;
        int m_WeaponLayer;
        readonly RaycastHit[] m_Hits = new RaycastHit[16];

        // Sequence state, kept so it can be restored if a sequence is cut short.
        float m_Pitch;
        bool m_WasInvincible;
        Vector3 m_SocketRest;
        readonly List<GameObject> m_Props = new List<GameObject>();

        // The hold-to-use focus the current hold started on, and how long it has been held.
        KeycardInteractable m_HoldTarget;
        float m_HoldTime;

        void Awake()
        {
            m_Character = GetComponent<PlayerCharacterController>();
            m_Weapons = GetComponent<PlayerWeaponsManager>();
            m_Input = GetComponent<PlayerInputHandler>();
            m_Executioner = GetComponent<CoreExecutioner>();
            m_Disassembler = GetComponent<Disassembler>();
            m_Controller = GetComponent<CharacterController>();
            m_Health = GetComponent<Health>();
            Inventory = GetComponent<KeycardInventory>();
            m_Audio = gameObject.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;
            m_Audio.spatialBlend = 0f;
        }

        void Start()
        {
            m_Camera = m_Character.PlayerCamera;
            m_Holder = m_Weapons.DefaultWeaponPosition ? m_Weapons.DefaultWeaponPosition.parent
                : m_Camera ? m_Camera.transform : transform;
            m_WeaponLayer = Mathf.RoundToInt(Mathf.Log(Mathf.Max(1, m_Weapons.FpsWeaponLayer.value), 2));

            if (InputSystem.actions != null) m_Interact = InputSystem.actions.FindAction(InteractActionName);
            if (m_Interact != null)
            {
                m_Interact.Enable();
                string key = m_Interact.GetBindingDisplayString(InputBinding.MaskByGroup("Keyboard&Mouse"));
                if (!string.IsNullOrEmpty(key)) PromptKey = key;
            }
            if (FindAnyObjectByType<AudioManager>())
                m_Audio.outputAudioMixerGroup = AudioUtility.GetAudioGroup(AudioUtility.AudioGroups.Pickup);
        }

        void OnDisable()
        {
            if (!IsBusy) return;
            StopAllCoroutines();
            EndSequence();
        }

        void Update()
        {
            Focus = FindFocus();
            if (!Focus || Focus.HoldDuration <= 0f)
            {
                ResetHold();
                if (Focus && InteractPressed()) Focus.Interact(this);
                return;
            }

            // A hold only counts if it started on this focus, not with the key already down from something else.
            if (m_HoldTarget != Focus)
            {
                ResetHold();
                if (InteractPressed()) m_HoldTarget = Focus;
            }
            if (m_HoldTarget != Focus) return;
            if (!InteractHeld())
            {
                ResetHold();
                return;
            }
            m_HoldTime += Time.deltaTime;
            HoldProgress = Mathf.Clamp01(m_HoldTime / Focus.HoldDuration);
            if (m_HoldTime < Focus.HoldDuration) return;
            var target = Focus;
            ResetHold();
            target.Interact(this);
        }

        void ResetHold()
        {
            m_HoldTarget = null;
            m_HoldTime = 0f;
            HoldProgress = 0f;
        }

        bool InteractPressed()
        {
            if (m_Input && !m_Input.CanProcessInput()) return false;
            if (m_Interact != null) return m_Interact.WasPressedThisFrame();
            return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
        }

        bool InteractHeld()
        {
            if (m_Input && !m_Input.CanProcessInput()) return false;
            if (m_Interact != null) return m_Interact.IsPressed();
            return Keyboard.current != null && Keyboard.current.eKey.isPressed;
        }

        KeycardInteractable FindFocus()
        {
            if (IsBusy || !m_Camera || m_Character.IsDead) return null;
            if (m_Executioner && (m_Executioner.IsExecuting || m_Executioner.Candidate)) return null;
            if (m_Disassembler && m_Disassembler.IsFiring) return null;

            Transform eye = m_Camera.transform;
            KeycardInteractable best = null;
            float bestAngle = MaxAimAngle;
            foreach (var interactable in KeycardInteractable.All)
            {
                if (!interactable || !interactable.IsAvailable) continue;
                Vector3 toFocus = interactable.FocusPosition - eye.position;
                float distance = toFocus.magnitude;
                if (distance > interactable.Range || distance < .05f) continue;
                float angle = Vector3.Angle(eye.forward, toFocus);
                if (angle > bestAngle || IsOccluded(eye.position, toFocus / distance, distance, interactable)) continue;
                best = interactable;
                bestAngle = angle;
            }
            return best;
        }

        bool IsOccluded(Vector3 origin, Vector3 direction, float distance, KeycardInteractable target)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, m_Hits, distance - .05f, OcclusionMask,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform hit = m_Hits[i].collider.transform;
                if (hit.IsChildOf(transform) || hit.IsChildOf(target.transform)) continue;
                // The disk a terminal has just ejected is part of the terminal.
                if (hit.GetComponentInParent<KeycardInteractable>() == target) continue;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ moves

        /// <summary>Holds a newly taken disk up to look it over, then pockets it; the card is added as it is pocketed.</summary>
        public bool Inspect(KeycardData card, Action onPocketed = null)
        {
            if (IsBusy || !card || !m_Camera) return false;
            StartCoroutine(InspectSequence(card, onPocketed));
            return true;
        }

        /// <summary>
        /// Steps up to an access machine and taps the disk against its reader. The reader's forward must point out
        /// of its face. onTap runs the moment the disk touches.
        /// </summary>
        public bool Tap(KeycardData card, Transform reader, Action onTap)
        {
            if (IsBusy || !card || !reader || !m_Camera) return false;
            StartCoroutine(TapSequence(card, reader, onTap));
            return true;
        }

        /// <summary>
        /// Slots each card into its slot in turn (a slot's forward points into the terminal). onInserted(i) runs
        /// as card i seats; the disks are left in the slots and returned through onFinished.
        /// </summary>
        public bool Insert(IList<KeycardData> cards, IList<Transform> slots, Transform lookAt, Action<int, GameObject> onInserted,
            Action onFinished)
        {
            if (IsBusy || cards == null || slots == null || cards.Count > slots.Count || !m_Camera) return false;
            StartCoroutine(InsertSequence(cards, slots, lookAt, onInserted, onFinished));
            return true;
        }

        IEnumerator InspectSequence(KeycardData card, Action onPocketed)
        {
            BeginSequence();
            try
            {
                Play(PickupSfx);
                var disk = SpawnHeld(card);
                Quaternion face = Quaternion.Euler(FaceEuler);
                Quaternion from = face * Quaternion.Euler(0f, 0f, 35f) * Quaternion.Euler(40f, 0f, 0f);

                // --- Lower the gun while the disk comes up into view ---
                for (float t = 0f; t < RiseDuration; t += Time.deltaTime)
                {
                    float k = EaseOutCubic(t / RiseDuration);
                    LowerWeapon(Mathf.Clamp01(t / LowerDuration));
                    Pose(disk, Vector3.Lerp(OffscreenPosition, InspectPosition, k), Quaternion.Slerp(from, face, k));
                    yield return null;
                }
                LowerWeapon(1f);

                // --- Look it over: turn it round to the back and return to the label, bobbing gently ---
                for (float t = 0f; t < InspectDuration; t += Time.deltaTime)
                {
                    float k = t / InspectDuration;
                    float turn = Mathf.Sin(Smooth(k) * Mathf.PI) * 150f;
                    float tilt = Mathf.Sin(k * Mathf.PI * 2f) * 8f;
                    Vector3 bob = new Vector3(Mathf.Sin(t * 2.3f) * .006f, Mathf.Sin(t * 3.1f) * .005f, 0f);
                    Pose(disk, InspectPosition + bob, Quaternion.Euler(tilt, turn, 0f) * face);
                    yield return null;
                }

                // --- Pocket it ---
                Vector3 start = disk.transform.localPosition;
                Quaternion startRotation = disk.transform.localRotation;
                Quaternion pocketed = face * Quaternion.Euler(70f, 0f, -30f);
                for (float t = 0f; t < PocketDuration; t += Time.deltaTime)
                {
                    float k = EaseInCubic(t / PocketDuration);
                    Pose(disk, Vector3.Lerp(start, PocketPosition, k), Quaternion.Slerp(startRotation, pocketed, k));
                    yield return null;
                }
                Play(PocketSfx);
                DestroyProp(disk);
                onPocketed?.Invoke();
                OnInspected?.Invoke(card);

                yield return RaiseWeapon();
            }
            finally
            {
                if (IsBusy) EndSequence();
            }
        }

        IEnumerator TapSequence(KeycardData card, Transform reader, Action onTap)
        {
            BeginSequence();
            try
            {
                var disk = SpawnHeld(card);
                Quaternion ready = Quaternion.Euler(FaceEuler) * Quaternion.Euler(0f, 0f, 20f);
                Vector3 stand = reader.position + Vector3.ProjectOnPlane(reader.forward, Vector3.up).normalized * TapReach;
                stand.y = transform.position.y;
                Vector3 startPosition = transform.position;
                bool step = Vector3.ProjectOnPlane(startPosition - stand, Vector3.up).magnitude > .15f;

                // --- Step up to the machine and face its reader, bringing the disk up ---
                Quaternion startYaw = transform.rotation;
                float startPitch = m_Pitch;
                for (float t = 0f; t < TurnDuration; t += Time.deltaTime)
                {
                    float k = Smooth(t / TurnDuration);
                    if (step) MoveTo(Vector3.Lerp(startPosition, stand, k));
                    Aim(reader.position, startYaw, startPitch, k);
                    LowerWeapon(Mathf.Clamp01(t / LowerDuration));
                    Pose(disk, Vector3.Lerp(OffscreenPosition, HandPosition, EaseOutCubic(k)), ready);
                    yield return null;
                }
                Aim(reader.position, startYaw, startPitch, 1f);
                LowerWeapon(1f);

                // --- Tap it flat against the reader, twice ---
                Vector3 normal = m_Holder.InverseTransformDirection(reader.forward);
                Quaternion flat = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.up, normal).normalized, normal);
                Vector3 contact = m_Holder.InverseTransformPoint(reader.position + reader.forward * .012f);
                Vector3 hover = contact + normal * .07f;
                for (float t = 0f; t < TapDuration * 1.6f; t += Time.deltaTime)
                {
                    float k = EaseOutCubic(t / (TapDuration * 1.6f));
                    Pose(disk, Vector3.Lerp(HandPosition, hover, k), Quaternion.Slerp(ready, flat, k));
                    yield return null;
                }
                for (int tap = 0; tap < 2; tap++)
                {
                    for (float t = 0f; t < TapDuration; t += Time.deltaTime)
                    {
                        float k = Mathf.Sin(t / TapDuration * Mathf.PI);
                        Pose(disk, Vector3.Lerp(hover, contact, k), flat);
                        yield return null;
                    }
                    Pose(disk, hover, flat);
                    Play(TapSfx, .7f);
                    if (tap == 0) onTap?.Invoke();
                    yield return new WaitForSeconds(.05f);
                }
                yield return new WaitForSeconds(.2f);

                // --- Put it away ---
                Quaternion pocketed = ready * Quaternion.Euler(70f, 0f, -30f);
                for (float t = 0f; t < PocketDuration; t += Time.deltaTime)
                {
                    float k = EaseInCubic(t / PocketDuration);
                    Pose(disk, Vector3.Lerp(hover, PocketPosition, k), Quaternion.Slerp(flat, pocketed, k));
                    yield return null;
                }
                Play(PocketSfx);
                DestroyProp(disk);
                yield return RaiseWeapon();
            }
            finally
            {
                if (IsBusy) EndSequence();
            }
        }

        IEnumerator InsertSequence(IList<KeycardData> cards, IList<Transform> slots, Transform lookAt,
            Action<int, GameObject> onInserted, Action onFinished)
        {
            BeginSequence();
            try
            {
                Vector3 focus = lookAt ? lookAt.position : slots[0].position;
                Quaternion startYaw = transform.rotation;
                float startPitch = m_Pitch;
                for (float t = 0f; t < TurnDuration; t += Time.deltaTime)
                {
                    float k = Smooth(t / TurnDuration);
                    Aim(focus, startYaw, startPitch, k);
                    LowerWeapon(Mathf.Clamp01(t / LowerDuration));
                    yield return null;
                }
                Aim(focus, startYaw, startPitch, 1f);
                LowerWeapon(1f);

                Quaternion ready = Quaternion.Euler(FaceEuler) * Quaternion.Euler(0f, 0f, 20f);
                for (int i = 0; i < cards.Count; i++)
                {
                    // --- Bring the disk up ---
                    var disk = SpawnHeld(cards[i]);
                    for (float t = 0f; t < RiseDuration * .8f; t += Time.deltaTime)
                    {
                        float k = EaseOutCubic(t / (RiseDuration * .8f));
                        Pose(disk, Vector3.Lerp(OffscreenPosition, HandPosition, k), ready);
                        yield return null;
                    }

                    // --- Hand it over to the world and slide it into its slot ---
                    SetLayer(disk, 0);
                    disk.transform.SetParent(null, true);
                    Transform slot = slots[i];
                    // Standing on its edge, face across the slot, pushed in along the slot's forward.
                    Quaternion seated = slot.rotation * Quaternion.Euler(0f, 0f, 90f);
                    Vector3 mouth = slot.position - slot.forward * .16f;
                    Vector3 seatedPosition = slot.position;
                    Vector3 from = disk.transform.position;
                    Quaternion fromRotation = disk.transform.rotation;
                    for (float t = 0f; t < InsertDuration; t += Time.deltaTime)
                    {
                        float k = Smooth(t / InsertDuration);
                        disk.transform.SetPositionAndRotation(Vector3.Lerp(from, mouth, k),
                            Quaternion.Slerp(fromRotation, seated, Mathf.Clamp01(k * 1.4f)));
                        yield return null;
                    }
                    for (float t = 0f; t < .12f; t += Time.deltaTime)
                    {
                        disk.transform.position = Vector3.Lerp(mouth, seatedPosition, EaseInCubic(t / .12f));
                        yield return null;
                    }
                    disk.transform.SetPositionAndRotation(seatedPosition, seated);
                    disk.transform.SetParent(slot, true);
                    m_Props.Remove(disk);
                    Play(InsertSfx);
                    onInserted?.Invoke(i, disk);
                    yield return new WaitForSeconds(.18f);
                }
                yield return new WaitForSeconds(.35f);
                onFinished?.Invoke();
                yield return RaiseWeapon();
            }
            finally
            {
                if (IsBusy) EndSequence();
            }
        }

        // ------------------------------------------------------------------ sequence helpers

        void BeginSequence()
        {
            IsBusy = true;
            Focus = null;
            m_WasInvincible = m_Health.Invincible;
            m_Health.Invincible = true;
            m_Pitch = m_Character.CameraVerticalAngle;
            m_Character.CharacterVelocity = Vector3.zero;
            m_Character.enabled = false;
            m_Weapons.enabled = false;
            // From the hip, even if the player was aiming down the sights.
            if (m_Weapons.WeaponParentSocket && m_Weapons.DefaultWeaponPosition)
                m_Weapons.WeaponParentSocket.localPosition = m_Weapons.DefaultWeaponPosition.localPosition;
            m_SocketRest = m_Weapons.WeaponParentSocket ? m_Weapons.WeaponParentSocket.localPosition : Vector3.zero;
            m_Weapons.SetFov(m_Weapons.DefaultFov);
        }

        void EndSequence()
        {
            foreach (var prop in m_Props)
                if (prop) Destroy(prop);
            m_Props.Clear();
            if (m_Weapons.WeaponParentSocket) m_Weapons.WeaponParentSocket.localPosition = m_SocketRest;
            m_Character.CameraVerticalAngle = m_Pitch;
            m_Character.CharacterVelocity = Vector3.zero;
            m_Character.enabled = true;
            m_Weapons.enabled = true;
            m_Health.Invincible = m_WasInvincible;
            IsBusy = false;
        }

        IEnumerator RaiseWeapon()
        {
            for (float t = 0f; t < LowerDuration; t += Time.deltaTime)
            {
                LowerWeapon(1f - Smooth(t / LowerDuration));
                yield return null;
            }
            LowerWeapon(0f);
        }

        void LowerWeapon(float k)
        {
            if (!m_Weapons.WeaponParentSocket || !m_Weapons.DownWeaponPosition) return;
            m_Weapons.WeaponParentSocket.localPosition =
                Vector3.Lerp(m_SocketRest, m_Weapons.DownWeaponPosition.localPosition, Smooth(k));
        }

        // Turns the body (yaw) and the view (pitch) from where they started toward a point, k of the way.
        void Aim(Vector3 point, Quaternion startYaw, float startPitch, float k)
        {
            Vector3 eye = m_Camera.transform.position;
            Vector3 flat = Vector3.ProjectOnPlane(point - eye, Vector3.up);
            if (flat.sqrMagnitude > .0001f)
                transform.rotation = Quaternion.Slerp(startYaw, Quaternion.LookRotation(flat), k);
            float pitch = -Mathf.Atan2(point.y - eye.y, Mathf.Max(.05f, flat.magnitude)) * Mathf.Rad2Deg;
            m_Pitch = Mathf.Lerp(startPitch, Mathf.Clamp(pitch, -60f, 60f), k);
            m_Camera.transform.localEulerAngles = new Vector3(m_Pitch, 0f, 0f);
        }

        void MoveTo(Vector3 position)
        {
            Vector3 delta = position - transform.position;
            delta.y = 0f;
            if (m_Controller && m_Controller.enabled) m_Controller.Move(delta);
            else transform.position += delta;
        }

        GameObject SpawnHeld(KeycardData card)
        {
            GameObject disk;
            if (card.Model) disk = Instantiate(card.Model, m_Holder, false);
            else
            {
                disk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                disk.transform.SetParent(m_Holder, false);
                disk.transform.localScale = new Vector3(.09f, .003f, .09f);
            }
            disk.name = card.DisplayName + " (Held)";
            foreach (var collider in disk.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            foreach (var body in disk.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
            foreach (var renderer in disk.GetComponentsInChildren<Renderer>(true))
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetLayer(disk, m_WeaponLayer);
            Pose(disk, OffscreenPosition, Quaternion.Euler(FaceEuler));
            m_Props.Add(disk);
            return disk;
        }

        void DestroyProp(GameObject prop)
        {
            m_Props.Remove(prop);
            Destroy(prop);
        }

        static void SetLayer(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        static void Pose(GameObject disk, Vector3 position, Quaternion rotation)
        {
            disk.transform.localPosition = position;
            disk.transform.localRotation = rotation;
        }

        void Play(AudioClip clip, float volume = 1f)
        {
            if (clip) m_Audio.PlayOneShot(clip, SfxVolume * volume);
        }

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        static float EaseOutCubic(float t)
        {
            t = 1f - Mathf.Clamp01(t);
            return 1f - t * t * t;
        }

        static float EaseInCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t;
        }
    }
}
