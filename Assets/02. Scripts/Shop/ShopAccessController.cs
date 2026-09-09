using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainCollapse
{
    // 플레이어가 지상(y >= shopAreaMinimumY)에 있을 때만 상점 인터페이스에 접근할 수 있도록 판정하고,
    // 접근 가능한 상태에서 지정 키를 누르면 ShopView(플레이스홀더 UI)를 열고 닫습니다.
    [RequireComponent(typeof(PlayerInventory), typeof(PlayerController), typeof(PickaxeInventory))]
    public sealed class ShopAccessController : MonoBehaviour
    {
        [SerializeField] private float shopAreaMinimumY = 0f; // 지상 기준선
        [SerializeField] private ShopView shopView; // 플레이스홀더 UI (없어도 접근 판정 자체는 동작)
        [SerializeField] private Key toggleKey = Key.B; // 상점 열기/닫기 키 (임시 배치, 추후 상호작용 UI로 교체 가능)

        private PlayerController playerController;
        private PlayerInventory inventory;
        private PickaxeInventory pickaxeInventory;
        private ShopService shopService;

        public bool IsShopAccessible { get; private set; }
        public bool IsShopOpen { get; private set; }
        public bool IsClosingWithEscapeThisFrame { get; private set; }

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            inventory = GetComponent<PlayerInventory>();
            pickaxeInventory = GetComponent<PickaxeInventory>();
        }

        private void Start()
        {
            if (DataManager.Instance == null)
            {
                Debug.LogWarning("[Shop] DataManager가 준비되지 않아 상점을 초기화하지 못했습니다.");
                return;
            }

            shopService = new ShopService(DataManager.Instance.ItemTable);
            shopView?.Bind(shopService, inventory, pickaxeInventory);
        }

        private void Update()
        {
            IsClosingWithEscapeThisFrame = false;
            if (PauseMenuController.IsGameplayInputBlocked) return;

            bool wasAccessible = IsShopAccessible;
            IsShopAccessible = playerController.IsDead == false && IsInShopArea();

            // 지상 구역을 벗어나면 상점이 열려 있어도 강제로 닫음
            if (wasAccessible && IsShopAccessible == false)
                CloseShop();

            if (IsShopAccessible == false) return;
            if (ChatInputState.IsTyping) return;

            Keyboard keyboard = Keyboard.current;
            IsClosingWithEscapeThisFrame = keyboard != null &&
                                           IsShopOpen &&
                                           keyboard[Key.Escape].wasPressedThisFrame;
            if (keyboard != null &&
                (keyboard[toggleKey].wasPressedThisFrame ||
                 IsClosingWithEscapeThisFrame))
                ToggleShop();
        }

        private bool IsInShopArea()
        {
            RoomClient roomClient = RoomClient.Instance;
            if (roomClient?.IsAuthoritativeTerrainMode == true)
                return roomClient.IsLocalPlayerInSpawnArea;

            return playerController.CurrentPosition.y >= shopAreaMinimumY;
        }

        private void ToggleShop()
        {
            if (IsShopOpen) CloseShop();
            else OpenShop();
        }

        public void OpenShop()
        {
            if (IsShopOpen) return;

            IsShopOpen = true;
            shopView?.BeginSession();
            shopView?.Show();
        }

        public void CloseShop()
        {
            if (IsShopOpen == false) return;

            IsShopOpen = false;
            shopView?.Hide();
        }
    }
}
