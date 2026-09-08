using UnityEngine;

namespace TerrainCollapse
{
    public class ExplosionEffect : MonoBehaviour
    {
        private const float EXPLOSION_BASE_SCALE = 3.95f;
        private const float EXPLOSION_DURATION = 0.5f;
        private float liveTime;

        private void OnEnable()
        {
            liveTime = 0.0f;
        }

        private void FixedUpdate()
        {
            if (liveTime < EXPLOSION_DURATION)
            {
                liveTime += Time.fixedDeltaTime;
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        public void SetExplosionRadius(float radius)
        {
            transform.localScale = Vector3.one * (EXPLOSION_BASE_SCALE * radius);
        }

        public void Set(Vector3 spawnPosition)
        {
            transform.localPosition = spawnPosition;
        }

    }
}