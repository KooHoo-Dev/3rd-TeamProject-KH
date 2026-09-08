using UnityEngine;

namespace TerrainCollapse
{
    public sealed class CharacterAnimationController : MonoBehaviour
    {
    
        [Header("Terrain Interaction Visual Settings")]
        [SerializeField] private int normalSortingOrder = 2;
        [SerializeField] private int buriedSortingOrder = -1;
        
        private ItemController itemController;
        
        private static readonly int ClimbAnimSpeedHash =
            Animator.StringToHash("ClimbAnimSpeed");
        
        private static readonly int SpeedHash =
            Animator.StringToHash("Speed");

        private static readonly int YVelocityHash =
            Animator.StringToHash("YVelocity");

        private static readonly int GroundedHash =
            Animator.StringToHash("Grounded");

        private static readonly int ClimbingHash =
            Animator.StringToHash("Climbing");

        private static readonly int MineHash =
            Animator.StringToHash("Mine");

        private static readonly int DeadHash =
            Animator.StringToHash("Dead");

        private static readonly int DeadStateHash =
            Animator.StringToHash("Base Layer.Dead");


        private Animator animator;
        private SpriteRenderer spriteRenderer;
        private BoxCollider2D bodyCollider;
        private float visualBaseScaleX;
        private Vector3 visualBaseLocalPosition;

        private IPlayerController playerController;
        private bool wasDead;
        private bool wasDroppingThroughSpawnPlatform;


        private void Awake()
        {
            animator = GetComponent<Animator>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            bodyCollider = GetComponentInParent<BoxCollider2D>();
            visualBaseScaleX = Mathf.Abs(transform.localScale.x);
            visualBaseLocalPosition = transform.localPosition;
            itemController = GetComponentInParent<ItemController>();
            // CharacterAnimationController는 Visual 자식에 있으므로
            // 부모 TestPlayer에서 컨트롤러를 찾는다.
            playerController = GetComponentInParent(typeof(IPlayerController)) as IPlayerController;
        }


        private void Update()
        {
            if (playerController == null)
                return;

            Vector2 velocity = playerController.Velocity;
            
            float climbAnimSpeed = 0f;

            if (playerController.IsClimbing &&
                velocity.sqrMagnitude > 0.001f)
            {
                climbAnimSpeed = 1f;
            }

            animator.SetFloat(
                ClimbAnimSpeedHash,
                climbAnimSpeed);

            animator.SetFloat(
                SpeedHash,
                Mathf.Abs(velocity.x));

            // 하향 점프의 시작 프레임만 Jump 전이에 사용한다. 통과 상태 전체에
            // 양의 Y 속도를 주면 착지한 뒤에도 Jump 상태가 유지될 수 있다.
            bool isDroppingThroughSpawnPlatform = playerController.IsDroppingThroughSpawnPlatform;
            float animationYVelocity = isDroppingThroughSpawnPlatform &&
                                       wasDroppingThroughSpawnPlatform == false
                ? 0.01f
                : velocity.y;
            animator.SetFloat(YVelocityHash, animationYVelocity);
            wasDroppingThroughSpawnPlatform = isDroppingThroughSpawnPlatform;

            animator.SetBool(
                GroundedHash,
                playerController.IsGrounded);

            animator.SetBool(
                ClimbingHash,
                playerController.IsClimbing);

            bool isDead = playerController.IsDead;
            animator.SetBool(DeadHash, isDead);
            if (isDead && wasDead == false)
                animator.Play(DeadStateHash, 0, 0f);
            wasDead = isDead;

            if (itemController == null ||
                !itemController.IsMining)
            {
                UpdateFacingDirection(velocity.x);
            }
            
            UpdateSortingOrder();
            
            bool isClimbing = playerController.IsClimbing;

            animator.SetBool(
                ClimbingHash,
                isClimbing);

            if (isClimbing)
            {
                animator.ResetTrigger(MineHash);
            }
        }
        
        private void OnEnable()
        {
            if (itemController != null)
            {
                itemController.MineStarted += HandleMineStarted;
            }
        }

        private void OnDisable()
        {
            if (itemController != null)
            {
                itemController.MineStarted -= HandleMineStarted;
            }
        }
        
        private void HandleMineStarted(float directionX)
        {
            UpdateFacingDirection(directionX);
            animator.SetTrigger(MineHash);
        }
        
        public void AnimationEvent_MineFinished()
        {
            itemController?.FinishMine();
        }
        
        public void AnimationEvent_MineImpact()
        {
            itemController?.ExecuteMineImpact();
        }

        private void UpdateFacingDirection(float velocityX)
        {
            if (Mathf.Abs(velocityX) <= 0.01f)
                return;

            // 몸통 BoxCollider2D를 가진 루트를 반전하면, 바로 옆 채굴 타일 쪽으로
            // 콜라이더가 이동해 지형 내부에 들어갈 수 있다. Visual만 반전해 물리는 고정한다.
            float facingSign = velocityX > 0f ? 1f : -1f;
            Vector3 scale = transform.localScale;
            scale.x = visualBaseScaleX * facingSign;
            transform.localScale = scale;
            spriteRenderer.flipX = false;

            // 현재 오른쪽 정렬에서 몸통 중심은 BoxCollider2D의 Offset X에 맞아 있다.
            // Visual을 반전하면 스프라이트 피벗도 반대편으로 이동하므로, Visual 위치를
            // 보정해 어느 방향에서도 그 중심이 같은 몸통 콜라이더 위에 남게 한다.
            float spriteCenterX = spriteRenderer.sprite != null
                ? spriteRenderer.sprite.bounds.center.x * visualBaseScaleX
                : 0f;
            float bodyCenterX = bodyCollider != null
                ? bodyCollider.offset.x
                : visualBaseLocalPosition.x + spriteCenterX;
            Vector3 visualPosition = visualBaseLocalPosition;
            visualPosition.x = bodyCenterX - spriteCenterX * facingSign;
            transform.localPosition = visualPosition;
        }

        private void UpdateSortingOrder()
        {
            spriteRenderer.sortingOrder =
                playerController.IsBuried
                    ? buriedSortingOrder
                    : normalSortingOrder;
        }

        public void PlayMine()
        {
            animator.SetTrigger(MineHash);
        }
    }
    
}
