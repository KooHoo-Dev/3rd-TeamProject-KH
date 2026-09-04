using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TerrainCollapse
{
    /// <summary>대기방의 고정 4인 슬롯 하나를 표시한다.</summary>
    public sealed class LobbyPlayerSlotView : MonoBehaviour
    {
        [SerializeField] private Image avatarImage;
        [SerializeField] private TMP_Text nickNameText;
        [SerializeField] private Sprite localPlayerSprite;
        [SerializeField] private Sprite remotePlayerSprite;
        [SerializeField] private Color playerColor = Color.white;

        public void SetPlayer(string nickName, bool isLocal)
        {
            if (avatarImage != null)
            {
                avatarImage.enabled = true;
                avatarImage.sprite = isLocal ? localPlayerSprite : remotePlayerSprite;
                avatarImage.color = playerColor;
            }
            if (nickNameText != null)
            {
                nickNameText.text = nickName;
                nickNameText.color = playerColor;
            }
        }

        public void SetEmpty()
        {
            if (avatarImage != null) avatarImage.enabled = false;
            if (nickNameText != null)
            {
                nickNameText.text = "Waiting...";
                nickNameText.color = new Color(1f, 1f, 1f, 0.4f);
            }
        }
    }
}
