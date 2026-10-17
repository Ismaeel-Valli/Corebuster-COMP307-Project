using System.Collections;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// The Disassembler: an emergency sidearm, a cyber blunderbuss worn on the left hip. Each core execution
    /// charges it; once full (ExecutionsForFullCharge) one press quick-draws it gunslinger style, fires a wide
    /// cluster of blue laser bolts from the hip, twirls it and holsters it. Every enemy the bolts catch is
    /// electrocuted: frozen with its core exposed, so it can be executed even with its armour on.
    /// It works alongside the main gun, which stays in the right hand.
    /// </summary>
    [RequireComponent(typeof(PlayerCharacterController), typeof(PlayerWeaponsManager))]
    public class Disassembler : MonoBehaviour
    {
        [Header("Charge")]
        [Min(1)] public int ExecutionsForFullCharge = 3;
        [Tooltip("Start with the Disassembler ready to fire.")]
        public bool StartCharged;

        [Header("Shot")]
        public DisassemblerBolt BoltPrefab;
        [Min(1)] public int BoltCount = 11;
        [Tooltip("Half-angle, in degrees, of the cone the cluster fills.")]
        public float SpreadAngle = 17f;
        [Tooltip("Seconds each enemy caught stays frozen with its core exposed.")]
        public float ShockDuration = 3f;
        public string ActionName = "Player/Disassembler";

        [Header("Gun")]
        [Tooltip("The gun's model; drawn by the weapon camera. Its origin is the grip, its forward the barrel.")]
        public GameObject GunPrefab;
        [Tooltip("Holstered on the left hip, in the first-person socket's space (below the view).")]
        public Vector3 HipPosition = new Vector3(-.34f, -.78f, .12f);
        public Vector3 HipEuler = new Vector3(75f, 10f, 25f);
        [Tooltip("Where it fires from: low on the left of the view, angled in at the crosshair.")]
        public Vector3 FirePosition = new Vector3(-.2f, -.27f, .46f);
        [Tooltip("How far ahead of the eye the barrel is aimed.")]
        public float AimDistance = 12f;
        [Tooltip("Degrees the gun is canted over as it fires, gunslinger style.")]
        public float FireCant = -18f;
        [Tooltip("Degrees the gun is turned off the aim line (pitch, yaw) so it shows its side rather than " +
                 "just its back. The bolts still fly to the crosshair.")]
        public Vector2 AimSkew = new Vector2(6f, 16f);

        [Header("Timing")]
        public float DrawDuration = .11f;
        public float RecoilDuration = .16f;
        public float TwirlDuration = .32f;
        public float HolsterDuration = .16f;
        [Tooltip("Turns the gun spins round the trigger finger before it is holstered.")]
        public float TwirlTurns = 1f;

        [Header("Recoil")]
        public float RecoilKick = 34f;
        public float RecoilPush = .09f;

        [Header("Effects")]
        [Tooltip("Spawned at the muzzle when it fires; rendered by the weapon camera.")]
        public GameObject MuzzleFlashPrefab;
        public Color FlashColor = new Color(.35f, .7f, 1f);
        public float FlashIntensity = 9f;

        [Header("Sound")]
        public AudioClip ChargedSfx;
        public AudioClip DrawSfx;
        public AudioClip FireSfx;
        public AudioClip TwirlSfx;
        public AudioClip HolsterSfx;
        public AudioClip NotChargedSfx;
        [Range(0f, 1f)] public float SfxVolume = 1f;

        /// <summary>Executions banked toward the next shot.</summary>
        public int Charge { get; private set; }
        public float ChargeRatio => Mathf.Clamp01(Charge / (float)ExecutionsForFullCharge);
        public bool IsCharged => Charge >= ExecutionsForFullCharge;
        public bool IsFiring { get; private set; }
        /// <summary>Time the charge last filled up (for HUD flashes).</summary>
        public float ChargedTime { get; private set; } = -100f;
        /// <summary>Time a shot was last attempted without charge.</summary>
        public float DeniedTime { get; private set; } = -100f;
        public string PromptKey { get; private set; } = "F";

        public UnityAction OnCharged;
        public UnityAction OnFired;

        PlayerCharacterController m_Character;
        PlayerWeaponsManager m_Weapons;
        PlayerInputHandler m_Input;
        CoreExecutioner m_Executioner;
        Camera m_Camera;
        InputAction m_Action;
        AudioSource m_Audio;
        Transform m_Socket;
        GameObject m_Gun;
        Transform m_Muzzle;
        Light m_FlashLight;
        int m_WeaponLayer;

        void Awake()
        {
            m_Character = GetComponent<PlayerCharacterController>();
            m_Weapons = GetComponent<PlayerWeaponsManager>();
            m_Input = GetComponent<PlayerInputHandler>();
            m_Executioner = GetComponent<CoreExecutioner>();
            m_Audio = gameObject.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;
            m_Audio.spatialBlend = 0f;
            if (StartCharged) Charge = ExecutionsForFullCharge;
        }

        void Start()
        {
            m_Camera = m_Character.PlayerCamera;
            m_WeaponLayer = Mathf.RoundToInt(Mathf.Log(Mathf.Max(1, m_Weapons.FpsWeaponLayer.value), 2));
            m_Socket = m_Weapons.DefaultWeaponPosition ? m_Weapons.DefaultWeaponPosition.parent
                : m_Camera ? m_Camera.transform : transform;

            if (InputSystem.actions != null) m_Action = InputSystem.actions.FindAction(ActionName);
            if (m_Action != null)
            {
                m_Action.Enable();
                string key = m_Action.GetBindingDisplayString(InputBinding.MaskByGroup("Keyboard&Mouse"));
                if (!string.IsNullOrEmpty(key)) PromptKey = key;
            }
            if (FindAnyObjectByType<AudioManager>())
                m_Audio.outputAudioMixerGroup = AudioUtility.GetAudioGroup(AudioUtility.AudioGroups.WeaponShoot);

            if (m_Executioner) m_Executioner.OnExecutionFinished += OnExecution;
            BuildGun();
        }

        void OnDestroy()
        {
            if (m_Executioner) m_Executioner.OnExecutionFinished -= OnExecution;
        }

        void OnDisable()
        {
            if (!IsFiring) return;
            StopAllCoroutines();
            IsFiring = false;
            if (m_Gun) m_Gun.SetActive(false);
        }

        void OnExecution(CoreExecutionTarget target) => AddCharge(1);

        /// <summary>Banks executions toward the next shot; plays the charged cue when it fills.</summary>
        public void AddCharge(int executions)
        {
            if (executions <= 0 || IsCharged) return;
            Charge = Mathf.Min(ExecutionsForFullCharge, Charge + executions);
            if (!IsCharged) return;
            ChargedTime = Time.time;
            Play(ChargedSfx);
            OnCharged?.Invoke();
        }

        /// <summary>Gives up banked cores (a reactor console's injection takes them); false if there were none.</summary>
        public bool SpendCharge(int executions = 1)
        {
            if (executions <= 0 || Charge <= 0 || IsFiring) return false;
            Charge = Mathf.Max(0, Charge - executions);
            return true;
        }

        void Update()
        {
            if (!Pressed()) return;
            if (IsCharged) TryFire();
            else
            {
                DeniedTime = Time.time;
                Play(NotChargedSfx, .6f);
            }
        }

        bool Pressed()
        {
            if (m_Input && !m_Input.CanProcessInput()) return false;
            if (m_Action != null) return m_Action.WasPressedThisFrame();
            return Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
        }

        /// <summary>Quick-draws and fires if charged and free to (not mid-execution, not dead).</summary>
        public bool TryFire()
        {
            if (!IsCharged || IsFiring || m_Character.IsDead || !m_Camera || !BoltPrefab) return false;
            if (m_Executioner && m_Executioner.IsExecuting) return false;
            StartCoroutine(QuickDraw());
            return true;
        }

        IEnumerator QuickDraw()
        {
            IsFiring = true;
            Charge = 0;
            m_Gun.SetActive(true);
            Quaternion hipRotation = Quaternion.Euler(HipEuler);
            Play(DrawSfx);

            // --- Draw: up off the hip and snapped onto the crosshair ---
            for (float t = 0f; t < DrawDuration; t += Time.deltaTime)
            {
                float k = EaseOutCubic(t / DrawDuration);
                // Swing it up in an arc (out, then in) rather than straight across.
                Vector3 arc = new Vector3(-.08f, 0f, 0f) * Mathf.Sin(k * Mathf.PI);
                Pose(Vector3.Lerp(HipPosition, FirePosition, k) + arc, Quaternion.Slerp(hipRotation, AimRotation(), k));
                yield return null;
            }
            Pose(FirePosition, AimRotation());

            // --- Fire ---
            Fire();
            OnFired?.Invoke();

            // --- Recoil: the barrel kicks up and back, then settles ---
            for (float t = 0f; t < RecoilDuration; t += Time.deltaTime)
            {
                float k = t / RecoilDuration;
                float kick = Mathf.Sin(Mathf.Min(1f, k * 3f) * Mathf.PI * .5f) * (1f - k);
                Pose(FirePosition + Vector3.back * (RecoilPush * kick) + Vector3.up * (RecoilPush * .4f * kick),
                    AimRotation() * Quaternion.Euler(-RecoilKick * kick, 0f, 0f));
                FadeFlash(k);
                yield return null;
            }
            FadeFlash(1f);

            // --- Twirl: spun round the trigger finger while dropping back toward the hip ---
            Play(TwirlSfx);
            Vector3 twirlStart = FirePosition;
            Vector3 twirlEnd = Vector3.Lerp(FirePosition, HipPosition, .45f);
            for (float t = 0f; t < TwirlDuration; t += Time.deltaTime)
            {
                float k = t / TwirlDuration;
                float spin = Smooth(k) * 360f * TwirlTurns;
                Quaternion aim = Quaternion.Slerp(AimRotation(), Quaternion.Euler(20f, 0f, 0f) * AimRotation(), k);
                Pose(Vector3.Lerp(twirlStart, twirlEnd, Smooth(k)), aim * Quaternion.AngleAxis(-spin, Vector3.right));
                yield return null;
            }

            // --- Holster: dropped back into the hip holster ---
            Quaternion from = m_Gun.transform.localRotation;
            for (float t = 0f; t < HolsterDuration; t += Time.deltaTime)
            {
                float k = EaseInCubic(t / HolsterDuration);
                Pose(Vector3.Lerp(twirlEnd, HipPosition, k), Quaternion.Slerp(from, hipRotation, k));
                yield return null;
            }
            Play(HolsterSfx);
            m_Gun.SetActive(false);
            IsFiring = false;
        }

        // The barrel pointed at what the crosshair is on, canted over.
        Quaternion AimRotation()
        {
            Vector3 target = new Vector3(0f, 0f, AimDistance);
            if (m_Socket != m_Camera.transform)
                target = m_Socket.InverseTransformPoint(m_Camera.transform.TransformPoint(target));
            return Quaternion.LookRotation(target - FirePosition) * Quaternion.Euler(AimSkew.x, AimSkew.y, FireCant);
        }

        void Pose(Vector3 position, Quaternion rotation)
        {
            m_Gun.transform.localPosition = position;
            m_Gun.transform.localRotation = rotation;
        }

        void Fire()
        {
            Transform eye = m_Camera.transform;
            Vector3 muzzle = m_Muzzle ? m_Muzzle.position : eye.position;
            Play(FireSfx);

            // A bolt down the middle, the rest in two staggered rings so the cluster covers the cone evenly.
            for (int i = 0; i < BoltCount; i++)
            {
                Vector2 offset = Vector2.zero;
                if (i > 0)
                {
                    int ring = BoltCount > 6 && i % 2 == 0 ? 0 : 1;
                    float angle = (i / (float)(BoltCount - 1)) * 360f + Random.Range(-12f, 12f);
                    float radius = SpreadAngle * (ring == 1 ? Random.Range(.8f, 1f) : Random.Range(.4f, .6f));
                    offset = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * radius;
                    // Wider than tall: the blunderbuss sweeps a crowd rather than the sky and floor.
                    offset.y *= .6f;
                }
                Vector3 direction = eye.rotation * Quaternion.Euler(-offset.y, offset.x, 0f) * Vector3.forward;
                var bolt = Instantiate(BoltPrefab);
                bolt.ShockDuration = ShockDuration;
                bolt.Launch(gameObject, eye.position, direction, muzzle);
            }

            if (MuzzleFlashPrefab && m_Muzzle)
            {
                var flash = Instantiate(MuzzleFlashPrefab, m_Muzzle.position, m_Muzzle.rotation, m_Muzzle);
                foreach (var t in flash.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = m_WeaponLayer;
                Destroy(flash, 1.5f);
            }
            if (m_FlashLight)
            {
                m_FlashLight.enabled = true;
                m_FlashLight.intensity = FlashIntensity;
            }
        }

        void FadeFlash(float k)
        {
            if (!m_FlashLight) return;
            m_FlashLight.intensity = FlashIntensity * (1f - Mathf.Clamp01(k * 2.5f));
            if (k >= 1f) m_FlashLight.enabled = false;
        }

        void BuildGun()
        {
            m_Gun = GunPrefab ? Instantiate(GunPrefab, m_Socket, false) : new GameObject("Disassembler");
            if (!GunPrefab) m_Gun.transform.SetParent(m_Socket, false);
            m_Gun.name = "Disassembler (Left Hip)";
            foreach (var t in m_Gun.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = m_WeaponLayer;
                if (t.name == "Muzzle") m_Muzzle = t;
            }
            foreach (var renderer in m_Gun.GetComponentsInChildren<Renderer>(true))
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Lights the gun and the hand from the muzzle as it fires (the scene too, as the weapon is in it).
            var lightObject = new GameObject("Disassembler Flash");
            lightObject.transform.SetParent(m_Muzzle ? m_Muzzle : m_Gun.transform, false);
            m_FlashLight = lightObject.AddComponent<Light>();
            m_FlashLight.type = LightType.Point;
            m_FlashLight.color = FlashColor;
            m_FlashLight.range = 6f;
            m_FlashLight.shadows = LightShadows.None;
            m_FlashLight.enabled = false;

            Pose(HipPosition, Quaternion.Euler(HipEuler));
            m_Gun.SetActive(false);
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
