using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainCollapse
{
    // Q키 입력을 인벤토리 아이템 필드 드롭 요청으로 변환

    [RequireComponent(typeof(PlayerInventory))]
    public sealed class PlayerItemDropController : MonoBehaviour
    {
        [SerializeField] private InventoryView inventoryView;
        [SerializeField] private ItemDropSpawner itemDropSpawner;
        [SerializeField, Min(0f)] private float dropHoldDuration = 2f;
        [SerializeField, Min(0f)] private float dropPickupDelay = 2f;
        [SerializeField] private Vector2 dropOffset = new(0f, 0.5f);

        private PlayerInventory inventory;
        private IPlayerController playerController;
        private int pressedItemID = -1;
        private float dropKeyPressedTime;
        private bool isDropKeyHeld;
        private PlayerItemDropService itemDropService;
        private Coroutine dropAllCoroutine;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            playerController = GetComponent(typeof(IPlayerController)) as IPlayerController;
        }

        private void Start()
        {
            itemDropSpawner ??= FindFirstObjectByType<ItemDropSpawner>();
            itemDropService = new PlayerItemDropService(itemDropSpawner);
        }

        private void Update()
        {
            if (ChatInputState.IsTyping || PauseMenuController.IsGameplayInputBlocked)
            {
                CancelPendingDrop();
                return;
            }

            if (playerController?.IsBuried == true)
            {
                CancelPendingDrop();
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.qKey.wasPressedThisFrame)
                BeginDrop();

            UpdateHoldProgress();

            if (keyboard.qKey.wasReleasedThisFrame)
                EndDrop();
        }

        private void BeginDrop()
        {
            pressedItemID = inventoryView != null ? inventoryView.FocusedItemID : -1;
            dropKeyPressedTime = Time.unscaledTime;
            isDropKeyHeld = pressedItemID >= 0;

            if (isDropKeyHeld)
                dropAllCoroutine = StartCoroutine(DropAllAfterHold());
        }

        private void EndDrop()
        {
            if (isDropKeyHeld == false) return;

            isDropKeyHeld = false;
            StopDropAllCoroutine();
            inventoryView?.SetHoldProgress(0f);

            int itemID = pressedItemID;
            pressedItemID = -1;

            TryDrop(itemID, 1);
        }

        private void UpdateHoldProgress()
        {
            if (isDropKeyHeld == false || inventoryView == null) return;

            float progress = dropHoldDuration <= 0f
                ? 1f
                : (Time.unscaledTime - dropKeyPressedTime) / dropHoldDuration;

            inventoryView.SetHoldProgress(progress);
        }

        private IEnumerator DropAllAfterHold()
        {
            yield return new WaitForSecondsRealtime(dropHoldDuration);

            if (isDropKeyHeld == false || Keyboard.current?.qKey.isPressed != true) yield break;

            isDropKeyHeld = false;
            dropAllCoroutine = null;
            inventoryView?.SetHoldProgress(0f);

            int itemID = pressedItemID;
            pressedItemID = -1;

            TryDrop(itemID, inventory.GetQuantity(itemID));
        }

        private void StopDropAllCoroutine()
        {
            if (dropAllCoroutine == null) return;

            StopCoroutine(dropAllCoroutine);
            dropAllCoroutine = null;
        }

        private void CancelPendingDrop()
        {
            if (isDropKeyHeld == false && dropAllCoroutine == null) return;

            isDropKeyHeld = false;
            pressedItemID = -1;
            StopDropAllCoroutine();
            inventoryView?.SetHoldProgress(0f);
        }

        private void TryDrop(int itemID, int quantity)
        {
            if (itemDropService == null) return;

            Vector2 dropPosition = (Vector2)transform.position + dropOffset;

            if (itemDropService.TryDrop(inventory, itemID, quantity, dropPosition, dropPickupDelay) == false)
                return;

            Debug.Log($"[Item Drop] Dropped: itemID={itemID}, quantity={quantity}");
        }
    }
}
