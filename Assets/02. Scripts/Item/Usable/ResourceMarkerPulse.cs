using UnityEngine;

namespace TerrainCollapse
{
    // Detector 마커 전용 - 스케일/알파를 흔들다 스스로 사라짐
    public class ResourceMarkerPulse : MonoBehaviour
    {
        [SerializeField] private float pulseSpeed = 4f;
        [SerializeField] private float minScale = 0.85f;
        [SerializeField] private float maxScale = 1.15f;
        private SpriteRenderer sprite;

        private void Start()
        {
            sprite = GetComponent<SpriteRenderer>();
        }
        
        private void Update()
        {
            float t = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
            float scale = Mathf.Lerp(minScale, maxScale, t);
            transform.localScale = Vector3.one * scale;

            if (sprite != null)
            {
                Color c = sprite.color;
                c.a = Mathf.Lerp(0.5f, 1f, t);
                sprite.color = c;
            }
        }
    }
}