using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 낙하 지형 접촉 시 탈출 공간 탐색, 매몰 상태와 지속 피해 요청 관리

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

        private IFallingTerrainCharacter character;
        private Collider2D bodyCollider;
        private bool isBuried;
        private float burialDamageElapsed;
        private Vector2 buriedPosition;
        private Coroutine burialRecheckRoutine;

        public bool IsBuried => isBuried;

        public event Action<bool> BurialStateChanged;
        public event Action<int> BurialDamageRequested;

        private void Awake()
        {
            character = GetComponent<IFallingTerrainCharacter>();
            bodyCollider = GetComponent<Collider2D>();

            if (terrainManager == null) terrainManager = FindFirstObjectByType<TerrainManager>();
        }

        private void OnEnable()
        {
            if (terrainManager != null) terrainManager.TerrainChanged += HandleTerrainChanged;
        }

        private void OnDisable()
        {
            if (terrainManager != null) terrainManager.TerrainChanged -= HandleTerrainChanged;
        }

        public void ResetState()
        {
            if (burialRecheckRoutine != null)
            {
                StopCoroutine(burialRecheckRoutine);
                burialRecheckRoutine = null;
            }

            resolvedCollapses.Clear();
            burialDamageElapsed = 0f;

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

            if (IsGridSpaceEmpty(character.CollisionBounds) == false)
                EnterBuriedState();
        }

        public FallingTerrainContactResult ResolveFallingTerrainContact(FallingChunk fallingChunk)
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

            if (IsInFallingTerrainPath(character.CollisionBounds, fallingChunk) == false)
                return RememberResult(fallingChunk.CollapseId, FallingTerrainContactResult.Ignored);

            character.CancelClimbing();

            Vector2 fallingVelocity = fallingChunk.BeforeVelocity;
            fallingVelocity.y = Mathf.Min(fallingVelocity.y, 0f);
            character.ApplyFallingVelocity(fallingVelocity);

            if (TryFindEscapeTarget(fallingChunk, out float targetX))
            {
                character.StartTerrainEscape(targetX, escapePushSpeed);

                return RememberResult(fallingChunk.CollapseId, FallingTerrainContactResult.Escaped);
            }

            return RememberResult(fallingChunk.CollapseId, FallingTerrainContactResult.NoEscapeSpace);
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

            float cellSize = terrainManager.CellSize;
            if (cellSize <= 0f) return false;

            float probeStep = Mathf.Max(0.005f, Mathf.Min(escapeProbeStep, cellSize * 0.25f));
            float maximumDistance = cellSize * escapeXOffset;
            int probeCount = Mathf.Max(1, Mathf.CeilToInt(maximumDistance / probeStep));

            for (int probe = 0; probe <= probeCount; probe++)
            {
                float distance = Mathf.Min(probe * probeStep, maximumDistance);

                if (IsEscapePositionSafe(-distance, fallingChunk))
                {
                    targetX += -distance;
                    return true;
                }

                if (probe == 0) continue;

                if (IsEscapePositionSafe(distance, fallingChunk))
                {
                    targetX += distance;
                    return true;
                }
            }

            return false;
        }

        private bool IsEscapePositionSafe(float offsetX, FallingChunk fallingChunk)
        {
            Bounds candidate = character.CollisionBounds;
            candidate.center += Vector3.right * offsetX;

            return IsGridSpaceEmpty(candidate) &&
                   IsPhysicalSpaceEmpty(offsetX) &&
                   IsInFallingTerrainPath(candidate, fallingChunk) == false;
        }

        private bool IsInFallingTerrainPath(Bounds candidate, FallingChunk fallingChunk)
        {
            float collisionSkin = character.CollisionSkin;
            float candidateMinX = candidate.min.x + collisionSkin;
            float candidateMaxX = candidate.max.x - collisionSkin;
            float candidateMinY = candidate.min.y + collisionSkin;
            float halfCellSize = fallingChunk.CellSize * 0.5f;

            foreach (Vector2Int localCell in fallingChunk.LocalCells)
            {
                Vector3 tileCenter = fallingChunk.GetLocalCellCenterWorld(localCell);
                float tileMinX = tileCenter.x - halfCellSize;
                float tileMaxX = tileCenter.x + halfCellSize;
                float tileMaxY = tileCenter.y + halfCellSize;

                if (tileMaxY <= candidateMinY) continue;
                if (candidateMaxX <= tileMinX || candidateMinX >= tileMaxX) continue;

                return true;
            }

            return false;
        }

        private void EnterBuriedState()
        {
            if (isBuried) return;

            isBuried = true;
            buriedPosition = character.Position;
            burialDamageElapsed = 0f;
            character.SetTerrainBuried(true, buriedPosition);
            BurialStateChanged?.Invoke(true);

            Debug.Log("[Terrain Collapse] Player buried", this);
        }

        private void ExitBuriedState()
        {
            if (isBuried == false || IsCurrentSpaceEmpty() == false) return;

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

        private IEnumerator RecheckBurialAfterTerrainRefresh()
        {
            yield return new WaitForEndOfFrame();

            Physics2D.SyncTransforms();

            if (IsCurrentSpaceEmpty() || TryFindNearbyBurialExit())
                ExitBuriedState();

            burialRecheckRoutine = null;
        }

        private bool IsCurrentSpaceEmpty()
            => IsGridSpaceEmpty(character.CollisionBounds) && IsPhysicalSpaceEmpty(0f);

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

            if (IsGridSpaceEmpty(candidate) == false || IsPhysicalSpaceEmpty(offsetX) == false)
                return false;

            character.SetPosition(character.Position + Vector2.right * offsetX);
            Physics2D.SyncTransforms();

            return true;
        }

        private bool IsPhysicalSpaceEmpty(float offsetX)
        {
            Vector2 candidateCenter =
                (Vector2)character.CollisionBounds.center + Vector2.right * offsetX;
            Vector2 probeSize =
                (Vector2)character.CollisionBounds.size - Vector2.one * character.CollisionSkin;

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
