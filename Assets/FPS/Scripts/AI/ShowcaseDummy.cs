using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.AI;

namespace Unity.FPS.AI
{
    /// <summary>
    /// Puts an enemy on display: it stands where it was placed, facing the way it was placed, and never notices,
    /// chases or attacks the player. Its health, armour, core execution, Disassembler shock and corpse all still
    /// work, so the tutorial drill can break, shock and execute it. The tutorial wing's targets carry it.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShowcaseDummy : MonoBehaviour
    {
        DetectionModule m_Detection;
        NavMeshAgent m_Agent;
        Quaternion m_Facing;
        Vector3 m_Position;
        Health m_Health;

        void Awake()
        {
            m_Facing = transform.rotation;
            m_Position = transform.position;
            m_Health = GetComponent<Health>();
        }

        void Start()
        {
            // Movement and attack brains are switched off for good. Electrocution only re-enables what it disabled
            // itself, so a shock never wakes them.
            foreach (var chase in GetComponentsInChildren<Robot1ChaseController>(true)) chase.enabled = false;
            foreach (var mobile in GetComponentsInChildren<EnemyMobile>(true)) mobile.enabled = false;
            foreach (var turret in GetComponentsInChildren<EnemyTurret>(true)) turret.enabled = false;
            foreach (var fist in GetComponentsInChildren<PistonFist>(true)) fist.enabled = false;
            foreach (var cannon in GetComponentsInChildren<RivetCannon>(true)) cannon.SetFrozen(true);

            // Blind: EnemyController still runs its detection every frame, so give it nothing to see.
            var enemy = GetComponent<EnemyController>();
            m_Detection = enemy ? enemy.DetectionModule : GetComponentInChildren<DetectionModule>();
            if (m_Detection)
            {
                m_Detection.DetectionRange = 0f;
                m_Detection.AttackRange = 0f;
            }

            m_Agent = GetComponent<NavMeshAgent>();
            if (m_Agent && m_Agent.enabled && m_Agent.isOnNavMesh)
            {
                m_Agent.isStopped = true;
                m_Agent.velocity = Vector3.zero;
            }
        }

        void LateUpdate()
        {
            // An execution turns the victim to face the executioner and its corpse falls; leave both alone.
            var target = GetComponent<CoreExecutionTarget>();
            if (target && target.Stage != CoreExecutionTarget.ExecutionStage.None) return;
            if (m_Health && m_Health.CurrentHealth <= 0f) return;
            transform.rotation = m_Facing;
            Vector3 drift = transform.position - m_Position;
            drift.y = 0f;
            if (drift.sqrMagnitude > .0025f) transform.position = new Vector3(m_Position.x, transform.position.y, m_Position.z);
        }
    }
}
