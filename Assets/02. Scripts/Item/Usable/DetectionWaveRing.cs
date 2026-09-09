using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>
    /// Detector 사용 시 중심(설치자)에서 바깥으로 퍼져나가는 소나(음파) 링 이펙트.
    /// - 반경 0 -> maxWorldRadius까지 커지면서 진행되고,
    /// - 링 자체는 방사형 그라데이션 텍스처를 사용해 안쪽/바깥쪽 "모서리"로 갈수록
    ///   부드럽게 알파값이 낮아지는(퍼져 사라지는) 형태로 보이며,
    /// - 시간이 지나 바깥으로 퍼질수록 전체 알파도 함께 옅어지다가 끝에 스스로 파괴된다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class DetectionWaveRing : MonoBehaviour
    {
        [Header("Ring Shape")]
        [SerializeField, Range(0.01f, 0.5f)] private float ringThickness = 0.05f; // 링 밴드 두께(반지름 대비 비율)
        [SerializeField, Range(0.01f, 0.5f)] private float edgeSoftness = 0.01f;  // 안쪽/바깥쪽 경계가 옅어지는 정도
        [SerializeField, Min(32)] private int textureResolution = 256;

        [Header("Sorting")]
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrder = 9999;

        private SpriteRenderer spriteRenderer;
        private float elapsed;
        private float duration;
        private float maxWorldRadius;
        private Color baseColor;
        private bool isPlaying;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            spriteRenderer.sprite = BuildRingSprite();
            spriteRenderer.sortingLayerName = sortingLayerName;
            spriteRenderer.sortingOrder = sortingOrder;
        }

        /// <summary>
        /// 링 애니메이션을 시작한다.
        /// </summary>
        /// <param name="playDuration">반경 0에서 maxRadius까지 도달하는 데 걸리는 시간(초). Detector의 waveDuration과 맞추면 파동 확산 속도와 일치한다.</param>
        /// <param name="worldRadius">최종 도달 반경(월드 단위) = Detector의 detectRadius.</param>
        /// <param name="color">링 색상(알파는 시작 알파값으로 사용됨, 시간에 따라 0까지 줄어듦).</param>
        public void Play(float playDuration, float worldRadius, Color color)
        {
            duration = Mathf.Max(0.01f, playDuration);
            maxWorldRadius = Mathf.Max(0.01f, worldRadius);
            baseColor = color;
            elapsed = 0f;
            isPlaying = true;

            transform.localScale = Vector3.zero;
            spriteRenderer.color = baseColor;
        }

        private void Update()
        {
            if (isPlaying == false) return;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // 스프라이트를 pixelsPerUnit = textureResolution으로 만들었기 때문에
            // 기본 스프라이트 지름은 1 월드유닛 -> 스케일 값 = 현재 지름(반경*2)이 된다.
            float currentRadius = Mathf.Lerp(0f, maxWorldRadius, t);
            transform.localScale = Vector3.one * (currentRadius * 2f);

            // 바깥으로 퍼질수록(t가 1에 가까워질수록) 전체적으로 옅어지다 사라짐
            Color c = baseColor;
            c.a = baseColor.a * (1f - t);
            spriteRenderer.color = c;

            if (t >= 1f)
            {
                isPlaying = false;
                Destroy(gameObject);
            }
        }

        // 중심은 완전히 비우고 ringThickness 근방(밴드)만 밝게, 그 안쪽/바깥쪽 경계는
        // edgeSoftness만큼 서서히 알파 0으로 페이드되는 방사형(radial) 텍스처를 생성한다.
        private Sprite BuildRingSprite()
        {
            int res = textureResolution;
            Texture2D texture = new Texture2D(res, res, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "DetectionWaveRingTexture"
            };

            Vector2 center = new Vector2(res * 0.5f, res * 0.5f);
            float maxDist = res * 0.5f;
            float ringRadius = maxDist * (1f - ringThickness * 0.5f); // 텍스처 가장자리 쪽에 밴드 위치
            float halfBand = maxDist * ringThickness * 0.5f;          // 밴드 절반 두께(이 안쪽은 완전 불투명)
            float soft = Mathf.Max(1f, maxDist * edgeSoftness);       // 밴드 밖에서 알파가 0까지 줄어드는 거리

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);

                    // 링 밴드 중심선(ringRadius)으로부터 얼마나 벗어났는지
                    float distFromBand = Mathf.Abs(dist - ringRadius);

                    // 밴드 안쪽(halfBand 이내)은 alpha 1, 그 밖은 soft 거리만큼 서서히 0으로 감쇠
                    float alpha = 1f - Mathf.Clamp01((distFromBand - halfBand) / soft);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
                }
            }

            texture.Apply();

            // pixelsPerUnit = res 로 지정 -> 텍스처 전체(지름 res px)가 정확히 1 월드유닛 지름이 됨
            return Sprite.Create(
                texture,
                new Rect(0, 0, res, res),
                new Vector2(0.5f, 0.5f),
                res);
        }
    }
}