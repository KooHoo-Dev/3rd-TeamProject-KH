using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TerrainCollapse
{
    /// <summary>곡괭이당 오버레이 하나를 재사용하는 클라이언트 전용 타격 효과.</summary>
    [DisallowMultipleComponent]
    public sealed class TerrainTileHitEffect : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float shakeDuration = 0.12f;
        [Tooltip("셀 크기에 대한 진동 비율. 1 유닛 셀에서 0.04 유닛.")]
        [SerializeField, Range(0f, 0.1f)] private float shakeStrength = 0.04f;
        [SerializeField, Min(1)] private int shakeVibrato = 8;
        [SerializeField, Range(0f, 180f)] private float shakeRandomness = 20f;

        private SpriteRenderer visual;
        private Vector3 origin;

        public void Play(Sprite sprite, Color color, Vector3 center, float cellSize)
        {
            Stop();
            if (isActiveAndEnabled == false || sprite == null || cellSize <= 0f) return;
            Vector3 size = sprite.bounds.size;
            if (size.x <= 0f || size.y <= 0f) return;

            if (visual == null)
            {
                // 플레이어의 이동/회전/크기를 상속하지 않는 별도 루트. Collider는 없다.
                GameObject overlay = new("Dig Tile Hit Visual", typeof(SpriteRenderer));
                SceneManager.MoveGameObjectToScene(overlay, gameObject.scene);
                overlay.layer = gameObject.layer;
                visual = overlay.GetComponent<SpriteRenderer>();
                // RenderChunk는 Default/0. 자원 및 하이라이트와 별개인 지형 복제층.
                visual.sortingOrder = 1;
            }

            visual.sprite = sprite;
            visual.color = color;
            Vector3 scale = new(cellSize / size.x, cellSize / size.y, 1f);
            visual.transform.localScale = scale;
            // Sprite의 PPU와 중앙이 아닌 Pivot도 한 셀의 중심/크기에 맞춘다.
            origin = center - Vector3.Scale(sprite.bounds.center, scale);
            visual.transform.position = origin;
            visual.gameObject.SetActive(true);

            // Shake의 난수 소비가 다른 게임 시스템의 Unity Random 상태를 바꾸지 않는다.
            Random.State randomState = Random.state;
            try
            {
                float strength = Mathf.Clamp(shakeStrength, 0f, 0.1f) * cellSize;
                visual.transform.DOShakePosition(
                        Mathf.Max(0.01f, shakeDuration),
                        new Vector3(strength, strength, 0f),
                        Mathf.Max(1, shakeVibrato),
                        Mathf.Clamp(shakeRandomness, 0f, 180f))
                    .SetUpdate(true)
                    .SetLink(visual.gameObject, LinkBehaviour.KillOnDisable)
                    .OnKill(ResetVisual)
                    .OnComplete(ResetVisual)
                    .ForceInit();
            }
            finally
            {
                Random.state = randomState;
            }
        }

        public void Stop()
        {
            if (visual == null) return;
            visual.transform.DOKill();
            ResetVisual();
        }

        private void ResetVisual()
        {
            if (visual == null) return;
            visual.transform.position = origin;
            visual.sprite = null;
            visual.gameObject.SetActive(false);
        }

        private void OnDisable() => Stop();

        private void OnDestroy()
        {
            Stop();
            if (visual != null) Destroy(visual.gameObject);
        }
    }
}
