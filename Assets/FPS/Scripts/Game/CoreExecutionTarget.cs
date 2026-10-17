using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.FPS.Game
{
    /// <summary>
    /// Makes an enemy core-bustable. Once it is vulnerable its UnstableCore is exposed and a CoreExecutioner
    /// can shove a gun into it and absorb it. Breaking the armour exposes the core for good; the Disassembler
    /// forces it open for a few seconds (ForceExpose), through the chest Window if the armour is still on. During the execution this plays the victim's half of the
    /// animation on top of the Animator's pose (doubling over the gun onto the executioner's right shoulder,
    /// clutching at them, convulsing, the galaxy body draining dark) and kills the enemy once the core is torn out.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health))]
    // After the Animator, Robot1ProceduralAnimator and the arm weapons have posed the body this frame.
    [DefaultExecutionOrder(200)]
    public class CoreExecutionTarget : MonoBehaviour
    {
        public enum ExecutionStage { None, Grabbed, Impaled, Finished }

        [Tooltip("The core in this enemy's chest.")]
        public UnstableCore Core;

        [Tooltip("Health the executioner gains by absorbing this core.")]
        [Min(0f)] public float HealthReward = 40f;

        [Tooltip("Galaxy body renderers that drain dark as the core is absorbed.")]
        public Renderer[] BodyRenderers;

        [Tooltip("Animator whose humanoid bones are posed during the execution. Defaults to this object's.")]
        public Animator BodyAnimator;

        [Tooltip("Hatch on the armoured chest that opens onto the core while the Disassembler forces it exposed.")]
        public CoreWindow Window;

        [Header("Victim Animation")]
        [Tooltip("Degrees the torso doubles over the gun while held.")]
        public float GrabHunch = 18f;
        [Tooltip("Extra degrees it folds over from the jab, like a punch to the gut.")]
        public float ImpaleHunch = 30f;
        [Tooltip("Degrees the head drops onto the executioner's shoulder.")]
        public float HeadSlump = 35f;
        [Tooltip("Where on the executioner the victim slumps: height above their feet, and how far to their right.")]
        public float ShoulderHeight = 1.4f;
        public float ShoulderOffset = .28f;
        [Tooltip("How hard the arms clutch at the executioner (0 = animation pose).")]
        [Range(0f, 1f)] public float ArmClutch = .6f;
        [Tooltip("Strength of the convulsions while the core is drained.")]
        public float ConvulseAngle = 8f;
        public float ConvulseFrequency = 9f;
        [Min(.01f)] public float PoseBlendSpeed = 7f;
        [Tooltip("Degrees per second the victim turns to face the executioner.")]
        public float TurnSpeed = 540f;

        [Header("Drain")]
        [Tooltip("Galaxy brightness left in the body once the core is gone.")]
        [Range(0f, 1f)] public float DrainedBrightness = .12f;

        /// <summary>Every enemy whose core is currently exposed.</summary>
        public static readonly List<CoreExecutionTarget> Vulnerable = new List<CoreExecutionTarget>();

        public UnityAction OnBecameVulnerable;
        public UnityAction<GameObject> OnExecutionStarted;
        public UnityAction OnCoreImpaled;
        public UnityAction OnExecutionFinished;

        /// <summary>The armour is off: the core stays exposed until it is executed.</summary>
        public bool IsVulnerable { get; private set; }
        /// <summary>The Disassembler has forced the core open for now, armour or not.</summary>
        public bool IsForcedExposed { get; private set; }
        public bool IsCoreExposed => IsVulnerable || IsForcedExposed;
        /// <summary>
        /// Keeps a Disassembler-forced hatch open until the core is executed, ignoring EndForcedExposure (the tutorial
        /// drill sets it so the player can't miss the window).
        /// </summary>
        public bool HoldForcedExposure { get; set; }
        public ExecutionStage Stage { get; private set; }
        public bool IsBeingExecuted => Stage == ExecutionStage.Grabbed || Stage == ExecutionStage.Impaled;
        public Health Health { get; private set; }

        public bool CanBeExecuted => IsCoreExposed && Stage == ExecutionStage.None && Core && !Core.IsConsumed &&
                                     Health && Health.CurrentHealth > 0f && isActiveAndEnabled;

        public Vector3 CorePosition => Core ? Core.transform.position : transform.position + Vector3.up * 1.3f;

        static readonly int k_NebulaBrightness = Shader.PropertyToID("_NebulaBrightness");
        static readonly int k_MinBrightness = Shader.PropertyToID("_MinBrightness");
        static readonly int k_CoreIntensity = Shader.PropertyToID("_CoreIntensity");

        class PosedBone
        {
            public Transform Transform;
            public Transform Child;
            public Quaternion Source;
            public Quaternion Written;
            public bool HasWritten;
        }

        PosedBone m_Spine, m_Chest, m_UpperChest, m_Neck, m_Head;
        PosedBone m_LeftUpperArm, m_RightUpperArm, m_LeftLowerArm, m_RightLowerArm;
        readonly List<PosedBone> m_Bones = new List<PosedBone>();

        GameObject m_Executioner;
        MaterialPropertyBlock m_DrainProperties;
        float m_BaseNebula = 1f, m_BaseMinBrightness = .35f, m_BaseCoreIntensity = 3f;
        float m_Pose;
        float m_Impact;
        float m_Drain;
        float m_Noise;

        // The project enters play mode without a domain reload, so statics outlive each session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Vulnerable.Clear();

        void Awake()
        {
            Health = GetComponent<Health>();
            if (!BodyAnimator) BodyAnimator = GetComponent<Animator>();
            if (!Core) Core = GetComponentInChildren<UnstableCore>(true);
            if (!Window) Window = GetComponentInChildren<CoreWindow>(true);
            if (Core) Core.IgniteOnStart = false;
            m_Noise = Random.Range(0f, 100f);
        }

        void OnDisable()
        {
            Vulnerable.Remove(this);
        }

        void OnEnable()
        {
            if (IsCoreExposed && Stage == ExecutionStage.None && !Vulnerable.Contains(this)) Vulnerable.Add(this);
        }

        /// <summary>Exposes the core (called when the armour breaks).</summary>
        public void SetVulnerable()
        {
            if (IsVulnerable) return;
            bool wasExposed = IsCoreExposed;
            IsVulnerable = true;
            IsForcedExposed = false;
            // The galaxy body shows the core from here on; the hatch has nothing left to cover.
            if (Window) Window.Close();
            if (Core && !wasExposed) Core.Ignite();
            if (isActiveAndEnabled && !Vulnerable.Contains(this)) Vulnerable.Add(this);
            OnBecameVulnerable?.Invoke();
        }

        /// <summary>
        /// The Disassembler's shock: exposes the core until EndForcedExposure, even under intact armour, by
        /// opening the chest window onto it. Does nothing once the armour is off (the core is exposed anyway).
        /// </summary>
        public void ForceExpose()
        {
            if (IsCoreExposed || Stage != ExecutionStage.None || !Core || Core.IsConsumed) return;
            IsForcedExposed = true;
            Core.Ignite();
            if (Window) Window.Open(Core);
            if (isActiveAndEnabled && !Vulnerable.Contains(this)) Vulnerable.Add(this);
        }

        /// <summary>The shock has worn off: the hatch shuts again, unless an execution already has hold of the core.</summary>
        public void EndForcedExposure()
        {
            if (!IsForcedExposed || Stage != ExecutionStage.None || HoldForcedExposure) return;
            IsForcedExposed = false;
            Vulnerable.Remove(this);
            if (Window) Window.Close();
            if (Core && !Core.IsConsumed) Core.SetDormant();
        }

        /// <summary>The executioner has grabbed this enemy: it stops fighting and can no longer be hurt.</summary>
        public void BeginExecution(GameObject executioner)
        {
            if (!CanBeExecuted) return;
            Stage = ExecutionStage.Grabbed;
            m_Executioner = executioner;
            Vulnerable.Remove(this);
            Health.Invincible = true;
            CacheBones();
            CacheDrainBase();
            OnExecutionStarted?.Invoke(executioner);
        }

        /// <summary>The gun has been driven into the core.</summary>
        public void Impale(Transform absorbTarget)
        {
            if (Stage != ExecutionStage.Grabbed) return;
            Stage = ExecutionStage.Impaled;
            m_Impact = 1f;
            if (Core) Core.BeginAbsorb(absorbTarget);
            OnCoreImpaled?.Invoke();
        }

        /// <summary>0..1 through the absorption: pulls the core out and drains the body.</summary>
        public void SetAbsorbProgress(float progress)
        {
            m_Drain = Mathf.Clamp01(progress);
            if (Core) Core.SetAbsorbProgress(m_Drain);
            ApplyDrain(m_Drain);
        }

        /// <summary>The core is torn out: it vanishes into the gun and the enemy dies.</summary>
        public void FinishExecution()
        {
            if (!IsBeingExecuted) return;
            Stage = ExecutionStage.Finished;
            if (Core) Core.CompleteAbsorb();
            OnExecutionFinished?.Invoke();
            Health.Invincible = false;
            Health.Kill();
        }

        void LateUpdate()
        {
            if (!IsBeingExecuted) return;

            float deltaTime = Time.deltaTime;
            bool impaled = Stage == ExecutionStage.Impaled;
            m_Pose = Mathf.MoveTowards(m_Pose, 1f, PoseBlendSpeed * deltaTime);
            m_Impact *= Mathf.Exp(-5f * deltaTime);

            FaceExecutioner(deltaTime);

            foreach (var bone in m_Bones) BeginPose(bone);

            float time = Time.time * ConvulseFrequency;
            float convulse = (impaled ? Mathf.Lerp(.35f, 1f, m_Drain) : .12f) * ConvulseAngle * m_Pose;
            float jerkBack = Noise(time, 0f) * convulse;
            float jerkSide = Noise(time, 7f) * convulse;
            float jerkTwist = Noise(time, 13f) * convulse;

            // Doubled over the gun like a punch to the gut, slumping onto the executioner's right shoulder.
            Transform executioner = m_Executioner ? m_Executioner.transform : null;
            Vector3 shoulder = executioner
                ? executioner.position + Vector3.up * ShoulderHeight + executioner.right * ShoulderOffset
                : transform.position + transform.forward + Vector3.up * ShoulderHeight;
            Vector3 chest = m_Chest != null ? m_Chest.Transform.position : transform.position + transform.up;
            Vector3 toShoulder = Vector3.ProjectOnPlane(shoulder - chest, Vector3.up);
            if (toShoulder.sqrMagnitude < 1e-4f) toShoulder = transform.forward;
            toShoulder.Normalize();

            float hunch = GrabHunch * m_Pose + (impaled ? ImpaleHunch * (.55f + .45f * m_Impact) : 0f);
            // Rigs without an UpperChest bone give its share of the fold to the chest.
            float upperShare = m_UpperChest != null ? .25f : 0f;
            LeanToward(m_Spine, toShoulder, hunch * .35f + jerkBack * .5f, jerkTwist * .4f);
            LeanToward(m_Chest, toShoulder, hunch * (.65f - upperShare) + jerkBack * .5f, jerkTwist * .6f);
            LeanToward(m_UpperChest, toShoulder, hunch * upperShare, jerkSide * .3f);
            // The head drops against the shoulder, cheek turned onto it.
            float cheek = Vector3.Dot(toShoulder, transform.right) >= 0f ? -18f : 18f;
            LeanToward(m_Neck, toShoulder, HeadSlump * .3f * m_Pose, 0f);
            LeanToward(m_Head, toShoulder, HeadSlump * m_Pose * (impaled ? 1f : .5f) + jerkBack * 1.2f,
                cheek * m_Pose + jerkTwist * 1.2f);

            // Arms clutch at the executioner's sides, then hang as the power leaves them.
            float clutch = ArmClutch * m_Pose * (impaled ? 1f : .6f);
            Vector3 body = executioner ? executioner.position + Vector3.up * 1.1f : transform.position + transform.forward;
            for (int side = -1; side <= 1; side += 2)
            {
                // The victim's left arm reaches for the executioner's right side, and vice versa.
                Vector3 grip = body + (executioner ? executioner.right : -transform.right) * (-side * .3f);
                Vector3 shake = new Vector3(Noise(time, 21f + side), Noise(time, 31f + side), Noise(time, 41f + side)) *
                                (.25f * convulse / Mathf.Max(1f, ConvulseAngle));
                PosedBone upper = side < 0 ? m_LeftUpperArm : m_RightUpperArm;
                PosedBone lower = side < 0 ? m_LeftLowerArm : m_RightLowerArm;
                if (upper == null) continue;
                Vector3 reach = (grip - upper.Transform.position).normalized;
                Vector3 hang = Vector3.Slerp(reach, Vector3.down, m_Drain * .7f) + shake;
                Aim(upper, hang, clutch);
                Aim(lower, hang + Vector3.down * .2f, clutch * .6f);
            }

            foreach (var bone in m_Bones) EndPose(bone);
        }

        void FaceExecutioner(float deltaTime)
        {
            if (!m_Executioner) return;
            Vector3 toExecutioner = Vector3.ProjectOnPlane(m_Executioner.transform.position - transform.position, Vector3.up);
            if (toExecutioner.sqrMagnitude < .0001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(toExecutioner), TurnSpeed * deltaTime);
        }

        float Noise(float time, float offset) => Mathf.PerlinNoise(time + offset * 3.7f, m_Noise + offset) * 2f - 1f;

        // Tips the bone over toward a horizontal direction (rotating world up toward it), plus a twist about up.
        static void LeanToward(PosedBone bone, Vector3 direction, float degrees, float twist)
        {
            if (bone == null) return;
            Vector3 axis = Vector3.Cross(Vector3.up, direction);
            if (axis.sqrMagnitude < 1e-6f) return;
            Quaternion lean = Quaternion.AngleAxis(degrees, axis.normalized) * Quaternion.AngleAxis(twist, Vector3.up);
            bone.Transform.rotation = lean * bone.Transform.rotation;
        }

        static void Aim(PosedBone bone, Vector3 worldDirection, float weight)
        {
            if (bone == null || !bone.Child || weight <= 0f) return;
            Vector3 current = bone.Child.position - bone.Transform.position;
            if (current.sqrMagnitude < 1e-6f || worldDirection.sqrMagnitude < 1e-6f) return;
            Quaternion delta = Quaternion.FromToRotation(current, worldDirection);
            bone.Transform.rotation = Quaternion.Slerp(Quaternion.identity, delta, weight) * bone.Transform.rotation;
        }

        // The Animator rewrites the bones every frame, but if it ever skips one (culling, disabled),
        // start again from last frame's source pose instead of stacking this frame's offsets on top.
        static void BeginPose(PosedBone bone)
        {
            if (bone.HasWritten && bone.Transform.localRotation == bone.Written)
                bone.Transform.localRotation = bone.Source;
            bone.Source = bone.Transform.localRotation;
        }

        static void EndPose(PosedBone bone)
        {
            bone.Written = bone.Transform.localRotation;
            bone.HasWritten = true;
        }

        void CacheBones()
        {
            m_Bones.Clear();
            if (!BodyAnimator || !BodyAnimator.isHuman) return;
            m_Spine = Bone(HumanBodyBones.Spine, HumanBodyBones.Chest);
            m_Chest = Bone(HumanBodyBones.Chest, HumanBodyBones.Neck);
            m_UpperChest = Bone(HumanBodyBones.UpperChest, HumanBodyBones.Neck);
            m_Neck = Bone(HumanBodyBones.Neck, HumanBodyBones.Head);
            m_Head = Bone(HumanBodyBones.Head, HumanBodyBones.LastBone);
            m_LeftUpperArm = Bone(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm);
            m_RightUpperArm = Bone(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm);
            m_LeftLowerArm = Bone(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            m_RightLowerArm = Bone(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        }

        PosedBone Bone(HumanBodyBones bone, HumanBodyBones child)
        {
            Transform t = BodyAnimator.GetBoneTransform(bone);
            if (!t) return null;
            var posed = new PosedBone
            {
                Transform = t,
                Child = child == HumanBodyBones.LastBone ? null : BodyAnimator.GetBoneTransform(child),
            };
            m_Bones.Add(posed);
            return posed;
        }

        void CacheDrainBase()
        {
            if (BodyRenderers == null) return;
            foreach (var body in BodyRenderers)
            {
                if (!body || !body.sharedMaterial) continue;
                Material material = body.sharedMaterial;
                if (material.HasProperty(k_NebulaBrightness)) m_BaseNebula = material.GetFloat(k_NebulaBrightness);
                if (material.HasProperty(k_MinBrightness)) m_BaseMinBrightness = material.GetFloat(k_MinBrightness);
                if (material.HasProperty(k_CoreIntensity)) m_BaseCoreIntensity = material.GetFloat(k_CoreIntensity);
                break;
            }
        }

        void ApplyDrain(float drain)
        {
            if (BodyRenderers == null) return;
            if (m_DrainProperties == null) m_DrainProperties = new MaterialPropertyBlock();
            // The body flares as the core is torn, then goes dark.
            float flare = Mathf.Sin(Mathf.Clamp01(drain * 2f) * Mathf.PI) * .8f;
            float remaining = Mathf.Lerp(1f, DrainedBrightness, drain * drain);
            foreach (var body in BodyRenderers)
            {
                if (!body) continue;
                body.GetPropertyBlock(m_DrainProperties);
                m_DrainProperties.SetFloat(k_NebulaBrightness, m_BaseNebula * (remaining + flare));
                m_DrainProperties.SetFloat(k_MinBrightness, m_BaseMinBrightness * remaining);
                m_DrainProperties.SetFloat(k_CoreIntensity, m_BaseCoreIntensity * remaining);
                body.SetPropertyBlock(m_DrainProperties);
            }
        }
    }
}
