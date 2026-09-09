using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Collider2D))]
    public sealed class FallingTerrainInteraction : MonoBehaviour, IFallingTerrainEscapeTarget
    {
        [Header("Escape Settings")]
        [SerializeField, Min(0.001f)] private float escapeProbeStep = 0.05f;
        [SerializeField, Min(0.01f)] private float escapePushSpeed = 4f;
        [SerializeField, Min(0f)] private float escapeXOffset = 0.5f;

        [Header("Burial Settings")]
        [SerializeField, Min(0.01f)] private float burialDamageInterval = 1f;
        [SerializeField, Min(1)] private int burialDamagePerTick = 1;
        [SerializeField] private TerrainManager terrainManager;

        private readonly Collider2D[] overlapHits = new Collider2D[16];
        private readonly Dictionary<long, FallingTerrainContactResult> resolvedCollapses = new();
        private readonly SortedDictionary<long, FallingTerrainImpactInfo> pendingBurialImpacts = new();

        private IFallingTerrainCharacter character;
        private Collider2D bodyCollider;
        private FallingChunk activeEscapeChunk;
        private FallingChunk burialFollowChunk;
        private Vector2 burialFollowOffset;
        private bool isBuried;
        private float burialDamageElapsed;
        private Vector2 buriedPosition;
        private Coroutine burialRecheckRoutine;

        public bool IsBuried => isBuried;

        public event Action<bool> BurialStateChanged;
        public event Action<FallingChunk, float> CollisionImpactRequested;
        public event Action<FallingTerrainImpactInfo> BurialImpactRequested;
        public event Action<int> BurialDamageRequested;

        private void Awake()
        {
            character = GetComponent<IFallingTerrainCharacter>();
            bodyCollider = GetComponent<Collider2D>();

            if (terrainManager == null) terrainManager = FindFirstObjectByType<TerrainManager>();
        }

        private void OnEnable()
        {
            if (terrainManager == null) return;

            terrainManager.TerrainChanged += HandleTerrainChanged;
            terrainManager.AuthoritativeBatchApplied += HandleAuthoritativeBatchApplied;
            terrainManager.CollapseCancelled += HandleCollapseCancelled;
        }

        private void OnDisable()
        {
            if (terrainManager == null) return;

            terrainManager.TerrainChanged -= HandleTerrainChanged;
            terrainManager.AuthoritativeBatchApplied -= HandleAuthoritativeBatchApplied;
            terrainManager.CollapseCancelled -= HandleCollapseCancelled;
        }

        public void ResetState()
        {
            if (burialRecheckRoutine != null)
            {
                StopCoroutine(burialRecheckRoutine);
                burialRecheckRoutine = null;
            }

            resolvedCollapses.Clear();
            ReleaseEscapeGuard();
            pendingBurialImpacts.Clear();
            burialFollowChunk = null;
            burialFollowOffset = Vector2.zero;
            burialDamageElapsed = 0f;
            character.SetTerrainInteractionLocked(false, character.Position);

            if (isBuried == false) return;

            isBuried = false;
            character.SetTerrainBuried(false, character.Position);
            BurialStateChanged?.Invoke(false);
        }

        private void FixedUpdate()
        {
            if (character == null || terrainManager == null || terrainManager.IsReady == false) return;

            if (isBuried)
            {
                character.SetPosition(buriedPosition);
                TickBurialDamage();
                return;
            }

            if (pendingBurialImpacts.Count == 0)
            {
                TickEscapeGuard();
                return;
            }

            if (burialFollowChunk != null)
            {
                // 청크가 중간 지형에 먼저 걸렸다면 플레이어 추적 고정을 풀어 아래에 남은 공간으로 계속 낙하할 수 있게 한다.
                // 매몰 후보는 서버의 배치 확정/취소가 올 때까지 그대로 유지한다.
                if (burialFollowChunk.IsComplete)
                {
                    burialFollowChunk = null;
                    burialFollowOffset = Vector2.zero;
                    character.SetTerrainInteractionLocked(false, character.Position);
                }
                else
                {
                    Vector2 followPosition = (Vector2)burialFollowChunk.transform.position + burialFollowOffset;
                    followPosition = ResolveBelowFallingChunk(followPosition, burialFollowChunk);
                    followPosition = ResolveFollowPositionAboveStaticTerrain(followPosition);
                    character.SetTerrainInteractionLocked(true, followPosition);
                }
            }

            if (IsGridSpaceEmpty(character.CollisionBounds) == false)
                EnterBuriedState();

            // 청크 추적이 끝나도 서버의 배치 확정 또는 취소가 도착할 때까지
            // pendingBurialImpacts를 유지해 최종 매몰 여부를 판정한다.
        }

        private Vector2 ResolveFollowPositionAboveStaticTerrain(Vector2 desiredPosition)
        {
            Bounds candidate = character.CollisionBounds;
            candidate.center += (Vector3)(desiredPosition - character.Position);

            // 낙하 Chunk 자체는 아직 Grid에서 제거된 상태이므로 여기서는 하단 static 지형만
            // 검사된다. 겹친 셀의 윗면까지 플레이어를 올려 지형 내부 진입을 막는다.
            for (int iteration = 0; iteration < 8; iteration++)
            {
                Vector3 inset = Vector3.one * character.CollisionSkin;
                if (terrainManager.TryWorldToCell(candidate.min + inset, out Vector2Int min) == false ||
                    terrainManager.TryWorldToCell(candidate.max - inset, out Vector2Int max) == false)
                    return desiredPosition;

                float requiredLift = 0f;
                for (int y = min.y; y <= max.y; y++)
                {
                    for (int x = min.x; x <= max.x; x++)
                    {
                        Vector2Int cell = new(x, y);
                        if (terrainManager.HasTerrain(cell) == false ||
                            terrainManager.TryCellToWorldCenter(cell, out Vector3 center) == false)
                            continue;

                        float cellMinX = center.x - terrainManager.CellSize * 0.5f;
                        float cellMaxX = center.x + terrainManager.CellSize * 0.5f;
                        if (candidate.max.x <= cellMinX || candidate.min.x >= cellMaxX) continue;

                        float cellBottom = center.y - terrainManager.CellSize * 0.5f;
                        float cellTop = center.y + terrainManager.CellSize * 0.5f;
                        if (candidate.max.y <= cellBottom) continue;
                        if (candidate.min.y >= cellTop - character.CollisionSkin) continue;

                        requiredLift = Mathf.Max(
                            requiredLift,
                            cellTop - candidate.min.y + character.CollisionSkin);
                    }
                }

                if (requiredLift <= Mathf.Epsilon) return desiredPosition;

                desiredPosition.y += requiredLift;
                candidate.center += Vector3.up * requiredLift;
            }

            return desiredPosition;
        }

        /// <summary>FallingChunk와 플레이어의 충돌을 무시하는 동안 플레이어가 청크 내부로 들어갔다면 청크 아래쪽으로 분리 </summary>
        private Vector2 ResolveBelowFallingChunk(Vector2 desiredPosition, FallingChunk fallingChunk)
        {
            if (fallingChunk == null || fallingChunk.CellSize <= 0f)
                return desiredPosition;

            Bounds candidate = character.CollisionBounds;
            candidate.center += (Vector3)(desiredPosition - character.Position);

            float collisionSkin = character.CollisionSkin;
            float playerMinX = candidate.min.x + collisionSkin;
            float playerMaxX = candidate.max.x - collisionSkin;
            float playerMinY = candidate.min.y + collisionSkin;
            float playerMaxY = candidate.max.y - collisionSkin;
            float halfCellSize = fallingChunk.CellSize * 0.5f;
            float requiredDrop = 0f;

            foreach (Vector2Int localCell in fallingChunk.LocalCells)
            {
                Vector3 tileCenter = fallingChunk.GetLocalCellCenterWorld(localCell);
                float tileMinX = tileCenter.x - halfCellSize;
                float tileMaxX = tileCenter.x + halfCellSize;
                float tileMinY = tileCenter.y - halfCellSize;
                float tileMaxY = tileCenter.y + halfCellSize;

                bool overlapsX = playerMaxX > tileMinX && playerMinX < tileMaxX;
                bool overlapsY = playerMaxY > tileMinY && playerMinY < tileMaxY;
                if (overlapsX == false || overlapsY == false) continue;

                requiredDrop = Mathf.Max(
                    requiredDrop,
                    candidate.max.y - tileMinY + collisionSkin);
            }

            if (requiredDrop > Mathf.Epsilon)
                desiredPosition.y -= requiredDrop;

            return desiredPosition;
        }

        public FallingTerrainContactResult ResolveFallingTerrainContact(
            FallingChunk fallingChunk,
            float impactSpeed)
        {
            if (fallingChunk == null || character == null ||
                terrainManager == null || terrainManager.IsReady == false)
                return FallingTerrainContactResult.Ignored;

            if (resolvedCollapses.TryGetValue(
                    fallingChunk.CollapseId,
                    out FallingTerrainContactResult previousResult))
                return previousResult;

            if (fallingChunk.IsComplete)
                return RememberResult(fallingChunk.CollapseId, FallingTerrainContactResult.Ignored);

            if (IntersectsFallingTerrain(
                    character.CollisionBounds,
                    fallingChunk,
                    includeVerticalPath: true) == false)
                return RememberResult(fallingChunk.CollapseId, FallingTerrainContactResult.Ignored);

            CollisionImpactRequested?.Invoke(fallingChunk, impactSpeed);

            character.CancelClimbing();

            Vector2 fallingVelocity = fallingChunk.BeforeVelocity;
            fallingVelocity.y = Mathf.Min(fallingVelocity.y, 0f);
            character.ApplyFallingVelocity(fallingVelocity);

            if (TryFindEscapeTarget(fallingChunk, out float targetX))
            {
                activeEscapeChunk = fallingChunk;
                character.StartTerrainEscape(targetX, escapePushSpeed);

                return RememberResult(fallingChunk.CollapseId, FallingTerrainContactResult.Escaped);
            }

            FallingTerrainContactResult result = RememberResult(
                fallingChunk.CollapseId,
                FallingTerrainContactResult.NoEscapeSpace);
            BeginAwaitingBurialResolution(fallingChunk);

            return result;
        }

        private void BeginAwaitingBurialResolution(FallingChunk fallingChunk)
        {
            pendingBurialImpacts[fallingChunk.CollapseId] = new FallingTerrainImpactInfo(
                fallingChunk.CollapseId,
                fallingChunk.LocalCells.Count,
                fallingChunk.gameObject);

            burialFollowChunk = fallingChunk;
            burialFollowOffset = character.Position - (Vector2)fallingChunk.transform.position;

            // 청크가 지형으로 변환될 때까지 충돌 무시 상태를 유지하고 플레이어 위치를 고정
            activeEscapeChunk = null;
            character.StopTerrainEscape();
            character.SetTerrainInteractionLocked(true, character.Position);
        }

        private void TickEscapeGuard()
        {
            if (activeEscapeChunk is null) return;
            if (activeEscapeChunk == null)
            {
                ReleaseEscapeGuard();
                return;
            }

            if (IntersectsFallingTerrain(character.CollisionBounds, activeEscapeChunk))
            {
                if (TryFindEscapeTarget(activeEscapeChunk, out float nextTargetX) &&
                    Mathf.Abs(nextTargetX - character.Position.x) > character.CollisionSkin)
                {
                    character.StartTerrainEscape(nextTargetX, escapePushSpeed);
                    return;
                }

                FallingChunk burialChunk = activeEscapeChunk;
                resolvedCollapses[burialChunk.CollapseId] =
                    FallingTerrainContactResult.NoEscapeSpace;
                BeginAwaitingBurialResolution(burialChunk);
                return;
            }

            if (activeEscapeChunk.WorldBounds.max.y > character.CollisionBounds.min.y) return;

            ReleaseEscapeGuard();
        }

        private void ReleaseEscapeGuard()
        {
            if (activeEscapeChunk != null && bodyCollider != null)
                Physics2D.IgnoreCollision(activeEscapeChunk.ShapeCollider, bodyCollider, false);

            activeEscapeChunk = null;
            character.StopTerrainEscape();
        }

        private FallingTerrainContactResult RememberResult(
            long collapseId,
            FallingTerrainContactResult result)
        {
            resolvedCollapses[collapseId] = result;

            return result;
        }

        private bool TryFindEscapeTarget(FallingChunk fallingChunk, out float targetX)
        {
            targetX = character.Position.x;

            if (terrainManager.CellSize <= 0f) return false;

            Bounds collisionBounds = character.CollisionBounds;
            if (terrainManager.TryWorldToCell(collisionBounds.center, out Vector2Int currentCell) == false)
                return false;

            float colliderCenterOffsetX = collisionBounds.center.x - character.Position.x;
            float maximumEscapeDistance = terrainManager.CellSize * escapeXOffset;
            bool isInsideFallingPath = TryGetFallingPathInterval(
                    collisionBounds,
                    fallingChunk,
                    out float tileMinX,
                    out float tileMaxX);

            if (TryGetCellCenteredEscapeTarget(
                    currentCell,
                    colliderCenterOffsetX,
                    fallingChunk,
                    out float currentCellTargetX) &&
                Mathf.Abs(currentCellTargetX - character.Position.x) <=
                maximumEscapeDistance + Mathf.Epsilon &&
                (isInsideFallingPath == false ||
                 IsConcaveEscapePocket(currentCell, fallingChunk)))
            {
                targetX = currentCellTargetX;
                return true;
            }

            if (isInsideFallingPath == false) return false;

            float tileCenterX = (tileMinX + tileMaxX) * 0.5f;
            float offsetFromTileCenter = collisionBounds.center.x - tileCenterX;

            if (Mathf.Abs(offsetFromTileCenter) <= character.CollisionSkin)
            {
                if ((tileMaxX - tileMinX) * 0.5f > maximumEscapeDistance + Mathf.Epsilon)
                    return false;

                return TryGetCellCenteredEscapeTarget(
                           currentCell + Vector2Int.left,
                           colliderCenterOffsetX,
                           fallingChunk,
                           out targetX) ||
                       TryGetCellCenteredEscapeTarget(
                           currentCell + Vector2Int.right,
                           colliderCenterOffsetX,
                           fallingChunk,
                           out targetX);
            }

            bool escapeLeft = offsetFromTileCenter < 0f;
            float distanceToEscapeEdge = escapeLeft
                ? collisionBounds.center.x - tileMinX
                : tileMaxX - collisionBounds.center.x;

            if (distanceToEscapeEdge > maximumEscapeDistance + Mathf.Epsilon) return false;

            return TryGetCellCenteredEscapeTarget(
                currentCell + (escapeLeft ? Vector2Int.left : Vector2Int.right),
                colliderCenterOffsetX,
                fallingChunk,
                out targetX);
        }

        private bool TryGetFallingPathInterval(
            Bounds collisionBounds,
            FallingChunk fallingChunk,
            out float tileMinX,
            out float tileMaxX)
        {
            tileMinX = 0f;
            tileMaxX = 0f;
            float playerCenterX = collisionBounds.center.x;
            float playerMinY = collisionBounds.min.y + character.CollisionSkin;
            float halfCellSize = fallingChunk.CellSize * 0.5f;

            foreach (Vector2Int localCell in fallingChunk.LocalCells)
            {
                Vector3 tileCenter = fallingChunk.GetLocalCellCenterWorld(localCell);
                float currentTileMinX = tileCenter.x - halfCellSize;
                float currentTileMaxX = tileCenter.x + halfCellSize;
                float currentTileMaxY = tileCenter.y + halfCellSize;

                if (playerCenterX <= currentTileMinX || playerCenterX >= currentTileMaxX) continue;
                if (currentTileMaxY <= playerMinY) continue;

                tileMinX = currentTileMinX;
                tileMaxX = currentTileMaxX;
                return true;
            }

            return false;
        }

        private bool IsConcaveEscapePocket(Vector2Int currentCell, FallingChunk fallingChunk)
        {
            if (terrainManager.TryCellToWorldCenter(currentCell, out Vector3 cellCenter) == false)
                return false;

            float cellSize = fallingChunk.CellSize;
            float coordinateTolerance = character.CollisionSkin + Mathf.Epsilon;

            foreach (Vector2Int localCell in fallingChunk.LocalCells)
            {
                Vector3 tileCenter = fallingChunk.GetLocalCellCenterWorld(localCell);
                bool isSameRow = Mathf.Abs(tileCenter.y - cellCenter.y) <= coordinateTolerance;
                bool isAdjacentColumn =
                    Mathf.Abs(Mathf.Abs(tileCenter.x - cellCenter.x) - cellSize) <= coordinateTolerance;

                if (isSameRow && isAdjacentColumn) return true;
            }

            return false;
        }

        private bool TryGetCellCenteredEscapeTarget(
            Vector2Int targetCell,
            float colliderCenterOffsetX,
            FallingChunk fallingChunk,
            out float targetX)
        {
            targetX = character.Position.x;

            if (terrainManager.HasTerrain(targetCell)) return false;

            if (terrainManager.TryCellToWorldCenter(targetCell, out Vector3 targetCellCenter) == false)
                return false;

            float candidateBodyX = targetCellCenter.x - colliderCenterOffsetX;
            float offsetX = candidateBodyX - character.Position.x;

            Bounds candidate = character.CollisionBounds;
            candidate.center += Vector3.right * offsetX;

            // 현재 Chunk 모양과 겹치지 않더라도 동일 열의 아래쪽은 곧 낙하 지형에
            // 덮일 위치이므로 탈출 공간으로 인정하면 안 된다.
            if (IntersectsFallingTerrain(
                    candidate,
                    fallingChunk,
                    includeVerticalPath: true))
                return false;

            targetX = candidateBodyX;
            return true;
        }

        private bool IntersectsFallingTerrain(
            Bounds candidate,
            FallingChunk fallingChunk,
            bool includeVerticalPath = false)
        {
            float collisionSkin = character.CollisionSkin;
            float candidateMinX = candidate.min.x + collisionSkin;
            float candidateMaxX = candidate.max.x - collisionSkin;
            float candidateMinY = candidate.min.y + collisionSkin;
            float candidateMaxY = candidate.max.y - collisionSkin;
            float halfCellSize = fallingChunk.CellSize * 0.5f;

            foreach (Vector2Int localCell in fallingChunk.LocalCells)
            {
                Vector3 tileCenter = fallingChunk.GetLocalCellCenterWorld(localCell);
                float tileMinX = tileCenter.x - halfCellSize;
                float tileMaxX = tileCenter.x + halfCellSize;
                float tileMinY = tileCenter.y - halfCellSize;
                float tileMaxY = tileCenter.y + halfCellSize;

                if (candidateMaxX <= tileMinX || candidateMinX >= tileMaxX) continue;
                if (tileMaxY <= candidateMinY) continue;
                if (includeVerticalPath == false && candidateMaxY <= tileMinY) continue;

                return true;
            }

            return false;
        }

        private void EnterBuriedState()
        {
            if (isBuried) return;

            isBuried = true;
            burialFollowChunk = null;
            burialFollowOffset = Vector2.zero;
            ReleaseEscapeGuard();
            buriedPosition = character.Position;
            burialDamageElapsed = 0f;
            character.SetTerrainInteractionLocked(false, buriedPosition);
            character.SetTerrainBuried(true, buriedPosition);
            BurialStateChanged?.Invoke(true);
            RequestPendingBurialImpacts();

            Debug.Log("[Terrain Collapse] Player buried", this);
        }

        private void RequestPendingBurialImpacts()
        {
            if (pendingBurialImpacts.Count == 0) return;

            List<FallingTerrainImpactInfo> impacts = new(pendingBurialImpacts.Values);
            pendingBurialImpacts.Clear();

            foreach (FallingTerrainImpactInfo impact in impacts)
                BurialImpactRequested?.Invoke(impact);
        }

        private void ExitBuriedState()
        {
            // 엣지콜라이더 재생성 타이밍 때문에 물리 오버랩이 한 프레임 더 남을 수 있어
            // 매몰 해제는 동일한 그리드 기준으로 판정
            if (isBuried == false || IsGridSpaceEmpty(character.CollisionBounds) == false) return;

            isBuried = false;
            burialDamageElapsed = 0f;
            character.SetTerrainBuried(false, character.Position);
            BurialStateChanged?.Invoke(false);

            Debug.Log("[Terrain Collapse] Player escaped burial", this);
        }

        private void TickBurialDamage()
        {
            burialDamageElapsed += Time.fixedDeltaTime;

            while (burialDamageElapsed >= burialDamageInterval)
            {
                burialDamageElapsed -= burialDamageInterval;
                BurialDamageRequested?.Invoke(burialDamagePerTick);
            }
        }

        private void HandleTerrainChanged(TerrainChangeReason _)
        {
            if (isBuried == false) return;

            if (burialRecheckRoutine != null) StopCoroutine(burialRecheckRoutine);

            burialRecheckRoutine = StartCoroutine(RecheckBurialAfterTerrainRefresh());
        }

        private void HandleAuthoritativeBatchApplied(TerrainChangeBatch batch)
        {
            if (batch == null || pendingBurialImpacts.ContainsKey(batch.CollapseId) == false)
                return;

            StartBurialResolution(new[] { batch.CollapseId });
        }

        private void HandleCollapseCancelled(IReadOnlyList<long> collapseIds)
        {
            if (collapseIds == null) return;

            if (HasPendingBurial(collapseIds))
            {
                StartBurialResolution(collapseIds);

                return;
            }

            if (isBuried == false) return;

            if (burialRecheckRoutine != null) StopCoroutine(burialRecheckRoutine);
            burialRecheckRoutine = StartCoroutine(RecheckBurialAfterTerrainRefresh());
        }

        private bool HasPendingBurial(IReadOnlyList<long> collapseIds)
        {
            for (int i = 0; i < collapseIds.Count; i++)
                if (pendingBurialImpacts.ContainsKey(collapseIds[i])) return true;

            return false;
        }

        // 코루틴 자리는 하나뿐이므로 번호마다 새로 돌리면 앞의 것이 잘린다.
        // 묶음을 통째로 넘겨 한 번에 처리한다.
        private void StartBurialResolution(IReadOnlyList<long> collapseIds)
        {
            if (burialRecheckRoutine != null) StopCoroutine(burialRecheckRoutine);
            burialRecheckRoutine = StartCoroutine(
                ResolvePendingBurialsAfterTerrainSettled(collapseIds));
        }

        private IEnumerator ResolvePendingBurialsAfterTerrainSettled(
            IReadOnlyList<long> collapseIds)
        {
            // TerrainGridWorld가 LateUpdate에서 Mesh와 EdgeCollider를 재생성한 뒤 검사한다.
            yield return new WaitForEndOfFrame();

            Physics2D.SyncTransforms();

            if (HasPendingBurial(collapseIds) == false)
            {
                burialRecheckRoutine = null;
                yield break;
            }

            if (IsCurrentSpaceEmpty())
            {
                ReleasePendingBurials(collapseIds);
            }
            else if (TryFindDownwardReleasePosition(out Vector2 releasePosition))
            {
                character.SetPosition(releasePosition);
                Physics2D.SyncTransforms();
                ReleasePendingBurials(collapseIds);
            }
            else
            {
                EnterBuriedState();
            }

            burialRecheckRoutine = null;
        }

        private void ReleasePendingBurials(IReadOnlyList<long> collapseIds)
        {
            for (int i = 0; i < collapseIds.Count; i++)
                ReleasePendingBurial(collapseIds[i]);
        }

        private IEnumerator RecheckBurialAfterTerrainRefresh()
        {
            yield return new WaitForEndOfFrame();

            Physics2D.SyncTransforms();

            // 이전 프레임의 엣지콜라이더가 남아 있어도 매몰 상태를 유지하지 않음
            if (IsGridSpaceEmpty(character.CollisionBounds) || TryFindNearbyBurialExit())
                ExitBuriedState();

            burialRecheckRoutine = null;
        }

        private bool IsCurrentSpaceEmpty()
            => IsGridSpaceEmpty(character.CollisionBounds) && IsPhysicalSpaceEmpty(Vector2.zero);

        private void ReleasePendingBurial(long collapseId)
        {
            pendingBurialImpacts.Remove(collapseId);
            burialFollowChunk = null;
            burialFollowOffset = Vector2.zero;
            character.SetTerrainInteractionLocked(false, character.Position);
        }

        private bool TryFindDownwardReleasePosition(out Vector2 releasePosition)
        {
            releasePosition = character.Position;
            float maximumDistance = Mathf.Max(
                terrainManager.CellSize * 2f,
                character.CollisionBounds.size.y + terrainManager.CellSize);
            int probeCount = Mathf.Max(1, Mathf.CeilToInt(maximumDistance / escapeProbeStep));

            for (int probe = 1; probe <= probeCount; probe++)
            {
                float distance = Mathf.Min(probe * escapeProbeStep, maximumDistance);
                Vector2 offset = Vector2.down * distance;
                Bounds candidate = character.CollisionBounds;
                candidate.center += (Vector3)offset;

                if (IsGridSpaceEmpty(candidate) == false || IsPhysicalSpaceEmpty(offset) == false)
                    continue;

                releasePosition += offset;
                return true;
            }

            return false;
        }

        private bool TryFindNearbyBurialExit()
        {
            float maximumDistance = terrainManager.CellSize * escapeXOffset;
            int probeCount = Mathf.Max(1, Mathf.CeilToInt(maximumDistance / escapeProbeStep));

            for (int probe = 1; probe <= probeCount; probe++)
            {
                float distance = Mathf.Min(probe * escapeProbeStep, maximumDistance);

                if (TryMoveToGridSpace(-distance) || TryMoveToGridSpace(distance)) return true;
            }

            return false;
        }

        private bool TryMoveToGridSpace(float offsetX)
        {
            Bounds candidate = character.CollisionBounds;
            candidate.center += Vector3.right * offsetX;

            if (IsGridSpaceEmpty(candidate) == false ||
                IsPhysicalSpaceEmpty(Vector2.right * offsetX) == false)
                return false;

            character.SetPosition(character.Position + Vector2.right * offsetX);
            Physics2D.SyncTransforms();

            return true;
        }

        private bool IsPhysicalSpaceEmpty(Vector2 offset)
        {
            Vector2 candidateCenter =
                (Vector2)character.CollisionBounds.center + offset;
            Vector2 probeSize =
                (Vector2)character.CollisionBounds.size -
                Vector2.one * character.CollisionSkin * 2f;

            ContactFilter2D filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = character.CollisionMask,
                useTriggers = false
            };

            int count = Physics2D.OverlapBox(candidateCenter, probeSize, 0f, filter, overlapHits);

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = overlapHits[i];
                if (hit == null || hit == bodyCollider) continue;
                if (Physics2D.GetIgnoreCollision(bodyCollider, hit)) continue;

                return false;
            }

            return true;
        }

        private bool IsGridSpaceEmpty(Bounds bounds)
        {
            Vector3 inset = Vector3.one * character.CollisionSkin;
            if (terrainManager.TryWorldToCell(bounds.min + inset, out Vector2Int min) == false)
                return false;
            if (terrainManager.TryWorldToCell(bounds.max - inset, out Vector2Int max) == false)
                return false;

            for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                    if (terrainManager.HasTerrain(new Vector2Int(x, y))) return false;

            return true;
        }
    }
}
