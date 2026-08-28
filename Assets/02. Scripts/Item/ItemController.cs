using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainCollapse
{
    public class ItemController : MonoBehaviour
    {
        [SerializeField] private Item equippedItem;

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
                // 1. 테이블에서 기획 데이터와 프리팹 에셋을 가져옵니다.
                ItemDef def = DataManager.Instance.ItemTable.Get(itemId);
    
                if (def == null) return;

                // 2. new 대신 Instantiate를 사용하여 실물 아이템 오브젝트를 생성합니다.
                Item itemInstance = Instantiate(def.Prefab); 
    
                // 3. 아이템 데이터를 초기화하고 슬롯에 장착합니다.
                itemInstance.Init(def);
                SetEquippedItem(itemInstance);
            }
            EquipItemFromTable(2);
        }

        private void Update()
        {
            if (equippedItem == null) return;

            Vector3 ownerPos = transform.position;
            Vector3 mouseWorldPos = GetMouseWorldPosition();

            // 매 프레임 현재 장착된 아이템의 프리뷰/표시선 업데이트
            equippedItem.OnUpdatePreview(ownerPos, mouseWorldPos);

            // 사용 입력 처리
            if (Pointer.current.press.wasPressedThisFrame)
            {
                equippedItem.Use(ownerPos, mouseWorldPos);
            }
        }

        private Vector3 GetMouseWorldPosition()
        {
            Camera mainCam = Camera.main;
            if (mainCam == null) return Vector3.zero;

            Vector2 mouseScreenPos = Pointer.current.position.ReadValue();
            return mainCam.ScreenToWorldPoint(mouseScreenPos);
        }
    }
}