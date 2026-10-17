using System.Collections;
using System.Collections.Generic;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Doom-style core execution, staged off to the right like a gut punch: the player steps in so the enemy's
    /// core sits just ahead of the gun's muzzle at the lower right of the view, tucks the gun in at the hip, jabs
    /// it straight into the core along the barrel, holds it there while the core is sucked in (the victim doubling
    /// over onto the player's right shoulder), then wrenches it free as the enemy dies. The gun never swings
    /// across the view. The player is invincible for the whole sequence and is healed by the absorbed core.
    /// </summary>
    [RequireComponent(typeof(PlayerCharacterController), typeof(PlayerWeaponsManager), typeof(Health))]
    public class CoreExecutioner : MonoBehaviour
    {
        [Header("Targeting")]
        [Tooltip("Furthest the exposed core can be from the camera to start an execution.")]
        public float MaxDistance = 3.2f;
        [Tooltip("How far off the crosshair the core can be.")]
        [Range(1f, 90f)] public float MaxAimAngle = 30f;
        [Tooltip("Geometry that blocks the view of a core.")]
        public LayerMask OcclusionMask = ~0;
        public string InteractActionName = "Player/Interact";

        [Header("Staging")]
        [Tooltip("Where the gun is pulled in to (from its hip pose, in its parent's space) for a close jab.")]
        public Vector3 GunTuck = new Vector3(.03f, .05f, -.34f);
        [Tooltip("How far along the barrel the core sits ahead of the tucked muzzle; the jab covers this distance.")]
        [Min(.05f)] public float ShoveDepth = .16f;
        [Tooltip("How far the view dips, bracing into the punch.")]
        public float CameraDip = 0f;
        [Tooltip("Degrees the view rolls toward the victim on the right (negative rolls right).")]
        public float CameraLean = -3f;
        [Tooltip("Pitch range the view may take while lining the core up with the gun (up, down).")]
        public Vector2 PitchLimits = new Vector2(-30f, 35f);
        [Tooltip("How quickly the view follows the victim's swaying chest once lined up.")]
        public float TrackSharpness = 12f;

        [Header("Timing")]
        public float AlignDuration = .3f;
        public float WindupDuration = .2f;
        public float ShoveDuration = .09f;
        public float AbsorbDuration = 1.7f;
        public float RipDuration = .22f;
        public float RecoverDuration = .45f;

        [Header("Gun Animation")]
        [Tooltip("Offset and rotation (from the tucked pose) the gun is drawn back to before the jab.")]
        public Vector3 WindupOffset = new Vector3(.03f, -.04f, -.14f);
        public Vector3 WindupEuler = new Vector3(-6f, 4f, -10f);
        [Tooltip("How far into the core the muzzle is driven (1 = muzzle right at the core's centre).")]
        [Range(0f, 1.2f)] public float ShoveReach = .95f;
        [Tooltip("Degrees the gun is wrenched back and forth while it drains the core.")]
        public float WrenchRoll = 8f;
        [Tooltip("How far the gun draws back out of the chest as the core comes loose.")]
        public float AbsorbPullBack = .1f;
        [Tooltip("Offset and rotation (from the hip pose) the gun is yanked to when the core is torn out.")]
        public Vector3 RipOffset = new Vector3(.05f, -.1f, -.22f);
        public Vector3 RipEuler = new Vector3(-12f, 10f, -8f);

        [Header("Camera")]
        public float ImpactShake = .06f;
        public float AbsorbShake = .012f;
        public float RipShake = .05f;
        [Tooltip("Field-of-view change while the core is drained (negative zooms in).")]
        public float AbsorbFovDelta = -4f;
        public float ImpactFovKick = 5f;

        [Header("Muzzle")]
        [Tooltip("Particles converging into the barrel while it absorbs; rendered on the first-person weapon layer.")]
        public GameObject MuzzleChargeVfx;
        public Color MuzzleLightColor = new Color(.35f, .7f, 1f);
        public float MuzzleLightIntensity = 7f;

        [Header("Sound")]
        public AudioClip ImpaleSfx;
        public AudioClip AbsorbSfx;
        public AudioClip RipSfx;
        public AudioClip HealSfx;
        [Range(0f, 1f)] public float SfxVolume = 1f;

        [Header("Reward")]
        [Tooltip("Health gained when the target doesn't set its own reward.")]
        public float FallbackHealthReward = 40f;

        public bool IsExecuting { get; private set; }
        /// <summary>The core the player would execute if they pressed Interact now.</summary>
        public CoreExecutionTarget Candidate { get; private set; }
        public string PromptKey { get; private set; } = "E";

        public UnityAction<CoreExecutionTarget> OnExecutionStarted;
        public UnityAction<CoreExecutionTarget> OnExecutionFinished;

        PlayerCharacterController m_Character;
        PlayerWeaponsManager m_Weapons;
        PlayerInputHandler m_Input;
        CharacterController m_Controller;
        Health m_Health;
        Camera m_Camera;
        InputAction m_Interact;
        AudioSource m_Loop;
        AudioSource m_OneShots;
        readonly RaycastHit[] m_Hits = new RaycastHit[16];

        // Sequence state, kept on the component so it can be restored if the sequence is cut short.
        WeaponController m_Weapon;
        Transform m_Gun;
        Transform m_Muzzle;
        Vector3 m_GunRestPosition;
        Quaternion m_GunRestRotation;
        Vector3 m_GunTuckPosition;
        Vector3 m_CameraRestPosition;
        float m_BaseFov;
        float m_Pitch;
        bool m_WasInvincible;
        float m_Trauma;
        float m_ContinuousShake;
        float m_FovKick;
        float m_FovOffset;
        float m_Stage;
        float m_ShakeSeed;
        Vector3 m_CoreInView;
        Vector3 m_Approach;
        Transform m_CoreSocket;
        Vector3 m_CoreSocketOffset;
        readonly List<Collider> m_IgnoredColliders = new List<Collider>();
        Light m_MuzzleLight;
        GameObject m_MuzzleVfx;
        ParticleSystem[] m_MuzzleParticles;

        void Awake()
        {
            m_Character = GetComponent<PlayerCharacterController>();
            m_Weapons = GetComponent<PlayerWeaponsManager>();
            m_Input = GetComponent<PlayerInputHandler>();
            m_Controller = GetComponent<CharacterController>();
            m_Health = GetComponent<Health>();
            m_ShakeSeed = Random.Range(0f, 100f);

            m_Loop = gameObject.AddComponent<AudioSource>();
            m_Loop.playOnAwake = false;
            m_Loop.spatialBlend = 0f;
            m_OneShots = gameObject.AddComponent<AudioSource>();
            m_OneShots.playOnAwake = false;
            m_OneShots.spatialBlend = 0f;
        }

        void Start()
        {
            m_Camera = m_Character.PlayerCamera;

            if (InputSystem.actions != null) m_Interact = InputSystem.actions.FindAction(InteractActionName);
            if (m_Interact != null)
            {
                m_Interact.Enable();
                string key = m_Interact.GetBindingDisplayString(InputBinding.MaskByGroup("Keyboard&Mouse"));
                if (!string.IsNullOrEmpty(key)) PromptKey = key;
            }

            if (FindAnyObjectByType<AudioManager>())
            {
                var group = AudioUtility.GetAudioGroup(AudioUtility.AudioGroups.WeaponShoot);
                m_Loop.outputAudioMixerGroup = group;
                m_OneShots.outputAudioMixerGroup = group;
            }
        }

        void OnDisable()
        {
            if (IsExecuting)
            {
                StopAllCoroutines();
                EndSequence();
            }
        }

        void Update()
        {
            if (IsExecuting) return;

            Candidate = FindCandidate();
            if (Candidate && InteractPressed())
                TryExecute(Candidate);
        }

        /// <summary>Starts an execution on the target if it can be executed right now (skips range and aim checks).</summary>
        public bool TryExecute(CoreExecutionTarget target)
        {
            if (IsExecuting || !target || !target.CanBeExecuted || m_Character.IsDead) return false;
            WeaponController weapon = m_Weapons.GetActiveWeapon();
            if (!weapon || !m_Camera) return false;
            StartCoroutine(Execute(target));
            return true;
        }

        /// <summary>
        /// The execution in reverse, for the Boss Hall's reactor consoles: the same step-in, wind-up and jab, but into
        /// the console's slot, and then the gun drives its banked cores out into it one at a time instead of drawing
        /// one in. onCoreInjected(i) fires as each core leaves the barrel; onFinished once the gun is back at the hip
        /// (not if the sequence is cut short).
        /// </summary>
        public bool TryInject(Transform slot, int cores, Material coreMaterial, System.Action<int> onCoreInjected,
            System.Action onFinished)
        {
            if (IsExecuting || !slot || m_Character.IsDead) return false;
            WeaponController weapon = m_Weapons.GetActiveWeapon();
            if (!weapon || !m_Camera) return false;
            StartCoroutine(Inject(slot, Mathf.Max(1, cores), coreMaterial, onCoreInjected, onFinished));
            return true;
        }

        IEnumerator Inject(Transform slot, int cores, Material coreMaterial, System.Action<int> onCoreInjected,
            System.Action onFinished)
        {
            EnterSequence();
            m_CoreInView = CoreTargetInView();
            m_CoreSocket = slot;
            m_CoreSocketOffset = Vector3.zero;
            m_Approach = Vector3.ProjectOnPlane(transform.position - slot.position, Vector3.up);
            if (m_Approach.sqrMagnitude < .0001f) m_Approach = slot.forward;
            m_Approach.Normalize();

            bool completed = false;
            GameObject orb = null;
            try
            {
                // --- Step in and line the slot up with the muzzle, exactly as for a core ---
                Vector3 startPosition = transform.position;
                Quaternion startYaw = transform.rotation;
                float startPitch = m_Pitch;
                for (float t = 0f; t < AlignDuration; t += Time.deltaTime)
                {
                    float k = Smooth(t / AlignDuration);
                    SolveStance(TrackedCore(), out Vector3 standPosition, out Quaternion yaw, out float pitch);
                    MoveTowards(Vector3.Lerp(startPosition, standPosition, k));
                    transform.rotation = Quaternion.Slerp(startYaw, yaw, k);
                    m_Pitch = Mathf.Lerp(startPitch, pitch, k);
                    m_Stage = k;
                    PoseGun(Vector3.Lerp(m_GunRestPosition, m_GunTuckPosition, k), m_GunRestRotation);
                    UpdateCamera(0f);
                    yield return null;
                }
                m_Stage = 1f;

                // --- Wind-up ---
                Vector3 windupPosition = m_GunTuckPosition + WindupOffset;
                Quaternion windupRotation = m_GunRestRotation * Quaternion.Euler(WindupEuler);
                for (float t = 0f; t < WindupDuration; t += Time.deltaTime)
                {
                    float k = EaseOutCubic(t / WindupDuration);
                    HoldStance();
                    PoseGun(Vector3.Lerp(m_GunTuckPosition, windupPosition, k),
                        Quaternion.Slerp(m_GunRestRotation, windupRotation, k));
                    m_FovOffset = -1.5f * k;
                    UpdateCamera(-.03f * k);
                    yield return null;
                }

                // --- Jab: the barrel goes into the slot ---
                Vector3 shovePosition = SolveShove(TrackedCore());
                for (float t = 0f; t < ShoveDuration; t += Time.deltaTime)
                {
                    float k = t / ShoveDuration;
                    k *= k;
                    HoldStance();
                    PoseGun(Vector3.Lerp(windupPosition, shovePosition, k), Quaternion.Slerp(windupRotation, m_GunRestRotation, k));
                    m_FovOffset = Mathf.Lerp(-1.5f, 1.5f, k);
                    UpdateCamera(Mathf.Lerp(-.03f, .02f, k));
                    yield return null;
                }
                PoseGun(shovePosition, m_GunRestRotation);
                m_Trauma = ImpactShake;
                m_FovKick = ImpactFovKick;
                PlayOneShot(ImpaleSfx);
                StartMuzzleEffects(m_Muzzle);
                if (AbsorbSfx)
                {
                    m_Loop.clip = AbsorbSfx;
                    m_Loop.volume = SfxVolume;
                    m_Loop.pitch = 1.45f;
                    m_Loop.Play();
                }

                // --- Inject: each core runs down the barrel and out into the slot ---
                orb = MakeCoreOrb(coreMaterial);
                float perCore = Mathf.Max(.35f, AbsorbDuration / cores);
                for (int core = 0; core < cores; core++)
                {
                    orb.SetActive(true);
                    for (float t = 0f; t < perCore; t += Time.deltaTime)
                    {
                        float p = t / perCore;
                        float overall = (core + p) / cores;
                        HoldStance();
                        float intensity = .35f + .65f * overall;
                        Vector3 tremble = Random.insideUnitSphere * (.004f * intensity);
                        float roll = Mathf.Sin(Time.time * 15f) * WrenchRoll * intensity;
                        // Each core is pushed home with a shove of the gun.
                        float push = Mathf.Sin(Mathf.Clamp01(p * 1.4f) * Mathf.PI) * .05f;
                        PoseGun(shovePosition + Vector3.forward * push + tremble,
                            m_GunRestRotation * Quaternion.Euler(0f, 0f, roll));

                        // The core slides from the chamber to the muzzle and shrinks away into the slot.
                        Vector3 chamber = m_Muzzle.position - m_Muzzle.forward * .32f;
                        float travel = Smooth(Mathf.Clamp01(p * 1.25f));
                        orb.transform.position = Vector3.Lerp(chamber, m_Muzzle.position + m_Muzzle.forward * .05f, travel);
                        orb.transform.localScale = Vector3.one * (.11f * (1f - .85f * Mathf.Clamp01((travel - .7f) / .3f)));

                        m_ContinuousShake = AbsorbShake * intensity;
                        m_FovOffset = Mathf.Lerp(1.5f, AbsorbFovDelta, Smooth(overall));
                        m_Loop.pitch = Mathf.Lerp(1.45f, .85f, overall);
                        UpdateMuzzleEffects(1f - overall * .5f);
                        UpdateCamera(.02f);
                        yield return null;
                    }
                    orb.SetActive(false);
                    m_Trauma = ImpactShake * .7f;
                    m_FovKick = ImpactFovKick * .6f;
                    BurstMuzzleEffects();
                    onCoreInjected?.Invoke(core);
                }
                m_ContinuousShake = 0f;
                m_Loop.Stop();

                // --- Pull out and recover ---
                PlayOneShot(RipSfx);
                m_Trauma = RipShake;
                Vector3 ripPosition = m_GunRestPosition + RipOffset;
                Quaternion ripRotation = m_GunRestRotation * Quaternion.Euler(RipEuler);
                for (float t = 0f; t < RipDuration; t += Time.deltaTime)
                {
                    float k = EaseOutCubic(t / RipDuration);
                    PoseGun(Vector3.Lerp(shovePosition, ripPosition, k), Quaternion.Slerp(m_GunRestRotation, ripRotation, k));
                    m_FovOffset = Mathf.Lerp(AbsorbFovDelta, 0f, k);
                    UpdateCamera(Mathf.Lerp(.02f, -.03f, k));
                    yield return null;
                }
                for (float t = 0f; t < RecoverDuration; t += Time.deltaTime)
                {
                    float k = Smooth(t / RecoverDuration);
                    PoseGun(Vector3.Lerp(ripPosition, m_GunRestPosition, k), Quaternion.Slerp(ripRotation, m_GunRestRotation, k));
                    if (m_MuzzleLight) m_MuzzleLight.intensity = MuzzleLightIntensity * (1f - k);
                    m_Stage = 1f - k;
                    UpdateCamera(Mathf.Lerp(-.03f, 0f, k));
                    yield return null;
                }
                completed = true;
            }
            finally
            {
                if (orb) Destroy(orb);
                if (IsExecuting) EndSequence();
            }
            if (completed) onFinished?.Invoke();
        }

        // A glowing core small enough to run down the barrel, drawn with the gun on the first-person layer.
        GameObject MakeCoreOrb(Material material)
        {
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "Injected Core";
            Destroy(orb.GetComponent<Collider>());
            var renderer = orb.GetComponent<Renderer>();
            if (material) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            orb.layer = Mathf.RoundToInt(Mathf.Log(Mathf.Max(1, m_Weapons.FpsWeaponLayer.value), 2));
            orb.transform.localScale = Vector3.one * .11f;
            orb.SetActive(false);
            return orb;
        }

        bool InteractPressed()
        {
            if (m_Input && !m_Input.CanProcessInput()) return false;
            if (m_Interact != null) return m_Interact.WasPressedThisFrame();
            return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
        }

        CoreExecutionTarget FindCandidate()
        {
            if (!m_Camera || m_Character.IsDead) return null;
            WeaponController weapon = m_Weapons.GetActiveWeapon();
            if (!weapon || !weapon.IsWeaponActive) return null;

            Transform eye = m_Camera.transform;
            CoreExecutionTarget best = null;
            float bestAngle = MaxAimAngle;
            foreach (var target in CoreExecutionTarget.Vulnerable)
            {
                if (!target || !target.CanBeExecuted) continue;
                Vector3 toCore = target.CorePosition - eye.position;
                float distance = toCore.magnitude;
                if (distance > MaxDistance || distance < .05f) continue;
                float angle = Vector3.Angle(eye.forward, toCore);
                if (angle > bestAngle || IsOccluded(eye.position, toCore / distance, distance, target)) continue;
                best = target;
                bestAngle = angle;
            }
            return best;
        }

        bool IsOccluded(Vector3 origin, Vector3 direction, float distance, CoreExecutionTarget target)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, m_Hits, distance - .05f, OcclusionMask,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform hit = m_Hits[i].collider.transform;
                if (hit.IsChildOf(transform) || hit.IsChildOf(target.transform)) continue;
                return true;
            }
            return false;
        }

        IEnumerator Execute(CoreExecutionTarget target)
        {
            BeginSequence(target);
            // Whatever happens mid-sequence, never leave the player locked in the execution pose.
            try
            {
                // --- Step in: line the core up just ahead of the muzzle, lower right, and tuck the gun in ---
                Vector3 startPosition = transform.position;
                Quaternion startYaw = transform.rotation;
                float startPitch = m_Pitch;
                for (float t = 0f; t < AlignDuration && target; t += Time.deltaTime)
                {
                    float k = Smooth(t / AlignDuration);
                    SolveStance(TrackedCore(), out Vector3 standPosition, out Quaternion yaw, out float pitch);
                    MoveTowards(Vector3.Lerp(startPosition, standPosition, k));
                    transform.rotation = Quaternion.Slerp(startYaw, yaw, k);
                    m_Pitch = Mathf.Lerp(startPitch, pitch, k);
                    m_Stage = k;
                    PoseGun(Vector3.Lerp(m_GunRestPosition, m_GunTuckPosition, k), m_GunRestRotation);
                    UpdateCamera(0f);
                    yield return null;
                }
                m_Stage = 1f;

                // --- Wind-up: draw the gun back a little, low and to the right ---
                Vector3 windupPosition = m_GunTuckPosition + WindupOffset;
                Quaternion windupRotation = m_GunRestRotation * Quaternion.Euler(WindupEuler);
                for (float t = 0f; t < WindupDuration && target; t += Time.deltaTime)
                {
                    float k = EaseOutCubic(t / WindupDuration);
                    HoldStance();
                    PoseGun(Vector3.Lerp(m_GunTuckPosition, windupPosition, k),
                        Quaternion.Slerp(m_GunRestRotation, windupRotation, k));
                    m_FovOffset = -1.5f * k;
                    UpdateCamera(-.03f * k);
                    yield return null;
                }

                // --- Jab: drive the muzzle straight along the barrel into the core ---
                Vector3 shovePosition = target ? SolveShove(TrackedCore()) : m_GunTuckPosition;
                for (float t = 0f; t < ShoveDuration && target; t += Time.deltaTime)
                {
                    float k = t / ShoveDuration;
                    k *= k;
                    HoldStance();
                    PoseGun(Vector3.Lerp(windupPosition, shovePosition, k), Quaternion.Slerp(windupRotation, m_GunRestRotation, k));
                    m_FovOffset = Mathf.Lerp(-1.5f, 1.5f, k);
                    UpdateCamera(Mathf.Lerp(-.03f, .02f, k));
                    yield return null;
                }
                PoseGun(shovePosition, m_GunRestRotation);

                if (target)
                {
                    // Impact.
                    target.Impale(m_Muzzle);
                    m_Trauma = ImpactShake;
                    m_FovKick = ImpactFovKick;
                    PlayOneShot(ImpaleSfx);
                    StartMuzzleEffects(m_Muzzle);
                    if (AbsorbSfx)
                    {
                        m_Loop.clip = AbsorbSfx;
                        m_Loop.volume = SfxVolume;
                        m_Loop.pitch = .9f;
                        m_Loop.Play();
                    }
                }

                // --- Absorb: hold the gun in the core, wrenching it, while the core is sucked in ---
                Quaternion absorbRotation = m_GunRestRotation;
                Vector3 absorbPosition = shovePosition;
                for (float t = 0f; t < AbsorbDuration && target; t += Time.deltaTime)
                {
                    float p = t / AbsorbDuration;
                    target.SetAbsorbProgress(p);
                    HoldStance();

                    float intensity = .35f + .65f * p;
                    Vector3 tremble = Random.insideUnitSphere * (.004f * intensity);
                    float roll = Mathf.Sin(t * 15f) * WrenchRoll * intensity;
                    float jitterPitch = (Mathf.PerlinNoise(t * 20f, m_ShakeSeed) - .5f) * 3f * intensity;
                    float jitterYaw = (Mathf.PerlinNoise(m_ShakeSeed, t * 20f) - .5f) * 3f * intensity;
                    absorbPosition = shovePosition + Vector3.back * (AbsorbPullBack * p * p) + tremble;
                    absorbRotation = m_GunRestRotation * Quaternion.Euler(jitterPitch, jitterYaw, roll);
                    PoseGun(absorbPosition, absorbRotation);

                    m_ContinuousShake = AbsorbShake * intensity;
                    m_FovOffset = Mathf.Lerp(1.5f, AbsorbFovDelta, Smooth(p));
                    m_Loop.pitch = Mathf.Lerp(.9f, 1.45f, p);
                    UpdateMuzzleEffects(p);
                    UpdateCamera(.02f);
                    yield return null;
                }
                m_ContinuousShake = 0f;

                // --- Rip: tear the core free; the enemy dies and the core's power heals the player ---
                if (target)
                {
                    float reward = target.HealthReward > 0f ? target.HealthReward : FallbackHealthReward;
                    // Gas can kill the player mid-execution; a dead player is not healed back up.
                    if (!m_Character.IsDead)
                    {
                        m_Health.Heal(reward);
                        PlayOneShot(HealSfx);
                    }
                    // The kill runs the game's death handlers (objectives, HUD); a fault there must not stall the sequence.
                    try
                    {
                        target.FinishExecution();
                    }
                    catch (System.Exception exception)
                    {
                        Debug.LogException(exception, target);
                    }
                }
                m_Loop.Stop();
                PlayOneShot(RipSfx);
                m_Trauma = RipShake;
                m_FovKick = ImpactFovKick * 1.2f;
                BurstMuzzleEffects();

                Vector3 ripPosition = m_GunRestPosition + RipOffset;
                Quaternion ripRotation = m_GunRestRotation * Quaternion.Euler(RipEuler);
                for (float t = 0f; t < RipDuration; t += Time.deltaTime)
                {
                    float k = EaseOutCubic(t / RipDuration);
                    PoseGun(Vector3.Lerp(absorbPosition, ripPosition, k), Quaternion.Slerp(absorbRotation, ripRotation, k));
                    m_FovOffset = Mathf.Lerp(AbsorbFovDelta, 0f, k);
                    if (m_MuzzleLight) m_MuzzleLight.intensity = MuzzleLightIntensity * 1.6f;
                    UpdateCamera(Mathf.Lerp(.02f, -.03f, k));
                    yield return null;
                }

                // --- Recover: bring the gun back to the hip and straighten up, still glowing with the stolen energy ---
                for (float t = 0f; t < RecoverDuration; t += Time.deltaTime)
                {
                    float k = Smooth(t / RecoverDuration);
                    PoseGun(Vector3.Lerp(ripPosition, m_GunRestPosition, k), Quaternion.Slerp(ripRotation, m_GunRestRotation, k));
                    if (m_MuzzleLight) m_MuzzleLight.intensity = MuzzleLightIntensity * 1.6f * (1f - k);
                    m_Stage = 1f - k;
                    UpdateCamera(Mathf.Lerp(-.03f, 0f, k));
                    yield return null;
                }
            }
            finally
            {
                if (IsExecuting) EndSequence();
            }
            OnExecutionFinished?.Invoke(target);
        }

        void BeginSequence(CoreExecutionTarget target)
        {
            EnterSequence();

            m_CoreInView = CoreTargetInView();
            // Aim at the core's socket in the chest: the core itself flies into the gun later.
            m_CoreSocket = target.Core && target.Core.transform.parent ? target.Core.transform.parent : target.transform;
            m_CoreSocketOffset = m_CoreSocket.InverseTransformPoint(target.CorePosition);
            m_Approach = Vector3.ProjectOnPlane(transform.position - target.transform.position, Vector3.up);
            if (m_Approach.sqrMagnitude < .0001f) m_Approach = target.transform.forward;
            m_Approach.Normalize();

            // The victim ends up closer than a body's width, so let the two overlap.
            m_IgnoredColliders.Clear();
            if (m_Controller)
            {
                foreach (var other in target.GetComponentsInChildren<Collider>())
                {
                    Physics.IgnoreCollision(m_Controller, other, true);
                    m_IgnoredColliders.Add(other);
                }
            }

            target.BeginExecution(gameObject);
            OnExecutionStarted?.Invoke(target);
        }

        // The staging shared by executions and injections: player locked and invincible, gun from the hip.
        void EnterSequence()
        {
            IsExecuting = true;
            Candidate = null;

            m_WasInvincible = m_Health.Invincible;
            m_Health.Invincible = true;
            CoreGasCloud.PlayerShieldedByExecution = !m_WasInvincible;

            m_Pitch = m_Character.CameraVerticalAngle;
            m_Character.CharacterVelocity = Vector3.zero;
            m_Character.enabled = false;
            m_Weapons.enabled = false;

            // Always from the hip, even if the player was aiming down the sights.
            if (m_Weapons.WeaponParentSocket && m_Weapons.DefaultWeaponPosition)
                m_Weapons.WeaponParentSocket.localPosition = m_Weapons.DefaultWeaponPosition.localPosition;
            m_BaseFov = m_Weapons.DefaultFov;

            m_Weapon = m_Weapons.GetActiveWeapon();
            m_Gun = m_Weapon.transform;
            m_Muzzle = m_Weapon.WeaponMuzzle ? m_Weapon.WeaponMuzzle : m_Gun;
            m_GunRestPosition = m_Gun.localPosition;
            m_GunRestRotation = m_Gun.localRotation;
            m_GunTuckPosition = m_GunRestPosition + GunTuck;
            m_CameraRestPosition = m_Camera.transform.localPosition;
            m_Trauma = 0f;
            m_ContinuousShake = 0f;
            m_FovKick = 0f;
            m_FovOffset = 0f;
            m_Stage = 0f;
        }

        void EndSequence()
        {
            if (m_Gun)
            {
                m_Gun.localPosition = m_GunRestPosition;
                m_Gun.localRotation = m_GunRestRotation;
            }
            if (m_Camera)
            {
                m_Camera.transform.localPosition = m_CameraRestPosition;
                m_Weapons.SetFov(m_BaseFov);
            }
            if (m_Loop) m_Loop.Stop();
            StopMuzzleEffects();

            foreach (var other in m_IgnoredColliders)
                if (other && m_Controller) Physics.IgnoreCollision(m_Controller, other, false);
            m_IgnoredColliders.Clear();

            m_Character.CameraVerticalAngle = m_Pitch;
            m_Character.CharacterVelocity = Vector3.zero;
            m_Character.enabled = true;
            m_Weapons.enabled = true;
            m_Health.Invincible = m_WasInvincible;
            CoreGasCloud.PlayerShieldedByExecution = false;
            m_CoreSocket = null;
            m_Stage = 0f;
            IsExecuting = false;
        }

        // Where, in camera space, the core must sit: just ahead of the tucked gun's muzzle along its barrel.
        Vector3 CoreTargetInView()
        {
            Transform eye = m_Camera.transform;
            Vector3 savedPosition = m_Gun.localPosition;
            Quaternion savedRotation = m_Gun.localRotation;
            m_Gun.localPosition = m_GunTuckPosition;
            m_Gun.localRotation = m_GunRestRotation;
            Vector3 muzzle = eye.InverseTransformPoint(m_Muzzle.position);
            Vector3 barrel = eye.InverseTransformDirection(m_Muzzle.forward);
            m_Gun.localPosition = savedPosition;
            m_Gun.localRotation = savedRotation;
            if (barrel.z < .5f) barrel = Vector3.forward;
            return muzzle + barrel.normalized * ShoveDepth;
        }

        Vector3 TrackedCore() => m_CoreSocket ? m_CoreSocket.TransformPoint(m_CoreSocketOffset) : transform.position;

        /// <summary>
        /// The player position, yaw and camera pitch that put the core at m_CoreInView, approached from
        /// the side the player came from. Pitch handles the height difference; yaw and position the rest.
        /// </summary>
        void SolveStance(Vector3 core, out Vector3 position, out Quaternion yaw, out float pitch)
        {
            Vector3 eyeLocal = transform.InverseTransformPoint(m_Camera.transform.position);
            Vector3 view = m_CoreInView;

            // Camera pitch p (positive looks down) maps the view point's height to y*cos(p) - z*sin(p).
            float height = core.y - (transform.position.y + eyeLocal.y);
            float radius = Mathf.Sqrt(view.y * view.y + view.z * view.z);
            float offset = Mathf.Atan2(view.z, view.y);
            float spread = Mathf.Acos(Mathf.Clamp(height / Mathf.Max(radius, 1e-4f), -1f, 1f));
            float first = Mathf.DeltaAngle(0f, (spread - offset) * Mathf.Rad2Deg);
            float second = Mathf.DeltaAngle(0f, (-spread - offset) * Mathf.Rad2Deg);
            pitch = Mathf.Clamp(Mathf.Abs(first) < Mathf.Abs(second) ? first : second, PitchLimits.x, PitchLimits.y);

            float radians = pitch * Mathf.Deg2Rad;
            float ahead = view.y * Mathf.Sin(radians) + view.z * Mathf.Cos(radians);
            Vector2 local = new Vector2(eyeLocal.x + view.x, eyeLocal.z + ahead);

            // Turn so that offset points from the player at the core, along the approach direction.
            float localAngle = Mathf.Atan2(local.x, local.y) * Mathf.Rad2Deg;
            float worldAngle = Mathf.Atan2(-m_Approach.x, -m_Approach.z) * Mathf.Rad2Deg;
            yaw = Quaternion.Euler(0f, worldAngle - localAngle, 0f);
            Vector3 reach = yaw * new Vector3(local.x, 0f, local.y);
            position = new Vector3(core.x - reach.x, transform.position.y, core.z - reach.z);
        }

        // Keeps the core lined up with the gun as the victim sways and doubles over.
        void HoldStance()
        {
            if (!m_CoreSocket) return;
            SolveStance(TrackedCore(), out Vector3 position, out Quaternion yaw, out float pitch);
            float follow = 1f - Mathf.Exp(-TrackSharpness * Time.deltaTime);
            MoveTowards(Vector3.Lerp(transform.position, position, follow));
            transform.rotation = Quaternion.Slerp(transform.rotation, yaw, follow);
            m_Pitch = Mathf.Lerp(m_Pitch, pitch, follow);
        }

        // Slides the player there, stopped by level geometry but not by actors: other enemies crowding in
        // (and the character controller pushing out of them) must not shove the player off the staging spot.
        // CharacterController.Move isn't used as it also ignores the tiny per-frame corrections that keep the
        // victim lined up.
        void MoveTowards(Vector3 position)
        {
            Vector3 delta = position - transform.position;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance < 1e-5f) return;

            if (m_Controller && distance > .01f)
            {
                float radius = m_Controller.radius * .9f;
                Vector3 center = transform.TransformPoint(m_Controller.center);
                float half = Mathf.Max(0f, m_Controller.height * .5f - m_Controller.radius);
                Vector3 top = center + Vector3.up * half;
                // Lift the bottom sphere clear of the floor so the sweep doesn't catch on it.
                Vector3 bottom = center - Vector3.up * Mathf.Max(0f, half - m_Controller.stepOffset);
                int count = Physics.CapsuleCastNonAlloc(top, bottom, radius, delta / distance, m_Hits, distance,
                    ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    Collider hit = m_Hits[i].collider;
                    if (hit.transform.IsChildOf(transform) || hit.GetComponentInParent<Actor>()) continue;
                    if (m_Hits[i].distance <= 0f) continue; // already overlapping: don't pin the player
                    distance = Mathf.Min(distance, Mathf.Max(0f, m_Hits[i].distance - .02f));
                }
                delta = delta.normalized * distance;
            }
            transform.position += delta;
            SettleOntoFloor();
        }

        // Started mid-jump? Sink onto the floor underneath rather than hanging in the air for the whole sequence.
        void SettleOntoFloor()
        {
            Vector3 feet = transform.position;
            int count = Physics.RaycastNonAlloc(feet + Vector3.up * .5f, Vector3.down, m_Hits, 3f, ~0,
                QueryTriggerInteraction.Ignore);
            float floor = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider hit = m_Hits[i].collider;
                if (hit.transform.IsChildOf(transform) || hit.GetComponentInParent<Actor>()) continue;
                floor = Mathf.Max(floor, m_Hits[i].point.y);
            }
            if (float.IsNegativeInfinity(floor) || floor > feet.y + .3f) return;
            transform.position = new Vector3(feet.x, Mathf.MoveTowards(feet.y, floor, 6f * Time.deltaTime), feet.z);
        }

        // Where the gun has to sit (in its parent's space) to put the muzzle into the core, pointing straight ahead.
        Vector3 SolveShove(Vector3 core)
        {
            Vector3 savedPosition = m_Gun.localPosition;
            Quaternion savedRotation = m_Gun.localRotation;
            m_Gun.localPosition = m_GunTuckPosition;
            m_Gun.localRotation = m_GunRestRotation;
            Vector3 reach = (core - m_Muzzle.position) * ShoveReach;
            Vector3 localReach = m_Gun.parent ? m_Gun.parent.InverseTransformVector(reach) : reach;
            m_Gun.localPosition = savedPosition;
            m_Gun.localRotation = savedRotation;
            return m_GunTuckPosition + localReach;
        }

        void PoseGun(Vector3 localPosition, Quaternion localRotation)
        {
            if (!m_Gun) return;
            m_Gun.localPosition = localPosition;
            m_Gun.localRotation = localRotation;
        }

        void UpdateCamera(float push)
        {
            float deltaTime = Time.deltaTime;
            m_Trauma *= Mathf.Exp(-7f * deltaTime);
            m_FovKick *= Mathf.Exp(-8f * deltaTime);

            float shake = Mathf.Max(m_Trauma, m_ContinuousShake);
            float time = Time.time * 28f;
            Vector3 noise = new Vector3(
                Mathf.PerlinNoise(time, m_ShakeSeed) - .5f,
                Mathf.PerlinNoise(m_ShakeSeed, time) - .5f,
                Mathf.PerlinNoise(time, m_ShakeSeed + 5f) - .5f) * 2f;

            float stage = Smooth(m_Stage);
            Transform eye = m_Camera.transform;
            eye.localPosition = m_CameraRestPosition + Vector3.down * (CameraDip * stage) + Vector3.forward * push +
                                noise * (shake * .4f);
            eye.localEulerAngles = new Vector3(m_Pitch + noise.x * shake * 40f, noise.y * shake * 40f,
                CameraLean * stage + noise.z * shake * 60f);
            m_Weapons.SetFov(m_BaseFov + m_FovOffset + m_FovKick);
        }

        void StartMuzzleEffects(Transform muzzle)
        {
            int layer = Mathf.RoundToInt(Mathf.Log(Mathf.Max(1, m_Weapons.FpsWeaponLayer.value), 2));

            var lightObject = new GameObject("Core Absorb Light");
            lightObject.transform.SetParent(muzzle, false);
            m_MuzzleLight = lightObject.AddComponent<Light>();
            m_MuzzleLight.type = LightType.Point;
            m_MuzzleLight.color = MuzzleLightColor;
            m_MuzzleLight.range = 2.5f;
            m_MuzzleLight.intensity = 0f;
            m_MuzzleLight.shadows = LightShadows.None;

            if (MuzzleChargeVfx)
            {
                m_MuzzleVfx = Instantiate(MuzzleChargeVfx, muzzle, false);
                // Gun muzzles often sit under a scaled model; keep the effect at world size.
                Vector3 muzzleScale = muzzle.lossyScale;
                m_MuzzleVfx.transform.localScale = new Vector3(1f / Mathf.Max(1e-4f, Mathf.Abs(muzzleScale.x)),
                    1f / Mathf.Max(1e-4f, Mathf.Abs(muzzleScale.y)), 1f / Mathf.Max(1e-4f, Mathf.Abs(muzzleScale.z)));
                foreach (Transform t in m_MuzzleVfx.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                m_MuzzleParticles = m_MuzzleVfx.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var particles in m_MuzzleParticles) particles.Play(true);
            }
        }

        void UpdateMuzzleEffects(float progress)
        {
            if (m_MuzzleLight)
            {
                float flicker = .8f + .4f * Mathf.PerlinNoise(Time.time * 25f, m_ShakeSeed);
                m_MuzzleLight.intensity = MuzzleLightIntensity * progress * flicker;
            }
            if (m_MuzzleParticles == null) return;
            foreach (var particles in m_MuzzleParticles)
            {
                var emission = particles.emission;
                emission.rateOverTimeMultiplier = Mathf.Lerp(15f, 140f, progress);
            }
        }

        void BurstMuzzleEffects()
        {
            if (m_MuzzleParticles == null) return;
            foreach (var particles in m_MuzzleParticles)
            {
                if (!particles) continue;
                var emission = particles.emission;
                emission.rateOverTimeMultiplier = 0f;
                particles.Emit(45);
            }
        }

        void StopMuzzleEffects()
        {
            if (m_MuzzleLight) Destroy(m_MuzzleLight.gameObject);
            if (m_MuzzleVfx)
            {
                // Let the last sparks finish before cleaning up.
                m_MuzzleVfx.transform.SetParent(null, true);
                foreach (var particles in m_MuzzleParticles)
                    if (particles) particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(m_MuzzleVfx, 1.2f);
            }
            m_MuzzleLight = null;
            m_MuzzleVfx = null;
            m_MuzzleParticles = null;
        }

        void PlayOneShot(AudioClip clip)
        {
            if (clip) m_OneShots.PlayOneShot(clip, SfxVolume);
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
    }
}
