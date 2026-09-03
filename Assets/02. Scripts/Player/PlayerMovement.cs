using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>물리 API에 의존하지 않는 플레이어 속도 계산</summary>
    public sealed class PlayerMovement
    {
        private readonly float moveSpeed;
        private readonly float climbSpeed;
        private readonly float jumpSpeed;
        private readonly float gravity;

        public Vector2 Velocity { get; private set; }

        public PlayerMovement(float moveSpeed, float climbSpeed, float jumpSpeed, float gravity)
        {
            this.moveSpeed = moveSpeed;
            this.climbSpeed = climbSpeed;
            this.jumpSpeed = jumpSpeed;
            this.gravity = gravity;
        }

        public void UpdateVelocity(PlayerInputData input, bool isClimbing, bool isGrounded, float deltaTime)
        {
            if (isClimbing)
            {
                Velocity = new Vector2(input.MoveX * climbSpeed, input.MoveY * climbSpeed);
                return;
            }

            Velocity = new Vector2(input.MoveX * moveSpeed, Velocity.y);
            if (input.JumpPressed && isGrounded) Velocity = new Vector2(Velocity.x, jumpSpeed);
            Velocity = new Vector2(Velocity.x, Velocity.y - gravity * deltaTime);
        }

        public void ApplyGravity(float deltaTime) => Velocity = new Vector2(Velocity.x, Velocity.y - gravity * deltaTime);
        public void SetVelocity(Vector2 velocity) => Velocity = velocity;
        public void Stop() => Velocity = Vector2.zero;
        public void StopHorizontal() => Velocity = new Vector2(0f, Velocity.y);
    }
}
