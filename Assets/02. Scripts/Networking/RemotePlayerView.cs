using UnityEngine;

namespace TerrainCollapse.Networking
{
    public sealed class RemotePlayerView : MonoBehaviour
    {
        [SerializeField] private float smooth = 12f;
        [SerializeField] private RemotePlayerAnimationView animationView;
        [SerializeField] private PlayerNameTag nameTag;

        private Vector2 targetPosition;
        private bool hasMovementState;
        private bool wasGrounded;
        private float airborneSince = -1f;
        private float nextFootstepTime;
        private bool hasHealthState;
        private int currentHealth;
        private bool healthIsDead;

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
            UpdateMovementSounds(state);
            animationView?.ApplyState(state);
        }

        public void Teleport(Vector2 position)
        {
            targetPosition = position;
            Vector3 current = transform.position;
            transform.position = new Vector3(position.x, position.y, current.z);
        }

        public void ApplyHealthSnapshot(PlayerHealthStateDto state)
        {
            if (state == null) return;
            hasHealthState = true;
            currentHealth = state.CurrentHealth;
            healthIsDead = state.IsDead;
            animationView?.SetDead(state.IsDead);
        }

        public void ApplyHealthChanged(PlayerHealthStateDto state)
        {
            if (state == null) return;

            Vector3 soundPosition = transform.position;
            if (hasHealthState && currentHealth - state.CurrentHealth >= 3)
                GameAudio.GameSound.Play(GameAudio.SoundCue.PlayerHurt, soundPosition);
            if (healthIsDead == false && state.IsDead)
                GameAudio.GameSound.Play(GameAudio.SoundCue.PlayerDeath, soundPosition);

            hasHealthState = true;
            currentHealth = state.CurrentHealth;
            healthIsDead = state.IsDead;
            animationView?.SetDead(state.IsDead);
        }

        public void ApplyDeath(PlayerHealthStateDto state)
        {
            if (healthIsDead == false)
                GameAudio.GameSound.Play(GameAudio.SoundCue.PlayerDeath, transform.position);

            if (state != null)
            {
                hasHealthState = true;
                currentHealth = state.CurrentHealth;
            }
            healthIsDead = true;
            animationView?.SetDead(true);
        }

        public void ApplyRespawn(PlayerHealthStateDto state)
        {
            if (state != null)
            {
                hasHealthState = true;
                currentHealth = state.CurrentHealth;
            }
            healthIsDead = false;
            ResetMovementSounds();
            animationView?.SetDead(false);
        }

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

        private void UpdateMovementSounds(PlayerStateDto state)
        {
            bool canPlayMovement = state.IsDead == false &&
                                   state.IsBuried == false &&
                                   state.IsClimbing == false;
            if (hasMovementState == false)
            {
                hasMovementState = true;
                wasGrounded = state.IsGrounded;
                if (canPlayMovement && state.IsGrounded == false)
                    airborneSince = Time.time;
                return;
            }

            if (canPlayMovement == false)
            {
                wasGrounded = state.IsGrounded;
                airborneSince = -1f;
                return;
            }

            Vector2 soundPosition = new(state.X, state.Y);
            if (state.IsGrounded)
            {
                if (wasGrounded == false && airborneSince >= 0f &&
                    Time.time - airborneSince >= 0.1f)
                {
                    GameAudio.GameSound.Play(GameAudio.SoundCue.PlayerLand, soundPosition);
                    nextFootstepTime = Time.time + 0.3f;
                }

                airborneSince = -1f;
                if (Mathf.Abs(state.VelocityX) > 0.01f && Time.time >= nextFootstepTime)
                {
                    GameAudio.GameSound.Play(GameAudio.SoundCue.PlayerRun, soundPosition);
                    nextFootstepTime = Time.time + 0.3f;
                }
            }
            else if (wasGrounded || airborneSince < 0f)
            {
                airborneSince = Time.time;
            }

            wasGrounded = state.IsGrounded;
        }

        private void ResetMovementSounds()
        {
            hasMovementState = false;
            wasGrounded = false;
            airborneSince = -1f;
            nextFootstepTime = 0f;
        }
    }
}
