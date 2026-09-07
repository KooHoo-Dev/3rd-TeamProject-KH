using UnityEngine;
using UnityEngine.UI;

namespace TerrainCollapse
{
    /// <summary>
    /// 아이템 아이콘 위에 겹쳐서 라디얼(부채꼴) 쿨다운 오버레이를 표시하는 컴포넌트.
    ///
    /// [Unity 세팅]
    /// 1. 아이콘 Image 위에 자식 Image를 하나 추가 (예: "CooldownOverlay").
    ///    - Rect Transform을 아이콘과 동일한 크기/위치로 맞춘다 (Stretch 권장).
    /// 2. 해당 Image의 Image Type = Filled, Fill Method = Radial 360,
    ///    Fill Origin = Top, Clockwise 체크(취향에 따라).
    /// 3. Source Image는 아이콘과 같은 원형/사각 스프라이트(단색 채우기용)면 충분하고,
    ///    Color는 반투명 검정(예: 0,0,0,0.6) 등 "덮인 느낌"이 나는 색으로.
    /// 4. 이 스크립트를 그 오버레이 오브젝트에 붙이고 itemController / itemID를 지정.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class ItemCooldownIndicator : MonoBehaviour
    {
        [SerializeField] private ItemController itemController; // 플레이어의 ItemController
        [SerializeField] private int itemID;
        [SerializeField] private Image fillImage;               // Image Type = Filled, Radial 360
        [SerializeField] private Text remainingText;             // 선택: 남은 초 표시용 (TMP면 아래 참고)

        private void Reset()
        {
            fillImage = GetComponent<Image>();
        }

        private void Awake()
        {
            if (fillImage == null)
                fillImage = GetComponent<Image>();
        }

        /// <summary>
        /// 아이템 슬롯이 재활용되는 UI(휠 등)에서 어떤 아이템을 감시할지 바꿀 때 호출.
        /// </summary>
        public void Bind(ItemController controller, int watchedItemID)
        {
            itemController = controller;
            itemID = watchedItemID;
        }

        private void Update()
        {
            if (itemController == null || fillImage == null)
                return;

            float remaining = itemController.GetItemCooldownRemaining(itemID);

            if (remaining <= 0f)
            {
                if (fillImage.enabled)
                    fillImage.enabled = false;

                if (remainingText != null && remainingText.enabled)
                    remainingText.enabled = false;

                return;
            }

            if (!fillImage.enabled)
                fillImage.enabled = true;

            float duration = itemController.GetItemCooldownDuration(itemID);
            fillImage.fillAmount = duration > 0f ? Mathf.Clamp01(remaining / duration) : 0f;

            if (remainingText != null)
            {
                remainingText.enabled = true;
                // 소수점 없이 올림 표시 (예: 2.3초 남음 -> "3")
                remainingText.text = Mathf.CeilToInt(remaining).ToString();
            }
        }
    }
}