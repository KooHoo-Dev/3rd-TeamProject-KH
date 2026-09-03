using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainCollapse
{
    /// <summary>
    /// 상점 NPC 클릭 감지.
    /// OnMouseDown은 레거시 Input Manager에 의존해 이 프로젝트(New Input System 전용)에서는
    /// 동작하지 않을 수 있어, Mouse.current 기반 수동 판정으로 구현합니다.
    /// 실제 접근 가능 여부(지상 여부, 사망 여부 등)는 ShopAccessController가 그대로 판단합니다 -
    /// 이 스크립트는 "NPC가 클릭됐다"는 신호만 전달합니다.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class ShopNpcInteractable : MonoBehaviour
    {
        [SerializeField] private ShopAccessController shopAccess; // 플레이어의 상점 접근/UI 컨트롤러

        private Collider2D npcCollider;

        private void Awake()
        {
            npcCollider = GetComponent<Collider2D>();
        }

        private void Update()
        {
            if (shopAccess.IsShopAccessible == false) return;

            Mouse mouse = Mouse.current;
            if (mouse.leftButton.wasPressedThisFrame == false) return;

            Camera mainCam = Camera.main;

            Vector3 worldPos = mainCam.ScreenToWorldPoint(mouse.position.ReadValue());
            worldPos.z = 0f;

            if (npcCollider.OverlapPoint(worldPos) == false) return;

            shopAccess.OpenShop();
        }
    }
}
