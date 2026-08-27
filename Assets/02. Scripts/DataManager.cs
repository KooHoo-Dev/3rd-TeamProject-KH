using UnityEngine;

namespace TerrainCollapse
{
    public class DataManager : MonoBehaviour
    {
        // 1. 어디서든 쉽게 데이터에 접근할 수 있도록 싱글톤 설정
        public static DataManager Instance { get; private set; }

        // 2. 관리할 테이블들을 프로퍼티로 들고 있기 (ItemTable)
        public ItemTable ItemTable { get; private set; } = new();

        // 만약 기존에 쓰던 적(Enemy) 테이블이 있다면 이곳에 함께 선언합니다.
        // public EnemyTable EnemyTable { get; private set; } = new();

        private void Awake()
        {
            // 싱글톤 중복 방지 및 씬 전환 시 파괴 방지 설정 [3, 4]
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);

                // 게임 시작과 동시에 모든 테이블을 순서대로 로드!
                LoadAllTables();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void LoadAllTables()
        {
            Debug.Log("[DataManager] 데이터 파이프라인 로딩 시작...");

            // 3. 아이템 테이블 로드 호출!
            ItemTable.Load();

            // 4. 의존성이 있는 테이블들은 여기서 순서를 맞춰 로드합니다.
            // (예: 적 데이터를 먼저 읽어야 웨이브가 적을 참조할 수 있습니다) [5]
            // EnemyTable.Load();
            // WaveTable.Load(EnemyTable); 


            Debug.Log("[DataManager] 모든 테이블 데이터 로딩 완료!");
        }
    }
}