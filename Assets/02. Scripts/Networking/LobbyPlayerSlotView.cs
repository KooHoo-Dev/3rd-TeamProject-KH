using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TerrainCollapse
{
    /// <summary>로비 슬롯 순서와 인게임 캐릭터가 공유하는 플레이어 색상표입니다.</summary>
    public static class PlayerColorPalette
    {
        private static readonly int NewColorId =
            Shader.PropertyToID("_ColorChangeNewCol");

        private static readonly Color[] Colors =
        {
            new(0.96f, 0.80f, 0.16f, 1f),
            new(0.93f, 0.23f, 0.23f, 1f),
            new(0.24f, 0.52f, 0.95f, 1f),
            new(0.25f, 0.78f, 0.35f, 1f),
        };

        public static Color GetColor(int colorIndex)
            => Colors[Mathf.Clamp(colorIndex, 0, Colors.Length - 1)];

        public static void ApplyToSpriteRenderers(GameObject playerRoot, int colorIndex)
        {
            if (playerRoot == null) return;

            Color color = GetColor(colorIndex);
            MaterialPropertyBlock properties = new();
            foreach (SpriteRenderer spriteRenderer in
                     playerRoot.GetComponentsInChildren<SpriteRenderer>(true))
            {
                spriteRenderer.GetPropertyBlock(properties);
                properties.SetColor(NewColorId, color);
                spriteRenderer.SetPropertyBlock(properties);
            }
        }
    }

    /// <summary>대기방의 고정 4인 슬롯 하나를 표시한다.</summary>
    public sealed class LobbyPlayerSlotView : MonoBehaviour
    {
        private const float PreviewScale = 75f;

        [SerializeField] private Image avatarImage;
        [SerializeField] private TMP_Text nickNameText;
        [SerializeField] private GameObject localPlayerPrefab;
        [SerializeField] private GameObject remotePlayerPrefab;

        private GameObject previewInstance;
        private GameObject currentPrefab;

        private void Awake()
        {
            if (avatarImage != null) avatarImage.enabled = false;
        }

        public void SetPlayer(string nickName, bool isLocal, int colorIndex)
        {
            Color playerColor = PlayerColorPalette.GetColor(colorIndex);
            ShowPrefab(isLocal ? localPlayerPrefab : remotePlayerPrefab, colorIndex);
            if (nickNameText != null)
            {
                nickNameText.text = nickName;
                nickNameText.color = playerColor;
            }
        }

        public void SetEmpty()
        {
            if (previewInstance != null) previewInstance.SetActive(false);
            if (nickNameText != null)
            {
                nickNameText.text = "Waiting...";
                nickNameText.color = new Color(1f, 1f, 1f, 0.4f);
            }
        }

        private void ShowPrefab(GameObject prefab, int colorIndex)
        {
            if (avatarImage == null || prefab == null)
            {
                if (prefab == null)
                    Debug.LogWarning("[LobbyPlayerSlotView] Player prefab is not assigned.", this);
                return;
            }

            if (currentPrefab != prefab || previewInstance == null)
            {
                if (previewInstance != null) Destroy(previewInstance);

                // 비활성 부모 아래에서 생성해 Awake/OnEnable 기반 게임플레이 진입을 막는다.
                GameObject avatarRoot = avatarImage.gameObject;
                bool wasActive = avatarRoot.activeSelf;
                avatarRoot.SetActive(false);
                previewInstance = Instantiate(prefab, avatarImage.transform, false);
                previewInstance.name = $"{prefab.name} Preview";
                DisableGameplayComponents(previewInstance);
                avatarRoot.SetActive(wasActive);
                ConfigureIdleAnimation(previewInstance);
                currentPrefab = prefab;
            }

            previewInstance.SetActive(true);
            PlayerColorPalette.ApplyToSpriteRenderers(previewInstance, colorIndex);
            FitToAvatarRect();
        }

        private void FitToAvatarRect()
        {
            SpriteRenderer[] renderers =
                previewInstance.GetComponentsInChildren<SpriteRenderer>(true);
            if (renderers.Length == 0) return;

            previewInstance.transform.localPosition = Vector3.zero;
            RectTransform avatarRect = avatarImage.rectTransform;
            previewInstance.transform.localScale = Vector3.one * PreviewScale;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            Vector3 offset = avatarRect.position - bounds.center;
            previewInstance.transform.position += new Vector3(offset.x, offset.y, -0.1f);
        }

        private static void DisableGameplayComponents(GameObject root)
        {
            foreach (MonoBehaviour behaviour in
                     root.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;

            foreach (Rigidbody2D body in root.GetComponentsInChildren<Rigidbody2D>(true))
                body.simulated = false;
            foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>(true))
                collider.enabled = false;
            foreach (AudioSource source in root.GetComponentsInChildren<AudioSource>(true))
                source.enabled = false;
        }

        private void ConfigureIdleAnimation(GameObject root)
        {
            Animator animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null) return;

            animator.enabled = true;
            animator.SetFloat("Speed", 0f);
            animator.SetFloat("YVelocity", 0f);
            animator.SetBool("Grounded", true);
            animator.SetBool("Climbing", false);
            animator.SetBool("Dead", false);
            animator.Play("Idle", 0, 0f);
        }

    }
}
