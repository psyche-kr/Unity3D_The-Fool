using UnityEngine;
using UnityEngine.AI;
using System.Collections;

public enum MonsterState { Wander, Chase, Attack, Death }

public abstract class MonsterBase : MonoBehaviour
{
    const float MinWanderDistance = 2f;      // 이보다 가까운 배회 목표는 다시 고른다
    const float ObstacleCheckRadius = 0.5f;  // 배회 목표 지점의 장애물 검사 반경

    #region Variables
    public MonsterState monsterState;
    public MonsterState MonsterState { set { monsterState = value; } }

    [SerializeField] protected Transform target;
    [SerializeField] protected float chaseRange;
    [SerializeField] protected float attackRange;
    [SerializeField] protected float moveRadius;
    [SerializeField] protected float distanceToPlayer;
    [SerializeField] LayerMask obstaclesLayer; // 장애물로 인지할 레이어
    [HideInInspector] public float ChaseRange { get { return chaseRange; } }
    [HideInInspector] public float AttackRange { get { return attackRange; } }
    [HideInInspector] public float MoveRadius { get { return moveRadius; } }
    protected NavMeshAgent agent;
    protected Animator animator;
    protected bool isAttacking = false;
    protected bool isDead = false;
    bool isRotate = false;
    float attackCooldown = 3.0f;
    float attackCooldownTimer = 0;
    #endregion

    #region Methods
    void Start()
    {
        animator = GetComponent<Animator>();
        agent = GetComponent<NavMeshAgent>();
        target = GameObject.FindWithTag("Player")?.transform;

        if (target == null)
        {
            Debug.LogError("MonsterBase.cs에서 Target 지정이 실패했습니다. 스크립트가 비활성화 됩니다");
            enabled = false;
        }
    }

    protected virtual void Update()
    {
        DistanceToPlayer();
        SetCooldown();
        StateMachine();
    }
    #endregion

    #region StateMachine
    void StateMachine()
    {
        switch (monsterState)
        {
            case MonsterState.Wander:
                CheckToRange(chaseRange, MonsterState.Chase); // 추적 범위 안이면 추적 상태
                Wander();
                break;

            case MonsterState.Chase:
                CheckToRange(attackRange, MonsterState.Attack, chaseRange, MonsterState.Wander); // 공격 범위 안이면 공격 상태, 추적 범위 밖이면 배회 상태
                Chase();
                break;

            case MonsterState.Attack:
                FallBackState(attackRange, MonsterState.Chase); // 공격 범위 밖이면 추적 상태
                TryAttack();
                break;

            case MonsterState.Death:
                if (!isDead)
                {
                    Death();
                    isDead = true;
                }
                break;
        }
    }

    // 쿨타임이 끝났을 때만 플레이어를 바라보고 공격을 시작한다
    void TryAttack()
    {
        if (attackCooldownTimer > 0) return;

        StartCoroutine(LookAtPlayer());
        StartCoroutine(Attack());
        attackCooldownTimer = attackCooldown;
    }

    protected virtual void Wander()
    {
        bool arrived = agent.remainingDistance <= agent.stoppingDistance && !agent.pathPending; // 목표에 도달했을 때만 다음 위치 찾기

        if (arrived && TryGetWanderPoint(out Vector3 destination))
        {
            agent.SetDestination(destination);
        }

        MoveBlendTree();
    }

    // 현재 위치 기준 반경 안에서 갈 수 있는 무작위 지점을 고른다 (너무 가깝거나 장애물과 겹치면 실패)
    bool TryGetWanderPoint(out Vector3 point)
    {
        point = transform.position + Random.insideUnitSphere * moveRadius;
        point.y = transform.position.y; // 수평으로만 이동하도록 고정

        bool tooClose = Vector3.Distance(transform.position, point) < MinWanderDistance;
        bool blocked = Physics.CheckSphere(point, ObstacleCheckRadius, obstaclesLayer);

        return !tooClose && !blocked;
    }

    protected virtual void Chase()
    {
        if (target == null)
        {
            Debug.LogError($"{transform.name}의 Chase 함수 에러 발생.\n타겟 플레이어가 감지되지 않았습니다.");
            return;
        }

        MoveToTarget();
        MoveBlendTree();
    }

    protected abstract IEnumerator Attack();

    protected virtual void Death()
    {
        if (agent != null)
        {
            agent.isStopped = true;
            agent.enabled = false;
        }
        animator.SetTrigger("Death");
        StartCoroutine(DestroyObject(3.0f));
    }
    #endregion

    #region Functions
    IEnumerator DestroyObject(float destroyTime) // Death 호출시 오브젝트 파괴
    {
        yield return new WaitForSeconds(destroyTime);
        Destroy(gameObject);
    }

    void DistanceToPlayer() // 플레이어 와의 거리
    {
        if (target != null)
        {
            distanceToPlayer = Vector3.Distance(transform.position, target.position);
        }
    }

    void CheckToRange(float rangeType, MonsterState wantState) // 거리에 따른 상태 변화
    {
        if (distanceToPlayer <= rangeType)
        {
            monsterState = wantState;
        }
    }

    void CheckToRange(float rangeType, MonsterState wantState, float fallBackRangeType, MonsterState fallBackState) // 거리에 따른 상태 변화
    {
        if (distanceToPlayer <= rangeType)
        {
            monsterState = wantState;
        }
        else if (distanceToPlayer > fallBackRangeType)
        {
            monsterState = fallBackState;
        }
    }

    void FallBackState(float rangeType, MonsterState wantState) // 이전 상태로 돌아가게
    {
        if (distanceToPlayer > rangeType)
        {
            monsterState = wantState;
        }
    }

    protected IEnumerator LookAtPlayer() // 플레이어 바라보기
    {
        if (isRotate || target == null) yield break;
        isRotate = true;

        Vector3 direction = (target.position - transform.position).normalized;
        direction.y = 0;
        Quaternion lookRotation = Quaternion.LookRotation(direction);
        float angle = Quaternion.Angle(transform.rotation, lookRotation);

        while (angle > 1f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 5f);
            yield return null;
            angle = Quaternion.Angle(transform.rotation, lookRotation);
        }
        isRotate = false;
    }

    protected void MoveToTarget() // 플레이어 추적
    {
        if (target == null)
        {
            Debug.LogError($"{transform.name}의 MoveToTarget 함수 에러 발생.\n타겟 플레이어가 감지되지 않았습니다.");
            return;
        }

        agent.SetDestination(target.position);
    }

    protected void MoveBlendTree() // 이동 블렌드트리 변경
    {
        float targetMagnitude = agent.velocity.magnitude > 0.1f ? 1f : 0f;
        float currentMagnitude = animator.GetFloat("SpeedMagnitude"); 
        float smoothMagnitude = Mathf.Lerp(currentMagnitude, targetMagnitude, Time.deltaTime * 1.5f);
        animator.SetFloat("SpeedMagnitude", smoothMagnitude);
    }

    void SetCooldown() // 공격 쿨타임
    {
        if (attackCooldown > 0)
        {
            attackCooldownTimer -= Time.deltaTime;
        }
    }
    #endregion
}