using UnityEngine;

namespace TerrainCollapse
{
    public class ExchangeItem : Item
    {
        public override bool Use(Vector3 ownerPos,  Vector3 targetPos)
        {
            // 환전용은 플레이어가 직접 사용할 수 없음
            Debug.Log($"{Def.Name}은 상점에 팔아서 환전해야 합니다!");
            
            return false;
        }
    }
}