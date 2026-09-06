using UnityEngine;
using UnityEngine.Serialization;

namespace TerrainCollapse
{
    public class DynamiteItem : Item
    {
        [Header("Preview Settings")]
        [SerializeField] private LineRenderer trajectoryLine; // 투척 예상 경로선
        [SerializeField] private GameObject landingHighlight; // 착탄 지점 하이라이트
        [SerializeField] private float lineThickness = 0.15f;
        [SerializeField, Min(2)] private int trajectorySegments = 20; // 경로선 정밀도
        [SerializeField] private Color highlightLineColor = Color.white;

        [Header("Throw Settings")]
        [SerializeField] private DynamiteExplosive explosivePrefab; // 투척되는 실제 폭발물 프리팹
        [SerializeField] private ExplosiveData data; // SO 데이터 참조
        
        
        private GameObject highlightInstance;

        public override void Init(ItemDef def)
        {
            base.Init(def);

            if (highlightInstance == null && landingHighlight != null)
            {
                highlightInstance = Instantiate(landingHighlight, transform);
                highlightInstance.SetActive(false);
            }

            void SetupLineRenderer()
            {
                if (trajectoryLine == null)
                    trajectoryLine = gameObject.AddComponent<LineRenderer>();

                // 1. 점 개수 및 공간 좌표계 설정 (포물선 표현을 위해 다수의 점 사용)
                trajectoryLine.positionCount = trajectorySegments;
                trajectoryLine.useWorldSpace = true;

                // 2. 두께 및 정렬 레이어 설정
                trajectoryLine.startWidth = lineThickness;
                trajectoryLine.endWidth = lineThickness;
                trajectoryLine.sortingLayerName = "Foreground";
                trajectoryLine.sortingOrder = 10;

                // 3. 머티리얼 및 색상 설정 (다이너마이트 느낌의 주황색 계열)
                trajectoryLine.material = new Material(Shader.Find("Sprites/Default"));
                trajectoryLine.startColor = highlightLineColor;
                trajectoryLine.endColor = highlightLineColor;

                // 4. 기본 비활성화
                trajectoryLine.enabled = false;
            }
            SetupLineRenderer();
        }

        // ItemController가 우클릭 홀드 중일 때만 매 프레임 호출합니다.
        public override void OnUpdatePreview(Vector3 ownerPos, Vector3 targetPos)
        {
            // 1. 마우스 방향 벡터 계산
            Vector3 throwDirection = (targetPos - ownerPos).normalized;

            // 2. 물리/중력을 반영한 궤적 및 예상 최종 위치 계산
            Vector3 expectedLandingPos = DrawTrajectory(ownerPos, throwDirection);

            // 3. 착탄 하이라이트 위치 업데이트
            if (highlightInstance != null)
            {
                highlightInstance.transform.position = expectedLandingPos;
                highlightInstance.SetActive(true);
            }
        }

        /// <summary>
        /// 마우스 방향과 속도, 중력을 반영하여 시간 경과에 따른 실제 낙하 궤적을 그립니다.
        /// </summary>
        private Vector3 DrawTrajectory(Vector3 startPos, Vector3 direction)
        {
            trajectoryLine.enabled = true;
            trajectoryLine.positionCount = trajectorySegments;

            float timeStep = 0.05f; // 점 사이의 시간 간격 (시뮬레이션 정밀도)
    
            // Rigidbody2D 물리를 사용하는 경우 Physics2D.gravity.y를, 
            // 아니면 직접 정의한 중력값(예: -9.81f)을 사용하세요.
            Vector3 gravity = Physics2D.gravity;

            Vector3 currentPos = startPos;
            Vector3 velocity = direction * data.ThrowSpeed;

            for (int i = 0; i < trajectorySegments; i++)
            {
                trajectoryLine.SetPosition(i, currentPos);

                // 물리 등가속도 운동 공식: pos = pos + v*dt + 0.5*g*dt^2
                currentPos += velocity * timeStep + gravity * (0.5f * (timeStep * timeStep));
                velocity += gravity * timeStep;
            }

            // 예상 최종 위치 반환 (하이라이트 표시용)
            return currentPos;
        }

        public override void OnClearPreview()
        {
            if (trajectoryLine != null)
                trajectoryLine.enabled = false;

            if (highlightInstance != null)
                highlightInstance.SetActive(false);
        }

        // 좌클릭 시 ItemController에 의해 호출됨 -> 실제 투척 실행
        public override void Use(Vector3 ownerPos, Vector3 targetPos)
        {
            if (explosivePrefab == null)
            {
                Debug.LogWarning("[DynamiteItem] explosivePrefab이 설정되지 않아 투척할 수 없습니다.");
                return;
            }

            // 마우스 위치 - 플레이어 위치 (방향 벡터)
            Vector3 throwDirection = (targetPos - ownerPos).normalized;

            DynamiteExplosive thrown = Instantiate(explosivePrefab, ownerPos, Quaternion.identity);
            // targetPos 대신 throwDirection을 넘겨줍니다.
            thrown.Launch(ownerPos, throwDirection);

            OnClearPreview();
        }
        
    }
}