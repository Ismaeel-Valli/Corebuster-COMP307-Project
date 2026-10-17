using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.AI;

namespace Unity.FPS.AI
{
    /// <summary>
    /// Drives Robot1's locomotion from NavMeshAgent motion. When the Animator has a controller,
    /// the humanoid walk/run clips play and only the piston attack pose is layered on top;
    /// otherwise a lightweight procedural walk cycle is applied to the bones found by name.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public class Robot1ProceduralAnimator : MonoBehaviour
    {
        [Header("Animation clips")]
        [Tooltip("Animator playing the locomotion blend tree. Leave empty to use the procedural walk cycle.")]
        public Animator LocomotionAnimator;

        [Tooltip("Float parameter in the controller that receives the movement speed, in clip (human-scale) metres per second.")]
        public string SpeedParameter = "Speed";

        [Tooltip("Float parameter used as the locomotion state's speed multiplier, so feet keep up above the fastest clip.")]
        public string PlaybackSpeedParameter = "PlaybackSpeed";

        [Tooltip("Natural speed of the fastest locomotion clip in the blend tree (human-scale m/s).")]
        [Min(.1f)]
        public float FastestClipSpeed = 3.8f;

        [Tooltip("How quickly the Speed parameter follows the robot's actual speed.")]
        [Min(.1f)]
        public float SpeedDampTime = .12f;

        [Header("Walk cycle")]
        [Tooltip("Number of complete steps per second at full movement speed.")]
        [Min(.1f)]
        public float CycleFrequency = 1.65f;

        [Tooltip("Maximum upper-arm swing in degrees.")]
        [Range(0f, 60f)]
        public float ArmSwingAngle = 24f;

        [Tooltip("Maximum forearm swing in degrees.")]
        [Range(0f, 60f)]
        public float ForearmSwingAngle = 12f;

        [Tooltip("Maximum upper-leg swing in degrees.")]
        [Range(0f, 70f)]
        public float LegSwingAngle = 30f;

        [Tooltip("Maximum foot pitch in degrees.")]
        [Range(0f, 35f)]
        public float FootSwingAngle = 10f;

        [Tooltip("How quickly the animation responds when movement starts or stops.")]
        [Min(.1f)]
        public float MovementResponse = 8f;

        [Tooltip("Small idle movement so a stationary robot does not look completely frozen.")]
        [Range(0f, 8f)]
        public float IdleSwayAngle = 1.25f;

        [Tooltip("Local bone axis used for forward/backward limb swings.")]
        public Vector3 LimbRotationAxis = Vector3.right;

        [Tooltip("Print the discovered bone names once when the robot starts.")]
        public bool LogBoneSetup = true;

        [Header("Combat pose")]
        [Tooltip("Fallback signed local rotation used only if an arm's limb direction cannot be determined.")]
        [Range(-120f, 120f)]
        public float AttackArmRaiseAngle = -90f;

        [Tooltip("How quickly the arms blend into and out of the raised attack pose.")]
        [Min(.1f)]
        public float AttackPoseResponse = 6f;

        [Header("Hook punch (animation clip mode)")]
        [Tooltip("Degrees the torso twists away from the target while winding up a hook.")]
        [Range(0f, 60f)]
        public float HookWindupTwist = 30f;

        [Tooltip("Degrees the torso twists through the target at the moment of impact.")]
        [Range(0f, 70f)]
        public float HookStrikeTwist = 40f;

        [Tooltip("Raise the non-punching arm in front of the face during a hook.")]
        public bool GuardWithOtherArm = true;

        [Header("Two-handed weapon (animation clip mode)")]
        [Tooltip("Controller layer that shoulders a two-handed weapon. If the controller has it, the aim pose " +
                 "blends this layer in and turns the torso, instead of straightening one arm.")]
        public string AimLayerName = DefaultAimLayerName;

        public const string DefaultAimLayerName = "Aim";

        [Tooltip("Transform fixed to the two-handed weapon with +Z along its barrel. The torso turns until it points at the target.")]
        public Transform WeaponAimReference;

        [Header("Appearance")]
        [Tooltip("Optional nebula material applied to the imported Robot1 body renderers.")]
        public Material NebulaMaterial;

        [Tooltip("Apply NebulaMaterial when the robot is initialized.")]
        public bool ApplyNebulaMaterial = true;

        struct BonePose
        {
            public Transform Transform;
            public Quaternion BaseRotation;

            public BonePose(Transform transform)
            {
                Transform = transform;
                BaseRotation = transform ? transform.localRotation : Quaternion.identity;
            }

            public bool IsValid => Transform != null;
        }

        enum BoneKind
        {
            Shoulder,
            Arm,
            Forearm,
            Hand,
            Leg,
            Foot,
        }

        NavMeshAgent m_Agent;
        Vector3 m_LastPosition;
        float m_SmoothedMotion;
        float m_Cycle;
        float m_AttackPoseWeight;
        bool m_AttackPoseRequested;

        BonePose m_LeftShoulder;
        BonePose m_RightShoulder;
        BonePose m_LeftArm;
        BonePose m_RightArm;
        BonePose m_LeftForearm;
        BonePose m_RightForearm;
        BonePose m_LeftHand;
        BonePose m_RightHand;
        BonePose m_LeftLeg;
        BonePose m_RightLeg;
        BonePose m_LeftFoot;
        BonePose m_RightFoot;
        Vector3 m_LeftArmDirectionLocal;
        Vector3 m_RightArmDirectionLocal;
        int m_SpeedHash;
        int m_PlaybackSpeedHash;
        int m_HookSide;
        float m_HookWeight;
        float m_HookStrike;
        int m_AimArmSide;
        float m_AimArmWeight;
        Vector3 m_AimArmTarget;
        int m_AimLayer = -1;

        bool UsesClips => LocomotionAnimator && LocomotionAnimator.runtimeAnimatorController &&
                          LocomotionAnimator.isHuman;

        void Awake()
        {
            m_Agent = GetComponent<NavMeshAgent>();
            m_LastPosition = transform.position;
            if (!LocomotionAnimator) LocomotionAnimator = GetComponent<Animator>();
            if (UsesClips)
            {
                // The NavMeshAgent moves the robot; the clips are in-place.
                LocomotionAnimator.applyRootMotion = false;
                m_SpeedHash = Animator.StringToHash(SpeedParameter);
                m_PlaybackSpeedHash = Animator.StringToHash(PlaybackSpeedParameter);
                if (!string.IsNullOrEmpty(AimLayerName))
                    m_AimLayer = LocomotionAnimator.GetLayerIndex(AimLayerName);
            }
            CacheBones();
            ApplyNebulaMaterialToModel();
        }

        void Update()
        {
            float deltaTime = Mathf.Max(Time.deltaTime, .0001f);
            float movementSpeed = GetMovementSpeed(deltaTime);

            m_AttackPoseWeight = Mathf.MoveTowards(m_AttackPoseWeight,
                m_AttackPoseRequested ? 1f : 0f, AttackPoseResponse * deltaTime);

            if (UsesClips)
            {
                UpdateClipParameters(movementSpeed, deltaTime);
                return;
            }

            float targetMotion = Mathf.Clamp01(movementSpeed / Mathf.Max(.1f, m_Agent.speed));

            // Ignore tiny NavMesh corrections so the limbs return to their neutral pose at rest.
            if (movementSpeed < .08f)
                targetMotion = 0f;

            m_SmoothedMotion = Mathf.MoveTowards(
                m_SmoothedMotion,
                targetMotion,
                MovementResponse * deltaTime);

            if (m_SmoothedMotion > .001f)
            {
                float cycleRate = Mathf.Lerp(.65f, 1f, m_SmoothedMotion);
                m_Cycle = Mathf.Repeat(m_Cycle + deltaTime * Mathf.PI * 2f * CycleFrequency * cycleRate,
                    Mathf.PI * 2f);
            }

            float walk = Mathf.Sin(m_Cycle) * m_SmoothedMotion;
            float idle = (1f - m_SmoothedMotion) * Mathf.Sin(Time.time * 1.35f) * IdleSwayAngle;
            float leftArm = walk * ArmSwingAngle + idle;
            float rightArm = -walk * ArmSwingAngle - idle;
            float leftLeg = -walk * LegSwingAngle;
            float rightLeg = walk * LegSwingAngle;

            float locomotionArmWeight = 1f - m_AttackPoseWeight;

            Apply(m_LeftShoulder, leftArm * .35f * locomotionArmWeight);
            Apply(m_RightShoulder, rightArm * .35f * locomotionArmWeight);
            ApplyForwardAttackPose(m_LeftArm, m_LeftArmDirectionLocal,
                LocomotionRotation(m_LeftArm, leftArm * locomotionArmWeight), m_AttackPoseWeight);
            ApplyForwardAttackPose(m_RightArm, m_RightArmDirectionLocal,
                LocomotionRotation(m_RightArm, rightArm * locomotionArmWeight), m_AttackPoseWeight);
            Apply(m_LeftForearm, leftArm * locomotionArmWeight *
                (ForearmSwingAngle / Mathf.Max(1f, ArmSwingAngle)));
            Apply(m_RightForearm, rightArm * locomotionArmWeight *
                (ForearmSwingAngle / Mathf.Max(1f, ArmSwingAngle)));
            Apply(m_LeftHand, leftArm * .2f * locomotionArmWeight);
            Apply(m_RightHand, rightArm * .2f * locomotionArmWeight);
            Apply(m_LeftLeg, leftLeg);
            Apply(m_RightLeg, rightLeg);
            Apply(m_LeftFoot, -leftLeg * (FootSwingAngle / Mathf.Max(1f, LegSwingAngle)));
            Apply(m_RightFoot, -rightLeg * (FootSwingAngle / Mathf.Max(1f, LegSwingAngle)));
        }

        void LateUpdate()
        {
            // The Animator has written this frame's walk pose; layer the attack on top of it.
            if (!UsesClips)
                return;

            // Takes effect from the next Animator update, a frame behind the torso turn below.
            if (m_AimLayer >= 0)
                LocomotionAnimator.SetLayerWeight(m_AimLayer, m_AimArmSide != 0 ? m_AimArmWeight : 0f);

            if (m_HookWeight > .0001f && m_HookSide != 0)
            {
                ApplyHook();
                return;
            }

            if (m_AimArmWeight > .0001f && m_AimArmSide != 0)
            {
                ApplyAimArm();
                return;
            }

            if (m_AttackPoseWeight <= .0001f)
                return;

            ApplyForwardAttackPose(m_LeftArm, m_LeftArmDirectionLocal,
                m_LeftArm.IsValid ? m_LeftArm.Transform.localRotation : Quaternion.identity, m_AttackPoseWeight);
            ApplyForwardAttackPose(m_RightArm, m_RightArmDirectionLocal,
                m_RightArm.IsValid ? m_RightArm.Transform.localRotation : Quaternion.identity, m_AttackPoseWeight);
        }

        void UpdateClipParameters(float movementSpeed, float deltaTime)
        {
            // Clip speeds are authored for a human-sized character; convert world speed to that scale.
            float scale = Mathf.Max(.01f, LocomotionAnimator.humanScale * Mathf.Abs(transform.lossyScale.y));
            float clipSpeed = movementSpeed < .08f ? 0f : movementSpeed / scale;

            LocomotionAnimator.SetFloat(m_SpeedHash, clipSpeed, SpeedDampTime, deltaTime);
            LocomotionAnimator.SetFloat(m_PlaybackSpeedHash, Mathf.Max(1f, clipSpeed / FastestClipSpeed));
        }

        void OnDisable()
        {
            m_AttackPoseRequested = false;
            m_AttackPoseWeight = 0f;
            if (UsesClips)
            {
                if (m_AimLayer >= 0)
                    LocomotionAnimator.SetLayerWeight(m_AimLayer, 0f);
                return;
            }

            Restore(m_LeftShoulder);
            Restore(m_RightShoulder);
            Restore(m_LeftArm);
            Restore(m_RightArm);
            Restore(m_LeftForearm);
            Restore(m_RightForearm);
            Restore(m_LeftHand);
            Restore(m_RightHand);
            Restore(m_LeftLeg);
            Restore(m_RightLeg);
            Restore(m_LeftFoot);
            Restore(m_RightFoot);
        }

        /// <summary>Raises or lowers both arms for the piston attack.</summary>
        public void SetAttackPose(bool raised)
        {
            m_AttackPoseRequested = raised;
        }

        /// <summary>
        /// Poses a hook punch over the locomotion clips. side: +1 right arm, -1 left arm.
        /// weight blends the whole pose in/out; strike goes 0 (wound up) to 1 (impact).
        /// </summary>
        public void SetHook(int side, float weight, float strike)
        {
            m_HookSide = side;
            m_HookWeight = Mathf.Clamp01(weight);
            m_HookStrike = Mathf.Clamp01(strike);
        }

        /// <summary>
        /// Raises one arm straight out at a world point, e.g. for a forearm-mounted gun.
        /// side: +1 right arm, -1 left arm. weight 0 leaves the arm to the locomotion clips.
        /// With an aim layer and WeaponAimReference, shoulders the two-handed weapon at the point instead.
        /// </summary>
        public void SetAimArm(int side, float weight, Vector3 worldTarget)
        {
            m_AimArmSide = side;
            m_AimArmWeight = Mathf.Clamp01(weight);
            m_AimArmTarget = worldTarget;
        }

        void ApplyAimArm()
        {
            if (m_AimLayer >= 0 && WeaponAimReference)
            {
                // Both hands are on the weapon, so turn the torso and let the arms ride along.
                // Split over the spine and chest so it reads as a lean rather than a kink.
                TurnTorsoTowardAim(LocomotionAnimator.GetBoneTransform(HumanBodyBones.Spine), .5f);
                TurnTorsoTowardAim(LocomotionAnimator.GetBoneTransform(HumanBodyBones.Chest), 1f);
                return;
            }

            HumanBodyBones upperArmBone = m_AimArmSide > 0 ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm;
            Transform upperArm = LocomotionAnimator.GetBoneTransform(upperArmBone);
            if (!upperArm)
                return;

            // A straight arm from the shoulder keeps the forearm (and anything on it) on the target line.
            Vector3 direction = m_AimArmTarget - upperArm.position;
            if (direction.sqrMagnitude < .0001f)
                return;
            direction.Normalize();
            AimArm(upperArmBone, direction, direction, m_AimArmWeight);
        }

        // Turns a torso bone by `share` of what remains between the weapon's barrel and the target.
        void TurnTorsoTowardAim(Transform bone, float share)
        {
            if (!bone)
                return;

            Vector3 toTarget = m_AimArmTarget - WeaponAimReference.position;
            if (toTarget.sqrMagnitude < .0001f)
                return;

            Quaternion delta = Quaternion.FromToRotation(WeaponAimReference.forward, toTarget);
            bone.rotation = Quaternion.Slerp(Quaternion.identity, delta, m_AimArmWeight * share) * bone.rotation;
        }

        void ApplyHook()
        {
            Animator animator = LocomotionAnimator;
            int side = m_HookSide > 0 ? 1 : -1;
            float w = m_HookWeight;
            float s = m_HookStrike;
            Vector3 up = transform.up;

            // Torso: turn the punching shoulder back on the wind-up, then drive it through the target.
            float twist = side * Mathf.Lerp(HookWindupTwist, -HookStrikeTwist, s) * w;
            RotateAroundUp(animator.GetBoneTransform(HumanBodyBones.Spine), twist * .4f, up);
            RotateAroundUp(animator.GetBoneTransform(HumanBodyBones.Chest), twist * .6f, up);

            // Arm directions are authored in the twisted torso's frame (x = robot right, z = forward).
            Quaternion torso = Quaternion.AngleAxis(twist, up) * transform.rotation;

            // Punching arm: cocked out wide with the elbow bent, then swept across the chest.
            Vector3 upperWindup = new Vector3(side * .95f, .15f, -.3f);
            Vector3 foreWindup = new Vector3(side * .25f, .25f, 1f);
            Vector3 upperStrike = new Vector3(side * .45f, .1f, 1f);
            Vector3 foreStrike = new Vector3(-side * 1f, .05f, .35f);
            AimArm(side > 0 ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm,
                torso * Vector3.Slerp(upperWindup.normalized, upperStrike.normalized, s),
                torso * Vector3.Slerp(foreWindup.normalized, foreStrike.normalized, s), w);

            // Other arm: tucked up as a guard in front of the face.
            if (GuardWithOtherArm)
            {
                AimArm(side > 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm,
                    torso * new Vector3(-side * .35f, -.55f, .45f).normalized,
                    torso * new Vector3(side * .15f, .9f, .35f).normalized, w * .85f);
            }
        }

        void AimArm(HumanBodyBones upperArmBone, Vector3 upperDirection, Vector3 foreDirection, float weight)
        {
            Animator animator = LocomotionAnimator;
            Transform upperArm = animator.GetBoneTransform(upperArmBone);
            Transform foreArm = animator.GetBoneTransform(upperArmBone == HumanBodyBones.RightUpperArm
                ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            Transform hand = animator.GetBoneTransform(upperArmBone == HumanBodyBones.RightUpperArm
                ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            if (!upperArm || !foreArm || !hand)
                return;

            AimBone(upperArm, foreArm.position, upperDirection, weight);
            AimBone(foreArm, hand.position, foreDirection, weight);
        }

        static void AimBone(Transform bone, Vector3 childPosition, Vector3 worldDirection, float weight)
        {
            Vector3 current = childPosition - bone.position;
            if (current.sqrMagnitude < .000001f)
                return;

            Quaternion delta = Quaternion.FromToRotation(current, worldDirection);
            bone.rotation = Quaternion.Slerp(Quaternion.identity, delta, weight) * bone.rotation;
        }

        static void RotateAroundUp(Transform bone, float angle, Vector3 up)
        {
            if (bone)
                bone.rotation = Quaternion.AngleAxis(angle, up) * bone.rotation;
        }

        float GetMovementSpeed(float deltaTime)
        {
            Vector3 frameDisplacement = (transform.position - m_LastPosition) / deltaTime;
            m_LastPosition = transform.position;

            float agentSpeed = 0f;
            if (m_Agent && m_Agent.enabled)
                agentSpeed = m_Agent.velocity.magnitude;

            // Robot1ChaseController has a direct-movement fallback, so use transform movement
            // as well when the agent is temporarily not on a NavMesh.
            return Mathf.Max(agentSpeed, frameDisplacement.magnitude);
        }

        void CacheBones()
        {
            m_LeftShoulder = FindBone(BoneKind.Shoulder, -1);
            m_RightShoulder = FindBone(BoneKind.Shoulder, 1);
            m_LeftArm = FindBone(BoneKind.Arm, -1);
            m_RightArm = FindBone(BoneKind.Arm, 1);
            m_LeftForearm = FindBone(BoneKind.Forearm, -1);
            m_RightForearm = FindBone(BoneKind.Forearm, 1);
            m_LeftHand = FindBone(BoneKind.Hand, -1);
            m_RightHand = FindBone(BoneKind.Hand, 1);
            m_LeftLeg = FindBone(BoneKind.Leg, -1);
            m_RightLeg = FindBone(BoneKind.Leg, 1);
            m_LeftFoot = FindBone(BoneKind.Foot, -1);
            m_RightFoot = FindBone(BoneKind.Foot, 1);

            m_LeftArmDirectionLocal = GetLimbDirectionLocal(m_LeftArm, m_LeftForearm, m_LeftHand);
            m_RightArmDirectionLocal = GetLimbDirectionLocal(m_RightArm, m_RightForearm, m_RightHand);

            if (LogBoneSetup)
            {
                Debug.Log(
                    $"Robot1 procedural animation bones: " +
                    $"L arm={NameOf(m_LeftArm.Transform)}, R arm={NameOf(m_RightArm.Transform)}, " +
                    $"L forearm={NameOf(m_LeftForearm.Transform)}, R forearm={NameOf(m_RightForearm.Transform)}, " +
                    $"L leg={NameOf(m_LeftLeg.Transform)}, R leg={NameOf(m_RightLeg.Transform)}, " +
                    $"L foot={NameOf(m_LeftFoot.Transform)}, R foot={NameOf(m_RightFoot.Transform)}",
                    this);
            }
        }

        void ApplyNebulaMaterialToModel()
        {
            if (!ApplyNebulaMaterial || !NebulaMaterial)
                return;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                string name = renderer.transform.name.ToLowerInvariant();

                // Keep the attached piston's steel, chrome, yellow and warning materials,
                // and any mounted weapon's own materials.
                if (name.Contains("piston") || name.Contains("fist") || name.Contains("knuckle") ||
                    name.Contains("warning") || name.Contains("cylinder") ||
                    renderer.GetComponentInParent<WeaponController>(true))
                    continue;

                Material[] materials = renderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    materials[materialIndex] = NebulaMaterial;
                renderer.sharedMaterials = materials;
            }
        }

        // Bone matching is intentionally name-and-position based so the existing FBX rig
        // can be used without adding serialized bone references to the prefab.
        BonePose FindBone(BoneKind kind, int side)
        {
            Transform best = null;
            float bestScore = float.MinValue;
            Transform[] candidates = GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < candidates.Length; i++)
            {
                Transform candidate = candidates[i];
                if (candidate == transform)
                    continue;

                string name = candidate.name.ToLowerInvariant();
                if (name.Contains("piston") || name.Contains("fist") || name.Contains("weapon") ||
                    candidate.GetComponentInParent<WeaponController>(true))
                    continue;

                int nameScore = NameScore(name, kind);
                if (nameScore < 0)
                    continue;

                float score = nameScore;
                float localSide = transform.InverseTransformPoint(candidate.position).x;
                bool onRequestedSide = side < 0 ? localSide < -.005f : localSide > .005f;
                score += onRequestedSide ? 35f : -35f;

                if (HasExplicitSide(name, side))
                    score += 100f;

                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return new BonePose(best);
        }

        static int NameScore(string name, BoneKind kind)
        {
            switch (kind)
            {
                case BoneKind.Shoulder:
                    return name.Contains("shoulder") ? 120 : -1;
                case BoneKind.Arm:
                    if (name.Contains("forearm") || name.Contains("lowerarm") || name.Contains("hand"))
                        return -1;
                    if (name == "arm" || name.Contains("upperarm"))
                        return 120;
                    return name.Contains("arm") ? 80 : -1;
                case BoneKind.Forearm:
                    if (name.Contains("forearm") || name.Contains("lowerarm"))
                        return 120;
                    return -1;
                case BoneKind.Hand:
                    if (name.Contains("hand") || name.Contains("wrist"))
                        return 120;
                    return -1;
                case BoneKind.Leg:
                    if (name.Contains("foot") || name.Contains("ankle"))
                        return -1;
                    if (name == "leg" || name.Contains("upperleg"))
                        return 120;
                    return name.Contains("leg") ? 80 : -1;
                case BoneKind.Foot:
                    return name.Contains("foot") || name.Contains("ankle") ? 120 : -1;
                default:
                    return -1;
            }
        }

        static bool HasExplicitSide(string name, int side)
        {
            if (side < 0)
                return name.Contains("left") || name.Contains("_l") || name.Contains(".l") || name.EndsWith("-l");

            return name.Contains("right") || name.Contains("_r") || name.Contains(".r") || name.EndsWith("-r");
        }

        void Apply(BonePose pose, float angle)
        {
            if (!pose.IsValid)
                return;

            Vector3 axis = LimbRotationAxis.sqrMagnitude > .0001f ? LimbRotationAxis.normalized : Vector3.right;
            pose.Transform.localRotation = pose.BaseRotation * Quaternion.AngleAxis(angle, axis);
        }

        Quaternion LocomotionRotation(BonePose pose, float angle)
        {
            if (!pose.IsValid)
                return Quaternion.identity;

            Vector3 axis = LimbRotationAxis.sqrMagnitude > .0001f ? LimbRotationAxis.normalized : Vector3.right;
            return pose.BaseRotation * Quaternion.AngleAxis(angle, axis);
        }

        void ApplyForwardAttackPose(BonePose pose, Vector3 limbDirectionLocal, Quaternion locomotionRotation,
            float attackWeight)
        {
            if (!pose.IsValid)
                return;

            Vector3 rotationAxis = LimbRotationAxis.sqrMagnitude > .0001f
                ? LimbRotationAxis.normalized
                : Vector3.right;

            if (attackWeight <= .0001f || limbDirectionLocal.sqrMagnitude < .0001f)
            {
                // Retain the old signed-angle behavior only as a fallback for an incomplete rig.
                Quaternion fallback = pose.BaseRotation * Quaternion.AngleAxis(
                    AttackArmRaiseAngle * attackWeight, rotationAxis);
                pose.Transform.localRotation = attackWeight > .0001f
                    ? Quaternion.Slerp(locomotionRotation, fallback, attackWeight)
                    : locomotionRotation;
                return;
            }

            Transform parent = pose.Transform.parent;
            Quaternion parentWorldRotation = parent ? parent.rotation : Quaternion.identity;
            Quaternion restWorldRotation = parentWorldRotation * pose.BaseRotation;
            Vector3 restingWorldDirection = restWorldRotation * limbDirectionLocal.normalized;
            Vector3 targetDirection = Vector3.ProjectOnPlane(transform.forward, transform.up).normalized;
            if (targetDirection.sqrMagnitude < .0001f)
                targetDirection = transform.forward;

            Quaternion desiredWorldRotation =
                Quaternion.FromToRotation(restingWorldDirection, targetDirection) * restWorldRotation;
            Quaternion desiredLocalRotation = parent
                ? Quaternion.Inverse(parent.rotation) * desiredWorldRotation
                : desiredWorldRotation;

            pose.Transform.localRotation = Quaternion.Slerp(locomotionRotation, desiredLocalRotation, attackWeight);
        }

        static Vector3 GetLimbDirectionLocal(BonePose upperArm, BonePose forearm, BonePose hand)
        {
            if (!upperArm.IsValid)
                return Vector3.zero;

            Transform endpoint = forearm.IsValid ? forearm.Transform : hand.Transform;
            if (!endpoint)
                return Vector3.zero;

            Vector3 worldDirection = endpoint.position - upperArm.Transform.position;
            return worldDirection.sqrMagnitude > .0001f
                ? upperArm.Transform.InverseTransformDirection(worldDirection.normalized)
                : Vector3.zero;
        }

        static void Restore(BonePose pose)
        {
            if (pose.IsValid)
                pose.Transform.localRotation = pose.BaseRotation;
        }

        static string NameOf(Transform transform) => transform ? transform.name : "not found";
    }
}
