using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.AI
{
    [RequireComponent(typeof(EnemyController))]
    public class EnemyTurret : MonoBehaviour
    {
        public enum AIState
        {
            Idle,
            Attack,
        }

        public Transform TurretPivot;
        public Transform TurretAimPoint;
        public Animator Animator;
        public float AimRotationSharpness = 5f;
        public float LookAtRotationSharpness = 2.5f;
        public float DetectionFireDelay = 1f;
        public float AimingTransitionBlendTime = 1f;

        [Tooltip("The random hit damage effects")]
        public ParticleSystem[] RandomHitSparks;

        public ParticleSystem[] OnDetectVfx;
        public AudioClip OnDetectSfx;

        public AIState AiState { get; private set; }

        EnemyController m_EnemyController;
        Health m_Health;
        Quaternion m_RotationWeaponForwardToPivot;
        float m_TimeStartedDetection;
        float m_TimeLostDetection;
        Quaternion m_PreviousPivotAimingRotation;
        Quaternion m_PivotAimingRotation;

        const string k_AnimOnDamagedParameter = "OnDamaged";
        const string k_AnimIsActiveParameter = "IsActive";

        void Start()
        {
            m_Health = GetComponent<Health>();
            DebugUtility.HandleErrorIfNullGetComponent<Health, EnemyTurret>(m_Health, this, gameObject);
            if (m_Health) m_Health.OnDamaged += OnDamaged;

            m_EnemyController = GetComponent<EnemyController>();
            DebugUtility.HandleErrorIfNullGetComponent<EnemyController, EnemyTurret>(m_EnemyController, this,
                gameObject);
            if (!m_EnemyController)
            {
                enabled = false;
                return;
            }

            m_EnemyController.onDetectedTarget += OnDetectedTarget;
            m_EnemyController.onLostTarget += OnLostTarget;

            // Remember the rotation offset between the pivot's forward and the weapon's forward
            WeaponController currentWeapon = m_EnemyController.GetCurrentWeapon();
            if (!TurretPivot || !TurretAimPoint || !currentWeapon || !currentWeapon.WeaponMuzzle)
            {
                Debug.LogError($"{name}: EnemyTurret requires a pivot, aim point, current weapon, and weapon muzzle.", this);
                enabled = false;
                return;
            }

            m_RotationWeaponForwardToPivot =
                Quaternion.Inverse(currentWeapon.WeaponMuzzle.rotation) * TurretPivot.rotation;

            // Start with idle
            AiState = AIState.Idle;

            m_TimeStartedDetection = Mathf.NegativeInfinity;
            m_PreviousPivotAimingRotation = TurretPivot.rotation;
        }

        void OnDestroy()
        {
            if (m_Health) m_Health.OnDamaged -= OnDamaged;
            if (m_EnemyController)
            {
                m_EnemyController.onDetectedTarget -= OnDetectedTarget;
                m_EnemyController.onLostTarget -= OnLostTarget;
            }
        }

        void Update()
        {
            UpdateCurrentAiState();
        }

        void LateUpdate()
        {
            UpdateTurretAiming();
        }

        void UpdateCurrentAiState()
        {
            // Handle logic 
            switch (AiState)
            {
                case AIState.Attack:
                    GameObject target = m_EnemyController.KnownDetectedTarget;
                    if (!target)
                    {
                        AiState = AIState.Idle;
                        return;
                    }

                    bool mustShoot = Time.time > m_TimeStartedDetection + DetectionFireDelay;
                    // Calculate the desired rotation of our turret (aim at target)
                    Vector3 directionToTarget =
                        (target.transform.position - TurretAimPoint.position).normalized;
                    Quaternion offsettedTargetRotation =
                        Quaternion.LookRotation(directionToTarget) * m_RotationWeaponForwardToPivot;
                    m_PivotAimingRotation = Quaternion.Slerp(m_PreviousPivotAimingRotation, offsettedTargetRotation,
                        (mustShoot ? AimRotationSharpness : LookAtRotationSharpness) * Time.deltaTime);

                    // shoot
                    if (mustShoot)
                    {
                        Vector3 correctedDirectionToTarget =
                            (m_PivotAimingRotation * Quaternion.Inverse(m_RotationWeaponForwardToPivot)) *
                            Vector3.forward;

                        m_EnemyController.TryAtack(TurretAimPoint.position + correctedDirectionToTarget);
                    }

                    break;
            }
        }

        void UpdateTurretAiming()
        {
            switch (AiState)
            {
                case AIState.Attack:
                    TurretPivot.rotation = m_PivotAimingRotation;
                    break;
                default:
                    // Use the turret rotation of the animation
                    TurretPivot.rotation = Quaternion.Slerp(m_PivotAimingRotation, TurretPivot.rotation,
                        (Time.time - m_TimeLostDetection) / AimingTransitionBlendTime);
                    break;
            }

            m_PreviousPivotAimingRotation = TurretPivot.rotation;
        }

        void OnDamaged(float dmg, GameObject source)
        {
            if (RandomHitSparks != null && RandomHitSparks.Length > 0)
            {
                int n = Random.Range(0, RandomHitSparks.Length - 1);
                if (RandomHitSparks[n]) RandomHitSparks[n].Play();
            }

            if (Animator) Animator.SetTrigger(k_AnimOnDamagedParameter);
        }

        void OnDetectedTarget()
        {
            if (AiState == AIState.Idle)
            {
                AiState = AIState.Attack;
            }

            for (int i = 0; OnDetectVfx != null && i < OnDetectVfx.Length; i++)
            {
                if (OnDetectVfx[i]) OnDetectVfx[i].Play();
            }

            if (OnDetectSfx)
            {
                AudioUtility.CreateSFX(OnDetectSfx, transform.position, AudioUtility.AudioGroups.EnemyDetection, 1f);
            }

            if (Animator) Animator.SetBool(k_AnimIsActiveParameter, true);
            m_TimeStartedDetection = Time.time;
        }

        void OnLostTarget()
        {
            if (AiState == AIState.Attack)
            {
                AiState = AIState.Idle;
            }

            for (int i = 0; OnDetectVfx != null && i < OnDetectVfx.Length; i++)
            {
                if (OnDetectVfx[i]) OnDetectVfx[i].Stop();
            }

            if (Animator) Animator.SetBool(k_AnimIsActiveParameter, false);
            m_TimeLostDetection = Time.time;
        }
    }
}
