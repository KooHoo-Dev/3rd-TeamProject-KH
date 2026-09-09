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
        [SerializeField, Range(1, 8)] private int hoverOutlinePixelWidth = 2;
        [SerializeField] private Material hoverOutlineMaterialTemplate;

        private Collider2D npcCollider;
        private SpriteRenderer spriteRenderer;
        private Material defaultMaterial;
        private Material hoverOutlineMaterial;
        private bool hasTriedCreatingOutlineMaterial;

        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineAlphaId = Shader.PropertyToID("_OutlineAlpha");
        private static readonly int OutlinePixelWidthId = Shader.PropertyToID("_OutlinePixelWidth");

        private void Awake()
        {
            npcCollider = GetComponent<Collider2D>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            defaultMaterial = spriteRenderer != null ? spriteRenderer.sharedMaterial : null;
        }

        private void Update()
        {
            if (PauseMenuController.IsGameplayInputBlocked)
            {
                SetHoverOutlineVisible(false);
                return;
            }

            bool isHovered = shopAccess != null && shopAccess.IsShopAccessible && IsPointerOverNpc();
            SetHoverOutlineVisible(isHovered);
            if (isHovered == false) return;

            Mouse mouse = Mouse.current;
            if (mouse.leftButton.wasPressedThisFrame == false) return;

            shopAccess.OpenShop();
        }

        private bool IsPointerOverNpc()
        {
            Mouse mouse = Mouse.current;
            Camera mainCam = Camera.main;
            if (mouse == null || mainCam == null || npcCollider == null) return false;

            Vector3 worldPos = mainCam.ScreenToWorldPoint(mouse.position.ReadValue());
            return npcCollider.OverlapPoint(worldPos);
        }

        private void SetHoverOutlineVisible(bool isVisible)
        {
            if (spriteRenderer == null || defaultMaterial == null) return;

            if (isVisible)
            {
                if (hoverOutlineMaterial == null && hasTriedCreatingOutlineMaterial == false)
                {
                    hasTriedCreatingOutlineMaterial = true;
                    hoverOutlineMaterial = CreateHoverOutlineMaterial();
                }

                if (hoverOutlineMaterial != null)
                    spriteRenderer.sharedMaterial = hoverOutlineMaterial;
                return;
            }

            if (spriteRenderer.sharedMaterial == hoverOutlineMaterial)
                spriteRenderer.sharedMaterial = defaultMaterial;
        }

        private Material CreateHoverOutlineMaterial()
        {
            if (hoverOutlineMaterialTemplate == null)
            {
                Debug.LogWarning("[Shop] 외곽선 머티리얼이 연결되지 않았습니다.", this);
                return null;
            }

            Material material = new(hoverOutlineMaterialTemplate) { name = "Shop Hover Outline" };
            material.SetColor(OutlineColorId, Color.white);
            material.SetFloat(OutlineAlphaId, 1f);
            material.SetInt(OutlinePixelWidthId, Mathf.Clamp(hoverOutlinePixelWidth, 1, 8));
            return material;
        }

        private void OnDisable() => SetHoverOutlineVisible(false);

        private void OnDestroy()
        {
            if (hoverOutlineMaterial != null)
                Destroy(hoverOutlineMaterial);
        }
    }
}
