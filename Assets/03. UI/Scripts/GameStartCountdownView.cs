using TMPro;
using UnityEngine;

namespace TerrainCollapse
{
    public sealed class GameStartCountdownView : MonoBehaviour
    {
        [SerializeField] private RoomClient roomClient;
        [SerializeField] private TMP_Text countdownText;

        private void Awake()
        {
            if (roomClient == null)
                roomClient = FindFirstObjectByType<RoomClient>();

            SetText(string.Empty);
        }

        private void Update()
        {
            if (roomClient == null ||
                roomClient.HasGameStartSchedule == false ||
                roomClient.IsGameplayActive)
            {
                SetText(string.Empty);
                return;
            }

            double secondsUntilStart =
                roomClient.SecondsUntilGameplayStart;

            int countdownSeconds =
                roomClient.GameCountdownSeconds;

            // 실제 시작 3초 전까지는 2초 준비 대기 구간
            if (secondsUntilStart > countdownSeconds)
            {
                SetText(string.Empty);
                return;
            }

            int number = Mathf.CeilToInt((float)secondsUntilStart);
            SetText(number > 0 ? number.ToString() : string.Empty);
        }

        private void SetText(string value)
        {
            if (countdownText != null)
            {
                countdownText.text = value;
                countdownText.gameObject.SetActive(
                    string.IsNullOrEmpty(value) == false);
            }
        }
    }
}
