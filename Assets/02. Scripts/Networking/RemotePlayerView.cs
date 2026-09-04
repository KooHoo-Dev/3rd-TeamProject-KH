using UnityEngine;

namespace TerrainCollapse.Networking
{
    public sealed class RemotePlayerView : MonoBehaviour
    {
        [SerializeField] private float smooth = 12f;

        private Vector2 targetPosition;

        public string PlayerId { get; private set; }
        public int ColorIndex { get; private set; }

        public void Initialize(string playerId, Vector2 initialPosition, int colorIndex)
        {
            PlayerId = playerId;
            ColorIndex = colorIndex;
            Vector3 current = transform.position;
            transform.position = new Vector3(initialPosition.x, initialPosition.y, current.z);
            targetPosition = initialPosition;
        }

        public void SetServerPosition(Vector2 position)
        {
            targetPosition = position;
        }

        public void SetColorIndex(int colorIndex)
        {
            ColorIndex = colorIndex;
        }

        private void Update()
        {
            float t = smooth <= 0f
                ? 1f
                : 1f - Mathf.Exp(-smooth * Time.deltaTime);
            Vector3 current = transform.position;
            Vector2 next = Vector2.Lerp(
                new Vector2(current.x, current.y),
                targetPosition,
                t);
            transform.position = new Vector3(next.x, next.y, current.z);
        }
    }
}
