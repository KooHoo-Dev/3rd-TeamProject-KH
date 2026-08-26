using UnityEngine;

namespace Character.Item
{
    public class InventoryItem : ScriptableObject
    {
        public int Key; // 아이템을 찾을 Key
        public string Name; // 명칭
        public string Description; // 설명
        public Sprite Icon; // 아이템의 Sprite 아이콘
        public int Price; // 가격
    }
}