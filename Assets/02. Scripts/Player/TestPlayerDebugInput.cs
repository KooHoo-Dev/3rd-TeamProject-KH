using Character;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainCollapse
{
    /// <summary>테스트 씬에만 선택적으로 붙이는 즉사 및 골드 지급 입력 컴포넌트</summary>
    [RequireComponent(typeof(PlayerHp))]
    public sealed class TestPlayerDebugInput : MonoBehaviour
    {
        private PlayerHp playerHp;
        private void Awake() => playerHp = GetComponent<PlayerHp>();
        private void Update()
        {
            if (ChatInputState.IsTyping) return;

            if (Keyboard.current?.kKey.wasPressedThisFrame == true)
                playerHp.ReportOrApplyDamage(
                    new DamageInfo(999, DamageType.Debug, null));

            if (Keyboard.current?.gKey.wasPressedThisFrame == true)
                if (RoomClient.Instance == null || RoomClient.Instance.RequestDebugGold() == false)
                    Debug.LogWarning("[Debug] 서버에 10,000 Gold 지급 요청을 보내지 못했습니다.", this);
        }
    }
}
