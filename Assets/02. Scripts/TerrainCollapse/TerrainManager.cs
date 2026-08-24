using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace TerrainCollapse
{
    // 지형 변경 진입점과 부유 그룹 탐색 및 서버 확정 Batch 적용 API 제공

    public enum TerrainRemoveResult
    {
        Removed,
        TerrainNotReady,
        EmptyCell,
        NotMineable,
        Damaged,
    }

    public enum TerrainChangeReason
    {
        Excavation,
        AuthoritativeBatch,
        CollapsePlacement
    }
    
    /// <summary>
    /// 커스텀 TerrainGridData를 지형의 Source of Truth로 관리한다.
    /// 타일 제거 후 4방향 연결 그룹을 찾고 기반과 끊긴 그룹을 물리 Chunk로 넘긴다.
    /// </summary>
    public sealed class TerrainManager : MonoBehaviour
    {
        [SerializeField] private TerrainGridWorld terrainWorld;
#if UNITY_EDITOR
        [SerializeField] private Camera inputCamera;
#endif
        [SerializeField] private bool logEvents = true;

#if UNITY_EDITOR
        private InputAction removeTileAction;
#endif

        private static readonly Vector2Int[] Directions =
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
        };

        public bool IsReady => terrainWorld != null && terrainWorld.Data != null;
        public float CellSize => terrainWorld != null ? terrainWorld.CellSize : 0f;
        public uint Revision => terrainWorld != null ? terrainWorld.Revision : 0;
        public event Action<TerrainChangeReason> TerrainChanged;
        public event Action<Vector2Int, int> TerrainDurabilityChanged;
        public event Action<TerrainChangeBatch> AuthoritativeBatchApplied;

        public void Configure(TerrainGridWorld world, Camera camera)
        {
            terrainWorld = world;
#if UNITY_EDITOR
            inputCamera = camera;
#endif
        }

#if UNITY_EDITOR
        private void OnEnable()
        {
            removeTileAction ??= new InputAction("Remove Terrain Cell", InputActionType.Button, "<Pointer>/press");
            removeTileAction.performed += OnRemoveTilePerformed;
            removeTileAction.Enable();
        }

        private void OnDisable()
        {
            removeTileAction.performed -= OnRemoveTilePerformed;
            removeTileAction.Disable();
        }

        private void OnDestroy() => removeTileAction?.Dispose();

        private void OnRemoveTilePerformed(InputAction.CallbackContext _)
        {
            if (Pointer.current == null) return;

            Camera camera = inputCamera != null ? inputCamera : Camera.main;
            Vector2 pointer = Pointer.current.position.ReadValue();
            float planeDistance = Mathf.Abs(terrainWorld.transform.position.z - camera.transform.position.z);
            Vector3 world = camera.ScreenToWorldPoint(new Vector3(pointer.x, pointer.y, planeDistance));
            Vector2Int cell = terrainWorld.WorldToCell(world);
            TerrainRemoveResult result = RequestRemoveCell(cell);

            if (logEvents && result == TerrainRemoveResult.EmptyCell)
                Debug.Log($"[Terrain Collapse] Empty cell clicked: screen={pointer}, " +
                          $"world={world}, cell={cell}");
            else if (logEvents && result == TerrainRemoveResult.NotMineable)
                Debug.Log($"[Terrain Collapse] Tile cannot be mined: screen={pointer}, " +
                          $"world={world}, cell={cell}");
        }
#endif

        // 지정 Grid 좌표의 지형 존재 여부 조회
        public bool HasTerrain(Vector2Int cell) => IsReady && terrainWorld.IsOccupied(cell);

        /// <summary>지정 Grid 좌표의 타일 종류 조회 시도</summary>
        public bool TryGetTileType(Vector2Int cell, out TerrainTileType type)
        {
            type = TerrainTileType.Empty;
            
            if (IsReady == false) return false;

            TerrainCell terrainCell = terrainWorld.Data.GetCellOrNull(cell);
            
            if (terrainCell == null) return false;

            type = terrainCell.Type;
            
            return true;
        }

        public bool TryGetDurability(Vector2Int cell, out int durability)
        {
            durability = 0;
            
            if (IsReady == false) return false;

            TerrainCell terrainCell = terrainWorld.Data.GetCellOrNull(cell);
            
            if (terrainCell == null) return false;

            durability = terrainCell.CurrentDurability;
            
            return true;
        }

        /// <summary>월드 좌표의 Grid 좌표 변환 시도</summary>
        public bool TryWorldToCell(Vector3 worldPosition, out Vector2Int cell)
        {
            cell = default;
            
            if (IsReady == false) return false;

            cell = terrainWorld.WorldToCell(worldPosition);
            
            return true;
        }

        /// <summary>지정 Grid 좌표의 채굴 가능 지형 제거 요청</summary>
        public TerrainRemoveResult RequestRemoveCell(Vector2Int cell)
        {
            if (TryGetTileType(cell, out TerrainTileType type) == false)
                return IsReady ? TerrainRemoveResult.EmptyCell : TerrainRemoveResult.TerrainNotReady;

            TerrainDataManager dataManager = TerrainDataManager.Instance;
            dataManager.EnsureLoaded();
            
            if (dataManager.TryGetTileDef(type, out TerrainTileDef tileDef) == false ||
                tileDef.IsMineable == false)
                return TerrainRemoveResult.NotMineable;

            if (terrainWorld.TryDamageCell(
                    cell,
                    1,
                    out int remainingDurability,
                    out bool isDestroyed) == false)
                return TerrainRemoveResult.EmptyCell;

            TerrainDurabilityChanged?.Invoke(cell, remainingDurability);
            if (isDestroyed == false)
            {
                if (logEvents)
                    Debug.Log($"[Terrain Collapse] Cell Damaged: {cell}, durability={remainingDurability}");
                
                return TerrainRemoveResult.Damaged;
            }

            if (logEvents) Debug.Log($"[Terrain Collapse] Cell Destroy: {cell}");

            TerrainChanged?.Invoke(TerrainChangeReason.Excavation);
            
            return TerrainRemoveResult.Removed;
        }

        // 기존 bool 반환 호출부 호환용 안전 제거 요청
        public bool RemoveCell(Vector2Int cell)
        {
            TerrainRemoveResult result = RequestRemoveCell(cell);
            
            return result == TerrainRemoveResult.Damaged || result == TerrainRemoveResult.Removed;
        }

        public bool ApplyAuthoritativeBatch(TerrainChangeBatch batch, out string error)
            => ApplyAuthoritativeBatch(batch, TerrainChangeReason.AuthoritativeBatch, out error);

        public bool ApplyAuthoritativeBatch(
            TerrainChangeBatch batch,
            TerrainChangeReason reason,
            out string error)
        {
            if (terrainWorld == null)
            {
                error = "TerrainGridWorld 참조가 없습니다.";
                
                return false;
            }

            if (terrainWorld.ApplyAuthoritativeBatch(batch, out error) == false) return false;
            if (logEvents)
                Debug.Log($"[Terrain Collapse] Authoritative batch applied: collapse={batch.CollapseId}, " +
                          $"revision={batch.ResultRevision}, changes={batch.Changes.Count}");
            
            AuthoritativeBatchApplied?.Invoke(batch);
            TerrainChanged?.Invoke(reason);
            
            return true;
        }

        public TerrainSnapshot CreateSnapshot() => terrainWorld != null ? terrainWorld.CreateSnapshot() : null;

        /// <summary>점유 셀을 BFS로 순회하고 기반암과 연결되지 않은 그룹을 분리한다.</summary>
        public List<List<Vector2Int>> FindFloatingGroups()
        {
            if (IsReady == false) return new List<List<Vector2Int>>();

            List<Vector2Int> occupiedSnapshot = new(terrainWorld.GetOccupiedCells());
            occupiedSnapshot.Sort(TerrainCoordinateComparer.Compare);
            HashSet<Vector2Int> visited = new();
            List<List<Vector2Int>> floatingGroups = new();

            foreach (Vector2Int start in occupiedSnapshot)
            {
                if (terrainWorld.IsOccupied(start) == false || visited.Add(start) == false) continue;

                List<Vector2Int> group = new();
                Queue<Vector2Int> queue = new();
                bool supported = false;

                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    Vector2Int cell = queue.Dequeue();
                    group.Add(cell);
                    
                    if (TryGetTileType(cell, out TerrainTileType type))
                        supported |= type == TerrainTileType.Bedrock;

                    foreach (Vector2Int direction in Directions)
                    {
                        Vector2Int next = cell + direction;
                        
                        if (terrainWorld.IsOccupied(next) && visited.Add(next)) queue.Enqueue(next);
                    }
                }

                if (supported == false)
                {
                    group.Sort(TerrainCoordinateComparer.Compare);
                    floatingGroups.Add(group);
                }
            }

            floatingGroups.Sort((left, right) => TerrainCoordinateComparer.Compare(left[0], right[0]));

            return floatingGroups;
        }
    }
}
