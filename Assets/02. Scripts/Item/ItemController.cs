using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainCollapse
{
    // [입력 스킴]
    // - 좌클릭        : 항상 곡괭이(pickaxeItem) 사용 (휠 선택과 무관하게 고정)
    // - 우클릭 홀드    : 휠로 선택된 액티브 아이템(activeItem)의 프리뷰 표시
    // - 우클릭 릴리즈  : 프리뷰 중이던 액티브 아이템 실제 사용
    // - 프리뷰 중 좌클릭 : 사용을 취소하고 프리뷰만 종료 (곡괭이도 발동되지 않음)
    //
    // [인벤토리 연동]
    // PlayerInventory는 건드리지 않고 IItemInventory 계약만 통해 접근합니다.
    // - 곡괭이: 인벤토리에 실제로 보유 중인 Pickaxe 타입 아이템을 찾아 자동 장착/교체.
    // - 액티브 아이템: 외부(휠 UI)에서는 itemID만 넘기면 되고, 여기서 보유량을 검증합니다.
    //   사용(우클릭 릴리즈) 시 1개 소모하며, 소진되면 자동 해제됩니다.
    [RequireComponent(typeof(PlayerInventory))]
    public class ItemController : MonoBehaviour
    {
        // 둘 다 인벤토리 상태에 따라 런타임에 채워지는 값이라 인스펙터에 노출하지 않습니다.
        private Item pickaxeItem;
        private Item activeItem;

        private TestPlayerController playerController;
        private IItemInventory inventory;

        // 우클릭을 눌러 액티브 아이템을 프리뷰하고 있는 중인지 여부
        private bool isPreviewingActiveItem;

        private void Awake()
        {
            playerController = GetComponent<TestPlayerController>();
            inventory = GetComponent<IItemInventory>();

            if (inventory == null)
                Debug.LogWarning("[ItemController] IItemInventory 구현체(PlayerInventory)를 찾지 못했습니다.");
        }

        private void OnEnable()
        {
            if (inventory != null)
                inventory.ItemChanged += HandleInventoryChanged;
        }

        private void OnDisable()
        {
            if (inventory != null)
                inventory.ItemChanged -= HandleInventoryChanged;
        }

        private void Start()
        {
            // 예전처럼 itemId=1을 강제 주입하지 않고, 실제 보유 상태를 기준으로 곡괭이를 채웁니다.
            RefreshPickaxeFromInventory();
        }

        // 인벤토리 변동(습득/판매/드롭/소모)이 생길 때마다 곡괭이 재계산 + 액티브 아이템 소진 여부 체크
        private void HandleInventoryChanged(InventoryItemChange change)
        {
            RefreshPickaxeFromInventory();

            if (activeItem != null && activeItem.Def.ID == change.ItemID && change.CurrentQuantity <= 0)
            {
                // 들고 있던 액티브 아이템이 다른 경로(판매, 드롭 등)로 다 떨어진 경우 자동 해제
                ClearActiveItem();
            }
        }

        /// <summary>
        /// 인벤토리가 실제로 보유한 Pickaxe 타입 아이템을 찾아 장착합니다.
        /// 보유한 곡괭이가 없으면 장착 해제, 다른 곡괭이로 바뀌었으면 교체합니다.
        /// </summary>
        private void RefreshPickaxeFromInventory()
        {
            if (inventory == null) return;

            ItemDef ownedPickaxeDef = null;

            foreach (KeyValuePair<int, int> entry in inventory.Quantities)
            {
                if (entry.Value <= 0) continue;
                if (DataManager.Instance.ItemTable.TryGet(entry.Key, out ItemDef def) == false) continue;
                if (def.Type != ItemType.Pickaxe) continue;

                ownedPickaxeDef = def;
                break; // 곡괭이는 한 번에 하나만 보유/장착한다고 가정
            }

            // 이미 같은 곡괭이가 장착돼 있으면 그대로 둠
            if (ownedPickaxeDef != null && pickaxeItem != null && pickaxeItem.Def.ID == ownedPickaxeDef.ID)
                return;

            if (pickaxeItem != null)
            {
                Destroy(pickaxeItem.gameObject);
                pickaxeItem = null;
            }

            if (ownedPickaxeDef != null)
            {
                pickaxeItem = Instantiate(ownedPickaxeDef.Prefab);
                pickaxeItem.Init(ownedPickaxeDef);
            }
        }

        /// <summary>
        /// 인벤토리 UI(휠/숫자키) 등 외부에서 호출. itemID 하나만 넘기면 되고,
        /// 실제 보유 여부와 타입 적합성은 여기서 검증합니다.
        /// 곡괭이(좌클릭 고정)/환전용/골드처럼 우클릭 프리뷰-사용 흐름과 맞지 않는 타입,
        /// 또는 보유하지 않은 아이템이면 장착되지 않고 false를 반환합니다.
        /// </summary>
        public bool SetActiveItem(int itemID)
        {
            if (inventory == null || inventory.Has(itemID, 1) == false)
                return false;

            if (DataManager.Instance.ItemTable.TryGet(itemID, out ItemDef def) == false)
                return false;

            if (def.Type == ItemType.Pickaxe || def.Type == ItemType.Exchange || def.Type == ItemType.Gold)
                return false;

            Item itemInstance = Instantiate(def.Prefab);
            itemInstance.Init(def);
            EquipActiveItemInstance(itemInstance);
            return true;
        }

        /// <summary>
        /// 액티브 아이템 슬롯을 비웁니다 (휠에서 선택 해제할 때 등).
        /// </summary>
        public void ClearActiveItem() => EquipActiveItemInstance(null);

        private void EquipActiveItemInstance(Item newItem)
        {
            if (activeItem != null)
            {
                activeItem.OnClearPreview();
                Destroy(activeItem.gameObject);
            }

            activeItem = newItem;
            isPreviewingActiveItem = false;
        }

        private void Update()
        {
            if (pickaxeItem == null && activeItem == null) return;

            // 플레이어가 사망했거나 조작 불가 상태일 경우 입력 및 프리뷰 전면 차단
            if (playerController != null && playerController.IsDead)
            {
                pickaxeItem?.OnClearPreview();
                activeItem?.OnClearPreview();
                isPreviewingActiveItem = false;
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            Vector3 ownerPos = transform.position;
            Vector3 mouseWorldPos = GetMouseWorldPosition();

            bool rightPressedThisFrame = mouse.rightButton.wasPressedThisFrame;
            bool rightReleasedThisFrame = mouse.rightButton.wasReleasedThisFrame;
            bool leftPressedThisFrame = mouse.leftButton.wasPressedThisFrame;

            // 액티브 아이템이 있을 때만 우클릭으로 프리뷰 시작 가능
            if (rightPressedThisFrame && activeItem != null && isPreviewingActiveItem == false)
            {
                isPreviewingActiveItem = true;
                // 프리뷰 중에는 곡괭이 조준선이 겹쳐 보이지 않도록 정리
                pickaxeItem?.OnClearPreview();
            }

            if (isPreviewingActiveItem)
            {
                UpdateActiveItemPreview(ownerPos, mouseWorldPos, leftPressedThisFrame, rightReleasedThisFrame);
            }
            else
            {
                UpdatePickaxe(ownerPos, mouseWorldPos, leftPressedThisFrame);
            }
        }

        // 우클릭 홀드 중 매 프레임 처리: 좌클릭이면 취소, 우클릭을 떼면 실제 사용
        private void UpdateActiveItemPreview(Vector3 ownerPos, Vector3 mouseWorldPos, bool leftPressedThisFrame, bool rightReleasedThisFrame)
        {
            // 프리뷰 도중 좌클릭 -> 사용 취소 (곡괭이도 발동하지 않음)
            if (leftPressedThisFrame)
            {
                activeItem.OnClearPreview();
                isPreviewingActiveItem = false;
                return;
            }

            activeItem.OnUpdatePreview(ownerPos, mouseWorldPos);

            // 우클릭을 뗀 시점에 실제 사용 실행
            if (rightReleasedThisFrame)
            {
                int itemID = activeItem.Def.ID;

                // 프리뷰 도중 다른 경로(판매/드롭 등)로 소진됐을 수 있으니 사용 직전 한 번 더 확인
                if (inventory != null && inventory.Has(itemID, 1) == false)
                {
                    ClearActiveItem();
                    return;
                }

                activeItem.Use(ownerPos, mouseWorldPos);

                // 사용한 만큼 인벤토리에서 소모 (0개가 되면 HandleInventoryChanged에서 자동 해제됨)
                inventory?.TryRemove(itemID, 1);

                isPreviewingActiveItem = false;
            }
        }

        // 평상시(프리뷰 중이 아닐 때) 처리: 곡괭이 조준 표시 + 좌클릭 시 즉시 채굴
        private void UpdatePickaxe(Vector3 ownerPos, Vector3 mouseWorldPos, bool leftPressedThisFrame)
        {
            if (pickaxeItem == null) return;

            pickaxeItem.OnUpdatePreview(ownerPos, mouseWorldPos);

            if (leftPressedThisFrame)
            {
                pickaxeItem.Use(ownerPos, mouseWorldPos);
            }
        }

        private Vector3 GetMouseWorldPosition()
        {
            Camera mainCam = Camera.main;
            if (mainCam == null) return Vector3.zero;

            if (Mouse.current == null) return Vector3.zero;

            Vector2 mouseScreenPos = Mouse.current.position.ReadValue();
            Vector3 worldPos = mainCam.ScreenToWorldPoint(mouseScreenPos);
            worldPos.z = 0f; // 2D 프로젝트 특성에 맞춰 Z축 보정
            return worldPos;
        }
    }
}