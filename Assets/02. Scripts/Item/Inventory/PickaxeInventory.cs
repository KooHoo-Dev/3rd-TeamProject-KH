using System;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>
    /// 플레이어가 실제로 보유 중인 곡괭이(ItemType.Pickaxe)를 관리하는 전용 창구.
    /// 일반 인벤토리에 곡괭이가 추가되는 즉시 그 수량을 회수(TryRemove)해서
    /// 이쪽으로 어태치한다 - 즉 곡괭이는 일반 인벤토리(휠 핫바)에 남지 않는다.
    /// "인벤토리에서 곡괭이를 찾는" 로직을 이 한 곳에만 두어,
    /// ItemController(좌클릭 사용)와 PickaxeSlotView(UI 표시)가 각자 중복으로 검색하지 않고
    /// 여기서 결과(CurrentDef/Quantity)만 받아쓰도록 한다.
    /// PlayerInventory는 IItemInventory 계약을 통해서만 접근하고, TryAdd/TryRemove도 공개 API로만 호출한다.
    /// </summary>
    [RequireComponent(typeof(PlayerInventory))]
    public sealed class PickaxeInventory : MonoBehaviour
    {
        // 실제 장착 대상(ItemDef)이 바뀔 때만 알림 (같은 곡괭이 수량만 느는 경우는 Quantity로 별도 조회)
        public event Action<ItemDef> PickaxeChanged;

        public ItemDef CurrentDef { get; private set; }
        public int Quantity { get; private set; }

        private IItemInventory inventory;

        private void Awake()
        {
            inventory = GetComponent<IItemInventory>();

            if (inventory == null)
                Debug.LogWarning("[PickaxeInventory] IItemInventory 구현체(PlayerInventory)를 찾지 못했습니다.");
        }

        private void OnEnable()
        {
            inventory.ItemChanged += HandleInventoryChanged;

            // 씬 시작 시점에 이미 일반 인벤토리에 곡괭이가 들어있는 경우(로드/개발용 초기 지급 등) 대비
            AttachExistingPickaxesFromInventory();
        }

        private void OnDisable()
        {
            inventory.ItemChanged -= HandleInventoryChanged;
        }

        // 일반 인벤토리가 늘어난 경우(습득/구매 등)에만 반응. 우리가 스스로 만든 TryRemove(감소)는 무시.
        private void HandleInventoryChanged(InventoryItemChange change)
        {
            if (change.Delta <= 0) return;
            if (DataManager.Instance.ItemTable.TryGet(change.ItemID, out ItemDef def) == false) return;
            if (def.Type != ItemType.Pickaxe) return;

            AttachPickaxe(def, change.Delta);
        }

        private void AttachExistingPickaxesFromInventory()
        {
            // 순회 중 TryRemove로 컬렉션을 변경하므로 스냅샷을 먼저 뜬다
            List<KeyValuePair<int, int>> snapshot = new(inventory.Quantities);

            foreach (KeyValuePair<int, int> entry in snapshot)
            {
                if (entry.Value <= 0) continue;
                if (DataManager.Instance.ItemTable.TryGet(entry.Key, out ItemDef def) == false) continue;
                if (def.Type != ItemType.Pickaxe) continue;

                AttachPickaxe(def, entry.Value);
            }
        }

        // 일반 인벤토리에서 곡괭이 수량만큼 실제로 회수해서 이 전용 창구에 붙인다.
        private void AttachPickaxe(ItemDef def, int addedQuantity)
        {
            if (inventory.TryRemove(def.ID, addedQuantity) == false) return;

            // 서버 인벤토리 Snapshot에는 장착 곡괭이가 계속 포함된다.
            // 이미 장착한 동일 곡괭이를 다시 받은 경우 수량을 중복 누적하지 않는다.
            if (CurrentDef != null && CurrentDef.ID == def.ID)
                return;

            // 다른 종류의 곡괭이를 새로 얻었다면 업그레이드로 취급 - 기존 것은 반납하지 않고 그대로 폐기한다.
            if (CurrentDef != null && CurrentDef.ID != def.ID)
            {
                Quantity = 0;
            }

            CurrentDef = def;
            Quantity += addedQuantity;

            PickaxeChanged?.Invoke(CurrentDef);
        }
    }
}
