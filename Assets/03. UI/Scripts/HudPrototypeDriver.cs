using System.Collections.Generic;
using Character;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>
    /// 실제 체력과 인벤토리 시스템이 완성되기 전 UI 표시를 확인하는 임시 컴포넌트다.
    /// UIPrototypeScene에서만 사용하고 실제 게임 씬에서는 비활성화한다.
    /// </summary>
    public sealed class HudPrototypeDriver : MonoBehaviour
    {
        [SerializeField] private bool previewEnabled;
        [SerializeField] private HealthBarView healthBarView;
        [SerializeField] private InventoryView inventoryView;

        [Header("Preview Inventory")]
        [SerializeField] private Sprite coalIcon;
        [SerializeField] private Sprite ironOreIcon;
        [SerializeField] private int gold = 1250;

        [SerializeField] private PlayerHp playerHp;

        private void Awake()
        {
            if (playerHp == null) playerHp = FindFirstObjectByType<PlayerHp>();
        }

        private void OnEnable()
        {
            if (playerHp != null) playerHp.HpChanged += HandleHpChanged;
        }

        private void OnDisable()
        {
            if (playerHp != null) playerHp.HpChanged -= HandleHpChanged;
        }

        private void Start()
        {
            if (healthBarView == null)
            {
                Debug.LogError("[UI] HealthBarView가 연결되지 않았습니다.", this);
            }
            else
            {
                if (playerHp == null)
                    Debug.LogError("[UI] PlayerHp를 찾을 수 없습니다.", this);
                else
                    healthBarView.SetHealth(playerHp.CurrentHp, playerHp.MaxHp);
            }

            if (previewEnabled == false) return;

            if (inventoryView == null) return;

            List<InventorySlotDisplayData> previewItems = new()
            {
                new InventorySlotDisplayData(coalIcon),
                new InventorySlotDisplayData(ironOreIcon)
            };

            inventoryView.Render(previewItems, gold);
        }

        private void HandleHpChanged(int currentHp, int maxHp)
        {
            if (healthBarView == null) return;

            healthBarView.SetHealth(currentHp, maxHp);
        }
    }
}
