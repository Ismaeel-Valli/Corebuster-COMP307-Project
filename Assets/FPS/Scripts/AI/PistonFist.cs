using System;
using System.Collections.Generic;
using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.AI
{
    /// <summary>
    /// A melee enemy with one or two piston arms. Local +Z of each arm is the punch direction.
    /// With two arms the attacks alternate, which Robot1ProceduralAnimator plays as left/right hooks.
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class PistonFist : MonoBehaviour
    {
        public enum AttackPhase { Ready, Retract, Warning, Punch, Return, Cooldown }

        public Transform Fist;
        public Transform SlidingCylinder;
        public Renderer WarningLamp;
        [Tooltip("The arm/forearm attachment transform that carries the piston geometry.")]
        public Transform ArmPivot;
        [Tooltip("Optional second piston arm on the other forearm. Attacks alternate between the two.")]
        public Transform SecondArmPivot;
        public bool AlignArmToBoneDirection = true;
        public bool RaiseArmDuringAttack = true;
        [Min(.1f)] public float AimArmBlendSpeed = 14f;
        [Min(.1f)] public float CloseDamageRadius = 3.25f;
        public Vector3 HitHalfExtents = new Vector3(.55f, .45f, .4f);
        public float Damage = 25f;
        public float RestExtension = 1f;
        public float RetractedExtension = .45f;
        public float PunchExtension = 3f;
        [Min(.01f)] public float RetractDuration = .4f;
        [Min(.01f)] public float WarningDuration = .45f;
        [Min(.01f)] public float PunchDuration = .16f;
        [Min(.01f)] public float ReturnDuration = .45f;
        [Min(.01f)] public float CooldownDuration = 1.1f;
        public LayerMask HitMask = ~0;
        public AttackPhase Phase { get; private set; }
        public bool IsFrozen { get; private set; }

        /// <summary>+1 when the current attack uses the robot's right arm, -1 for the left.</summary>
        public int CurrentSide => Arm != null ? Arm.Side : 1;

        class ArmRig
        {
            public Transform Pivot;
            public Transform Fist;
            public Transform Cylinder;
            public Renderer Lamp;
            public int Side = 1;
            public Vector3 FistRestLocalPosition;
            public Vector3 CylinderRestLocalPosition;
            public Vector3 CylinderRestLocalScale;
            public bool HasAuthoredPartPose;
            public Quaternion RestLocalRotation = Quaternion.identity;
            public bool HasRestPose;
        }

        EnemyController m_Enemy;
        Health m_Health;
        Actor m_Actor;
        GameFlowManager m_GameFlow;
        Robot1ProceduralAnimator m_ProceduralAnimator;
        MaterialPropertyBlock m_LampProperties;
        readonly List<ArmRig> m_Arms = new List<ArmRig>();
        int m_ArmIndex;
        float m_Elapsed;
        bool m_DidHit;
        bool m_Dead;
        Vector3 m_AttackDirection;

        ArmRig Arm => m_Arms.Count > 0 ? m_Arms[m_ArmIndex] : null;

        void Awake()
        {
            // The fist can live on the robot root or on an attached arm object.
            m_Enemy = GetComponentInParent<EnemyController>();
            m_Health = GetComponentInParent<Health>();
            m_Actor = GetComponentInParent<Actor>();
            m_GameFlow = FindAnyObjectByType<GameFlowManager>();
            m_ProceduralAnimator = GetComponentInParent<Robot1ProceduralAnimator>();
            m_LampProperties = new MaterialPropertyBlock();
            AutoBindParts();
            foreach (var arm in m_Arms)
            {
                CaptureAuthoredPartPose(arm);
                SetExtension(arm, RestExtension);
                CaptureAndAlignArm(arm);
            }
            // Start on the last arm so the first attack uses the primary (ArmPivot) arm.
            m_ArmIndex = Mathf.Max(0, m_Arms.Count - 1);
            if (m_Health) m_Health.OnDie += OnDeath;
        }

        void OnDestroy() { if (m_Health) m_Health.OnDie -= OnDeath; }
        void OnDeath() { m_Dead = true; CancelAttack(); }
        void OnDisable() { CancelAttack(); }

        // There is no freeze service in the current project. A future freeze source
        // can call this without changing Health or damage routing. Thaw requires a new wind-up.
        public void SetFrozen(bool frozen)
        {
            if (IsFrozen == frozen) return;
            IsFrozen = frozen;
            CancelAttack();
        }

        void CancelAttack()
        {
            Phase = AttackPhase.Cooldown;
            m_Elapsed = 0f;
            m_DidHit = true;
            if (m_ProceduralAnimator)
            {
                m_ProceduralAnimator.SetAttackPose(false);
                m_ProceduralAnimator.SetHook(0, 0f, 0f);
            }
            foreach (var arm in m_Arms)
            {
                SetExtension(arm, RestExtension);
                RestoreArmPoseImmediate(arm);
                SetLamp(arm, IsFrozen ? Color.cyan : Color.black);
            }
        }

        void Update()
        {
            if (m_Dead || IsFrozen || !m_Enemy || !m_Enemy.enabled || Time.deltaTime <= 0f) return;
            if (m_GameFlow && m_GameFlow.GameIsEnding) { CancelAttack(); return; }
            if (Arm == null || !Arm.Fist || !m_Enemy.DetectionModule) return;

            if (Phase == AttackPhase.Ready)
            {
                var target = m_Enemy.KnownDetectedTarget;
                if (!target || !m_Enemy.IsSeeingTarget) return;
                m_Enemy.OrientTowards(target.transform.position);
                if (!m_Enemy.IsTargetInAttackRange) return;

                // Alternate arms so consecutive attacks read as left and right hooks.
                m_ArmIndex = (m_ArmIndex + 1) % m_Arms.Count;
                m_AttackDirection = TargetAimPosition(target) - (Arm.Pivot ? Arm.Pivot.position : transform.position);
                if (m_AttackDirection.sqrMagnitude < .0001f) m_AttackDirection = transform.forward;
                m_AttackDirection.Normalize();
                // Lock the complete telegraphed attack to the target's position at wind-up.
                m_DidHit = false;
                Enter(AttackPhase.Retract);
                return;
            }

            m_Elapsed += Time.deltaTime;
            switch (Phase)
            {
                case AttackPhase.Retract:
                    ApplyArmAim(m_AttackDirection, Time.deltaTime, false);
                    SetExtension(Arm, Mathf.Lerp(RestExtension, RetractedExtension, Progress(RetractDuration)));
                    SetLamp(Arm, new Color(1f, .35f, 0f));
                    SetHook(Smooth(Progress(RetractDuration)), 0f);
                    if (m_Elapsed >= RetractDuration) Enter(AttackPhase.Warning);
                    break;
                case AttackPhase.Warning:
                    ApplyArmAim(m_AttackDirection, Time.deltaTime, false);
                    SetLamp(Arm, new Color(1f, .35f, 0f) * (1f + Mathf.PingPong(m_Elapsed * 8f, 2f)));
                    SetHook(1f, 0f);
                    if (m_Elapsed >= WarningDuration)
                    {
                        Enter(AttackPhase.Punch);
                        m_Enemy.onAttack?.Invoke();
                    }
                    break;
                case AttackPhase.Punch:
                    ApplyArmAim(m_AttackDirection, Time.deltaTime, false);
                    Vector3 previous = Arm.Fist.position;
                    SetExtension(Arm, Mathf.Lerp(RetractedExtension, PunchExtension, Progress(PunchDuration)));
                    SetLamp(Arm, Color.red * 3f);
                    // Ease out so the fist whips through the target rather than drifting into it.
                    float strike = 1f - (1f - Progress(PunchDuration)) * (1f - Progress(PunchDuration));
                    SetHook(1f, strike);
                    SweepDamage(previous, Arm.Fist.position);
                    if (m_Elapsed >= PunchDuration) Enter(AttackPhase.Return);
                    break;
                case AttackPhase.Return:
                    ApplyArmAim(m_AttackDirection, Time.deltaTime, true);
                    SetExtension(Arm, Mathf.Lerp(PunchExtension, RestExtension, Progress(ReturnDuration)));
                    SetLamp(Arm, Color.black);
                    SetHook(1f - Smooth(Progress(ReturnDuration)), 1f);
                    if (m_Elapsed >= ReturnDuration) Enter(AttackPhase.Cooldown);
                    break;
                case AttackPhase.Cooldown:
                    ApplyArmAim(m_AttackDirection, Time.deltaTime, true);
                    SetHook(0f, 0f);
                    if (m_Elapsed >= CooldownDuration) Enter(AttackPhase.Ready);
                    break;
            }
        }

        float Progress(float duration) => Mathf.Clamp01(m_Elapsed / Mathf.Max(.01f, duration));
        static float Smooth(float t) => t * t * (3f - 2f * t);

        void SetHook(float weight, float strike)
        {
            if (m_ProceduralAnimator) m_ProceduralAnimator.SetHook(CurrentSide, weight, strike);
        }

        void Enter(AttackPhase phase)
        {
            Phase = phase;
            m_Elapsed = 0f;
            if (m_ProceduralAnimator)
            {
                bool raiseArms = phase == AttackPhase.Retract || phase == AttackPhase.Warning ||
                                 phase == AttackPhase.Punch;
                m_ProceduralAnimator.SetAttackPose(raiseArms);
            }
        }

        void AutoBindParts()
        {
            var pivots = new List<Transform>();
            foreach (Transform candidate in GetComponentsInChildren<Transform>(true))
                if (candidate.name.StartsWith("Robot1 Piston Fist Arm"))
                    pivots.Add(candidate);

            if (!ArmPivot)
            {
                ArmPivot = pivots.Find(p => p.name == "Robot1 Piston Fist Arm (1)");
                if (!ArmPivot && pivots.Count > 0) ArmPivot = pivots[0];
            }
            if (!SecondArmPivot)
                SecondArmPivot = pivots.Find(p => p != ArmPivot);

            if (ArmPivot || Fist)
                m_Arms.Add(BindArm(ArmPivot, Fist, SlidingCylinder, WarningLamp));
            if (SecondArmPivot && SecondArmPivot != ArmPivot)
                m_Arms.Add(BindArm(SecondArmPivot, null, null, null));

            // Keep the public fields pointing at the primary arm for existing tools and checks.
            if (m_Arms.Count > 0)
            {
                Fist = m_Arms[0].Fist;
                SlidingCylinder = m_Arms[0].Cylinder;
                WarningLamp = m_Arms[0].Lamp;
            }
        }

        ArmRig BindArm(Transform pivot, Transform fist, Transform cylinder, Renderer lamp)
        {
            var arm = new ArmRig { Pivot = pivot, Fist = fist, Cylinder = cylinder, Lamp = lamp };
            if (pivot)
            {
                foreach (Transform candidate in pivot.GetComponentsInChildren<Transform>(true))
                {
                    if (!arm.Fist && candidate.name == "Piston Fist")
                        arm.Fist = candidate;
                    else if (!arm.Cylinder && candidate.name == "Piston sliding cylinder")
                        arm.Cylinder = candidate;
                    else if (!arm.Lamp && candidate.name == "Piston warning lamp")
                        arm.Lamp = candidate.GetComponent<Renderer>();
                }
            }
            arm.Side = SideOf(pivot ? pivot : transform);
            return arm;
        }

        int SideOf(Transform t)
        {
            for (Transform current = t; current && current != transform; current = current.parent)
            {
                string name = current.name.ToLowerInvariant();
                if (name.Contains("left")) return -1;
                if (name.Contains("right")) return 1;
            }
            return transform.InverseTransformPoint(t.position).x < 0f ? -1 : 1;
        }

        void CaptureAuthoredPartPose(ArmRig arm)
        {
            if (!arm.Fist) return;
            arm.FistRestLocalPosition = arm.Fist.localPosition;
            if (arm.Cylinder)
            {
                arm.CylinderRestLocalPosition = arm.Cylinder.localPosition;
                arm.CylinderRestLocalScale = arm.Cylinder.localScale;
            }
            arm.HasAuthoredPartPose = true;
        }

        void CaptureAndAlignArm(ArmRig arm)
        {
            if (!arm.Pivot) return;

            if (AlignArmToBoneDirection)
            {
                Transform socket = arm.Pivot.parent;
                Transform armBone = FindArmBone(socket);
                if (socket && armBone)
                {
                    Vector3 armDirection = socket.position - armBone.position;
                    if (armDirection.sqrMagnitude > .0001f)
                    {
                        armDirection.Normalize();
                        arm.Pivot.rotation = Quaternion.LookRotation(armDirection, StableUp(armDirection));

                        // The imported piston mesh is authored with its housing at local Y=1.2.
                        float worldScale = Mathf.Max(.0001f, Mathf.Abs(arm.Pivot.lossyScale.y));
                        arm.Pivot.position = socket.position - arm.Pivot.up * (1.2f * worldScale);
                    }
                }
            }

            arm.RestLocalRotation = arm.Pivot.localRotation;
            arm.HasRestPose = true;
        }

        Transform FindArmBone(Transform socket)
        {
            for (Transform current = socket; current; current = current.parent)
            {
                string name = current.name.ToLowerInvariant();
                if (name.Contains("forearm") || name.Contains("lowerarm") || name.Contains("upperarm") ||
                    name == "arm" || name.EndsWith(" arm"))
                    return current;
            }
            return null;
        }

        Vector3 StableUp(Vector3 direction)
        {
            Vector3 up = Vector3.ProjectOnPlane(transform.up, direction);
            if (up.sqrMagnitude < .0001f) up = Vector3.ProjectOnPlane(transform.forward, direction);
            return up.sqrMagnitude > .0001f ? up.normalized : Vector3.up;
        }

        void ApplyArmAim(Vector3 direction, float deltaTime, bool restore)
        {
            ArmRig arm = Arm;
            if (arm == null || !arm.Pivot || !arm.HasRestPose || !RaiseArmDuringAttack) return;

            // Robot1ProceduralAnimator owns the real arm bones and poses the hook.
            // Do not counter-rotate the attached piston beneath it.
            if (m_ProceduralAnimator) return;

            Quaternion desired = arm.RestLocalRotation;
            if (!restore && direction.sqrMagnitude > .0001f)
            {
                Quaternion worldRotation = Quaternion.LookRotation(direction.normalized, StableUp(direction));
                desired = arm.Pivot.parent
                    ? Quaternion.Inverse(arm.Pivot.parent.rotation) * worldRotation
                    : worldRotation;
            }

            float blend = 1f - Mathf.Exp(-AimArmBlendSpeed * Mathf.Max(0f, deltaTime));
            arm.Pivot.localRotation = Quaternion.Slerp(arm.Pivot.localRotation, desired, blend);
        }

        void RestoreArmPoseImmediate(ArmRig arm)
        {
            if (arm.Pivot && arm.HasRestPose && RaiseArmDuringAttack && !m_ProceduralAnimator)
                arm.Pivot.localRotation = arm.RestLocalRotation;
        }

        Vector3 TargetAimPosition(GameObject target)
        {
            Actor actor = target ? target.GetComponentInParent<Actor>() : null;
            if (actor && actor.AimPoint) return actor.AimPoint.position;
            return target ? target.transform.position : transform.position + transform.forward;
        }

        void SetExtension(ArmRig arm, float extension)
        {
            if (arm == null) return;
            float extensionDelta = extension - RestExtension;
            if (arm.Fist)
                arm.Fist.localPosition = arm.HasAuthoredPartPose
                    ? arm.FistRestLocalPosition + Vector3.forward * extensionDelta
                    : new Vector3(0f, 1.2f, extension);

            if (arm.Cylinder)
            {
                if (arm.HasAuthoredPartPose)
                {
                    arm.Cylinder.localPosition = arm.CylinderRestLocalPosition + Vector3.forward * (extensionDelta * .5f);
                    float extensionRatio = extension / Mathf.Max(.01f, RestExtension);
                    arm.Cylinder.localScale = new Vector3(arm.CylinderRestLocalScale.x,
                        Mathf.Max(.05f, arm.CylinderRestLocalScale.y * extensionRatio),
                        arm.CylinderRestLocalScale.z);
                }
                else
                {
                    arm.Cylinder.localPosition = new Vector3(0f, 1.2f, extension * .5f);
                    arm.Cylinder.localScale = new Vector3(.28f, Mathf.Max(.05f, extension * .5f), .28f);
                }
            }
        }

        void SetLamp(ArmRig arm, Color color)
        {
            if (arm == null || !arm.Lamp || m_LampProperties == null) return;
            m_LampProperties.SetColor("_BaseColor", color);
            m_LampProperties.SetColor("_Color", color);
            m_LampProperties.SetColor("_EmissionColor", color);
            arm.Lamp.SetPropertyBlock(m_LampProperties);
        }

        void SweepDamage(Vector3 from, Vector3 to)
        {
            if (Phase != AttackPhase.Punch || IsFrozen || m_Dead || m_DidHit) return;
            Vector3 scale = transform.lossyScale;
            Vector3 half = Vector3.Scale(HitHalfExtents, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Quaternion hitRotation = Arm != null && Arm.Pivot ? Arm.Pivot.rotation : transform.rotation;
            // Resolve initial overlap as well as the swept volume, including low frame rates.
            var overlaps = Physics.OverlapBox(from, half, hitRotation, HitMask, QueryTriggerInteraction.Ignore);
            // Solid scenery wins over an overlapping actor so punches cannot pass through walls.
            foreach (var col in overlaps)
                if (!IsSelf(col) && !col.GetComponentInParent<Actor>()) { m_DidHit = true; return; }
            foreach (var col in overlaps)
                if (TryHit(col)) return;

            Vector3 delta = to - from;
            if (delta.sqrMagnitude < .000001f)
            {
                TryCloseRangeDamage();
                return;
            }
            var hits = Physics.BoxCastAll(from, half, delta.normalized, hitRotation, delta.magnitude,
                HitMask, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (IsSelf(hit.collider)) continue;
                TryHit(hit.collider);
                // The first solid object blocks this stroke, even if it is friendly or invincible.
                m_DidHit = true;
                return;
            }

            // The arm can be hanging down while the player is already in melee range.
            // Preserve line-of-sight, but do not require the moving fist volume to overlap
            // a particular limb collider before the attack can connect.
            TryCloseRangeDamage();
        }

        bool TryCloseRangeDamage()
        {
            if (m_DidHit || !m_Enemy || !m_Enemy.IsSeeingTarget || CloseDamageRadius <= 0f) return false;
            GameObject target = m_Enemy.KnownDetectedTarget;
            if (!target) return false;

            Vector3 center = m_Enemy.DetectionModule && m_Enemy.DetectionModule.DetectionSourcePoint
                ? m_Enemy.DetectionModule.DetectionSourcePoint.position
                : transform.position;
            Vector3 targetPosition = TargetAimPosition(target);
            if ((targetPosition - center).sqrMagnitude > CloseDamageRadius * CloseDamageRadius) return false;

            foreach (var col in target.GetComponentsInChildren<Collider>(true))
                if (TryHit(col)) return true;
            return false;
        }

        bool IsSelf(Collider col)
        {
            if (!col) return true;
            Transform enemyRoot = m_Enemy ? m_Enemy.transform : transform.root;
            return col.transform == enemyRoot || col.transform.IsChildOf(enemyRoot);
        }

        bool TryHit(Collider col)
        {
            if (IsSelf(col)) return false;
            var actor = col.GetComponentInParent<Actor>();
            if (!actor || (m_Actor && actor.Affiliation == m_Actor.Affiliation)) return false;
            var damageable = col.GetComponent<Damageable>();
            var health = col.GetComponentInParent<Health>();
            if (!health || health.CurrentHealth <= 0f) return false;
            m_DidHit = true; // Set before callbacks; multiple colliders cannot cause a second hit.
            if (damageable) damageable.InflictDamage(Damage, false, gameObject);
            else health.TakeDamage(Damage, gameObject);
            return true;
        }
    }
}
