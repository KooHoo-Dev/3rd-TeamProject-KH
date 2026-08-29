using UnityEngine;

namespace TerrainCollapse
{
    // 플레이어와 겹친 필드 드롭 아이템을 인벤토리로 수집

    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(PlayerInventory))]
    public sealed class ItemPickupController : MonoBehaviour
    {
        [SerializeField] private LayerMask itemDropMask;
        [SerializeField, Min(0f)] private float pickupRangeRadius = 0.5f;

        private readonly Collider2D[] overlapHits = new Collider2D[16];

        private Collider2D playerCollider;
        private PlayerInventory inventory;

        private void Awake()
        {
            playerCollider = GetComponent<Collider2D>();
            inventory = GetComponent<PlayerInventory>();

            if (itemDropMask.value == 0)
                itemDropMask = LayerMask.GetMask("ItemDrop");
        }

        private void FixedUpdate()
        {
            ContactFilter2D filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = itemDropMask,
                useTriggers = true
            };

            int hitCount = Physics2D.OverlapCircle(playerCollider.bounds.center, pickupRangeRadius, filter, overlapHits);

            for (int i = 0; i < hitCount; i++)
            {
                WorldItemDrop itemDrop = overlapHits[i].GetComponent<WorldItemDrop>();

                if (itemDrop?.TryCollect(inventory) == true)
                    Debug.Log($"[Item Pickup] Acquired: itemID={itemDrop.ItemID}, quantity={itemDrop.Quantity}", itemDrop);
            }
        }
    }
}
