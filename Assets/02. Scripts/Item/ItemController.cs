using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization; // New Input System 전용 네임스페이스

namespace TerrainCollapse
{
    /// <summary>
    /// 플레이어의 입력(마우스 클릭)을 받아 현재 퀵슬롯/장착 중인 아이템의 Use()를 호출하는 컨트롤러
    /// </summary>
    public class ItemController : MonoBehaviour
    {
        [Header("Target References")]
        [SerializeField] private TestPlayerController testPlayerController; // 플레이어
        [SerializeField] private Camera mainCamera;                         // 마우스 좌표를 월드 좌표로 변환할 카메라
        [SerializeField] private Item equippedItem;                         // 현재 장착 중인 아이템

        private Transform playerTransform;

        private void Awake()
        {
            if (testPlayerController == null)
                testPlayerController = GetComponent<TestPlayerController>();

            if (mainCamera == null)
                mainCamera = Camera.main;
            
            playerTransform = testPlayerController.transform;
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
            // 마우스 좌클릭/터치 입력 감지
            if (IsPrimaryActionTriggered())
            {
                UseEquippedItem();
            }
        }

        public void UseEquippedItem()
        {
            if (testPlayerController.IsDead)
            {
                Debug.LogWarning("[ItemController] 플레이어가 사망 상태라 아이템을 사용할 수 없습니다.");
                return;
            }

            if (equippedItem == null)
            {
                Debug.LogWarning("[ItemController] 장착된 아이템이 없습니다.");
                return;
            }

            if (playerTransform == null)
            {
                Debug.LogError("[ItemController] Player Transform 참조가 없습니다.", this);
                return;
            }

            if (mainCamera == null)
            {
                mainCamera = Camera.main;
                if (mainCamera == null)
                {
                    Debug.LogError("[ItemController] Scene 내에 Camera를 찾을 수 없습니다.", this);
                    return;
                }
            }

            Vector3 ownerPos = playerTransform.position;

            // 마우스 클릭 위치의 월드 좌표 추출
            if (TryGetMouseWorldPosition(out Vector3 targetPos) == false)
            {
                return;
            }


            Debug.Log($"[ItemController] 아이템 사용 요청: {equippedItem.name} | " +
                      $"OwnerPos: {ownerPos}, TargetPos: {targetPos}");

            equippedItem.Use(ownerPos, targetPos);
        }

        public void SetEquippedItem(Item newItem)
        {
            equippedItem = newItem;
            if (equippedItem != null)
            {
                Debug.Log($"[ItemController] 장착 아이템 변경: {equippedItem.name}");
            }
        }

        /// <summary>
        /// 마우스 스크린 좌표를 지형 평면 기준의 월드 좌표로 변환 (New Input System 전용)
        /// </summary>
        private bool TryGetMouseWorldPosition(out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;

            // Pointer.current가 없거나 활성화되지 않은 경우 예외 방지
            if (Pointer.current == null) return false;

            // UnityEngine.Input.mousePosition 대신 Pointer.current.position 사용
            Vector2 mouseScreenPos = Pointer.current.position.ReadValue();

            float planeDistance = Mathf.Abs(transform.position.z - mainCamera.transform.position.z);
            worldPosition = mainCamera.ScreenToWorldPoint(new Vector3(mouseScreenPos.x, mouseScreenPos.y, planeDistance));
            worldPosition.z = transform.position.z;

            return true;
        }

        /// <summary>
        /// 마우스 좌클릭/터치 누름 순간 체크 (New Input System 전용)
        /// </summary>
        private bool IsPrimaryActionTriggered()
        {
            return Pointer.current != null && Pointer.current.press.wasPressedThisFrame;
        }
    }
}