using UnityEngine;
using UnityEngine.InputSystem;
using System;

namespace TerrainCollapse
{
    public class ItemController : MonoBehaviour
    {
        [SerializeField] private Item equippedItem;
        
        private TestPlayerController playerController;

        public event Action<float> MineStarted;

        private bool isMining;
        private PickaxeItem activePickaxe;
        public bool IsMining => isMining;
        
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

            EquipItemFromTable(2);
        }

        private void Update()
        {
            if (equippedItem == null) return;

            if (playerController != null &&
                playerController.IsDead)
            {
                CancelMine();

                equippedItem.OnClearPreview();
                return;
            }

            Vector3 ownerPos = transform.position;
            Vector3 mouseWorldPos = GetMouseWorldPosition();

            // 매 프레임 현재 장착된 아이템의 프리뷰/표시선 업데이트
            equippedItem.OnUpdatePreview(ownerPos, mouseWorldPos);

            // 아이템 사용 입력 처리
            if (Pointer.current == null)
                return;

            if (equippedItem is PickaxeItem)
            {
                // 곡괭이는 누르고 있는 동안 반복 사용
                // 단, 현재 Mine 한 사이클이 끝난 상태에서만 다음 사이클 시작
                if (Pointer.current.press.isPressed && !isMining)
                {
                    TryUseEquippedItem(ownerPos, mouseWorldPos);
                }
            }
            else
            {
                // 다른 아이템은 기존처럼 클릭 1회당 사용 1회
                if (Pointer.current.press.wasPressedThisFrame)
                {
                    TryUseEquippedItem(ownerPos, mouseWorldPos);
                }
            }
        }
        
        private void CancelMine()
        {
            activePickaxe?.CancelPreparedUse();

            activePickaxe = null;
            isMining = false;
        }
        
        private void TryUseEquippedItem(
            Vector3 ownerPos,
            Vector3 targetPos)
        {
            if (isMining)
                return;

            if (equippedItem is PickaxeItem pickaxe)
            {
                activePickaxe = pickaxe;

                if (!activePickaxe.PrepareUse(ownerPos, targetPos))
                {
                    activePickaxe = null;
                    return;
                }

                isMining = true;

                float mineDirectionX = targetPos.x - ownerPos.x;
                MineStarted?.Invoke(mineDirectionX);

                return;
            }

            equippedItem.Use(ownerPos, targetPos);
        }
        
        public void ExecuteMineImpact()
        {
            if (!isMining)
                return;

            if (activePickaxe == null)
                return;

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

            if (Pointer.current == null) return Vector3.zero;

            Vector2 mouseScreenPos = Pointer.current.position.ReadValue();
            Vector3 worldPos = mainCam.ScreenToWorldPoint(mouseScreenPos);
            worldPos.z = 0f; // 2D 프로젝트 특성에 맞춰 Z축 보정
            return worldPos;
        }
    }
}