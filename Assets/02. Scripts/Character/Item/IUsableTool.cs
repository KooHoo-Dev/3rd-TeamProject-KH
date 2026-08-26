using UnityEngine;

namespace Character.Item
{
    public interface IUsable
    {
        // 사용 대상(예: 플레이어)을 매개변수로 받아 효과를 적용합니다 [4]
        void Use(GameObject user); 
    }
}