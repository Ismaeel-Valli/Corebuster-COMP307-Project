using System.Collections.Generic;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.AI;

namespace Unity.FPS.AI
{
    /// <summary>
    /// The Disassembler's vulnerable state. The enemy freezes where it stands (brain, weapons, navigation and
    /// animation all stop), shakes with the current running through it while lightning arcs off it to the
    /// ground, and its chest hatch opens onto the core, so it can be core-executed even with its armour on.
    /// Added to enemies at runtime by the first bolt that hits them. An execution started during the shock
    /// takes over the frozen body; otherwise everything resumes when the shock wears off.
    /// </summary>
    [DisallowMultipleComponent]
    // After the Animator and the procedural animators have posed the body, before CoreExecutionTarget (200).
    [DefaultExecutionOrder(150)]
    public class DisassemblerShock : Electrocutable
    {
        const string VfxResource = "CoreBuster/VFX_Electrocution";

        [Tooltip("Spawned on the body for the length of the shock. Defaults to Resources/" + VfxResource + ".")]
        public ElectrocutionVfx VfxPrefab;

        [Header("Convulsion")]
        [Tooltip("Degrees the limbs and torso twitch.")]
        public float ShakeAngle = 9f;
        public float ShakeFrequency = 38f;
        [Tooltip("Degrees the arms are thrown out stiff from the body.")]
        public float ArmFlare = 28f;
        [Tooltip("Degrees the head snaps back.")]
        public float HeadSnap = 14f;
        [Tooltip("Metres a body without a humanoid rig is jittered by.")]
        public float MeshJitter = .025f;

        public override bool IsElectrocuted => m_Active;
        public float Remaining => m_Active ? Mathf.Max(0f, m_EndTime - Time.time) : 0f;

        static ElectrocutionVfx s_DefaultVfx;

        class PosedBone
        {
            public Transform Transform;
            public Quaternion Source;
            public Quaternion Written;
            public bool HasWritten;
            public float Weight;
            public Vector3 Seed;
        }

        struct Stashed
        {
            public Transform Transform;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        Health m_Health;
        NavMeshAgent m_Agent;
        CoreExecutionTarget m_Target;
        Animator[] m_Animators;
        float[] m_AnimatorSpeeds;
        readonly List<Behaviour> m_Disabled = new List<Behaviour>();
        readonly List<PistonFist> m_FrozenFists = new List<PistonFist>();
        readonly List<RivetCannon> m_FrozenCannons = new List<RivetCannon>();
        readonly List<PosedBone> m_Bones = new List<PosedBone>();
        readonly List<Stashed> m_JitterParts = new List<Stashed>();
        PosedBone m_Head, m_LeftArm, m_RightArm;
        ElectrocutionVfx m_Vfx;
        bool m_Active;
        bool m_AgentWasStopped;
        bool m_HandedOver;
        float m_EndTime;
        float m_Strength;
        float m_Seed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            s_DefaultVfx = null;
            Electrocutable.Factory = AddTo;
        }

        static Electrocutable AddTo(GameObject enemy)
        {
            if (!enemy.GetComponent<EnemyController>()) return null;
            var health = enemy.GetComponent<Health>();
            if (health && health.CurrentHealth <= 0f) return null;
            return enemy.AddComponent<DisassemblerShock>();
        }

        void Awake()
        {
            m_Health = GetComponent<Health>();
            m_Agent = GetComponent<NavMeshAgent>();
            m_Target = GetComponent<CoreExecutionTarget>();
            m_Seed = Random.Range(0f, 100f);
            if (m_Health) m_Health.OnDie += OnDie;
            if (m_Target) m_Target.OnExecutionStarted += OnExecutionStarted;

            if (!VfxPrefab)
            {
                if (!s_DefaultVfx) s_DefaultVfx = Resources.Load<ElectrocutionVfx>(VfxResource);
                VfxPrefab = s_DefaultVfx;
            }
        }

        void OnDestroy()
        {
            if (m_Health) m_Health.OnDie -= OnDie;
            if (m_Target) m_Target.OnExecutionStarted -= OnExecutionStarted;
            if (m_Vfx) m_Vfx.Stop();
        }

        void OnDisable()
        {
            if (m_Active) EndShock(true);
        }

        public override bool Electrocute(float duration, Vector3 hitPoint, GameObject source)
        {
            if (!isActiveAndEnabled || (m_Health && m_Health.CurrentHealth <= 0f)) return false;
            // Already in someone's hands: the execution owns the body now.
            if (m_Target && m_Target.Stage != CoreExecutionTarget.ExecutionStage.None) return false;

            m_EndTime = Mathf.Max(m_EndTime, Time.time + duration);
            if (m_Active)
            {
                m_Strength = 1.4f; // a fresh hit jolts it again
                return true;
            }

            m_Active = true;
            m_HandedOver = false;
            m_Strength = 1.4f;
            Freeze();
            CacheBody();
            StartVfx();
            if (m_Target) m_Target.ForceExpose();
            return true;
        }

        void Update()
        {
            if (!m_Active) return;
            m_Strength = Mathf.MoveTowards(m_Strength, 1f, Time.deltaTime * 3f);
            if (Time.time >= m_EndTime) EndShock(true);
        }

        void Freeze()
        {
            m_Disabled.Clear();
            // Robot1ProceduralAnimator stays on: with the agent stopped it settles to idle, and an execution
            // that takes over from the shock needs it driving the Animator again.
            DisableAll<Robot1ChaseController>();
            DisableAll<EnemyMobile>();
            DisableAll<EnemyTurret>();

            m_FrozenFists.Clear();
            foreach (var fist in GetComponentsInChildren<PistonFist>(true))
            {
                if (fist.IsFrozen) continue;
                fist.SetFrozen(true);
                m_FrozenFists.Add(fist);
            }
            m_FrozenCannons.Clear();
            foreach (var cannon in GetComponentsInChildren<RivetCannon>(true))
            {
                if (cannon.IsFrozen) continue;
                cannon.SetFrozen(true);
                m_FrozenCannons.Add(cannon);
            }

            if (m_Agent && m_Agent.enabled && m_Agent.isOnNavMesh)
            {
                m_AgentWasStopped = m_Agent.isStopped;
                m_Agent.isStopped = true;
                m_Agent.velocity = Vector3.zero;
            }

            // Stopped mid-stride: the Animator keeps writing the same frozen pose, which the convulsion sits on.
            m_Animators = GetComponentsInChildren<Animator>();
            m_AnimatorSpeeds = new float[m_Animators.Length];
            for (int i = 0; i < m_Animators.Length; i++)
            {
                m_AnimatorSpeeds[i] = m_Animators[i].speed;
                m_Animators[i].speed = 0f;
            }
        }

        void DisableAll<T>() where T : Behaviour
        {
            foreach (var behaviour in GetComponentsInChildren<T>())
            {
                if (!behaviour.enabled) continue;
                behaviour.enabled = false;
                m_Disabled.Add(behaviour);
            }
        }

        void EndShock(bool resume)
        {
            if (!m_Active) return;
            m_Active = false;
            m_EndTime = 0f;
            StopVfx();
            RestorePose();
            RestoreAnimators();

            bool alive = m_Health && m_Health.CurrentHealth > 0f;
            if (resume && alive && !m_HandedOver)
            {
                foreach (var behaviour in m_Disabled)
                    if (behaviour) behaviour.enabled = true;
                foreach (var fist in m_FrozenFists)
                    if (fist) fist.SetFrozen(false);
                foreach (var cannon in m_FrozenCannons)
                    if (cannon) cannon.SetFrozen(false);
                if (m_Agent && m_Agent.enabled && m_Agent.isOnNavMesh) m_Agent.isStopped = m_AgentWasStopped;
                if (m_Target) m_Target.EndForcedExposure();
            }
            m_Disabled.Clear();
            m_FrozenFists.Clear();
            m_FrozenCannons.Clear();
        }

        // The execution keeps the enemy frozen (ArmouredEnemy) and animates it; the shock steps aside.
        void OnExecutionStarted(GameObject executioner)
        {
            if (!m_Active) return;
            m_HandedOver = true;
            EndShock(false);
        }

        void OnDie()
        {
            if (!m_Active) return;
            m_HandedOver = true;
            EndShock(false);
        }

        void RestoreAnimators()
        {
            if (m_Animators == null) return;
            for (int i = 0; i < m_Animators.Length; i++)
                if (m_Animators[i]) m_Animators[i].speed = m_AnimatorSpeeds[i];
            m_Animators = null;
        }

        // ------------------------------------------------------------------ convulsion

        void CacheBody()
        {
            m_Bones.Clear();
            m_JitterParts.Clear();
            m_Head = m_LeftArm = m_RightArm = null;

            Animator animator = GetComponent<Animator>();
            if (animator && animator.isHuman)
            {
                AddBone(animator, HumanBodyBones.Spine, .5f);
                AddBone(animator, HumanBodyBones.Chest, .6f);
                AddBone(animator, HumanBodyBones.Neck, .5f);
                m_Head = AddBone(animator, HumanBodyBones.Head, 1f);
                m_LeftArm = AddBone(animator, HumanBodyBones.LeftUpperArm, 1f);
                m_RightArm = AddBone(animator, HumanBodyBones.RightUpperArm, 1f);
                AddBone(animator, HumanBodyBones.LeftLowerArm, 1.3f);
                AddBone(animator, HumanBodyBones.RightLowerArm, 1.3f);
                AddBone(animator, HumanBodyBones.LeftHand, 1.6f);
                AddBone(animator, HumanBodyBones.RightHand, 1.6f);
                AddBone(animator, HumanBodyBones.LeftUpperLeg, .35f);
                AddBone(animator, HumanBodyBones.RightUpperLeg, .35f);
                return;
            }

            // No rig: jitter the visible parts hanging off the root (a hover bot's body, a turret's head).
            foreach (Transform child in transform)
            {
                if (!child.GetComponentInChildren<Renderer>()) continue;
                m_JitterParts.Add(new Stashed { Transform = child, Position = child.localPosition, Rotation = child.localRotation });
            }
        }

        PosedBone AddBone(Animator animator, HumanBodyBones id, float weight)
        {
            Transform bone = animator.GetBoneTransform(id);
            if (!bone) return null;
            var posed = new PosedBone { Transform = bone, Weight = weight, Seed = Random.insideUnitSphere * 50f };
            m_Bones.Add(posed);
            return posed;
        }

        void LateUpdate()
        {
            if (!m_Active) return;

            float time = Time.time * ShakeFrequency;
            // Bursts of stronger spasms on top of the constant buzz.
            float spasm = Mathf.Pow(Mathf.PerlinNoise(Time.time * 3f, m_Seed), 3f) * 2.5f;
            float strength = m_Strength * (1f + spasm);

            foreach (var bone in m_Bones)
            {
                BeginPose(bone);
                Vector3 twitch = new Vector3(
                    Mathf.PerlinNoise(time, bone.Seed.x) - .5f,
                    Mathf.PerlinNoise(bone.Seed.y, time) - .5f,
                    Mathf.PerlinNoise(time + bone.Seed.z, bone.Seed.z) - .5f) * (2f * ShakeAngle * bone.Weight * strength);
                bone.Transform.localRotation *= Quaternion.Euler(twitch);
            }

            // Stiff with the current: arms thrown out from the sides, head snapped back.
            FlareArm(m_LeftArm, 1f);
            FlareArm(m_RightArm, -1f);
            if (m_Head != null)
                m_Head.Transform.rotation = Quaternion.AngleAxis(-HeadSnap * m_Strength, transform.right) * m_Head.Transform.rotation;

            foreach (var bone in m_Bones) EndPose(bone);

            foreach (var part in m_JitterParts)
            {
                if (!part.Transform) continue;
                Vector3 offset = new Vector3(Mathf.PerlinNoise(time, m_Seed) - .5f, Mathf.PerlinNoise(m_Seed, time) - .5f,
                    Mathf.PerlinNoise(time, m_Seed + 9f) - .5f) * (2f * MeshJitter * strength);
                part.Transform.localPosition = part.Position + offset;
                part.Transform.localRotation = part.Rotation * Quaternion.Euler(offset * (ShakeAngle * 40f));
            }
        }

        void FlareArm(PosedBone arm, float side)
        {
            if (arm == null) return;
            // Roll the arm up and out around the body's forward axis.
            arm.Transform.rotation = Quaternion.AngleAxis(side * ArmFlare * m_Strength, transform.forward) * arm.Transform.rotation;
        }

        // The Animator rewrites the bones every frame, but if it skips one (culled), start again from last
        // frame's source pose instead of stacking this frame's twitch on top of the last.
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

        void RestorePose()
        {
            foreach (var bone in m_Bones)
                if (bone.Transform && bone.HasWritten && bone.Transform.localRotation == bone.Written)
                    bone.Transform.localRotation = bone.Source;
            m_Bones.Clear();
            foreach (var part in m_JitterParts)
            {
                if (!part.Transform) continue;
                part.Transform.localPosition = part.Position;
                part.Transform.localRotation = part.Rotation;
            }
            m_JitterParts.Clear();
        }

        // ------------------------------------------------------------------ effects

        void StartVfx()
        {
            if (!VfxPrefab) return;
            Renderer body = BodyRenderer();
            Vector3 center = body ? body.bounds.center : transform.position + Vector3.up;
            m_Vfx = Instantiate(VfxPrefab, center, Quaternion.identity, transform);

            var anchors = new List<Transform>();
            foreach (var bone in m_Bones) anchors.Add(bone.Transform);
            Animator animator = GetComponent<Animator>();
            if (animator && animator.isHuman)
            {
                foreach (var id in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
                             HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg })
                {
                    Transform bone = animator.GetBoneTransform(id);
                    if (bone) anchors.Add(bone);
                }
            }
            m_Vfx.Play(transform, anchors, body);
        }

        void StopVfx()
        {
            if (m_Vfx) m_Vfx.Stop();
            m_Vfx = null;
        }

        // The largest visible mesh: the body rather than a weapon or an eye.
        Renderer BodyRenderer()
        {
            Renderer best = null;
            float bestSize = 0f;
            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)) continue;
                if (!renderer.enabled || renderer.GetComponentInParent<ElectrocutionVfx>()) continue;
                float size = renderer.bounds.size.sqrMagnitude;
                if (size <= bestSize) continue;
                best = renderer;
                bestSize = size;
            }
            return best;
        }
    }
}
