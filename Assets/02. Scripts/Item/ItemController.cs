using UnityEngine;
using UnityEngine.InputSystem;
using System;
using TerrainCollapse.Networking;

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
    // - 곡괭이: 직접 검색하지 않고 PickaxeInventory(전용 창구)가 알려주는 대로 장착/교체.
    // - 액티브 아이템: 외부(휠 UI)에서는 itemID만 넘기면 되고, 여기서 보유량을 검증합니다.
    //   사용(우클릭 릴리즈) 시 1개 소모하며, 소진되면 자동 해제됩니다.
    [RequireComponent(typeof(PlayerInventory))]
    public class ItemController : MonoBehaviour
    {
        // 둘 다 인벤토리 상태에 따라 런타임에 채워지는 값이라 인스펙터에 노출하지 않습니다.
        [SerializeField] private Item pickaxeItem;
        [SerializeField] private Item activeItem;

        [Header("곡괭이 보유 정보를 알려주는 전용 창구")] [SerializeField]
        private PickaxeInventory pickaxeInventory;

        private IPlayerController playerController;
        private IItemInventory inventory;

        // 우클릭을 눌러 액티브 아이템을 프리뷰하고 있는 중인지 여부
        private bool isPreviewingActiveItem;

        // itemID -> 재사용 가능 시각(Time.time 기준). 인스턴스가 아니라 itemID에 귀속시켜
        // 아이템을 교체/재장착해도(인스턴스가 새로 생성돼도) 쿨다운이 초기화되지 않게 한다.
        private readonly System.Collections.Generic.Dictionary<int, float> itemReadyTime = new();

        public event Action<float> MineStarted;

        private bool isMining;
        private PickaxeItem activePickaxe;
        public bool IsMining => isMining;

        private void Awake()
        {
            playerController = GetComponent(typeof(IPlayerController)) as IPlayerController;
            inventory = GetComponent<IItemInventory>();

            if (inventory == null)
                Debug.LogWarning("[ItemController] IItemInventory 구현체(PlayerInventory)를 찾지 못했습니다.");

            if (pickaxeInventory == null)
                Debug.LogWarning("[ItemController] IItemInventory 구현체(pickaxeInventory)를 찾지 못했습니다.");
        }

        private void OnEnable()
        {
            inventory.ItemChanged += HandleInventoryChanged;
            pickaxeInventory.PickaxeChanged += HandlePickaxeChanged;
            if (RoomClient.Instance != null)
                RoomClient.Instance.UsableItemUsed += HandleUsableItemUsed;
        }

        private void OnDisable()
        {
            inventory.ItemChanged -= HandleInventoryChanged;
            pickaxeInventory.PickaxeChanged -= HandlePickaxeChanged;
            if (RoomClient.Instance != null)
                RoomClient.Instance.UsableItemUsed -= HandleUsableItemUsed;
        }

        private void Start()
        {
            HandlePickaxeChanged(pickaxeInventory.CurrentDef);
        }

        // 액티브 아이템이 다른 경로(판매, 드롭 등)로 다 떨어진 경우 자동 해제
        private void HandleInventoryChanged(InventoryItemChange change)
        {
            if (activeItem != null && activeItem.Def.ID == change.ItemID && change.CurrentQuantity <= 0)
            {
                ClearActiveItem();
            }
        }

        // PickaxeInventory가 "현재 곡괭이가 이걸로 바뀌었다"고 알려줄 때만 장착/교체.
        // 직접 인벤토리를 뒤지지 않고 통보받은 결과를 그대로 반영한다.
        private void HandlePickaxeChanged(ItemDef newPickaxeDef)
        {
            // 이미 같은 곡괭이가 장착돼 있으면 그대로 둠 (구독 시점 차이로 중복 통지될 수 있음)
            if (pickaxeItem != null && newPickaxeDef != null && pickaxeItem.Def.ID == newPickaxeDef.ID)
                return;

            if (pickaxeItem != null)
            {
                CancelMine();
                pickaxeItem.OnClearPreview();
                Destroy(pickaxeItem.gameObject);
                pickaxeItem = null;
            }

            if (newPickaxeDef != null)
            {
                pickaxeItem = Instantiate(newPickaxeDef.Prefab);
                pickaxeItem.Init(newPickaxeDef);
            }
        }

        /// <summary>
        /// 인벤토리 UI(휠/숫자키) 등 외부에서 호출. itemID 하나만 넘기면 되고,
        /// 실제 보유 여부와 타입 적합성은 여기서 검증합니다.
        /// 곡괭이(좌클릭 고정)/환전용/골드처럼 우클릭 프리뷰-사용 흐름과 맞지 않는 타입,
        /// 또는 보유하지 않은 아이템이면 장착되지 않고 return을 반환합니다.
        /// </summary>
        public void SetActiveItem(int itemID)
        {
            if (DataManager.Instance.ItemTable.TryGet(itemID, out ItemDef def) == false)
                return;

            // 실제 도메인 로직: 휠이 가리키는 대상이
            // "액티브 아이템(우클릭 프리뷰-사용 흐름)"으로 쓸 수 있는 대상인지 판단.
            // 환전/골드 타입이거나, 보유 수량이 0이면 액티브 아이템으로 쓸 수 없다.
            bool isUsableAsActiveItem =
                def.Type != ItemType.Exchange &&
                def.Type != ItemType.Gold &&
                inventory.Has(itemID, 1);

            if (isUsableAsActiveItem == false)
            {
                ClearActiveItem();
                return;
            }

            Item itemInstance = Instantiate(def.Prefab);
            itemInstance.Init(def);
            EquipActiveItemInstance(itemInstance);
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

        /// <summary>
        /// 해당 itemID가 현재 쿨다운 중인지 여부 (휠 UI 등에서 아이콘 표시용).
        /// </summary>
        public bool IsItemOnCooldown(int itemID)
        {
            if (DataManager.Instance.ItemTable.TryGet(itemID, out ItemDef def) == false)
                return false;

            return IsOnCooldown(def);
        }

        /// <summary>
        /// 해당 itemID의 남은 쿨다운(초). 쿨다운 중이 아니면 0.
        /// </summary>
        public float GetItemCooldownRemaining(int itemID)
        {
            if (DataManager.Instance.ItemTable.TryGet(itemID, out ItemDef def) == false)
                return 0f;

            return GetRemainingCooldown(def);
        }

        /// <summary>
        /// 해당 itemID의 쿨다운 총 지속시간(초). UI에서 remaining / duration으로
        /// 라디얼 필 비율을 계산할 때 사용합니다.
        /// </summary>
        public float GetItemCooldownDuration(int itemID)
        {
            if (DataManager.Instance.ItemTable.TryGet(itemID, out ItemDef def) == false)
                return 0f;

            return def.Cooldown;
        }

        private bool IsOnCooldown(ItemDef def)
            => def.Cooldown > 0f && itemReadyTime.TryGetValue(def.ID, out float readyTime) && Time.time < readyTime;

        private float GetRemainingCooldown(ItemDef def)
            => itemReadyTime.TryGetValue(def.ID, out float readyTime) ? Mathf.Max(0f, readyTime - Time.time) : 0f;

        private void StartCooldown(ItemDef def)
        {
            if (def.Cooldown > 0f)
                itemReadyTime[def.ID] = Time.time + def.Cooldown;
        }

        /// <summary>
        /// 아이템 입력을 처리할 수 없는 상태인지 판단.
        /// 장착된 아이템이 없으면 그냥 무시(취소 불필요), 그 외 차단 사유는
        /// 진행 중이던 사용/프리뷰를 취소한 뒤 이번 프레임 입력 처리를 막는다.
        /// </summary>
        private bool IsInputBlocked()
        {
            if (pickaxeItem == null && activeItem == null) return true;

            bool isTyping = ChatInputState.IsTyping;
            bool isGameplayInactive = RoomClient.Instance != null && RoomClient.Instance.IsGameplayActive == false;
            bool isDead = playerController != null && playerController.IsDead;
            bool isInSpawnArea = playerController != null && playerController.IsInSpawnArea;

            if (isTyping == false && isGameplayInactive == false && isDead == false && isInSpawnArea == false)
                return false;

            CancelCurrentUse();
            return true;
        }

        private void Update()
        {
            if (IsInputBlocked()) return;

            bool isBuried = playerController?.IsBuried == true;
            if (isBuried)
            {
                activeItem?.OnClearPreview();
                isPreviewingActiveItem = false;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            Vector3 ownerPos = transform.position;
            Vector3 mouseWorldPos = GetMouseWorldPosition();

            bool rightPressedThisFrame = mouse.rightButton.wasPressedThisFrame;
            bool rightReleasedThisFrame = mouse.rightButton.wasReleasedThisFrame;
            bool leftPressedThisFrame = mouse.leftButton.wasPressedThisFrame;

            // 액티브 아이템 프리뷰가 시작되면 그 동안 곡괭이 입력은 처리하지 않는다.
            // 쿨다운 중인 아이템은 애초에 프리뷰(조준)조차 시작하지 않는다.
            bool canPreviewActiveItem =
                isBuried == false &&
                activeItem != null &&
                isPreviewingActiveItem == false &&
                IsOnCooldown(activeItem.Def) == false;

            if (rightPressedThisFrame && canPreviewActiveItem)
            {
                isPreviewingActiveItem = true;
                pickaxeItem?.OnClearPreview();
            }

            if (isPreviewingActiveItem)
            {
                UpdateActiveItemPreview(ownerPos, mouseWorldPos, leftPressedThisFrame, rightReleasedThisFrame);
                return;
            }

            UpdatePickaxe(ownerPos, mouseWorldPos);
        }

        private void UpdateActiveItemPreview(Vector3 ownerPos, Vector3 mouseWorldPos, bool leftPressedThisFrame,
            bool rightReleasedThisFrame)
        {
            if (leftPressedThisFrame)
            {
                activeItem.OnClearPreview();
                isPreviewingActiveItem = false;
                return;
            }

            activeItem.OnUpdatePreview(ownerPos, mouseWorldPos);

            if (rightReleasedThisFrame == false) return;

            if (IsOnCooldown(activeItem.Def) == false)
            {
                Item item = activeItem; // Clear/Destroy 이전에 참조 확보
                if (TryUseActiveItem(item, ownerPos, mouseWorldPos))
                    StartCooldown(item.Def); // ← 성공 시 부수효과는 여기 딱 한 곳에서만
            }

            // 어떤 분기를 탔든 프리뷰 종료는 여기서 한 번만 (activeItem이 이미 null이어도 안전)
            activeItem?.OnClearPreview();
            isPreviewingActiveItem = false;
        }

// "사용에 성공했는가"만 반환. 쿨다운/소모 여부는 이 함수가 신경 쓰지 않는다.
        private bool TryUseActiveItem(Item item, Vector3 ownerPos, Vector3 mouseWorldPos)
        {
            if (RoomClient.Instance is { IsConnected: true } roomClient)
                return TryUseActiveItemOnline(roomClient, item, ownerPos, mouseWorldPos);

            return TryUseActiveItemOffline(item, ownerPos, mouseWorldPos);
        }

        private bool TryUseActiveItemOnline(RoomClient roomClient, Item item, Vector3 ownerPos, Vector3 mouseWorldPos)
        {
            bool? requested = item switch
            {
                DynamiteItem => roomClient.RequestDynamiteThrow(item.Def.ID, ownerPos, mouseWorldPos),
                MineItem or TorchItem => roomClient.RequestPlaceablePlacement(item.Def.ID, ownerPos),
                Potion or Detector => roomClient.RequestUsableItemUse(item.Def.ID),
                _ => (bool?)null
            };

            if (requested == null)
            {
                Debug.LogWarning("[ItemController] 서버가 지원하지 않는 아이템 사용을 멀티플레이에서 차단했습니다.", this);
                return false;
            }

            if (requested == false)
                Debug.LogWarning($"[ItemController] {item.GetType().Name} 서버 사용 요청을 보내지 못했습니다.", this);

            return requested.Value;
        }

        private bool TryUseActiveItemOffline(Item item, Vector3 ownerPos, Vector3 mouseWorldPos)
        {
            int itemID = item.Def.ID;

            if (inventory != null && inventory.Has(itemID, 1) == false)
            {
                ClearActiveItem();
                return false;
            }

            bool wasUsed = item.Use(ownerPos, mouseWorldPos);
            if (wasUsed)
                inventory?.TryRemove(itemID, 1);

            return wasUsed;
        }

        private void HandleUsableItemUsed(UsableItemUsedMessage message)
        {
            if (message == null || activeItem == null || activeItem.Def.ID != message.ItemID) return;
            if (activeItem is Potion potion) potion.PlayConfirmedEffect(transform.position);
            else if (activeItem is Detector detector)
                detector.ShowConfirmedResults(transform.position, message.DetectedCells);
        }

        // 평상시 처리: 곡괭이 조준 표시 + 좌클릭 홀드 시 애니메이션 1사이클 단위 반복 채굴
        private void UpdatePickaxe(
            Vector3 ownerPos,
            Vector3 mouseWorldPos)
        {
            if (pickaxeItem == null)
                return;

            // 클라이밍 중에는 굴착도, 굴착 대상 표시도 하지 않는다.
            if (playerController != null &&
                playerController.IsClimbing)
            {
                if (isMining)
                {
                    CancelMine();
                }

                pickaxeItem.OnClearPreview();
                return;
            }

            // 굴착 가능한 타일을 매 프레임 탐색하고
            // 대상 타일에 노란 테두리를 표시한다.
            pickaxeItem.OnUpdatePreview(
                ownerPos,
                mouseWorldPos);

            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            // 좌클릭 홀드 중이며 현재 Mine 사이클이 끝났다면
            // 다음 굴착을 시작한다.
            if (mouse.leftButton.isPressed &&
                !isMining)
            {
                TryStartMine(
                    ownerPos,
                    mouseWorldPos);
            }
        }

        private void CancelMine()
        {
            activePickaxe?.CancelPreparedUse();

            activePickaxe = null;
            isMining = false;
        }

        private void CancelCurrentUse()
        {
            CancelMine();
            pickaxeItem?.OnClearPreview();
            activeItem?.OnClearPreview();
            isPreviewingActiveItem = false;
        }

        private void TryStartMine(
            Vector3 ownerPos,
            Vector3 targetPos)
        {
            if (isMining)
                return;

            // 클라이밍 중에는 굴착 시작 불가
            if (playerController != null &&
                playerController.IsClimbing)
            {
                return;
            }

            PickaxeItem pickaxe = pickaxeItem as PickaxeItem;
            if (pickaxe == null)
                return;

            if (!pickaxe.PrepareUse(ownerPos, targetPos))
                return;

            activePickaxe = pickaxe;
            isMining = true;

            float mineDirectionX =
                targetPos.x - ownerPos.x;

            MineStarted?.Invoke(mineDirectionX);
        }

        public void ExecuteMineImpact()
        {
            if (!isMining)
                return;

            if (activePickaxe == null)
                return;

            // 어떤 이유로 Animation Event가 호출되더라도
            // 클라이밍 상태에서는 절대로 실제 굴착하지 않는다.
            if (playerController != null &&
                playerController.IsClimbing)
            {
                CancelMine();
                return;
            }

            activePickaxe.ExecutePreparedUse(transform.position);
        }

        public void FinishMine()
        {
            if (!isMining)
                return;

            activePickaxe?.CancelPreparedUse();

            activePickaxe = null;
            isMining = false;
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
