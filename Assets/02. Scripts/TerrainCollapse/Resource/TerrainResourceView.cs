using UnityEngine;

namespace TerrainCollapse
{
    // 자원 타일에 포함된 광물 정보를 시각적으로 표현하는 객체

    public sealed class TerrainResourceView : MonoBehaviour
    {
        public int ResourceID { get; private set; }

        public void Initialize(int resourceID)
        {
            ResourceID = resourceID;
        }
    }
}
