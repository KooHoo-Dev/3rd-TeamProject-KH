using System.Collections;
using System.Collections.Generic;
using TerrainCollapse.Networking;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>
    /// MineItem.Use()에 의해 플레이어가 서있던 타일에 설치되는 지뢰.
    /// DynamiteExplosive와 달리 퓨즈 타이머로 터지는 게 아니라,
    /// 설치 후 armDelay(감지 비활성 유예 시간) 동안은 아무것도 감지하지 않다가,
    /// 그 이후 감지가 활성화되면 감지 반경 안에 플레이어가 들어오는 순간 폭발한다.
    /// 실제 폭발 결과 처리(이펙트/지형 채굴/플레이어 피해)는 ExplosiveBase를 상속받아 재사용한다.
    /// 같은 타일 셀에는 중복 설치가 불가능하도록 셀 점유 상태를 정적으로 관리한다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class MineExplosive : ExplosiveBase
    {
        [Header("Detection")]
        [SerializeField] private LayerMask playerLayerMask; // 감지(트리거) 대상 - 플레이어만

        [Header("Mine Setting Asset")]
        [SerializeField] private MineData data;

        [Header("Visual Feedback - Armed Blink")]
        [SerializeField] private float armedBlinkInterval = 0.25f; // 감지 활성화 후 경고 깜빡임 간격
        [SerializeField] private Color armedBlinkColor = Color.red;
        [SerializeField] private Color disarmedColor = Color.white; // 유예 시간 동안(안전) 표시 색

        // 어떤 타일 셀에 지뢰가 설치돼 있는지 전역으로 추적 (중복 설치 방지용)
        private static readonly Dictionary<Vector2Int, MineExplosive> occupiedCells = new();

        private SpriteRenderer spriteRenderer;
        private Color originalColor;
        private bool isDetectionActive;
        private bool isServerControlled;
        private bool explosionRequested;
        private string mineID;
        private float detectionRadius;
        private bool hasCell;
        private Vector2Int placedCell;

        /// <summary>주어진 셀에 이미 지뢰가 설치돼 있는지 여부.</summary>
        public static bool IsCellOccupied(Vector2Int cell) => occupiedCells.ContainsKey(cell);

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            originalColor = spriteRenderer.color;
        }

        /// <summary>
        /// MineItem.Use()에서 설치 직후 호출. 위치/셀 세팅 및 유예시간 코루틴 시작.
        /// cell을 알 수 없는 경우(지형 정보 없음 등)에는 hasCell을 false로 두고,
        /// 이 지뢰는 중복 설치 판정에서 제외된다.
        /// </summary>
        public void Place(Vector3 position, Vector2Int? cell = null)
        {
            detectionRadius = data.DetectionRadius;
            PlaceInternal(position, cell, data.ArmDelay);
        }

        /// <summary>서버가 승인한 설치 결과만 멀티플레이 지뢰로 표시한다.</summary>
        public void PlaceFromServer(DynamiteThrownMessage message)
        {
            mineID = message.ProjectileID;
            isServerControlled = true;
            detectionRadius = message.DetectionRadius;
            PlaceInternal(
                new Vector2(message.StartX, message.StartY),
                new Vector2Int(message.CellX, message.CellY),
                message.ArmDelay);
        }

        private void PlaceInternal(Vector3 position, Vector2Int? cell, float armDelay)
        {
            transform.position = position;
            isDetectionActive = false;
            explosionRequested = false;

            if (cell.HasValue)
            {
                hasCell = true;
                placedCell = cell.Value;
                occupiedCells[placedCell] = this;
            }

            spriteRenderer.color = disarmedColor;

            StartCoroutine(ArmRoutine(Mathf.Max(0f, armDelay)));
        }

        private void OnDestroy()
        {
            if (hasCell == false)
                return;

            // 이 지뢰가 그 셀의 마지막 등록자일 때만 제거 (혹시 모를 재설치 경합 대비)
            if (occupiedCells.TryGetValue(placedCell, out MineExplosive registered) && registered == this)
                occupiedCells.Remove(placedCell);
        }

        // 설치자가 대피할 수 있도록 armDelay 동안 감지를 비활성 상태로 유지하다가 활성화
        private IEnumerator ArmRoutine(float armDelay)
        {
            yield return new WaitForSeconds(armDelay);

            isDetectionActive = true;

            StartCoroutine(DetectionRoutine());
            StartCoroutine(ArmedBlinkRoutine());
        }

        // 감지 활성화 이후, 일정 주기로 감지 반경 내 플레이어 존재 여부를 확인
        private IEnumerator DetectionRoutine()
        {
            WaitForSeconds wait = new WaitForSeconds(data.DetectionInterval);

            while (isDetectionActive)
            {
                if (IsPlayerInRange())
                {
                    if (isServerControlled)
                    {
                        if (explosionRequested == false &&
                            RoomClient.Instance?.RequestDynamiteExplosion(mineID, transform.position) == true)
                            explosionRequested = true;
                    }
                    else
                    {
                        Explode();
                        yield break;
                    }
                }

                yield return wait;
            }
        }

        /// <summary>서버가 감지 요청을 거절했을 때 다음 감지 주기에 다시 요청한다.</summary>
        public void AllowExplosionRetry()
        {
            if (isServerControlled)
                explosionRequested = false;
        }

        private bool IsPlayerInRange()
        {
            if (isServerControlled)
            {
                RoomClient roomClient = RoomClient.Instance;
                return roomClient != null &&
                       (roomClient.LocalPlayerPosition - (Vector2)transform.position).sqrMagnitude <=
                       detectionRadius * detectionRadius;
            }

            Collider2D hit = Physics2D.OverlapCircle(
                transform.position,
                detectionRadius,
                playerLayerMask);

            return hit != null;
        }

        // 감지 활성화 상태임을 알리는 경고 깜빡임
        private IEnumerator ArmedBlinkRoutine()
        {
            bool isBlinkOn = false;

            while (isDetectionActive)
            {
                isBlinkOn = !isBlinkOn;
                spriteRenderer.color = isBlinkOn ? armedBlinkColor : originalColor;

                yield return new WaitForSeconds(armedBlinkInterval);
            }
        }

        private void Explode()
        {
            isDetectionActive = false;

            StopAllCoroutines();

            Detonate(data.ExplosionRadius, data.Power);

            Destroy(gameObject);
        }

        public void ExplodeFromServer(Vector2 explosionPosition, float explosionRadius)
        {
            isDetectionActive = false;
            StopAllCoroutines();
            transform.position = explosionPosition;
            SpawnExplosionEffect(explosionPosition, explosionRadius);
            Destroy(gameObject);
        }
    }
}
