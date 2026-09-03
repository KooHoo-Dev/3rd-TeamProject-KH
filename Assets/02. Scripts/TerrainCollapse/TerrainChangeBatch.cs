using System;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 서버 확정 지형 변경 Batch, 중간 참여 Snapshot, 결정적 좌표 정렬 규칙 정의
    
    /// <summary>
    /// 한 좌표에 적용할 최종 타일 상태
    /// <see cref="TerrainTileType.Empty"/>는 해당 좌표의 타일을 제거한다는 의미
    /// </summary>
    [Serializable]
    public struct TerrainCellChange
    {
        public Vector2Int Coord;     // 변경할 Grid 좌표
        public TerrainTileType Type; // 서버가 확정한 변경 후 타일 타입
        public int Durability;       // 변경 후 현재 내구도, Empty이면 0
        public int ResourceID;       // 타일에 부착된 자원 ID, 자원이 없으면 0
        public List<TerrainCellLootEntry> LootEntries; // 타일이 보관하는 드롭 항목, 일반 타일이면 비어 있음

        /// <summary>좌표와 해당 좌표의 최종 타일 타입으로 변경 항목을 생성</summary>
        /// <param name="coord">변경할 Grid 좌표</param>
        /// <param name="type">변경 후 타일 타입, Empty이면 타일 제거</param>
        public TerrainCellChange(
            Vector2Int coord,
            TerrainTileType type,
            int durability = 0,
            int resourceID = 0,
            IEnumerable<TerrainCellLootEntry> lootEntries = null)
        {
            Coord = coord;
            Type = type;
            Durability = type == TerrainTileType.Empty ? 0 : Mathf.Max(0, durability);
            ResourceID = type == TerrainTileType.Empty ? 0 : Mathf.Max(0, resourceID);
            LootEntries = type == TerrainTileType.Empty || lootEntries == null
                ? new List<TerrainCellLootEntry>()
                : new List<TerrainCellLootEntry>(lootEntries);
        }
    }

    /// <summary>
    /// 서버가 확정한 한 차례의 지형 변경 묶음
    /// 클라이언트는 <see cref="BaseRevision"/>이 현재 Revision과 일치할 때만
    /// <see cref="Changes"/> 전체를 적용하고 Revision을 <see cref="ResultRevision"/>으로 갱신
    /// </summary>
    [Serializable]
    public sealed class TerrainChangeBatch
    {
        public long CollapseId;                         // 이 변경이 어느 붕괴 요청의 결과인지 식별하는 ID
        // 네트워크 메시지의 RequestId로 판단한 로컬 클라이언트의 낙하 배치 소유 여부.
        // 런타임 전용 값이며 서버 계약으로 직렬화하지 않는다.
        public bool IsPlacementOwner;
        public uint BaseRevision;                       // 서버가 이 변경을 계산할 때 기준으로 삼은 지형 Revision
        public uint ResultRevision;                     // 변경 전체를 성공적으로 적용한 뒤의 지형 Revision
        public List<TerrainCellChange> Changes = new(); // 원자적으로 함께 적용할 최종 타일 상태 목록
        public TerrainChangeBatch() { }                 // Unity 직렬화 및 네트워크 역직렬화를 위한 빈 생성자


        /// <summary>
        /// 서버 확정 변경 묶음을 생성, 전송 및 재현 순서가 항상 같도록 변경 좌표 정렬
        /// </summary>
        /// <param name="collapseId">원인이 된 붕괴의 식별자</param>
        /// <param name="baseRevision">변경 적용 전 Revision</param>
        /// <param name="resultRevision">변경 적용 후 Revision</param>
        /// <param name="changes">함께 적용할 좌표별 최종 타일 상태</param>
        public TerrainChangeBatch(long collapseId, uint baseRevision, uint resultRevision,
            IEnumerable<TerrainCellChange> changes)
        {
            CollapseId = collapseId;
            BaseRevision = baseRevision;
            ResultRevision = resultRevision;
            Changes.AddRange(changes);
            Changes.Sort(TerrainCoordinateComparer.CompareChanges);
        }
    }

    /// <summary>
    /// 중간 참여 클라이언트가 현재 지형 전체를 한 번에 복원하기 위한 런타임 스냅샷
    /// JSON 맵 파일과 달리 초기 제작 데이터가 아니라 특정 Revision의 실시간 상태를 표현
    /// </summary>
    [Serializable]
    public sealed class TerrainSnapshot
    {
        public uint Revision;                         // 스냅샷이 표현하는 지형 Revision
        public int MapWidth;                          // 전체 Grid 너비
        public int MapHeight;                         // 전체 Grid 높이
        public float CellSize;                        // Grid 한 칸의 월드 크기
        public Vector2 Origin;                        // Grid 좌표 (0, 0)에 대응하는 월드 원점
        public RectInt SpawnArea;                     // 서버가 지정한 스폰/안전 구역의 Grid 범위
        public List<TerrainCellChange> Cells = new(); // 빈 칸을 제외한 전체 타일 목록, 생성 시 결정적으로 정렬

    }

    /// <summary>
    /// Dictionary 순회 순서나 플랫폼에 영향을 받지 않도록 지형 좌표의 표준 순서 정의
    /// 모든 좌표 목록은 아래에서 위로(y), 같은 행에서는 왼쪽에서 오른쪽으로(x) 정렬
    /// </summary>
    public static class TerrainCoordinateComparer
    {
        /// <summary>두 Grid 좌표를 y 우선, x 차선 순서로 비교</summary>
        /// <returns>left가 앞이면 음수, 같으면 0, 뒤이면 양수</returns>
        public static int Compare(Vector2Int left, Vector2Int right)
        {
            int y = left.y.CompareTo(right.y);
            return y != 0 ? y : left.x.CompareTo(right.x);
        }

        /// <summary>두 타일 변경 항목을 각 항목의 Grid 좌표 기준으로 비교</summary>
        /// <returns>left가 앞이면 음수, 같으면 0, 뒤이면 양수</returns>
        public static int CompareChanges(TerrainCellChange left, TerrainCellChange right)
            => Compare(left.Coord, right.Coord);
    }
}
