using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 상점이 판매하는 아이템 한 줄 (조회용 읽기 전용 데이터)
    public readonly struct ShopListing
    {
        public int ItemID { get; }
        public string Name { get; }
        public int Price { get; }
        public Sprite Icon { get; }

        public ShopListing(int itemID, string name, int price, Sprite icon)
        {
            ItemID = itemID;
            Name = name;
            Price = price;
            Icon = icon;
        }
    }

    public enum ShopPurchaseFailReason
    {
        None,
        NotForSale,          // 상점에서 취급하지 않는 itemID
        InsufficientGold,    // 골드 부족
        InventoryRejected,   // 골드는 충분했지만 인벤토리가 지급을 거부(가득 참 등)
        ServerAuthorityUnsupported
    }

    public readonly struct ShopPurchaseResult
    {
        public bool Success { get; }
        public ShopPurchaseFailReason FailReason { get; }
        public string ItemName { get; }
        public int SpentGold { get; }

        private ShopPurchaseResult(bool success, ShopPurchaseFailReason failReason, string itemName, int spentGold)
        {
            Success = success;
            FailReason = failReason;
            ItemName = itemName;
            SpentGold = spentGold;
        }

        public static ShopPurchaseResult Ok(string itemName, int spentGold) =>
            new(true, ShopPurchaseFailReason.None, itemName, spentGold);

        public static ShopPurchaseResult Fail(ShopPurchaseFailReason reason) =>
            new(false, reason, null, 0);
    }

    // 인벤토리(PlayerInventory)는 건드리지 않고 IItemInventory 계약으로만 접근합니다.
    public sealed class ShopService
    {
        private readonly ItemTable itemTable;

        public ShopService(ItemTable itemTable)
        {
            this.itemTable = itemTable;
        }

        // 상점에서 취급 가능한 타입인지 판단.
        // 곡괭이(시작 지급 도구)/환전용(광물, 반대로 상점에 파는 대상)/골드는 상점 "판매 목록"에서 제외.
        // 지금은 다이너마이트만 여기 해당하지만, 새 소모품 타입이 추가돼도 자동으로 포함됩니다.
        public static bool IsShopPurchasable(ItemDef def)
        {
            if (def.Price <= 0) return false;

            return def.Type != ItemType.Exchange
                   && def.Type != ItemType.Gold
                   && def.Type != ItemType.Pickaxe;
        }

        public IReadOnlyList<ShopListing> GetListings()
        {
            List<ShopListing> listings = new();

            if (itemTable == null) return listings;

            foreach (ItemDef def in itemTable.GetDefs())
            {
                if (IsShopPurchasable(def) == false) continue;

                listings.Add(new ShopListing(def.ID, def.Name, def.Price, def.Sprite));
            }

            return listings;
        }

        /// <summary>
        /// itemID를 quantity개 구매 시도합니다.
        /// 골드 부족/미판매 아이템/인벤토리 거부(가득 참 등) 시 상태 변경 없이 실패를 반환합니다.
        /// </summary>
        public ShopPurchaseResult TryPurchase(IItemInventory inventory, int itemID, int quantity = 1)
        {
            // 현재 서버에는 구매 명령이 없으므로 로컬 수량을 바꾸면 다음 Snapshot과 충돌한다.
            if (RoomClient.Instance != null)
                return ShopPurchaseResult.Fail(ShopPurchaseFailReason.ServerAuthorityUnsupported);

            if (quantity <= 0)
                return ShopPurchaseResult.Fail(ShopPurchaseFailReason.NotForSale);

            if (itemTable.TryGet(itemID, out ItemDef def) == false || IsShopPurchasable(def) == false)
                return ShopPurchaseResult.Fail(ShopPurchaseFailReason.NotForSale);

            if (itemTable.TryGetGold(out ItemDef goldItem) == false)
                return ShopPurchaseResult.Fail(ShopPurchaseFailReason.NotForSale);

            int totalPrice = def.Price * quantity;

            // 1. 골드가 충분한지 먼저 확인 (부족하면 아무 것도 건드리지 않고 실패)
            if (inventory.Has(goldItem.ID, totalPrice) == false)
                return ShopPurchaseResult.Fail(ShopPurchaseFailReason.InsufficientGold);

            // 2. 골드 차감
            if (inventory.TryRemove(goldItem.ID, totalPrice) == false)
                return ShopPurchaseResult.Fail(ShopPurchaseFailReason.InsufficientGold);

            // 3. 아이템 지급. 인벤토리 쪽 사정(가득 참 등)으로 실패하면 골드를 되돌려줍니다.
            if (inventory.TryAdd(itemID, quantity) == false)
            {
                inventory.TryAdd(goldItem.ID, totalPrice);
                return ShopPurchaseResult.Fail(ShopPurchaseFailReason.InventoryRejected);
            }

            return ShopPurchaseResult.Ok(def.Name, totalPrice);
        }
    }
}
