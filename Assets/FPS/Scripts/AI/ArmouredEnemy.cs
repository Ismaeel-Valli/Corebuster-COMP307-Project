using System.Collections.Generic;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

namespace Unity.FPS.AI
{
    /// <summary>
    /// Armour shell for a humanoid enemy. Shots throw sparks off the armour and part of the damage
    /// is soaked while it holds. Below BreakHealthRatio the armour is torn off the body as physical
    /// chunks (cut from the current skinned pose) and the body underneath switches to ExposedMaterial.
    /// Breaking the armour also exposes the enemy's Unstable Core for a core execution.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health))]
    public class ArmouredEnemy : MonoBehaviour, IArmourBreakable
    {
        [Header("Armour")]
        [Tooltip("The armour breaks off once health falls to this fraction of max health.")]
        [Range(0f, 1f)] public float BreakHealthRatio = .4f;

        [Tooltip("Damage taken is multiplied by this while the armour is intact.")]
        [Range(.05f, 1f)] public float ArmouredDamageMultiplier = .75f;

        [Tooltip("Armoured body mesh. Its current materials are what the falling armour chunks use.")]
        public SkinnedMeshRenderer BodyRenderer;

        [Tooltip("Material the body switches to once the armour is gone (the original robots' galaxy look).")]
        public Material ExposedMaterial;

        [Header("Hit Sparks")]
        [Tooltip("Spark emitters on the body. The one nearest the incoming shot fires back toward the shooter.")]
        public ParticleSystem[] HitSparks;

        [Tooltip("How far in front of the bone, toward the shooter, the sparks appear (roughly the armour's depth).")]
        [Min(0f)] public float SparkSurfaceOffset = .18f;

        [Header("Armour Break")]
        [Tooltip("Material for the falling chunks; ideally double-sided since each chunk is an open shell. Defaults to the body's armour material.")]
        public Material DebrisMaterial;
        public AudioClip BreakSfx;
        [Min(0f)] public float DebrisLifetime = 5f;
        [Tooltip("Horizontal speed the chunks are thrown away from the body with.")]
        [Min(0f)] public float DebrisOutwardSpeed = 3f;
        [Min(0f)] public float DebrisUpwardSpeed = 2.5f;
        [Min(.01f)] public float DebrisMass = 3f;

        [Header("Core Execution")]
        [Tooltip("Exposed when the armour breaks. The enemy's AI is frozen while its core is being absorbed.")]
        public CoreExecutionTarget CoreTarget;

        public UnityAction onArmourBroken;
        public bool IsArmourBroken { get; private set; }

        // Regions the armour splits into. Each human bone maps to one; unmapped bones use their parent's.
        static readonly Dictionary<HumanBodyBones, int> k_Regions = new Dictionary<HumanBodyBones, int>
        {
            { HumanBodyBones.Head, 0 }, { HumanBodyBones.Neck, 0 }, { HumanBodyBones.Jaw, 0 },
            { HumanBodyBones.Chest, 1 }, { HumanBodyBones.UpperChest, 1 },
            { HumanBodyBones.Spine, 2 },
            { HumanBodyBones.Hips, 3 },
            { HumanBodyBones.LeftShoulder, 4 }, { HumanBodyBones.LeftUpperArm, 4 },
            { HumanBodyBones.RightShoulder, 5 }, { HumanBodyBones.RightUpperArm, 5 },
            { HumanBodyBones.LeftLowerArm, 6 }, { HumanBodyBones.LeftHand, 6 },
            { HumanBodyBones.RightLowerArm, 7 }, { HumanBodyBones.RightHand, 7 },
            { HumanBodyBones.LeftUpperLeg, 8 }, { HumanBodyBones.RightUpperLeg, 9 },
            { HumanBodyBones.LeftLowerLeg, 10 }, { HumanBodyBones.LeftFoot, 10 }, { HumanBodyBones.LeftToes, 10 },
            { HumanBodyBones.RightLowerLeg, 11 }, { HumanBodyBones.RightFoot, 11 }, { HumanBodyBones.RightToes, 11 },
        };
        const int k_RegionCount = 12;

        Health m_Health;
        Animator m_Animator;
        NavMeshAgent m_Agent;
        ActorsManager m_ActorsManager;
        Damageable[] m_Damageables;
        float[] m_BaseDamageMultipliers;
        Material[] m_ArmourMaterials;
        Vector3[] m_SparkRestPositions;

        void Awake()
        {
            if (HitSparks == null) HitSparks = new ParticleSystem[0];
            m_SparkRestPositions = new Vector3[HitSparks.Length];
            for (int i = 0; i < HitSparks.Length; i++)
                if (HitSparks[i]) m_SparkRestPositions[i] = HitSparks[i].transform.localPosition;

            m_Health = GetComponent<Health>();
            m_Animator = GetComponent<Animator>();
            m_Agent = GetComponent<NavMeshAgent>();
            m_ActorsManager = FindAnyObjectByType<ActorsManager>();
            if (!BodyRenderer) BodyRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
            if (BodyRenderer) m_ArmourMaterials = BodyRenderer.sharedMaterials;

            m_Damageables = GetComponentsInChildren<Damageable>(true);
            m_BaseDamageMultipliers = new float[m_Damageables.Length];
            for (int i = 0; i < m_Damageables.Length; i++)
            {
                m_BaseDamageMultipliers[i] = m_Damageables[i].DamageMultiplier;
                m_Damageables[i].DamageMultiplier *= ArmouredDamageMultiplier;
            }

            m_Health.OnDamaged += OnDamaged;

            if (!CoreTarget) CoreTarget = GetComponent<CoreExecutionTarget>();
            if (CoreTarget) CoreTarget.OnExecutionStarted += FreezeForExecution;
        }

        void OnDestroy()
        {
            if (m_Health) m_Health.OnDamaged -= OnDamaged;
            if (CoreTarget) CoreTarget.OnExecutionStarted -= FreezeForExecution;
        }

        // The victim stops fighting and moving; CoreExecutionTarget animates it from here until it dies.
        // EnemyController stays enabled: it owns the death handling (VFX, unregistering, destroy).
        void FreezeForExecution(GameObject executioner)
        {
            foreach (var fist in GetComponentsInChildren<PistonFist>(true)) fist.SetFrozen(true);
            foreach (var cannon in GetComponentsInChildren<RivetCannon>(true)) cannon.enabled = false;
            var chase = GetComponent<Robot1ChaseController>();
            if (chase) chase.enabled = false;

            if (m_Agent && m_Agent.enabled && m_Agent.isOnNavMesh)
            {
                m_Agent.isStopped = true;
                m_Agent.ResetPath();
                m_Agent.velocity = Vector3.zero;
            }
        }

        void OnDamaged(float damage, GameObject damageSource)
        {
            PlayHitSpark(damageSource);

            if (!IsArmourBroken && m_Health.CurrentHealth > 0f && m_Health.GetRatio() <= BreakHealthRatio)
                BreakArmour();
        }

        void PlayHitSpark(GameObject damageSource)
        {
            if (HitSparks.Length == 0) return;

            bool hasShot = TryGetShotRay(damageSource, out Vector3 origin, out Vector3 direction);
            int bestIndex = -1;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < HitSparks.Length; i++)
            {
                if (!HitSparks[i]) continue;
                Transform anchor = HitSparks[i].transform;
                Vector3 restPosition = anchor.parent ? anchor.parent.TransformPoint(m_SparkRestPositions[i]) : anchor.position;
                // Without a shot line, fall back to the HoverBot behaviour of a random emitter.
                float score = hasShot
                    ? Vector3.Cross(direction, restPosition - origin).sqrMagnitude
                    : Random.value;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }
            if (bestIndex < 0) return;

            ParticleSystem best = HitSparks[bestIndex];
            best.transform.localPosition = m_SparkRestPositions[bestIndex];
            if (hasShot)
            {
                // Put the sparks on the shot line at the armour's surface, spraying back at the shooter.
                float depth = Vector3.Dot(best.transform.position - origin, direction);
                best.transform.SetPositionAndRotation(origin + direction * (depth - SparkSurfaceOffset),
                    Quaternion.LookRotation(-direction));
            }
            // Restart the burst even if this emitter is still fading from the previous hit.
            best.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            best.Play(true);
        }

        bool TryGetShotRay(GameObject damageSource, out Vector3 origin, out Vector3 direction)
        {
            origin = Vector3.zero;
            direction = Vector3.zero;
            if (!damageSource) return false;

            // The player's camera is where their shots come from; aim through it for a close match.
            var eye = damageSource.GetComponentInChildren<Camera>();
            if (eye)
            {
                origin = eye.transform.position;
                direction = eye.transform.forward;
                return true;
            }

            origin = damageSource.transform.position;
            direction = transform.position + Vector3.up - origin;
            if (direction.sqrMagnitude < .0001f) return false;
            direction.Normalize();
            return true;
        }

        /// <summary>Strips the armour immediately, regardless of health.</summary>
        public void BreakArmour()
        {
            if (IsArmourBroken) return;
            IsArmourBroken = true;

            for (int i = 0; i < m_Damageables.Length; i++)
                if (m_Damageables[i]) m_Damageables[i].DamageMultiplier = m_BaseDamageMultipliers[i];

            if (BodyRenderer)
            {
                SpawnDebris();
                if (ExposedMaterial)
                {
                    var exposed = new Material[BodyRenderer.sharedMaterials.Length];
                    for (int i = 0; i < exposed.Length; i++) exposed[i] = ExposedMaterial;
                    BodyRenderer.sharedMaterials = exposed;
                }
            }

            Vector3 center = BodyCenter();
            for (int i = 0; i < HitSparks.Length; i++)
            {
                ParticleSystem spark = HitSparks[i];
                if (!spark) continue;
                spark.transform.localPosition = m_SparkRestPositions[i];
                Vector3 outward = spark.transform.position - center;
                if (outward.sqrMagnitude > .0001f) spark.transform.rotation = Quaternion.LookRotation(outward);
                spark.Play(true);
            }

            if (BreakSfx)
                AudioUtility.CreateSFX(BreakSfx, transform.position, AudioUtility.AudioGroups.Impact, 1f);

            if (CoreTarget) CoreTarget.SetVulnerable();
            onArmourBroken?.Invoke();
        }

        Vector3 BodyCenter()
        {
            return BodyRenderer ? BodyRenderer.bounds.center : transform.position + Vector3.up;
        }

        void SpawnDebris()
        {
            Mesh sourceMesh = BodyRenderer.sharedMesh;
            if (!sourceMesh || !sourceMesh.isReadable) return;

            var baked = new Mesh();
            BodyRenderer.BakeMesh(baked, true);
            Vector3[] vertices = baked.vertices;
            Vector3[] normals = baked.normals;
            Vector4[] tangents = baked.tangents;
            Vector2[] uvs = baked.uv;
            BoneWeight[] weights = sourceMesh.boneWeights;
            int[] boneRegions = BoneRegions(BodyRenderer.bones);

            // Triangles grouped by the armour region of their first vertex's strongest bone.
            var regionTriangles = new List<int>[k_RegionCount];
            for (int r = 0; r < k_RegionCount; r++) regionTriangles[r] = new List<int>();
            int[] triangles = baked.triangles;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int region = weights.Length > 0 ? boneRegions[weights[triangles[t]].boneIndex0] : 1;
                regionTriangles[Mathf.Max(0, region)].AddRange(new[] { triangles[t], triangles[t + 1], triangles[t + 2] });
            }
            Object.Destroy(baked);

            Transform rendererTransform = BodyRenderer.transform;
            Vector3 bodyCenter = BodyCenter();
            Vector3 inheritedVelocity = m_Agent && m_Agent.enabled ? m_Agent.velocity : Vector3.zero;
            Material[] materials = DebrisMaterial ? new[] { DebrisMaterial }
                : m_ArmourMaterials != null && m_ArmourMaterials.Length > 0 ? new[] { m_ArmourMaterials[0] }
                : BodyRenderer.sharedMaterials;

            var pieces = new List<Collider>();
            var remap = new int[vertices.Length];
            for (int r = 0; r < k_RegionCount; r++)
            {
                List<int> tris = regionTriangles[r];
                if (tris.Count < 9) continue;

                for (int i = 0; i < remap.Length; i++) remap[i] = -1;
                var pieceVertices = new List<Vector3>();
                var pieceNormals = new List<Vector3>();
                var pieceTangents = new List<Vector4>();
                var pieceUvs = new List<Vector2>();
                var pieceTriangles = new int[tris.Count];
                for (int i = 0; i < tris.Count; i++)
                {
                    int v = tris[i];
                    if (remap[v] < 0)
                    {
                        remap[v] = pieceVertices.Count;
                        pieceVertices.Add(vertices[v]);
                        if (normals.Length == vertices.Length) pieceNormals.Add(normals[v]);
                        if (tangents.Length == vertices.Length) pieceTangents.Add(tangents[v]);
                        if (uvs.Length == vertices.Length) pieceUvs.Add(uvs[v]);
                    }
                    pieceTriangles[i] = remap[v];
                }

                // Recentre each chunk on itself so it tumbles around its own middle.
                var bounds = new Bounds(pieceVertices[0], Vector3.zero);
                foreach (var v in pieceVertices) bounds.Encapsulate(v);
                for (int i = 0; i < pieceVertices.Count; i++) pieceVertices[i] -= bounds.center;

                var mesh = new Mesh { name = "Armour chunk " + r };
                mesh.SetVertices(pieceVertices);
                if (pieceNormals.Count == pieceVertices.Count) mesh.SetNormals(pieceNormals);
                if (pieceTangents.Count == pieceVertices.Count) mesh.SetTangents(pieceTangents);
                if (pieceUvs.Count == pieceVertices.Count) mesh.SetUVs(0, pieceUvs);
                mesh.SetTriangles(pieceTriangles, 0);
                mesh.RecalculateBounds();

                // BakeMesh already applied the renderer's scale, so only position and rotation remain.
                Vector3 worldCenter = rendererTransform.position + rendererTransform.rotation * bounds.center;
                var piece = new GameObject("Armour Debris " + r);
                piece.transform.SetPositionAndRotation(worldCenter, rendererTransform.rotation);
                piece.AddComponent<MeshFilter>().sharedMesh = mesh;
                piece.AddComponent<MeshRenderer>().sharedMaterials = materials;
                var box = piece.AddComponent<BoxCollider>();
                pieces.Add(box);

                var body = piece.AddComponent<Rigidbody>();
                body.mass = DebrisMass;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                Vector3 outward = Vector3.ProjectOnPlane(worldCenter - bodyCenter, Vector3.up);
                outward = outward.sqrMagnitude > .0001f ? outward.normalized : Random.insideUnitSphere;
                body.linearVelocity = inheritedVelocity + outward * (DebrisOutwardSpeed * Random.Range(.7f, 1.3f)) +
                                      Vector3.up * (DebrisUpwardSpeed * Random.Range(.6f, 1.2f));
                body.angularVelocity = Random.insideUnitSphere * 8f;

                piece.AddComponent<ArmourDebris>().Lifetime = DebrisLifetime * Random.Range(.85f, 1.15f);
            }

            // Chunks start interlocked; letting them collide with each other or the robot would blast them apart.
            var ignored = new List<Collider>(GetComponentsInChildren<Collider>(true));
            if (m_ActorsManager && m_ActorsManager.Player)
                ignored.AddRange(m_ActorsManager.Player.GetComponentsInChildren<Collider>(true));
            for (int i = 0; i < pieces.Count; i++)
            {
                for (int j = i + 1; j < pieces.Count; j++) Physics.IgnoreCollision(pieces[i], pieces[j]);
                foreach (var other in ignored)
                    if (other) Physics.IgnoreCollision(pieces[i], other);
            }
        }

        int[] BoneRegions(Transform[] bones)
        {
            var boneToRegion = new Dictionary<Transform, int>();
            if (m_Animator && m_Animator.isHuman)
            {
                foreach (var pair in k_Regions)
                {
                    Transform bone = m_Animator.GetBoneTransform(pair.Key);
                    if (bone) boneToRegion[bone] = pair.Value;
                }
            }

            var regions = new int[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                regions[i] = 1;
                for (Transform current = bones[i]; current && current != transform; current = current.parent)
                {
                    if (boneToRegion.TryGetValue(current, out int region))
                    {
                        regions[i] = region;
                        break;
                    }
                }
            }
            return regions;
        }
    }
}
