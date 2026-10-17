using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.AI;

namespace Unity.FPS.AI
{
    /// <summary>Chase brain for Robot1 using the project's actor registry and NavMesh.</summary>
    [RequireComponent(typeof(EnemyController), typeof(NavMeshAgent))]
    public class Robot1ChaseController : MonoBehaviour
    {
        [Tooltip("How often the robot refreshes its path to the player.")]
        public float RepathInterval = .15f;

        [Tooltip("Distance at which the robot stops chasing the player.")]
        public float StoppingDistance = 2.8f;

        [Tooltip("Allows the robot to move in a straight line when it is not on a NavMesh.")]
        public bool UseDirectMoveFallback = true;

        [Tooltip("Maximum distance used when trying to place an off-mesh robot onto the NavMesh.")]
        public float NavMeshSnapDistance = 4f;

        [Tooltip("How close the robot gets to its post before it stands still, when its target is in another room.")]
        public float PostArrivalRadius = 1.5f;

        [Header("Environment Collision")]
        [Tooltip("Prevents the robot from crossing static corridor and level geometry, including when direct movement is used.")]
        public bool BlockEnvironment = true;

        [Tooltip("Layers considered solid environment for the robot movement sweep.")]
        public LayerMask ObstacleMask = ~0;

        [Min(0f)] public float CollisionPadding = .04f;

        EnemyController m_Enemy;
        NavMeshAgent m_Agent;
        CapsuleCollider m_BodyCollider;
        ActorsManager m_ActorsManager;
        float m_NextRepathTime;
        float m_NextNavMeshProbeTime;

        public GameObject CurrentTarget { get; private set; }

        /// <summary>Set by a weapon to plant the robot's feet (e.g. while a cannon burst is firing).</summary>
        public bool HoldPosition { get; set; }

        void Awake()
        {
            m_Enemy = GetComponent<EnemyController>();
            m_Agent = GetComponent<NavMeshAgent>();
            m_BodyCollider = GetComponent<CapsuleCollider>();
            m_ActorsManager = FindAnyObjectByType<ActorsManager>();

            if (m_Agent)
            {
                m_Agent.stoppingDistance = Mathf.Max(m_Agent.stoppingDistance, StoppingDistance);
                // EnemyController handles facing so the fist can aim at the player while chasing.
                m_Agent.updateRotation = false;
            }
        }

        void Update()
        {
            if (!m_Enemy || !m_Agent || !m_Agent.enabled)
                return;

            // Use the normal detection target when it is ready, but do not make movement
            // depend on EnemyController finishing its Start initialization first.
            var detected = m_Enemy.DetectionModule ? m_Enemy.KnownDetectedTarget : null;
            var target = detected;
            if (!target && m_ActorsManager)
                target = m_ActorsManager.Player;

            CurrentTarget = target;
            if (!target)
            {
                if (m_Agent.hasPath) m_Agent.ResetPath();
                return;
            }

            // Doors split the NavMesh into rooms. A target the robot cannot walk to is in another room:
            // hold this room instead of pressing against the doorway.
            if (!m_Enemy.CanReach(target))
            {
                CurrentTarget = detected;
                ReturnToPost(detected);
                return;
            }

            m_Enemy.OrientTowards(target.transform.position);

            Vector3 planarOffset = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
            float stoppingDistance = Mathf.Max(0.1f, Mathf.Max(StoppingDistance, m_Agent.stoppingDistance));
            if (HoldPosition || planarOffset.magnitude <= stoppingDistance)
            {
                if (m_Agent.isOnNavMesh)
                {
                    m_Agent.isStopped = true;
                    m_Agent.ResetPath();
                }
                return;
            }

            if (!m_Agent.isOnNavMesh)
            {
                // A prefab can be dropped a small distance above or beside the baked surface.
                // Snap it once before falling back to direct movement.
                if (Time.time >= m_NextNavMeshProbeTime)
                {
                    m_NextNavMeshProbeTime = Time.time + 0.5f;
                    if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit,
                            Mathf.Max(0.1f, NavMeshSnapDistance), m_Agent.areaMask))
                    {
                        m_Agent.Warp(hit.position);
                    }
                }

                if (!m_Agent.isOnNavMesh)
                {
                    MoveDirectly(planarOffset);
                    return;
                }
            }

            m_Agent.isStopped = false;
            if (Time.time >= m_NextRepathTime)
            {
                bool accepted = m_Agent.SetDestination(target.transform.position);
                m_NextRepathTime = Time.time + Mathf.Max(.02f, RepathInterval);

                // A target can be outside the baked surface, or the robot can be on a
                // disconnected NavMesh island. Keep the chase functional in that case.
                if (!accepted || (!m_Agent.pathPending && m_Agent.pathStatus == NavMeshPathStatus.PathInvalid))
                    MoveDirectly(planarOffset);
            }
        }

        void ReturnToPost(GameObject threat)
        {
            if (threat) m_Enemy.OrientTowards(threat.transform.position);
            if (!m_Agent.isOnNavMesh) return;

            Vector3 toPost = Vector3.ProjectOnPlane(m_Enemy.HomePosition - transform.position, Vector3.up);
            if (HoldPosition || toPost.magnitude <= PostArrivalRadius)
            {
                m_Agent.isStopped = true;
                m_Agent.ResetPath();
                return;
            }

            m_Agent.isStopped = false;
            if (Time.time >= m_NextRepathTime)
            {
                m_Agent.SetDestination(m_Enemy.HomePosition);
                m_NextRepathTime = Time.time + Mathf.Max(.02f, RepathInterval);
            }
        }

        void MoveDirectly(Vector3 planarOffset)
        {
            if (!UseDirectMoveFallback || planarOffset.sqrMagnitude < .0001f)
                return;

            Vector3 delta = planarOffset.normalized * (m_Agent.speed * Time.deltaTime);
            Vector3 safeDelta = GetEnvironmentSafeDelta(delta);
            if (m_Agent.isOnNavMesh)
                m_Agent.Move(safeDelta);
            else
                transform.position += safeDelta;
        }

        Vector3 GetEnvironmentSafeDelta(Vector3 delta)
        {
            if (!BlockEnvironment || delta.sqrMagnitude < .000001f || !m_BodyCollider)
                return delta;

            GetWorldCapsule(out Vector3 point1, out Vector3 point2, out float radius);
            Vector3 direction = delta.normalized;
            float distance = delta.magnitude;
            float castDistance = distance + Mathf.Max(0f, CollisionPadding);
            RaycastHit[] hits = Physics.CapsuleCastAll(point1, point2, radius, direction, castDistance,
                ObstacleMask, QueryTriggerInteraction.Ignore);

            float nearestDistance = float.PositiveInfinity;
            foreach (RaycastHit hit in hits)
            {
                if (!IsBlockingEnvironmentHit(hit)) continue;
                if (hit.distance < nearestDistance) nearestDistance = hit.distance;
            }

            if (float.IsPositiveInfinity(nearestDistance))
                return delta;

            float safeDistance = Mathf.Clamp(nearestDistance - Mathf.Max(0f, CollisionPadding), 0f, distance);
            return direction * safeDistance;
        }

        void GetWorldCapsule(out Vector3 point1, out Vector3 point2, out float radius)
        {
            Transform colliderTransform = m_BodyCollider.transform;
            Vector3 scale = colliderTransform.lossyScale;
            Vector3 axis = Vector3.zero;
            axis[m_BodyCollider.direction] = 1f;
            axis = colliderTransform.TransformDirection(axis).normalized;

            float axisScale = Mathf.Abs(scale[m_BodyCollider.direction]);
            float perpendicularScale = m_BodyCollider.direction == 0
                ? Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))
                : m_BodyCollider.direction == 1
                    ? Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z))
                    : Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));

            float bodyRadius = Mathf.Max(.01f, m_BodyCollider.radius * perpendicularScale);
            radius = Mathf.Max(.01f, bodyRadius - Mathf.Max(.01f, CollisionPadding));
            float height = Mathf.Max(2f * bodyRadius, m_BodyCollider.height * axisScale);
            Vector3 center = colliderTransform.TransformPoint(m_BodyCollider.center);
            float halfSegment = Mathf.Max(0f, height * .5f - radius);
            point1 = center + axis * halfSegment;
            point2 = center - axis * halfSegment;

            // The prefab's body capsule reaches slightly below the NavMesh plane. Raise
            // only the lower cast endpoint so horizontal movement does not start inside
            // the floor and report an immediate distance-zero obstruction.
            float minimumLowerHeight = Vector3.Dot(transform.position, axis) + radius + CollisionPadding;
            float lowerHeight = Vector3.Dot(point2, axis);
            if (lowerHeight < minimumLowerHeight)
                point2 += axis * (minimumLowerHeight - lowerHeight);
        }

        bool IsBlockingEnvironmentHit(RaycastHit hit)
        {
            Collider collider = hit.collider;
            if (!collider) return false;
            Transform colliderTransform = collider.transform;
            if (colliderTransform == transform || colliderTransform.IsChildOf(transform)) return false;

            // The player and other actors are handled by stopping distance and combat,
            // not as corridor walls.
            if (collider.GetComponentInParent<Actor>()) return false;

            // Upward-facing surfaces are floors or walkable ramps, not corridor walls.
            if (hit.normal.sqrMagnitude > .0001f && Vector3.Dot(hit.normal.normalized, transform.up) > .55f)
                return false;

            return true;
        }
    }
}
