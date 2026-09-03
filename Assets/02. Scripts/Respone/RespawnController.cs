using System;
using System.Collections;
using Character;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>PlayerHp의 사망 이벤트를 감지해 대기 시간 이후 시작 지점에서 재시작시키는 리스폰 컨트롤러.
    /// 이동/매몰 등 플레이어 자체 상태는 IPlayerController가, 체력은 PlayerHp가 소유하며
    /// 이 컨트롤러는 둘을 연결해 "사망 → 대기 → 리스폰" 흐름만 제어</summary>
    [RequireComponent(typeof(PlayerHp), typeof(PlayerInventory))]
    public sealed class RespawnController : MonoBehaviour
    {
        [Header("Respawn Settings")]
        [SerializeField, Min(0f)] private float respawnDelay = 2f;
        [SerializeField] private Transform spawnPoint;

        [SerializeField] private TerrainManager terrainManager;

        private IPlayerController playerController;
        private PlayerHp playerHp;
        private PlayerInventory inventory;
        private PlayerDeathDropService deathDropService;
        private Vector2 spawnPosition;
        private Coroutine respawnRoutine;

        public event Action PlayerDied;
        public event Action PlayerRespawned;

        private void Awake()
        {
            playerController = GetComponent(typeof(IPlayerController)) as IPlayerController;
            playerHp = GetComponent<PlayerHp>();
            inventory = GetComponent<PlayerInventory>();

            terrainManager ??= FindFirstObjectByType<TerrainManager>();
            deathDropService = new PlayerDeathDropService();
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
            bool isDeathLootCreated = false;

            if (terrainManager.TryWorldToCell(
                    playerController.CurrentPosition, out Vector2Int deathCell))
            {
                isDeathLootCreated = deathDropService.TryStoreInventory(
                    inventory,
                    terrainManager,
                    deathCell);
            }

            playerController.EnterDeadState(isDeathLootCreated);
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
