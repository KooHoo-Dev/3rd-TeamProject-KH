using System;
using System.Collections;
using Character;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>PlayerHp의 사망 이벤트를 감지해 대기 시간 이후 시작 지점에서 재시작시키는 리스폰 컨트롤러.
    /// 이동/매몰 등 플레이어 자체 상태는 TestPlayerController가, 체력은 PlayerHp가 소유하며
    /// 이 컨트롤러는 둘을 연결해 "사망 → 대기 → 리스폰" 흐름만 제어</summary>
    [RequireComponent(typeof(TestPlayerController), typeof(PlayerHp))]
    public sealed class RespawnController : MonoBehaviour
    {
        [Header("Respawn Settings")]
        [SerializeField, Min(0f)] private float respawnDelay = 2f;
        [SerializeField] private Transform spawnPoint;

        private TestPlayerController playerController;
        private PlayerHp playerHp;
        private Vector2 spawnPosition;
        private Coroutine respawnRoutine;

        public event Action PlayerDied;
        public event Action PlayerRespawned;

        private void Awake()
        {
            playerController = GetComponent<TestPlayerController>();
            playerHp = GetComponent<PlayerHp>();
        }

        private void Start()
        {
            spawnPosition = spawnPoint != null
                ? spawnPoint.position
                : playerController.CurrentPosition;
        }

        private void OnEnable()
        {
            playerHp.Died += HandleDied;
        }

        private void OnDisable()
        {
            playerHp.Died -= HandleDied;
        }

        private void HandleDied()
        {
            playerController.EnterDeadState();
            PlayerDied?.Invoke();

            if (respawnRoutine != null) StopCoroutine(respawnRoutine);
            respawnRoutine = StartCoroutine(RespawnAfterDelay());
        }

        private IEnumerator RespawnAfterDelay()
        {
            if (respawnDelay > 0f) yield return new WaitForSeconds(respawnDelay);

            playerController.Respawn(spawnPosition);
            playerHp.ResetHp();

            respawnRoutine = null;
            PlayerRespawned?.Invoke();
        }
    }
}