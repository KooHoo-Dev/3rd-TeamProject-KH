using TMPro;
using UnityEngine;

namespace TerrainCollapse.Networking
{
    /// <summary>서버에서 받은 닉네임을 플레이어 머리 위에 고유 색상으로 표시한다.</summary>
    public sealed class PlayerNameTag : MonoBehaviour
    {
        [SerializeField] private Vector3 localPosition = new(0f, 1f, -0.1f);
        [SerializeField] private TMP_FontAsset fontAsset;
        [SerializeField, Min(0.1f)] private float fontSize = 30f;
        [SerializeField, Min(0.01f)] private float worldScale = 0.1f;
        [SerializeField] private int sortingOrder = 100;
        [SerializeField, Min(0f)] private float visibleDistance = 4.5f;

        private TextMeshPro label;
        private string playerName;
        private Color playerColor = Color.white;

        private void Start()
        {
            EnsureLabel();
            Apply();
        }

        private void Update()
        {
            RoomClient roomClient = RoomClient.Instance;

            if (roomClient == null)
            {
                label.enabled = false;
                return;
            }

            Vector2 playerPosition = transform.position;
            Vector2 offset = playerPosition - roomClient.LocalPlayerPosition;

            bool isInSpawnArea = roomClient.IsInSpawnArea(playerPosition);
            bool isWithinDistance = offset.sqrMagnitude <= visibleDistance * visibleDistance;

            label.enabled = isInSpawnArea || isWithinDistance;
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
            label.font = fontAsset;
            label.fontSize = fontSize;
            label.fontStyle = FontStyles.Bold;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.richText = false;
            label.raycastTarget = false;
            label.sortingOrder = sortingOrder;
        }

        private void LateUpdate()
        {
            if (label == null) return;

            // 플레이어 루트는 콜라이더까지 함께 반전하기 위해 X 스케일을 바꾼다.
            // 네임태그는 그 반전을 한 번 되돌려 항상 읽을 수 있게 유지한다.
            float xScale = transform.localScale.x < 0f ? -worldScale : worldScale;
            label.transform.localScale = new Vector3(xScale, worldScale, worldScale);
        }

        private void Apply()
        {
            if (label == null) return;
            label.text = playerName;
            label.color = playerColor;
        }
    }
}
