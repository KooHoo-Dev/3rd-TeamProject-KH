using System;
using System.Collections.Generic;

namespace TerrainCollapse
{
    // 아이템 수량 조회와 변경을 제공하는 소지 상태 계약

    public interface IItemInventory
    {
        event Action<InventoryItemChange> ItemChanged;

        IReadOnlyDictionary<int, int> Quantities { get; }

        int GetQuantity(int itemID);
        bool Has(int itemID, int quantity);
        bool TryAdd(int itemID, int quantity);
        bool TryRemove(int itemID, int quantity);
    }
}
