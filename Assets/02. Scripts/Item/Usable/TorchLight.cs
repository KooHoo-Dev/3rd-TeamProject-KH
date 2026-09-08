using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TerrainCollapse
{
    /// <summary>
    /// TorchItem.Use()에 의해 설치되는 횃불. 폭발/감지 로직 없이,
    /// 설치되면 SpotLight2D(Light2D, LightType.Point)를 켜는 것이 전부다.
    /// 셀 중복 설치 방지는 MineExplosive와 동일하게 GridOccupancy&lt;TorchLight&gt;를 재사용한다.
    /// 설치된 셀이 나중에 다시 지형으로 채워져(매몰) 벽 안에 갇히면 스스로 파괴된다.
    /// </summary>
    public class TorchLight : MonoBehaviour, IPlaceable
    {
        [Header("Light Settings")]
        [SerializeField] private Light2D spotLight; // Light2D의 Light Type을 Point(스팟 조명)로 설정해서 사용

        private bool hasCell;
        private Vector2Int placedCell;
        private TerrainManager terrainManager;

        /// <summary>주어진 셀에 이미 횃불이 설치돼 있는지 여부.</summary>
        public static bool IsCellOccupied(Vector2Int cell) => GridOccupancy<TorchLight>.IsOccupied(cell);

        /// <summary>
        /// TorchItem.Use()에서 설치 직후 호출. 위치/셀 세팅 후 조명을 켜고,
        /// 이후 매몰 여부를 감시하기 위해 지형 변경 이벤트를 구독한다.
        /// </summary>
        public void Place(Vector3 position, Vector2Int? cell = null)
        {
            transform.position = position;

            if (cell.HasValue)
            {
                hasCell = true;
                placedCell = cell.Value;
                GridOccupancy<TorchLight>.Register(placedCell, this);

                terrainManager = FindFirstObjectByType<TerrainManager>();
                if (terrainManager != null)
                    terrainManager.TerrainChanged += HandleTerrainChanged;
            }

            if (spotLight != null)
                spotLight.enabled = true;
            else
                Debug.LogWarning("[TorchLight] spotLight(Light2D)가 설정되지 않았습니다.");
        }

        // 지형이 변경될 때마다(채굴/낙석/서버 배치 등) 내가 설치된 셀이 다시
        // 지형으로 막혔는지(매몰) 확인하고, 막혔다면 벽 안에 갇힌 것이므로 파괴한다.
        private void HandleTerrainChanged(TerrainChangeReason reason)
        {
            if (terrainManager.HasTerrain(placedCell))
                Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (hasCell)
                GridOccupancy<TorchLight>.Unregister(this);

            if (terrainManager != null)
                terrainManager.TerrainChanged -= HandleTerrainChanged;
        }
    }
}