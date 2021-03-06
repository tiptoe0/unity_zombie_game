using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Photon.Pun;


public class Enemy : LivingEntity
{
    public Slider zombieHealthSlider; //좀비 체력을 표시할 UI슬라이더

    public LayerMask whatIsTarget;  //추적 대상 레이어

    private LivingEntity targetEntity;  //추적할 대상
    private NavMeshAgent pathFinder;    //경로계산 AI 에이전트

    public ParticleSystem hitEffect;    //피격시 재생할 파티클 효과
    public AudioClip deathSound;    //사망시 재생할 소리
    public AudioClip hitSound;  //피격시 재생할 소리

    private Animator enemyAnimaotr;  //애니메이터 컴포넌트
    private AudioSource enemyAudioPlayer;    //오디오소스 컴포넌트
    private Renderer enemyRenderer;  //렌더러 컴포넌트

    public float damage = 20f; //공격력
    public float timeBetAttack = 0.5f;  //공격 간격
    private float lastAttackTime;   //마지막 공격 시점

    //추적할 대상이 존재하는지 알려주는 프로퍼티
    private bool hasTarget {
        get {
            //추적할 대상이 존재하고, 대상이 사망 안하면 true
            if (targetEntity != null && !targetEntity.dead) { return true; }
            return false;
        }
    }

    private void Awake()
    {
        //게임오브젝트로부터 사용할 컴포넌트 가져옴
        pathFinder = GetComponent<NavMeshAgent>();
        enemyAnimaotr = GetComponent<Animator>();
        enemyAudioPlayer = GetComponent<AudioSource>();
        //렌더러 컴포넌트는 자식 게임 오브젝트에 있으므로
        enemyRenderer = GetComponentInChildren<Renderer>();
    }

    //적 AI의 초기 스펙을 결정하는 셋업 메서드
    [PunRPC]
    public void Setup(float newHealth, float newDamage, float newSpeed, Color skinColor) {
        //체력 설정
        startingHealth = newHealth;
        health = newHealth;
        //공격력 설정
        damage = newDamage;
        //내비메시 에이전트의 이동속도 설정
        pathFinder.speed = newSpeed;
        //렌더러가 사용중인 머티리얼의 컬러를 변경, 외형 색이 변함
        enemyRenderer.material.color = skinColor;
    }

    //게임 오브젝트 활성화와 동시에 AI추격 루틴 시작
    private void Start()
    {
        zombieHealthSlider.gameObject.SetActive(true);  //체력 슬라이더 표시
        if (!PhotonNetwork.IsMasterClient) { return; }
        StartCoroutine(UpdatePath());
    }

    private void Update()
    {
        if (!PhotonNetwork.IsMasterClient) { return; }

        //추적 대상의 존재 여부에 따라 다른 애니메이션 재생
        enemyAnimaotr.SetBool("HasTarget", hasTarget);

    }

    //주기적으로 추적할 대상의 위치를 찾아 경로 갱신
    private IEnumerator UpdatePath() {
        //살아있는 동안 무한 루프
        while (!dead) {
            if (hasTarget)
            {
                //추적 대상 존재 : 경로 갱신하고 AI이동 진행
                pathFinder.isStopped = false;
                pathFinder.SetDestination(targetEntity.transform.position);
            }
            else {
                //추적대상 없음 : AI이동 중지
                pathFinder.isStopped = true;

                //20유닛의 반지름을 가진 가상의 구와 겹치는 모든 콜라이더 가져옴
                //whatIsTarget레이어를 가진 콜라이더만 가져오도록 필터링
                Collider[] colliders = Physics.OverlapSphere(transform.position, 20f, whatIsTarget);

                //모든 콜라이더를 순회하면서 살아있는 LivingEntity찾기
                for (int i = 0; i < colliders.Length; i++) {
                    //콜라이더로부터 LivingEntity컴포넌트 가져옴
                    LivingEntity livingEntity = colliders[i].GetComponent<LivingEntity>();
                    
                    //컴포넌트 존재, 살아있는 경우
                    if (livingEntity != null && !livingEntity.dead) {
                        //추적 대상으로 설정
                        targetEntity = livingEntity;
                        break;
                    }
                }
            }
            yield return new WaitForSeconds(0.25f); //0.25초 주기로 반복
        }
    }

    //데미지를 입었을때 실행 처리
    [PunRPC]
    public override void OnDamage(float damage, Vector3 hitPoint, Vector3 hitNormal)
    {
        //아직 사망하지 않은 경우 피격효과 발생
        if (!dead)
        {
            //공격을 받은 지점과 방향으로 파티클 효과
            hitEffect.transform.position = hitPoint;
            hitEffect.transform.rotation = Quaternion.LookRotation(hitNormal);
            hitEffect.Play();

            zombieHealthSlider.maxValue = startingHealth;
            zombieHealthSlider.value = health;

            //효과음재생
            enemyAudioPlayer.PlayOneShot(hitSound);
        }

        //if (health <= 0) { zombieHealthSlider.gameObject.SetActive(false); }

        //데미지 적용
        base.OnDamage(damage, hitPoint, hitNormal);
    }

    //사망처리
    public override void Die()
    {
        //사망 처리적용
        base.Die();

        //다른 AI를 방해하지 않도록 자신의 모든 콜라이더 비활성화
        Collider[] enemyColliders = GetComponents<Collider>();
        for(int i=0; i<enemyColliders.Length; i++)
        {
            enemyColliders[i].enabled = false;
        }
        zombieHealthSlider.gameObject.SetActive(false); //체력바 비활성화

        //AI추적을 중지하고 내비메시 컴포넌트 비활성화
        pathFinder.isStopped = true;
        pathFinder.enabled = false;
        //사망 애니메이션 재생
        enemyAnimaotr.SetTrigger("Die");
        //사망효과음 재생
        enemyAudioPlayer.PlayOneShot(deathSound);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!PhotonNetwork.IsMasterClient) { return; }

        //트리거 충돌한 상대방 게임 오브젝트가 추적 대상이라면 공격 실행
        //자신 사망 X, 최근 공격 시점에서 timeBetAttack이상 시간이 지났다면 공격 가능
        if (!dead && Time.time >= lastAttackTime + timeBetAttack) {
            //상대방의 LivingEntity 타입 가져오기 시도
            LivingEntity attackTarget = other.GetComponent<LivingEntity>();
            //상대방의 LivingEntity가 자신의 추적 대상이면 공격 실행
            if (attackTarget != null && attackTarget == targetEntity) {
                //최근 공격시간 갱신
                lastAttackTime = Time.time;

                //상대방의 피격 위치와 피격 방향을 근삿값으로 계산
                Vector3 hitPoint = other.ClosestPoint(transform.position);
                Vector3 hitNormal = transform.position - other.transform.position;
                //공격 실행
                attackTarget.OnDamage(damage, hitPoint, hitNormal);
            }
        }
    }
}
