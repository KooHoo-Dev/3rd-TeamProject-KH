using UnityEngine;
using UnityEngine.InputSystem; // 새 Input System 기준 [9]

namespace Character.Item
{
    public class SlotSelector : MonoBehaviour
    {
        [SerializeField] private RectTransform selectorUI; // 선택기 테두리 UI [10]
        [SerializeField] private InventorySlot[] slots; // 1x9 슬롯 배열 [5, 7]

        private int currentIndex = 0;

        private void Update()
        {
            // 키보드 방향키 입력 감지 [9]
            if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
            {
                MoveIndex(-1);
            }
            else if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
            {
                MoveIndex(1);
            }
        }

        private void MoveIndex(int direction)
        {
            currentIndex += direction;

            // 인덱스 순환 처리 (0 미만이면 끝으로, 9 이상이면 처음으로) [2, 8]
            if (currentIndex < 0)
                currentIndex = slots.Length - 1;
            else if (currentIndex >= slots.Length)
                currentIndex = 0;

            UpdateSelectorPosition();
        }

        private void UpdateSelectorPosition()
        {
            // 선택된 슬롯의 RectTransform 좌표로 선택기 UI를 부드럽게 이동시킵니다 [8, 10]
            RectTransform targetSlot = slots[currentIndex].GetComponent<RectTransform>();
            selectorUI.anchoredPosition = targetSlot.anchoredPosition; // [8, 10]
        }
    }
}