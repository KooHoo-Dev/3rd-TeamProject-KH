using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainCollapse
{
    public class ItemController : MonoBehaviour
    {
        [SerializeField] private Item equippedItem;
        
        private TestPlayerController playerController;

        private void Awake()
        {
            playerController = GetComponent<TestPlayerController>();
        }

        public void SetEquippedItem(Item newItem)
        {
            // 기존 아이템 해제 시 프리뷰 클리어
            if (equippedItem != null)
            {
                equippedItem.OnClearPreview();
            }

            equippedItem = newItem;
        }
        
        private void Start()
        {
            void EquipItemFromTable(int itemId)
            {
                ItemDef def = DataManager.Instance.ItemTable.Get(itemId);
                if (def == null) return;

                Item itemInstance = Instantiate(def.Prefab); 
                itemInstance.Init(def);
                SetEquippedItem(itemInstance);
            }

            EquipItemFromTable(1);
        }

        private void Update()
        {
            if (equippedItem == null) return;

            // 플레이어가 사망했거나 조작 불가 상태일 경우 입력 및 프리뷰 전면 차단
            if (playerController != null && playerController.IsDead)
            {
                // 사망 즉시 잔여 프리뷰/조준선을 지우고 업데이트 중단
                equippedItem.OnClearPreview();
                return;
            }

            Vector3 ownerPos = transform.position;
            Vector3 mouseWorldPos = GetMouseWorldPosition();

            // 매 프레임 현재 장착된 아이템의 프리뷰/표시선 업데이트
            equippedItem.OnUpdatePreview(ownerPos, mouseWorldPos);

            // 아이템 사용 입력 처리
            if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
            {
                equippedItem.Use(ownerPos, mouseWorldPos);
            }
        }

        private Vector3 GetMouseWorldPosition()
        {
            Camera mainCam = Camera.main;
            if (mainCam == null) return Vector3.zero;

            if (Pointer.current == null) return Vector3.zero;

            Vector2 mouseScreenPos = Pointer.current.position.ReadValue();
            Vector3 worldPos = mainCam.ScreenToWorldPoint(mouseScreenPos);
            worldPos.z = 0f; // 2D 프로젝트 특성에 맞춰 Z축 보정
            return worldPos;
        }
    }
}