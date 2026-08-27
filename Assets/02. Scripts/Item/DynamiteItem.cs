using UnityEngine;

namespace TerrainCollapse
{
    public class DynamiteItem : Item
    {
        public override void Use(Vector3 ownerPos,  Vector3 targetPos)
        {
            // 다이너마이트 전용 사용 로직 (폭발 범위와 강도 데이터 활용)
            var dynamiteData = Def.DynamiteData.Value;
            Debug.Log($"{Def.Name}을 던졌습니다! 폭발 범위: {dynamiteData.Radius}, 강도: {dynamiteData.Power}");
        }
    }
}