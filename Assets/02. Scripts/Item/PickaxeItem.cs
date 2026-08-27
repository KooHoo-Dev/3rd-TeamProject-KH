using UnityEngine;

namespace TerrainCollapse
{
    public class PickaxeItem : Item
    {
        public override void Use(Vector3 ownerPos,  Vector3 targetPos)
        {
            // 0. ItemDef 데이터 검증 (Def 및 PickaxeData 존재 여부 확인)
            if (Def == null || Def.PickaxeData.HasValue == false)
            {
                Debug.LogWarning("[PickaxeItem] 곡괭이 데이터(ItemDef / PickaxeData)가 올바르게 설정되지 않았습니다.");
                return;
            }

            PickaxeInfo stats = Def.PickaxeData.Value;
            float range = stats.Range;
            int digPower = stats.DigPower;

            // 1. TerrainManager 준비 상태 검증
            TerrainManager terrainManager = FindFirstObjectByType<TerrainManager>();
            if (terrainManager == null || terrainManager.IsReady == false)
            {
                Debug.LogWarning("[PickaxeItem] TerrainManager가 준비되지 않았습니다.");
                return;
            }

            // 2. DefDTO의 PickaxeRange를 적용한 사거리 검증
            if (Vector3.Distance(ownerPos, targetPos) > range)
            {
                Debug.Log($"[PickaxeItem] 사거리 초과: 거리={Vector3.Distance(ownerPos, targetPos):F2}, 최대 사거리={range}");
                return;
            }

            // 3. 월드 좌표를 Terrain Grid Cell 좌표로 변환
            if (terrainManager.TryWorldToCell(targetPos, out Vector2Int targetCell) == false)
            {
                Debug.LogWarning($"[PickaxeItem] 대상 월드 좌표({targetPos})를 그리드 셀로 변환할 수 없습니다.");
                return;
            }

            // 4. 지형 존재 및 채굴 가능 여부 사전 검증
            if (terrainManager.TryGetTileType(targetCell, out TerrainTileType tileType) == false ||
                tileType == TerrainTileType.Empty)
            {
                Debug.Log($"[PickaxeItem] 빈 공간입니다: Cell={targetCell}");
                return;
            }

            TerrainDataManager dataManager = TerrainDataManager.Instance;
            dataManager.EnsureLoaded();

            if (dataManager.TryGetTileDef(tileType, out TerrainTileDef tileDef) == false || tileDef.IsMineable == false)
            {
                Debug.Log($"[PickaxeItem] 채굴 불가능한 타일입니다: Cell={targetCell}, Type={tileType}");
                return;
            }

            // 5. DefDTO의 DigPower(굴착력)를 전달하여 지형 손상/파괴 요청
            // (TerrainGridWorld / TerrainManager의 TryDamageCell API 직접 활용)
            bool damagedSuccess = ExecuteDig(terrainManager, targetCell, digPower, out int remainingDurability,
                out bool isDestroyed);

            if (damagedSuccess == false)
            {
                Debug.LogWarning($"[PickaxeItem] 굴착 요청 처리 실패: Cell={targetCell}");
                return;
            }

            // 6. 결과 판정 및 피드백 처리
            if (isDestroyed)
            {
                Debug.Log($"[PickaxeItem] 타일 완파 성공! Cell={targetCell}, 피해량={digPower}");
                // TODO: 파괴 SFX/VFX, 드롭 아이템 생성 등
            }
            else
            {
                Debug.Log($"[PickaxeItem] 타일 타격 성공! Cell={targetCell}, 피해량={digPower}, 남은 내구도={remainingDurability}");
                // TODO: 타격 SFX/VFX, 애니메이션 트리거 등
            }
        }

        /// <summary>
        /// TerrainManager 내부 래핑 함수를 거쳐 지형에 피해를 입히는 헬퍼 메서드
        /// </summary>
        private bool ExecuteDig(
            TerrainManager manager,
            Vector2Int cell,
            int damage,
            out int remainingDurability,
            out bool isDestroyed)
        {
            // TerrainGridWorld 인스턴스를 통해 DigPower만큼 데미지 적용
            TerrainGridWorld world = manager.GetComponent<TerrainGridWorld>();
            if (world != null)
            {
                return world.TryDamageCell(cell, damage, out remainingDurability, out isDestroyed);
            }

            remainingDurability = 0;
            isDestroyed = false;
            return false;
        }
    }
}