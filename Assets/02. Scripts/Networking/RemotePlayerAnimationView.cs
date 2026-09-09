using UnityEngine;

namespace TerrainCollapse.Networking
{
    /// <summary>서버가 확정해 중계한 다른 플레이어 상태를 원격 Animator에 표시한다.</summary>
    public sealed class RemotePlayerAnimationView : MonoBehaviour
    {
        private static readonly int ClimbAnimSpeedHash = Animator.StringToHash("ClimbAnimSpeed");
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int YVelocityHash = Animator.StringToHash("YVelocity");
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int ClimbingHash = Animator.StringToHash("Climbing");
        private static readonly int MineHash = Animator.StringToHash("Mine");
        private static readonly int DeadHash = Animator.StringToHash("Dead");

        [SerializeField] private Animator animator;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private int normalSortingOrder = 2;
        [SerializeField] private int buriedSortingOrder = -1;
        [SerializeField] private Material surfaceUnlitMaterial;
        [SerializeField, Min(0f)] private float mineSoundOffset = 0.75f;

        private Material undergroundMaterial;
        private float mineDirectionX = 1f;

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            undergroundMaterial = spriteRenderer != null ? spriteRenderer.sharedMaterial : null;
        }

        public void ApplyState(PlayerStateDto state)
        {
            if (state == null) return;

            if (animator != null)
            {
                animator.SetFloat(ClimbAnimSpeedHash,
                    state.IsClimbing &&
                    state.VelocityX * state.VelocityX + state.VelocityY * state.VelocityY > 0.001f
                        ? 1f
                        : 0f);
                animator.SetFloat(SpeedHash, Mathf.Abs(state.VelocityX));
                animator.SetFloat(YVelocityHash, state.VelocityY);
                animator.SetBool(GroundedHash, state.IsGrounded);
                animator.SetBool(ClimbingHash, state.IsClimbing);
                animator.SetBool(DeadHash, state.IsDead);
            }

            if (spriteRenderer == null) return;
            UpdateLightingMaterial(state.Y > 0f);
            if (state.VelocityX > 0.01f) spriteRenderer.flipX = false;
            else if (state.VelocityX < -0.01f) spriteRenderer.flipX = true;
            spriteRenderer.sortingOrder = state.IsBuried
                ? buriedSortingOrder
                : normalSortingOrder;
        }

        private void UpdateLightingMaterial(bool isOnSurface)
        {
            if (isOnSurface == false)
            {
                if (undergroundMaterial != null && spriteRenderer.sharedMaterial != undergroundMaterial)
                    spriteRenderer.sharedMaterial = undergroundMaterial;
                return;
            }

            if (surfaceUnlitMaterial != null && spriteRenderer.sharedMaterial != surfaceUnlitMaterial)
                spriteRenderer.sharedMaterial = surfaceUnlitMaterial;
        }

        public void SetDead(bool isDead)
        {
            if (animator != null) animator.SetBool(DeadHash, isDead);
        }

        public void PlayMine(float directionX)
        {
            if (spriteRenderer != null && Mathf.Abs(directionX) > 0.01f)
                spriteRenderer.flipX = directionX < 0f;
            if (Mathf.Abs(directionX) > 0.01f)
                mineDirectionX = Mathf.Sign(directionX);
            animator?.SetTrigger(MineHash);
        }

        // 원격 채굴은 지형/아이템 로직을 실행하지 않고 타격 프레임의 위치 효과음만 재생한다.
        public void AnimationEvent_MineFinished() { }
        public void AnimationEvent_MineImpact()
        {
            Vector3 soundPosition = transform.position +
                                    Vector3.right * mineDirectionX * mineSoundOffset;
            GameAudio.GameSound.Play(GameAudio.SoundCue.Dig, soundPosition);
        }
    }
}
