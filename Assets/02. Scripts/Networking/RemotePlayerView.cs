using UnityEngine;

namespace TerrainCollapse.Networking
{
    public sealed class RemotePlayerView : MonoBehaviour
    {
        [SerializeField] private float smooth = 12f;
        [SerializeField] private RemotePlayerAnimationView animationView;
        [SerializeField] private PlayerNameTag nameTag;

        private Vector2 targetPosition;

        public string PlayerId { get; private set; }
        public int ColorIndex { get; private set; }

        private void Awake()
        {
            if (animationView == null)
                animationView = GetComponentInChildren<RemotePlayerAnimationView>(true);
            if (nameTag == null) nameTag = GetComponent<PlayerNameTag>();
        }

        public void Initialize(
            string playerId,
            string nickName,
            Vector2 initialPosition,
            int colorIndex)
        {
            PlayerId = playerId;
            ColorIndex = colorIndex;
            Vector3 current = transform.position;
            transform.position = new Vector3(initialPosition.x, initialPosition.y, current.z);
            targetPosition = initialPosition;
            nameTag?.SetIdentity(nickName, colorIndex);
        }

        public void SetServerPosition(Vector2 position)
        {
            targetPosition = position;
        }

        public void SetServerState(PlayerStateDto state)
        {
            if (state == null) return;
            targetPosition = new Vector2(state.X, state.Y);
            animationView?.ApplyState(state);
        }

        public void Teleport(Vector2 position)
        {
            targetPosition = position;
            Vector3 current = transform.position;
            transform.position = new Vector3(position.x, position.y, current.z);
        }

        public void SetDead(bool isDead) => animationView?.SetDead(isDead);
        public void PlayMine(float directionX) => animationView?.PlayMine(directionX);

        public void SetColorIndex(int colorIndex)
        {
            ColorIndex = colorIndex;
        }

        public void SetIdentity(string nickName, int colorIndex)
        {
            ColorIndex = colorIndex;
            nameTag?.SetIdentity(nickName, colorIndex);
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
