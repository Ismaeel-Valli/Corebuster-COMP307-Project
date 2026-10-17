using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.AI
{
    /// <summary>
    /// Forearm-mounted enemy cannon that fires large, slow rivet bolts in short bursts.
    /// The barrel tracks the target, a warning lamp flashes, then each bolt kicks the barrel back.
    /// Local +Z of AimPivot is the firing direction. Works on any robot with an EnemyController
    /// in its parents, regardless of which movement brain drives that robot. On a Robot1 the
    /// carrying arm is raised at the target first, so the cannon never fires from a lowered arm.
    /// </summary>
    // Runs after Robot1ProceduralAnimator so aiming and firing use this frame's final arm pose.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(WeaponController))]
    public class RivetCannon : MonoBehaviour
    {
        public enum CannonPhase { Idle, Aiming, Warning, Burst, Cooldown }

        [Header("Parts")]
        [Tooltip("Rotates to face the target. Should contain the barrel and the weapon muzzle.")]
        public Transform AimPivot;
        [Tooltip("Slides back along local -Z on every shot.")]
        public Transform Barrel;
        public Renderer WarningLamp;

        [Header("Aiming")]
        [Tooltip("Degrees per second the barrel turns while acquiring a target.")]
        [Min(1f)] public float AimTurnSpeed = 220f;
        [Tooltip("Degrees per second the barrel turns during a burst. Low values let a strafing player out-run the bolts.")]
        [Min(0f)] public float BurstTurnSpeed = 35f;
        [Tooltip("Maximum angle the barrel may swing away from its rest direction on the arm.")]
        [Range(0f, 180f)] public float MaxAimAngle = 75f;
        [Tooltip("The barrel must be within this many degrees of the target before the warning starts.")]
        [Range(.5f, 45f)] public float AimTolerance = 6f;

        [Header("Burst")]
        [Min(1)] public int ShotsPerBurst = 4;
        [Min(.02f)] public float ShotInterval = .22f;
        [Min(0f)] public float WarningDuration = .65f;
        [Min(0f)] public float CooldownDuration = 1.8f;

        [Header("Recoil")]
        public float RecoilDistance = .2f;
        [Min(.01f)] public float RecoilDuration = .14f;

        [Header("Warning Lamp")]
        public Color WarningColor = new Color(1f, .55f, .05f);
        public Color FiringColor = new Color(1f, .12f, .02f);
        public Color FrozenColor = Color.cyan;
        [Min(0f)] public float WarningFlashRate = 9f;
        [Min(0f)] public float LampIntensity = 3f;
        public AudioClip WarningSfx;

        [Header("Carrying Arm")]
        [Tooltip("Raise the robot arm that carries the cannon at the target before winding up (Robot1 only).")]
        public bool RaiseArm = true;
        [Tooltip("How quickly the arm lifts into and drops out of the aiming pose (full raises per second).")]
        [Min(.1f)] public float ArmRaiseSpeed = 4f;
        [Tooltip("Stop the robot's chase during the warning and burst, then let it close in again.")]
        public bool PlantFeetWhileFiring = true;

        public CannonPhase Phase { get; private set; }
        public bool IsFrozen { get; private set; }

        WeaponController m_Weapon;
        EnemyController m_Enemy;
        Health m_Health;
        GameFlowManager m_GameFlow;
        AudioSource m_Audio;
        MaterialPropertyBlock m_LampProperties;
        Quaternion m_PivotRestLocalRotation;
        Vector3 m_BarrelRestLocalPosition;
        float m_Elapsed;
        float m_RecoilTimer;
        int m_ShotsFired;
        bool m_Dead;
        Robot1ProceduralAnimator m_ArmAnimator;
        Robot1ChaseController m_Chase;
        int m_ArmSide = 1;
        float m_ArmRaise;
        Vector3 m_LastAimPoint;

        bool ArmReady => !m_ArmAnimator || !RaiseArm || m_ArmRaise >= .95f;

        void Awake()
        {
            m_Weapon = GetComponent<WeaponController>();
            m_Audio = GetComponent<AudioSource>();
            m_Enemy = GetComponentInParent<EnemyController>();
            m_Health = GetComponentInParent<Health>();
            m_GameFlow = FindAnyObjectByType<GameFlowManager>();
            m_LampProperties = new MaterialPropertyBlock();

            // EnemyController.TryAtack only ever sends a "held" input. Manual mode ignores that,
            // so this component alone decides when each bolt of the burst is fired.
            m_Weapon.ShootType = WeaponShootType.Manual;

            if (!AimPivot) AimPivot = m_Weapon.WeaponMuzzle ? m_Weapon.WeaponMuzzle.parent : transform;
            m_PivotRestLocalRotation = AimPivot.localRotation;
            if (Barrel) m_BarrelRestLocalPosition = Barrel.localPosition;

            m_ArmAnimator = GetComponentInParent<Robot1ProceduralAnimator>();
            m_ArmSide = SideOfCarryingArm();
            m_Chase = GetComponentInParent<Robot1ChaseController>();

            if (m_Health) m_Health.OnDie += OnDeath;
            SetLamp(Color.black);
        }

        void OnDestroy() { if (m_Health) m_Health.OnDie -= OnDeath; }
        void OnDeath() { m_Dead = true; Cancel(); LowerArmImmediate(); }
        void OnDisable() { Cancel(); LowerArmImmediate(); }

        int SideOfCarryingArm()
        {
            Transform root = m_ArmAnimator ? m_ArmAnimator.transform : null;
            for (Transform current = transform.parent; current && current != root; current = current.parent)
            {
                string name = current.name.ToLowerInvariant();
                if (name.Contains("left")) return -1;
                if (name.Contains("right")) return 1;
            }
            // Rigs like "LowerArm.L" don't spell out the side; use which side of the body the cannon is on.
            return root && root.InverseTransformPoint(transform.position).x < 0f ? -1 : 1;
        }

        void LowerArmImmediate()
        {
            m_ArmRaise = 0f;
            if (m_ArmAnimator) m_ArmAnimator.SetAimArm(0, 0f, Vector3.zero);
        }

        void UpdateArm(bool engaged, GameObject target)
        {
            if (!m_ArmAnimator || !RaiseArm) return;
            if (target) m_LastAimPoint = TargetAimPosition(target);
            m_ArmRaise = Mathf.MoveTowards(m_ArmRaise, engaged ? 1f : 0f, ArmRaiseSpeed * Time.deltaTime);
            float eased = m_ArmRaise * m_ArmRaise * (3f - 2f * m_ArmRaise);
            m_ArmAnimator.SetAimArm(m_ArmSide, eased, m_LastAimPoint);
        }

        // Hook for the Disassembler freeze. Thawing always restarts from a fresh wind-up.
        public void SetFrozen(bool frozen)
        {
            if (IsFrozen == frozen) return;
            IsFrozen = frozen;
            Cancel();
        }

        void Cancel()
        {
            Phase = CannonPhase.Idle;
            m_Elapsed = 0f;
            m_ShotsFired = 0;
            if (m_Chase) m_Chase.HoldPosition = false;
            SetLamp(IsFrozen ? FrozenColor : Color.black);
        }

        void LateUpdate()
        {
            UpdateRecoil();

            if (m_Dead || IsFrozen || !m_Enemy || !m_Enemy.enabled || Time.deltaTime <= 0f)
            {
                UpdateArm(false, null);
                return;
            }
            if (m_GameFlow && m_GameFlow.GameIsEnding) { if (Phase != CannonPhase.Idle) Cancel(); UpdateArm(false, null); return; }

            GameObject target = m_Enemy.KnownDetectedTarget;
            bool canEngage = target && m_Enemy.IsSeeingTarget && m_Enemy.IsTargetInAttackRange;
            m_Elapsed += Time.deltaTime;
            UpdateArm(Phase == CannonPhase.Aiming || Phase == CannonPhase.Warning || Phase == CannonPhase.Burst ||
                      (Phase == CannonPhase.Idle && canEngage), target);

            switch (Phase)
            {
                case CannonPhase.Idle:
                    ReturnToRest(AimTurnSpeed * .5f);
                    if (canEngage && m_Weapon.GetCurrentAmmo() >= 1) Enter(CannonPhase.Aiming);
                    break;

                case CannonPhase.Aiming:
                    if (!canEngage) { Enter(CannonPhase.Idle); break; }
                    // Only telegraph once the arm is up and the barrel is actually pointing at the
                    // target, so the warning always means "bolts are about to come this way".
                    if (AimAt(TargetAimPosition(target), AimTurnSpeed, out bool onTarget) && onTarget && ArmReady)
                    {
                        Enter(CannonPhase.Warning);
                        if (WarningSfx && m_Audio) m_Audio.PlayOneShot(WarningSfx);
                    }
                    break;

                case CannonPhase.Warning:
                    if (!canEngage) { Enter(CannonPhase.Cooldown); break; }
                    AimAt(TargetAimPosition(target), AimTurnSpeed, out _);
                    float flash = Mathf.PingPong(m_Elapsed * WarningFlashRate, 1f) > .5f ? 1f : .15f;
                    SetLamp(WarningColor * (LampIntensity * flash));
                    if (m_Elapsed >= WarningDuration)
                    {
                        Enter(CannonPhase.Burst);
                        m_ShotsFired = 0;
                        m_Enemy.onAttack?.Invoke();
                        FireBolt();
                    }
                    break;

                case CannonPhase.Burst:
                    // The burst is committed once it starts; the slow turn rate is what makes it dodgeable.
                    if (target) AimAt(TargetAimPosition(target), BurstTurnSpeed, out _);
                    SetLamp(FiringColor * LampIntensity);
                    if (m_Elapsed >= ShotInterval) FireBolt();
                    break;

                case CannonPhase.Cooldown:
                    ReturnToRest(AimTurnSpeed * .25f);
                    if (m_Elapsed >= CooldownDuration) Enter(CannonPhase.Idle);
                    break;
            }
        }

        void Enter(CannonPhase phase)
        {
            Phase = phase;
            m_Elapsed = 0f;
            if (m_Chase)
                m_Chase.HoldPosition = PlantFeetWhileFiring &&
                                       (phase == CannonPhase.Warning || phase == CannonPhase.Burst);
            if (phase != CannonPhase.Warning && phase != CannonPhase.Burst) SetLamp(Color.black);
        }

        void FireBolt()
        {
            // EnemyController assigns Owner lazily; projectiles need it to ignore their own robot.
            if (!m_Weapon.Owner) m_Weapon.Owner = m_Enemy.gameObject;

            m_Elapsed = 0f;
            if (m_Weapon.HandleShootInputs(true, false, false))
            {
                m_ShotsFired++;
                m_RecoilTimer = RecoilDuration;
            }
            else
            {
                // Out of ammo or still inside the weapon's own shot delay: end the burst early.
                m_ShotsFired = ShotsPerBurst;
            }

            if (m_ShotsFired >= ShotsPerBurst) Enter(CannonPhase.Cooldown);
        }

        /// <summary>Turns the pivot toward a world point within MaxAimAngle of rest. Returns false if no valid direction.</summary>
        bool AimAt(Vector3 worldPoint, float turnSpeed, out bool onTarget)
        {
            onTarget = false;
            Vector3 toTarget = worldPoint - AimPivot.position;
            if (toTarget.sqrMagnitude < .0001f) return false;

            Transform parent = AimPivot.parent;
            Quaternion parentRotation = parent ? parent.rotation : Quaternion.identity;
            Vector3 restForward = parentRotation * (m_PivotRestLocalRotation * Vector3.forward);
            Vector3 restUp = parentRotation * (m_PivotRestLocalRotation * Vector3.up);

            Vector3 desired = toTarget.normalized;
            float offRest = Vector3.Angle(restForward, desired);
            bool inCone = offRest <= MaxAimAngle;
            if (!inCone)
                desired = Vector3.RotateTowards(restForward, desired, MaxAimAngle * Mathf.Deg2Rad, 0f);

            Quaternion desiredWorld = Quaternion.LookRotation(desired, restUp);
            AimPivot.rotation = Quaternion.RotateTowards(AimPivot.rotation, desiredWorld, turnSpeed * Time.deltaTime);

            onTarget = inCone && Vector3.Angle(AimPivot.forward, toTarget) <= AimTolerance;
            return true;
        }

        void ReturnToRest(float turnSpeed)
        {
            AimPivot.localRotation = Quaternion.RotateTowards(AimPivot.localRotation, m_PivotRestLocalRotation,
                turnSpeed * Time.deltaTime);
        }

        void UpdateRecoil()
        {
            if (!Barrel) return;
            if (m_RecoilTimer > 0f) m_RecoilTimer = Mathf.Max(0f, m_RecoilTimer - Time.deltaTime);
            // Instant kick, eased return.
            float kick = m_RecoilTimer / Mathf.Max(.01f, RecoilDuration);
            Barrel.localPosition = m_BarrelRestLocalPosition + Vector3.back * (RecoilDistance * kick * kick);
        }

        Vector3 TargetAimPosition(GameObject target)
        {
            Actor actor = target.GetComponentInParent<Actor>();
            return actor && actor.AimPoint ? actor.AimPoint.position : target.transform.position;
        }

        void SetLamp(Color color)
        {
            if (!WarningLamp || m_LampProperties == null) return;
            m_LampProperties.SetColor("_BaseColor", color);
            m_LampProperties.SetColor("_Color", color);
            m_LampProperties.SetColor("_EmissionColor", color);
            WarningLamp.SetPropertyBlock(m_LampProperties);
        }
    }
}
