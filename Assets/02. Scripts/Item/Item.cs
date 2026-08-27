using UnityEngine;

namespace TerrainCollapse
{
    public abstract class Item : MonoBehaviour
    {
        // 이 아이템의 정의 데이터 (테이블에서 파싱한 정보)
        public ItemDef Def { get; private set; }

        // 초기화 함수 (에셋이 생성될 때 호출)
        public virtual void Init(ItemDef def)
        {
            Def = def;
        }

        // 모든 아이템이 땅에 떨어졌을 때 가질 수 있는 공통 기능 (예: 자석 효과 등)
        public void Drop()
        {
            // 공통 드랍 로직
            // 사망시 일정 범위 내의 타일을 검색하고 지금 가지고 있는 아이템을 흩뿌린다.
        }

        // 아이템이 사용될 때 실행할 핵심 추상 메서드 (자식들이 직접 구현)
        public abstract void Use(Vector3 ownerPos,  Vector3 targetPos);
    }
}