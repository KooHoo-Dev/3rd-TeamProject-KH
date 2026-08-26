using UnityEngine;
using UnityEngine.UI;

namespace Character.Item
{
    public class InventorySlot : MonoBehaviour
    {
        public InventoryItem currentItem { get; private set; }
        private Image itemIconImage;

        private void Awake()
        {
            // 같은 오브젝트에 붙은 아이콘 이미지 컴포넌트를 가져옵니다 [2]
            itemIconImage = transform.Find("Icon").GetComponent<Image>();
        }

        public void SetItem(InventoryItem newItem)
        {
            currentItem = newItem;
            if (newItem != null)
            {
                itemIconImage.sprite = newItem.Icon;
                itemIconImage.enabled = true; // 아이콘 켜기 [5]
            }
            else
            {
                itemIconImage.enabled = false; // 빈 칸일 땐 끄기 [5]
            }
        }
    }
}