using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameUI
{
    /// <summary>
    /// 체력 값을 소유하지 않고 전달받은 값을 화면에 표시한다.
    /// 실제 체력 시스템은 SetHealth를 호출하는 별도 연결 컴포넌트에서 담당한다.
    /// </summary>
    public sealed class HealthBarView : MonoBehaviour
    {
        [SerializeField] private Image fillImage;
        [SerializeField] private TMP_Text valueText;

        public void SetHealth(float currentHealth, float maxHealth)
        {
            float safeMaxHealth = Mathf.Max(1f, maxHealth);
            float safeCurrentHealth = Mathf.Clamp(currentHealth, 0f, safeMaxHealth);

            if (fillImage == null)
            {
                Debug.LogError("[UI] HealthBarView의 Fill Image가 연결되지 않았습니다.", this);
                return;
            }

            fillImage.fillAmount = safeCurrentHealth / safeMaxHealth;

            if (valueText == null) return;

            valueText.text = $"{Mathf.CeilToInt(safeCurrentHealth)} / {Mathf.CeilToInt(safeMaxHealth)}";
        }
    }
}
