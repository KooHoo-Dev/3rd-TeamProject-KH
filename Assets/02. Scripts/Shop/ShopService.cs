using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 상점 UI 탭 구분용 대분류. 아이템이 늘어나도 ItemType → 이 값으로만 매핑하면 되도록 분리했다.
    public enum ShopCategory
    {
        Pickaxe,
        Item
    }

    // 상점이 판매하는 아이템 한 줄 (조회용 읽기 전용 데이터)
    public readonly struct ShopListing
    {
        public int ItemID { get; }
        public string Name { get; }
        public int Price { get; }
        public Sprite Icon { get; }
        public ShopCategory Category { get; }
        public int DigPower { get; } // Pickaxe 카테고리가 아니면 의미 없음 (0). 채굴력으로 티어를 판별한다.

        public ShopListing(int itemID, string name, int price, Sprite icon, ShopCategory category, int digPower)
        {
            ItemID = itemID;
            Name = name;
            Price = price;
            Icon = icon;
            Category = category;
            DigPower = digPower;
        }
    }

    public enum ShopPurchaseFailReason
    {
        None,
        NotForSale,          // 상점에서 취급하지 않는 itemID
        InsufficientGold,    // 골드 부족
        InventoryRejected    // 골드는 충분했지만 인벤토리가 지급을 거부(가득 참 등)
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

    public enum ShopUndoFailReason
    {
        None,
        NothingToUndo,   // 이번 방문에서 되돌릴 구매 내역이 없음
        ItemUnavailable  // 이미 사용/드랍 등으로 소모돼 인벤토리에 남아있지 않아 되돌릴 수 없음
    }

    public readonly struct ShopUndoResult
    {
        public bool Success { get; }
        public ShopUndoFailReason FailReason { get; }
        public string ItemName { get; }
        public int RefundedGold { get; }

        private ShopUndoResult(bool success, ShopUndoFailReason failReason, string itemName, int refundedGold)
        {
            Success = success;
            FailReason = failReason;
            ItemName = itemName;
            RefundedGold = refundedGold;
        }

        public static ShopUndoResult Ok(string itemName, int refundedGold) =>
            new(true, ShopUndoFailReason.None, itemName, refundedGold);

        public static ShopUndoResult Fail(ShopUndoFailReason reason, string itemName = null) =>
            new(false, reason, itemName, 0);
    }

    // 되돌리기 스택에 쌓이는 구매 기록. 되돌릴 때 "그때 낸 가격"을 그대로 환불하기 위해
    // 구매 시점 금액을 그대로 들고 있는다.
    internal readonly struct ShopPurchaseRecord
    {
        public int ItemID { get; }
        public int Quantity { get; }
        public int PaidGold { get; }

        public ShopPurchaseRecord(int itemID, int quantity, int paidGold)
        {
            ItemID = itemID;
            Quantity = quantity;
            PaidGold = paidGold;
        }
    }

    // 인벤토리(PlayerInventory)는 건드리지 않고 IItemInventory 계약으로만 접근합니다.
    public sealed class ShopService
    {
        private readonly ItemTable itemTable;

        // 이번 상점 방문(BeginSession ~ 다음 BeginSession 전까지)에 구매한 내역을 선입후출로 쌓아둔다.
        private readonly Stack<ShopPurchaseRecord> purchaseHistory = new();

        public bool HasUndoableHistory => purchaseHistory.Count > 0;

        public ShopService(ItemTable itemTable)
        {
            this.itemTable = itemTable;
        }

        // 상점에 새로 진입할 때 호출 - 이전 방문의 되돌리기 내역을 비운다.
        public void BeginSession()
        {
            purchaseHistory.Clear();
        }

        // 상점에서 취급 가능한 타입인지 판단.
        // 환전용(광물, 반대로 상점에 파는 대상)/골드(화폐 그 자체)만 상점 "판매 목록"에서 제외.
        // 곡괭이는 사면 PickaxeInventory가 자동으로 회수해 업그레이드 처리한다.
        public static bool IsShopPurchasable(ItemDef def)
        {
            if (def.Price <= 0) return false;

            return def.Type != ItemType.Exchange
                   && def.Type != ItemType.Gold;
        }

        // 상점 UI 탭 분류. 지금은 곡괭이만 별도 탭이고 나머지는 전부 "아이템" 탭으로 묶인다.
        private static ShopCategory GetCategory(ItemDef def) =>
            def.Type == ItemType.Pickaxe ? ShopCategory.Pickaxe : ShopCategory.Item;

        public IReadOnlyList<ShopListing> GetListings()
        {
            List<ShopListing> listings = new();

            if (itemTable == null) return listings;

            foreach (ItemDef def in itemTable.GetDefs())
            {
                if (IsShopPurchasable(def) == false) continue;

                listings.Add(new ShopListing(
                    def.ID,
                    def.Name,
                    def.Price,
                    def.Sprite,
                    GetCategory(def),
                    def.PickaxeData?.DigPower ?? 0));
            }

            return listings;
        }

        /// <summary>
        /// itemID를 quantity개 구매 시도합니다.
        /// 골드 부족/미판매 아이템/인벤토리 거부(가득 참 등) 시 상태 변경 없이 실패를 반환합니다.
        /// </summary>
        public ShopPurchaseResult TryPurchase(IItemInventory inventory, int itemID, int quantity = 1)
        {
            if (inventory == null || itemTable == null || quantity <= 0)
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

            purchaseHistory.Push(new ShopPurchaseRecord(itemID, quantity, totalPrice));

            return ShopPurchaseResult.Ok(def.Name, totalPrice);
        }

        /// <summary>
        /// 이번 방문에서 가장 최근에 구매한 항목을 선입후출로 되돌립니다.
        /// 구매했던 만큼 인벤토리에서 회수하고, 그때 낸 금액을 그대로 환불합니다.
        /// 이미 사용/드랍 등으로 그 수량이 남아있지 않으면 이번 건은 포기하고(스택에서 제거),
        /// 그보다 이전 구매는 이후 되돌리기 시도에서 계속 다룰 수 있습니다.
        /// </summary>
        public ShopUndoResult TryUndoLastPurchase(IItemInventory inventory)
        {
            if (inventory == null || itemTable == null)
                return ShopUndoResult.Fail(ShopUndoFailReason.NothingToUndo);

            if (purchaseHistory.Count == 0)
                return ShopUndoResult.Fail(ShopUndoFailReason.NothingToUndo);

            ShopPurchaseRecord record = purchaseHistory.Pop();

            if (itemTable.TryGet(record.ItemID, out ItemDef def) == false)
                return ShopUndoResult.Fail(ShopUndoFailReason.ItemUnavailable);

            // 구매 이후 이미 써버렸거나 다른 경로로 사라져 수량이 모자라면 되돌릴 수 없음
            if (inventory.TryRemove(record.ItemID, record.Quantity) == false)
                return ShopUndoResult.Fail(ShopUndoFailReason.ItemUnavailable, def.Name);

            if (itemTable.TryGetGold(out ItemDef goldItem))
                inventory.TryAdd(goldItem.ID, record.PaidGold);

            return ShopUndoResult.Ok(def.Name, record.PaidGold);
        }
    }
}