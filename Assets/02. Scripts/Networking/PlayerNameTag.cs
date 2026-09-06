using TMPro;
using UnityEngine;

namespace TerrainCollapse.Networking
{
    /// <summary>서버에서 받은 닉네임을 플레이어 머리 위에 고유 색상으로 표시한다.</summary>
    public sealed class PlayerNameTag : MonoBehaviour
    {
        [SerializeField] private Vector3 localPosition = new(0f, 1f, -0.1f);
        [SerializeField, Min(0.1f)] private float fontSize = 30f;
        [SerializeField, Min(0.01f)] private float worldScale = 0.1f;
        [SerializeField] private int sortingOrder = 100;

        private TextMeshPro label;
        private string playerName;
        private Color playerColor = Color.white;

        private void Start()
        {
            EnsureLabel();
            Apply();
        }

        public void SetIdentity(string nickName, int colorIndex)
        {
            playerName = string.IsNullOrWhiteSpace(nickName)
                ? string.Empty
                : nickName.Trim();
            playerColor = PlayerColorPalette.GetColor(colorIndex);
            Apply();
        }

        private void EnsureLabel()
        {
            if (label != null) return;

            GameObject labelObject = new("NameTag", typeof(RectTransform), typeof(TextMeshPro));
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localPosition = localPosition;
            labelObject.transform.localScale = Vector3.one * worldScale;

            label = labelObject.GetComponent<TextMeshPro>();
            label.rectTransform.sizeDelta = new Vector2(10f, 2f);
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = fontSize;
            label.fontStyle = FontStyles.Bold;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.richText = false;
            label.raycastTarget = false;
            label.sortingOrder = sortingOrder;
        }

        private void Apply()
        {
            if (label == null) return;
            label.text = playerName;
            label.color = playerColor;
        }
    }
}
