using Character;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainCollapse
{
    /// <summary>테스트 씬에만 선택적으로 붙이는 즉사 입력 컴포넌트</summary>
    [RequireComponent(typeof(PlayerHp))]
    public sealed class TestPlayerDebugInput : MonoBehaviour
    {
        private PlayerHp playerHp;
        private void Awake() => playerHp = GetComponent<PlayerHp>();
        private void Update()
        {
            if (Keyboard.current?.kKey.wasPressedThisFrame == true)
                playerHp.ReportOrApplyDamage(
                    new DamageInfo(999, DamageType.Debug, null));
        }
    }
}
