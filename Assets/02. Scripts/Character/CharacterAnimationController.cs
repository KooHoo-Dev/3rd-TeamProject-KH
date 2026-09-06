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

        private IPlayerController playerController;
        private bool wasDead;


        private void Awake()
        {
            animator = GetComponent<Animator>();
            spriteRenderer = GetComponent<SpriteRenderer>();
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

            animator.SetFloat(
                YVelocityHash,
                velocity.y);

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
            if (velocityX > 0.01f)
            {
                spriteRenderer.flipX = false;
            }
            else if (velocityX < -0.01f)
            {
                spriteRenderer.flipX = true;
            }
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
