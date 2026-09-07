using UnityEngine;

namespace TerrainCollapse
{
    public abstract class Item : MonoBehaviour
    {
        public ItemDef Def { get; private set; }

        public virtual void Init(ItemDef def)
        {
            Def = def;
        }

        // 아이템 실제 사용 (클릭 시). 실제로 사용(소모)되었는지를 bool로 반환한다.
        // false를 반환하면 호출 측(ItemController)에서 인벤토리 소모를 하지 않는다.
        public abstract bool Use(Vector3 ownerPos, Vector3 targetPos);

        // 매 프레임 조준/스냅 위치 업데이트 및 하이라이트 표시
        public virtual void OnUpdatePreview(Vector3 ownerPos, Vector3 targetPos) { }

        // 장착 해제 / 스왑 / 비활성화 시 프리뷰 끄기
        public virtual void OnClearPreview() { }

        protected virtual void OnDisable()
        {
            OnClearPreview();
        }
    }
}