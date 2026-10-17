using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.FPS.Gameplay
{
    [RequireComponent(typeof(CharacterController), typeof(PlayerInputHandler), typeof(AudioSource))]
    public class PlayerCharacterController : MonoBehaviour
    {
        [Header("References")] [Tooltip("Reference to the main camera used for the player")]
        public Camera PlayerCamera;

        [Tooltip("Audio source for footsteps, jump, etc...")]
        public AudioSource AudioSource;

        [Header("General")] [Tooltip("Force applied downward when in the air")]
        public float GravityDownForce = 20f;

        [Tooltip("Physic layers checked to consider the player grounded")]
        public LayerMask GroundCheckLayers = -1;

        [Tooltip("Physic layers checked when detecting an obstruction above the player before standing up from a crouch (should exclude the FirstPersonWeapon layer, or any layer the held weapon model sits on)")]
        public LayerMask StandUpObstructionLayers = -1;

        [Tooltip("distance from the bottom of the character controller capsule to test for grounded")]
        public float GroundCheckDistance = 0.05f;

        [Header("Movement")] [Tooltip("Max movement speed when grounded (when not sprinting)")]
        public float MaxSpeedOnGround = 10f;

        [Tooltip(
            "Sharpness for the movement when grounded, a low value will make the player accelerate and decelerate slowly, a high value will do the opposite")]
        public float MovementSharpnessOnGround = 15;

        [Tooltip("Max movement speed when crouching")] [Range(0, 1)]
        public float MaxSpeedCrouchedRatio = 0.5f;

        [Tooltip("Max movement speed when not grounded")]
        public float MaxSpeedInAir = 10f;

        [Tooltip("Acceleration speed when in the air")]
        public float AccelerationSpeedInAir = 25f;

        [Tooltip("Multiplicator for the sprint speed (based on grounded speed)")]
        public float SprintSpeedModifier = 2f;

        [Tooltip("Movement speed while walking (toggled with Walk, V), as a fraction of the grounded speed")]
        [Range(0.1f, 1f)]
        public float WalkSpeedRatio = 0.4f;

        [Tooltip("Height at which the player dies instantly when falling off the map")]
        public float KillHeight = -50f;

        [Header("Rotation")] [Tooltip("Rotation speed for moving the camera")]
        public float RotationSpeed = 200f;

        [Range(0.1f, 1f)] [Tooltip("Rotation speed multiplier when aiming")]
        public float AimingRotationMultiplier = 0.4f;

        [Header("Jump")] [Tooltip("Force applied upward when jumping")]
        public float JumpForce = 9f;

        [Tooltip("Upward force applied on a mid-air double jump")]
        public float DoubleJumpForce = 8f;

        [Header("Wall Run")]
        [Tooltip("Layers considered as wall-runnable surfaces")]
        public LayerMask WallRunLayers = -1;

        [Tooltip("Distance to check for a wall on either side of the player")]
        public float WallCheckDistance = 0.7f;

        [Tooltip("Minimum horizontal speed required to start a wall run")]
        public float MinSpeedToWallRun = 4f;

        [Tooltip("Movement speed while wall running")]
        public float WallRunSpeed = 9f;

        [Tooltip("Downward gravity applied while wall running (lower than normal gravity)")]
        public float WallRunGravityDownForce = 4f;

        [Tooltip("Small upward boost applied the instant a wall run is caught")]
        public float WallRunInitialBoost = 2f;

        [Tooltip("Force applied away from the wall and upward when wall jumping")]
        public float WallJumpForce = 10f;

        [Tooltip("Max duration of a single wall run before forced detachment")]
        public float MaxWallRunDuration = 3f;

        [Tooltip("Time after leaving a wall before the player can wall run again")]
        public float WallRunCooldown = 0.5f;

        [Tooltip("Camera roll angle applied while wall running, for feedback")]
        public float WallRunCameraTilt = 10f;

        [Tooltip("Speed of the camera tilt transition")]
        public float WallRunCameraTiltSharpness = 8f;

        [Header("Slide")]
        [Tooltip("Minimum ground speed required to start a slide")]
        public float MinSpeedToSlide = 6f;

        [Tooltip("Speed boost applied the instant a slide starts")]
        public float SlideInitialBoost = 3f;

        [Tooltip("How quickly slide speed decays (higher = shorter slide)")]
        public float SlideFriction = 2.5f;

        [Tooltip("Max duration of a slide before the player is forced back up")]
        public float MaxSlideDuration = 1f;

        [Tooltip("Speed multiplier applied when sliding while already carrying speed above normal running (e.g. from a sprint-jump) — lets slide-hop chains compound speed, Titanfall-style")]
        public float SlideChainSpeedMultiplier = 1.2f;

        [Tooltip("How long extra speed gained from a slide lingers into normal running before decaying back to the sprint cap")]
        public float SlideExitMomentumDuration = 1.2f;

        [Tooltip("Camera roll angle applied while sliding, for feedback (uses the same tilt sharpness as wall running)")]
        public float SlideCameraTilt = 6f;

        [Tooltip("How fast the movement input can bend a slide's direction, in degrees per second")]
        public float SlideSteerSpeed = 70f;

        [Tooltip("Downhill pull while sliding, as a fraction of GravityDownForce (uphill slows the slide the same way)")]
        public float SlideSlopeAcceleration = 1f;

        [Tooltip("Hard cap on slide speed, so slide-hop chains can't grow without limit")]
        public float MaxSlideSpeed = 30f;

        [Tooltip("Minimum time between slide boosts; a slide inside this window keeps its speed but gets no boost")]
        public float SlideBoostCooldown = 1.2f;

        [Header("Dodge")]
        [Tooltip("Speed of the dodge burst")]
        public float DodgeForce = 22f;

        [Tooltip("How long the dodge burst overrides normal movement")]
        public float DodgeDuration = 0.18f;

        [Tooltip("Minimum time between dodges")]
        public float DodgeCooldown = 0.6f;

        [Header("Stance")] [Tooltip("Ratio (0-1) of the character height where the camera will be at")]
        public float CameraHeightRatio = 0.9f;

        [Tooltip("Height of character when standing")]
        public float CapsuleHeightStanding = 1.8f;

        [Tooltip("Height of character when crouching")]
        public float CapsuleHeightCrouching = 0.9f;

        [Tooltip("Speed of crouching transitions")]
        public float CrouchingSharpness = 10f;

        [Header("Audio")] [Tooltip("Amount of footstep sounds played when moving one meter")]
        public float FootstepSfxFrequency = 1f;

        [Tooltip("Amount of footstep sounds played when moving one meter while sprinting")]
        public float FootstepSfxFrequencyWhileSprinting = 1f;

        [Tooltip("Sound played for footsteps")]
        public AudioClip FootstepSfx;

        [Tooltip("Sound played when jumping")] public AudioClip JumpSfx;
        [Tooltip("Sound played when landing")] public AudioClip LandSfx;

        [Tooltip("Sound played when taking damage froma fall")]
        public AudioClip FallDamageSfx;

        [Header("Fall Damage")]
        [Tooltip("Whether the player will recieve damage when hitting the ground at high speed")]
        public bool RecievesFallDamage;

        [Tooltip("Minimun fall speed for recieving fall damage")]
        public float MinSpeedForFallDamage = 10f;

        [Tooltip("Fall speed for recieving th emaximum amount of fall damage")]
        public float MaxSpeedForFallDamage = 30f;

        [Tooltip("Damage recieved when falling at the mimimum speed")]
        public float FallDamageAtMinSpeed = 10f;

        [Tooltip("Damage recieved when falling at the maximum speed")]
        public float FallDamageAtMaxSpeed = 50f;

        public UnityAction<bool> OnStanceChanged;

        public Vector3 CharacterVelocity { get; set; }
        public bool IsGrounded { get; private set; }
        public bool HasJumpedThisFrame { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsCrouching { get; private set; }
        /// <summary>Slow walk, toggled with Walk (V); sprinting ends it.</summary>
        public bool IsWalking { get; private set; }
        public bool IsWallRunning { get; private set; }
        public bool IsSliding { get; private set; }
        public bool IsDodging { get; private set; }

        /// <summary>Camera pitch in degrees (positive looks down). Lets scripted sequences hand the view back.</summary>
        public float CameraVerticalAngle
        {
            get => m_CameraVerticalAngle;
            set => m_CameraVerticalAngle = Mathf.Clamp(value, -89f, 89f);
        }

        public float RotationMultiplier
        {
            get
            {
                if (m_WeaponsManager.IsAiming)
                {
                    return AimingRotationMultiplier;
                }

                return 1f;
            }
        }

        Health m_Health;
        PlayerInputHandler m_InputHandler;
        CharacterController m_Controller;
        PlayerWeaponsManager m_WeaponsManager;
        Actor m_Actor;
        Vector3 m_GroundNormal;
        Vector3 m_CharacterVelocity;
        Vector3 m_LatestImpactSpeed;
        float m_LastTimeJumped = 0f;
        float m_CameraVerticalAngle = 0f;
        float m_FootstepDistanceCounter;
        float m_TargetCharacterHeight;

        Vector3 m_WallNormal;
        float m_WallRunTimer;
        float m_LastWallRunEndTime = -10f;
        float m_CurrentCameraTilt;
        float m_WallRunSpeed;
        float m_SlideTimer;
        bool m_SlideHeld;
        int m_SlideStartFrame;
        float m_NextSlideBoostTime;
        float m_MomentumSpeed;
        float m_MomentumExpireTime = -10f;
        float m_DodgeTimer;
        float m_LastDodgeEndTime = -10f;
        Vector3 m_DodgeDirection;
        bool m_DoubleJumpUsed;

        const float k_JumpGroundingPreventionTime = 0.2f;
        const float k_GroundCheckDistanceInAir = 0.07f;

        void Awake()
        {
            ActorsManager actorsManager = FindAnyObjectByType<ActorsManager>();
            if (actorsManager != null)
                actorsManager.SetPlayer(gameObject);
        }

        void Start()
        {
            // fetch components on the same gameObject
            m_Controller = GetComponent<CharacterController>();
            DebugUtility.HandleErrorIfNullGetComponent<CharacterController, PlayerCharacterController>(m_Controller,
                this, gameObject);

            m_InputHandler = GetComponent<PlayerInputHandler>();
            DebugUtility.HandleErrorIfNullGetComponent<PlayerInputHandler, PlayerCharacterController>(m_InputHandler,
                this, gameObject);

            m_WeaponsManager = GetComponent<PlayerWeaponsManager>();
            DebugUtility.HandleErrorIfNullGetComponent<PlayerWeaponsManager, PlayerCharacterController>(
                m_WeaponsManager, this, gameObject);

            m_Health = GetComponent<Health>();
            DebugUtility.HandleErrorIfNullGetComponent<Health, PlayerCharacterController>(m_Health, this, gameObject);

            m_Actor = GetComponent<Actor>();
            DebugUtility.HandleErrorIfNullGetComponent<Actor, PlayerCharacterController>(m_Actor, this, gameObject);

            m_Controller.enableOverlapRecovery = true;

            m_Health.OnDie += OnDie;

            // force the crouch state to false when starting
            SetCrouchingState(false, true);
            UpdateCharacterHeight(true);
        }

        void Update()
        {
            // check for Y kill
            if (!IsDead && transform.position.y < KillHeight)
            {
                m_Health.Kill();
            }

            HasJumpedThisFrame = false;

            bool wasGrounded = IsGrounded;
            GroundCheck();

            // landing
            if (IsGrounded && !wasGrounded)
            {
                m_DoubleJumpUsed = false;

                // Fall damage
                float fallSpeed = -Mathf.Min(CharacterVelocity.y, m_LatestImpactSpeed.y);
                float fallSpeedRatio = (fallSpeed - MinSpeedForFallDamage) /
                                       (MaxSpeedForFallDamage - MinSpeedForFallDamage);
                if (RecievesFallDamage && fallSpeedRatio > 0f)
                {
                    float dmgFromFall = Mathf.Lerp(FallDamageAtMinSpeed, FallDamageAtMaxSpeed, fallSpeedRatio);
                    m_Health.TakeDamage(dmgFromFall, null);

                    // fall damage SFX
                    AudioSource.PlayOneShot(FallDamageSfx);
                }
                else
                {
                    // land SFX
                    AudioSource.PlayOneShot(LandSfx);
                }
            }

            HandleDodge();

            // Crouch (C) slides out of a sprint, Titanfall-style, and toggles a plain crouch otherwise.
            // The dedicated slide key (Ctrl) slides from any running speed while held, and falls back to a
            // plain crouch if you're not moving fast enough to slide.
            bool slidePressed = !IsDodging && !IsSliding && m_InputHandler.GetSlideInputDown() && IsGrounded;
            bool crouchPressed = !IsDodging && !IsSliding && m_InputHandler.GetCrouchInputDown();
            if (slidePressed || crouchPressed)
            {
                float speed = Vector3.ProjectOnPlane(CharacterVelocity, Vector3.up).magnitude;
                float speedNeeded = slidePressed ? MinSpeedToSlide : MaxSpeedOnGround * 1.05f;
                if (IsGrounded && speed >= speedNeeded)
                {
                    StartSlide(slidePressed);
                }
                else
                {
                    SetCrouchingState(!IsCrouching, false);
                }
            }

            if (IsSliding)
            {
                UpdateSlide();
            }

            if (!IsDodging)
            {
                HandleWallRun();
            }

            UpdateCameraTilt();

            UpdateCharacterHeight(false);

            HandleCharacterMovement();
        }

        void OnDie()
        {
            IsDead = true;

            // Tell the weapons manager to switch to a non-existing weapon in order to lower the weapon
            m_WeaponsManager.SwitchToWeaponIndex(-1, true);

            EventManager.Broadcast(Events.PlayerDeathEvent);
        }

        void GroundCheck()
        {
            // Make sure that the ground check distance while already in air is very small, to prevent suddenly snapping to ground
            float chosenGroundCheckDistance =
                IsGrounded ? (m_Controller.skinWidth + GroundCheckDistance) : k_GroundCheckDistanceInAir;

            // reset values before the ground check
            IsGrounded = false;
            m_GroundNormal = Vector3.up;

            // only try to detect ground if it's been a short amount of time since last jump; otherwise we may snap to the ground instantly after we try jumping
            if (Time.time >= m_LastTimeJumped + k_JumpGroundingPreventionTime)
            {
                // if we're grounded, collect info about the ground normal with a downward capsule cast representing our character capsule
                if (Physics.CapsuleCast(GetCapsuleBottomHemisphere(), GetCapsuleTopHemisphere(m_Controller.height),
                    m_Controller.radius, Vector3.down, out RaycastHit hit, chosenGroundCheckDistance, GroundCheckLayers,
                    QueryTriggerInteraction.Ignore))
                {
                    // storing the upward direction for the surface found
                    m_GroundNormal = hit.normal;

                    // Only consider this a valid ground hit if the ground normal goes in the same direction as the character up
                    // and if the slope angle is lower than the character controller's limit
                    if (Vector3.Dot(hit.normal, transform.up) > 0f &&
                        IsNormalUnderSlopeLimit(m_GroundNormal))
                    {
                        IsGrounded = true;

                        // handle snapping to the ground
                        if (hit.distance > m_Controller.skinWidth)
                        {
                            m_Controller.Move(Vector3.down * hit.distance);
                        }
                    }
                }
            }
        }

        // Casts to either side of the player to find a wall-runnable surface
        bool CheckForWall(out RaycastHit hit, out Vector3 wallNormal)
        {
            Vector3 origin = transform.position + (transform.up * m_Controller.height * 0.5f);

            if (Physics.Raycast(origin, transform.right, out hit, WallCheckDistance, WallRunLayers,
                QueryTriggerInteraction.Ignore) && IsRunnableWall(hit))
            {
                wallNormal = hit.normal;
                return true;
            }

            if (Physics.Raycast(origin, -transform.right, out hit, WallCheckDistance, WallRunLayers,
                QueryTriggerInteraction.Ignore) && IsRunnableWall(hit))
            {
                wallNormal = hit.normal;
                return true;
            }

            wallNormal = Vector3.zero;
            return false;
        }

        // Near-vertical scenery only: brushing an enemy or a ramp mid-air must not catch a wall run.
        static bool IsRunnableWall(RaycastHit hit)
        {
            return Mathf.Abs(hit.normal.y) < 0.3f && !hit.collider.GetComponentInParent<Actor>();
        }

        void HandleWallRun()
        {
            bool eligibleToWallRun = !IsGrounded && !IsSliding &&
                                      Time.time >= m_LastWallRunEndTime + WallRunCooldown;

            if (eligibleToWallRun && CheckForWall(out RaycastHit wallHit, out Vector3 wallNormal))
            {
                Vector3 horizontalVelocity = Vector3.ProjectOnPlane(CharacterVelocity, Vector3.up);
                Vector3 moveInput = m_InputHandler.GetMoveInput();

                // catch the wall: needs forward intent, minimum speed, and to not already be moving strongly upward
                if (!IsWallRunning && moveInput.z > 0.1f && horizontalVelocity.magnitude >= MinSpeedToWallRun &&
                    CharacterVelocity.y <= 0.5f)
                {
                    IsWallRunning = true;
                    m_WallRunTimer = 0f;
                    CharacterVelocity += Vector3.up * WallRunInitialBoost;

                    // Keep the momentum carried onto the wall. Snapping every catch to WallRunSpeed halved a
                    // sprint-jump's speed the instant an air strafe brushed a wall.
                    Vector3 alongWall = Vector3.Cross(wallNormal, transform.up);
                    m_WallRunSpeed = Mathf.Max(WallRunSpeed, Mathf.Abs(Vector3.Dot(horizontalVelocity, alongWall.normalized)));
                }

                if (IsWallRunning)
                {
                    m_WallNormal = wallNormal;

                    // direction along the wall's surface, aligned with the way the player is facing
                    Vector3 wallForward = Vector3.Cross(m_WallNormal, transform.up);
                    if (Vector3.Dot(wallForward, transform.forward) < 0f)
                        wallForward = -wallForward;

                    CharacterVelocity = (wallForward * m_WallRunSpeed) + (Vector3.up * CharacterVelocity.y);

                    // reduced gravity while attached to the wall
                    CharacterVelocity += Vector3.down * WallRunGravityDownForce * Time.deltaTime;

                    // slight pull toward the wall so the raycast keeps detecting it every frame
                    CharacterVelocity -= m_WallNormal * 2f * Time.deltaTime;

                    m_WallRunTimer += Time.deltaTime;

                    if (m_InputHandler.GetJumpInputDown())
                    {
                        // wall jump: keep the forward speed you were running along the wall with, then add
                        // the push-away and upward components on top — losing that forward momentum here would
                        // kill any wall-run-into-slide chain before it gets a chance to build speed
                        Vector3 preservedForwardVelocity = Vector3.ProjectOnPlane(CharacterVelocity, Vector3.up);
                        CharacterVelocity = preservedForwardVelocity + (m_WallNormal * WallJumpForce * 0.6f) +
                                           (Vector3.up * WallJumpForce);
                        AudioSource.PlayOneShot(JumpSfx);
                        m_LastTimeJumped = Time.time;
                        HasJumpedThisFrame = true;
                        m_DoubleJumpUsed = false;
                        EndWallRun();
                    }
                    else if (m_WallRunTimer >= MaxWallRunDuration)
                    {
                        EndWallRun();
                    }
                }
            }
            else if (IsWallRunning)
            {
                EndWallRun();
            }

            if (IsGrounded && IsWallRunning)
            {
                EndWallRun();
            }
        }

        void UpdateCameraTilt()
        {
            // camera roll feedback: tilt away from the wall while wall running, lean into a slide, level out otherwise
            float targetTilt = 0f;
            if (IsWallRunning)
            {
                float side = Vector3.Dot(m_WallNormal, transform.right) > 0f ? -1f : 1f;
                targetTilt = WallRunCameraTilt * side;
            }
            else if (IsSliding)
            {
                targetTilt = SlideCameraTilt;
            }

            m_CurrentCameraTilt = Mathf.Lerp(m_CurrentCameraTilt, targetTilt, WallRunCameraTiltSharpness * Time.deltaTime);
        }

        void EndWallRun()
        {
            IsWallRunning = false;
            m_LastWallRunEndTime = Time.time;
        }

        void StartSlide(bool heldInput)
        {
            IsSliding = true;
            m_SlideHeld = heldInput;
            m_SlideStartFrame = Time.frameCount;
            m_SlideTimer = 0f;
            SetCrouchingState(true, false);

            Vector3 horizontalVelocity = Vector3.ProjectOnPlane(CharacterVelocity, Vector3.up);
            float currentSpeed = horizontalVelocity.magnitude;
            if (Time.time < m_NextSlideBoostTime) return;
            m_NextSlideBoostTime = Time.time + SlideBoostCooldown;

            // chaining a slide onto speed already above normal running (from a sprint-jump, or a previous
            // slide's leftover momentum) compounds it further; a slide from a standing start just gets the flat boost
            bool isChaining = currentSpeed > MaxSpeedOnGround * 1.05f;
            float boost = isChaining ? currentSpeed * (SlideChainSpeedMultiplier - 1f) : SlideInitialBoost;
            float boostedSpeed = Mathf.Max(currentSpeed, Mathf.Min(currentSpeed + boost, MaxSlideSpeed));

            CharacterVelocity += horizontalVelocity.normalized * (boostedSpeed - currentSpeed);
        }

        void UpdateSlide()
        {
            m_SlideTimer += Time.deltaTime;

            // decay horizontal speed over time (friction), leave vertical speed untouched
            Vector3 horizontalVelocity = Vector3.ProjectOnPlane(CharacterVelocity, Vector3.up);
            Vector3 decayedVelocity = Vector3.MoveTowards(horizontalVelocity, Vector3.zero, SlideFriction * Time.deltaTime);
            CharacterVelocity = decayedVelocity + (Vector3.up * CharacterVelocity.y);

            // A held slide (Ctrl) ends on release; a crouch-key slide (C) is cancelled by pressing crouch again.
            // The press that started a crouch-key slide is still "down" this frame, so it can't cancel it.
            bool cancelled = m_SlideHeld
                ? m_InputHandler.GetSlideInputReleased()
                : Time.frameCount != m_SlideStartFrame && m_InputHandler.GetCrouchInputDown();
            bool tooSlow = decayedVelocity.magnitude < MinSpeedToSlide * 0.4f;

            if (m_SlideTimer >= MaxSlideDuration || tooSlow || cancelled || !IsGrounded)
            {
                // stand back up only if the player let go of the slide (or cancelled it)
                EndSlide(cancelled);
            }
        }

        void EndSlide(bool standUp)
        {
            IsSliding = false;

            // hand any surplus speed off to grounded movement as a decaying momentum window,
            // instead of letting it get snapped back to the sprint cap instantly
            float exitSpeed = Vector3.ProjectOnPlane(CharacterVelocity, Vector3.up).magnitude;
            if (exitSpeed > MaxSpeedOnGround)
            {
                m_MomentumSpeed = exitSpeed;
                m_MomentumExpireTime = Time.time + SlideExitMomentumDuration;
            }

            if (standUp)
            {
                SetCrouchingState(false, false);
            }
        }

        void HandleDodge()
        {
            bool canTrigger = !IsDodging && !IsWallRunning &&
                               Time.time >= m_LastDodgeEndTime + DodgeCooldown;

            if (canTrigger && m_InputHandler.GetDodgeInputDown())
            {
                Vector3 moveInput = m_InputHandler.GetMoveInput();
                Vector3 localDir = new Vector3(moveInput.x, 0f, moveInput.z);

                // dodge in the direction of whatever movement key is held; default to forward if none
                Vector3 dodgeDir = localDir.sqrMagnitude > 0.01f
                    ? transform.TransformVector(localDir).normalized
                    : transform.forward;

                IsDodging = true;
                IsSliding = false;
                m_DodgeTimer = 0f;
                m_DodgeDirection = dodgeDir;
            }

            if (IsDodging)
            {
                m_DodgeTimer += Time.deltaTime;

                float verticalVelocity = CharacterVelocity.y;
                CharacterVelocity = (m_DodgeDirection * DodgeForce) + (Vector3.up * verticalVelocity);

                if (!IsGrounded)
                {
                    CharacterVelocity += Vector3.down * GravityDownForce * Time.deltaTime;
                }

                if (m_DodgeTimer >= DodgeDuration)
                {
                    IsDodging = false;
                    m_LastDodgeEndTime = Time.time;
                }
            }
        }

        void HandleCharacterMovement()
        {
            // horizontal character rotation
            {
                // rotate the transform with the input speed around its local Y axis
                transform.Rotate(
                    new Vector3(0f, (m_InputHandler.GetLookInputsHorizontal() * RotationSpeed * RotationMultiplier),
                        0f), Space.Self);
            }

            // vertical camera rotation
            {
                // add vertical inputs to the camera's vertical angle
                m_CameraVerticalAngle += m_InputHandler.GetLookInputsVertical() * RotationSpeed * RotationMultiplier;

                // limit the camera's vertical angle to min/max
                m_CameraVerticalAngle = Mathf.Clamp(m_CameraVerticalAngle, -89f, 89f);

                // apply the vertical angle as a local rotation to the camera transform along its right axis (makes it pivot up and down)
                // the z component applies a roll tilt for wall-running feedback
                PlayerCamera.transform.localEulerAngles = new Vector3(m_CameraVerticalAngle, 0, m_CurrentCameraTilt);
            }

            // character movement handling
            bool isSprinting = m_InputHandler.GetSprintInputHeld();
            {
                if (isSprinting && !IsSliding)
                {
                    isSprinting = SetCrouchingState(false, false);
                }

                if (m_InputHandler.GetWalkInputDown()) IsWalking = !IsWalking;
                if (isSprinting) IsWalking = false;

                float speedModifier = isSprinting ? SprintSpeedModifier : IsWalking ? WalkSpeedRatio : 1f;

                // converts move input to a worldspace vector based on our character's transform orientation
                Vector3 worldspaceMoveInput = transform.TransformVector(m_InputHandler.GetMoveInput());

                // handle grounded movement
                if (IsDodging)
                {
                    // HandleDodge() has already fully driven CharacterVelocity this frame
                }
                else if (IsGrounded)
                {
                    // normal sprint cap, unless we're still riding out momentum from a slide-hop chain
                    float effectiveMaxSpeed = MaxSpeedOnGround * speedModifier;
                    if (Time.time < m_MomentumExpireTime)
                    {
                        float remaining = Mathf.Clamp01((m_MomentumExpireTime - Time.time) / SlideExitMomentumDuration);
                        float decayedMomentum = Mathf.Lerp(effectiveMaxSpeed, m_MomentumSpeed, remaining);
                        effectiveMaxSpeed = Mathf.Max(effectiveMaxSpeed, decayedMomentum);
                    }

                    if (IsSliding)
                    {
                        // A slide runs on its own momentum (UpdateSlide applies the friction). Pulling it toward
                        // the crouch-walk target, as normal movement does, killed the slide within a few frames.
                        // Slopes speed it up or slow it down, and the input can only bend its direction.
                        Vector3 slideVelocity = Vector3.ProjectOnPlane(CharacterVelocity, m_GroundNormal);
                        slideVelocity += Vector3.ProjectOnPlane(Vector3.down, m_GroundNormal) *
                                         (GravityDownForce * SlideSlopeAcceleration * Time.deltaTime);
                        if (worldspaceMoveInput.sqrMagnitude > .01f && slideVelocity.sqrMagnitude > .01f)
                        {
                            Vector3 steerTarget = GetDirectionReorientedOnSlope(worldspaceMoveInput.normalized,
                                m_GroundNormal) * slideVelocity.magnitude;
                            slideVelocity = Vector3.RotateTowards(slideVelocity, steerTarget,
                                SlideSteerSpeed * Mathf.Deg2Rad * Time.deltaTime, 0f);
                        }

                        CharacterVelocity = Vector3.ClampMagnitude(slideVelocity, MaxSlideSpeed);
                    }
                    else
                    {
                        // calculate the desired velocity from inputs, max speed, and current slope
                        Vector3 targetVelocity = worldspaceMoveInput * effectiveMaxSpeed;
                        // reduce speed if crouching by crouch speed ratio
                        if (IsCrouching)
                            targetVelocity *= MaxSpeedCrouchedRatio;
                        targetVelocity = GetDirectionReorientedOnSlope(targetVelocity.normalized, m_GroundNormal) *
                                         targetVelocity.magnitude;

                        // smoothly interpolate between our current velocity and the target velocity based on acceleration speed
                        CharacterVelocity = Vector3.Lerp(CharacterVelocity, targetVelocity,
                            MovementSharpnessOnGround * Time.deltaTime);
                    }

                    // jumping
                    if (IsGrounded && m_InputHandler.GetJumpInputDown())
                    {
                        // force the crouch state to false
                        if (SetCrouchingState(false, false))
                        {
                            // a slide-jump carries the slide's speed through the momentum window
                            if (IsSliding)
                                EndSlide(false);

                            // start by canceling out the vertical component of our velocity
                            CharacterVelocity = new Vector3(CharacterVelocity.x, 0f, CharacterVelocity.z);

                            // then, add the jumpSpeed value upwards
                            CharacterVelocity += Vector3.up * JumpForce;

                            // play sound
                            AudioSource.PlayOneShot(JumpSfx);

                            // remember last time we jumped because we need to prevent snapping to ground for a short time
                            m_LastTimeJumped = Time.time;
                            HasJumpedThisFrame = true;

                            // Force grounding to false
                            IsGrounded = false;
                            m_GroundNormal = Vector3.up;
                        }
                    }

                    // footsteps sound
                    float chosenFootstepSfxFrequency =
                        (isSprinting ? FootstepSfxFrequencyWhileSprinting : FootstepSfxFrequency);
                    if (m_FootstepDistanceCounter >= 1f / chosenFootstepSfxFrequency)
                    {
                        m_FootstepDistanceCounter = 0f;
                        AudioSource.PlayOneShot(FootstepSfx);
                    }

                    // keep track of distance traveled for footsteps sound (no steps while sliding)
                    if (!IsSliding)
                        m_FootstepDistanceCounter += CharacterVelocity.magnitude * Time.deltaTime;
                }
                // handle air movement
                else if (!IsWallRunning)
                {
                    // double jump: one extra jump usable while airborne, Titanfall-style
                    if (!m_DoubleJumpUsed && m_InputHandler.GetJumpInputDown())
                    {
                        CharacterVelocity = new Vector3(CharacterVelocity.x, 0f, CharacterVelocity.z);
                        CharacterVelocity += Vector3.up * DoubleJumpForce;
                        AudioSource.PlayOneShot(JumpSfx);
                        m_DoubleJumpUsed = true;
                        HasJumpedThisFrame = true;
                    }

                    // add air acceleration
                    Vector3 preAccelHorizontal = Vector3.ProjectOnPlane(CharacterVelocity, Vector3.up);
                    CharacterVelocity += worldspaceMoveInput * AccelerationSpeedInAir * Time.deltaTime;

                    // limit air speed to a maximum, but only horizontally — never shave speed below what we
                    // already had, so momentum carried out of a slide-hop survives the jump instead of getting clamped away
                    float verticalVelocity = CharacterVelocity.y;
                    Vector3 horizontalVelocity = Vector3.ProjectOnPlane(CharacterVelocity, Vector3.up);
                    float airSpeedCap = Mathf.Max(MaxSpeedInAir * speedModifier, preAccelHorizontal.magnitude);
                    horizontalVelocity = Vector3.ClampMagnitude(horizontalVelocity, airSpeedCap);
                    CharacterVelocity = horizontalVelocity + (Vector3.up * verticalVelocity);

                    // apply the gravity to the velocity
                    CharacterVelocity += Vector3.down * GravityDownForce * Time.deltaTime;
                }
                // while wall running, HandleWallRun() has already fully driven CharacterVelocity this frame
            }

            // apply the final calculated velocity value as a character movement
            Vector3 capsuleBottomBeforeMove = GetCapsuleBottomHemisphere();
            Vector3 capsuleTopBeforeMove = GetCapsuleTopHemisphere(m_Controller.height);
            m_Controller.Move(CharacterVelocity * Time.deltaTime);

            // detect obstructions to adjust velocity accordingly
            m_LatestImpactSpeed = Vector3.zero;
            if (Physics.CapsuleCast(capsuleBottomBeforeMove, capsuleTopBeforeMove, m_Controller.radius,
                CharacterVelocity.normalized, out RaycastHit hit, CharacterVelocity.magnitude * Time.deltaTime, -1,
                QueryTriggerInteraction.Ignore))
            {
                // We remember the last impact speed because the fall damage logic might need it
                m_LatestImpactSpeed = CharacterVelocity;

                CharacterVelocity = Vector3.ProjectOnPlane(CharacterVelocity, hit.normal);
            }
        }

        // Returns true if the slope angle represented by the given normal is under the slope angle limit of the character controller
        bool IsNormalUnderSlopeLimit(Vector3 normal)
        {
            return Vector3.Angle(transform.up, normal) <= m_Controller.slopeLimit;
        }

        // Gets the center point of the bottom hemisphere of the character controller capsule    
        Vector3 GetCapsuleBottomHemisphere()
        {
            return transform.position + (transform.up * m_Controller.radius);
        }

        // Gets the center point of the top hemisphere of the character controller capsule    
        Vector3 GetCapsuleTopHemisphere(float atHeight)
        {
            return transform.position + (transform.up * (atHeight - m_Controller.radius));
        }

        // Gets a reoriented direction that is tangent to a given slope
        public Vector3 GetDirectionReorientedOnSlope(Vector3 direction, Vector3 slopeNormal)
        {
            Vector3 directionRight = Vector3.Cross(direction, transform.up);
            return Vector3.Cross(slopeNormal, directionRight).normalized;
        }

        void UpdateCharacterHeight(bool force)
        {
            // Update height instantly
            if (force)
            {
                m_Controller.height = m_TargetCharacterHeight;
                m_Controller.center = Vector3.up * m_Controller.height * 0.5f;
                PlayerCamera.transform.localPosition = Vector3.up * m_TargetCharacterHeight * CameraHeightRatio;
                m_Actor.AimPoint.transform.localPosition = m_Controller.center;
            }
            // Update smooth height
            else if (m_Controller.height != m_TargetCharacterHeight)
            {
                // resize the capsule and adjust camera position
                m_Controller.height = Mathf.Lerp(m_Controller.height, m_TargetCharacterHeight,
                    CrouchingSharpness * Time.deltaTime);
                m_Controller.center = Vector3.up * m_Controller.height * 0.5f;
                PlayerCamera.transform.localPosition = Vector3.Lerp(PlayerCamera.transform.localPosition,
                    Vector3.up * m_TargetCharacterHeight * CameraHeightRatio, CrouchingSharpness * Time.deltaTime);
                m_Actor.AimPoint.transform.localPosition = m_Controller.center;
            }
        }

        // returns false if there was an obstruction
        bool SetCrouchingState(bool crouched, bool ignoreObstructions)
        {
            // set appropriate heights
            if (crouched)
            {
                m_TargetCharacterHeight = CapsuleHeightCrouching;
            }
            else
            {
                // Detect obstructions
                if (!ignoreObstructions)
                {
                    Collider[] standingOverlaps = Physics.OverlapCapsule(
                        GetCapsuleBottomHemisphere(),
                        GetCapsuleTopHemisphere(CapsuleHeightStanding),
                        m_Controller.radius,
                        StandUpObstructionLayers,
                        QueryTriggerInteraction.Ignore);
                    foreach (Collider c in standingOverlaps)
                    {
                        if (c != m_Controller)
                        {
                            return false;
                        }
                    }
                }

                m_TargetCharacterHeight = CapsuleHeightStanding;
            }

            if (OnStanceChanged != null)
            {
                OnStanceChanged.Invoke(crouched);
            }

            IsCrouching = crouched;
            return true;
        }
    }
}