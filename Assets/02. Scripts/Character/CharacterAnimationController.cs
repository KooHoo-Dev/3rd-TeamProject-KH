using TerrainCollapse;
using UnityEngine;

namespace TerrainCollapse
{
    public sealed class CharacterAnimationController : MonoBehaviour
{
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


    private Animator animator;
    private SpriteRenderer spriteRenderer;

    private TestPlayerController playerController;


    private void Awake()
    {
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        // CharacterAnimationController는 Visual 자식에 있으므로
        // 부모 TestPlayer에서 컨트롤러를 찾는다.
        playerController = GetComponentInParent<TestPlayerController>();
    }


    private void Update()
    {
        if (playerController == null)
            return;

        Vector2 velocity = playerController.Velocity;

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

        animator.SetBool(
            DeadHash,
            playerController.IsDead);

        UpdateFacingDirection(velocity.x);
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


    public void PlayMine()
    {
        animator.SetTrigger(MineHash);
    }
}
    
}
