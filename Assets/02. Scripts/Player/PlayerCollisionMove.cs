using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    public readonly struct CollisionMoveResult
    {
        public readonly Vector2 Position;
        public readonly Vector2 Velocity;
        public readonly bool Grounded;
        public CollisionMoveResult(Vector2 position, Vector2 velocity, bool grounded)
        { Position = position; Velocity = velocity; Grounded = grounded; }
    }

    /// <summary>실제 BoxCollider2D Cast를 이용한 X/Y 축 분리 이동</summary>
    public sealed class PlayerCollisionMove
    {
        private readonly Rigidbody2D body;
        private readonly BoxCollider2D box;
        private readonly LayerMask collisionMask;
        private readonly float skin;
        private readonly float tolerance;
        private readonly float blockingNormalDotThreshold;
        private readonly List<RaycastHit2D> castHits = new List<RaycastHit2D>(16);

        public bool IsGrounded { get; private set; }
        public bool IgnoreDownwardCollisions { get; set; }
        public bool IgnoreUpwardCollisions { get; set; }
        public void ClearGrounded() => IsGrounded = false;

        public PlayerCollisionMove(Rigidbody2D body, BoxCollider2D box, LayerMask collisionMask, float skin, float tolerance, float blockingNormalDotThreshold)
        { this.body = body; this.box = box; this.collisionMask = collisionMask; this.skin = skin; this.tolerance = tolerance; this.blockingNormalDotThreshold = blockingNormalDotThreshold; }

        public CollisionMoveResult Move(Vector2 position, Vector2 velocity, float deltaTime)
        {
            bool ignoredGround = false;
            position += ResolveAxis(position, Vector2.right, velocity.x * deltaTime, ref velocity.x, ref ignoredGround);
            bool grounded = false;
            position += ResolveAxis(position, Vector2.up, velocity.y * deltaTime, ref velocity.y, ref grounded);
            IsGrounded = grounded;
            return new CollisionMoveResult(position, velocity, grounded);
        }

        public CollisionMoveResult MoveVertical(Vector2 position, Vector2 velocity, float deltaTime)
        {
            bool grounded = false;
            position += ResolveAxis(position, Vector2.up, velocity.y * deltaTime, ref velocity.y, ref grounded);
            IsGrounded = grounded;
            return new CollisionMoveResult(position, velocity, grounded);
        }

        public CollisionMoveResult MoveEscape(Vector2 position, Vector2 velocity, float targetX, float speed, float deltaTime)
        {
            bool ignoredGround = false;
            float deltaX = Mathf.MoveTowards(position.x, targetX, speed * deltaTime) - position.x;
            position += ResolveAxis(position, Vector2.right, deltaX, ref velocity.x, ref ignoredGround);
            bool grounded = false;
            position += ResolveAxis(position, Vector2.up, velocity.y * deltaTime, ref velocity.y, ref grounded);
            IsGrounded = grounded;
            return new CollisionMoveResult(position, velocity, grounded);
        }

        private Vector2 ResolveAxis(Vector2 startPosition, Vector2 axis, float distance, ref float axisVelocity, ref bool grounded)
        {
            if (Mathf.Approximately(distance, 0f)) return Vector2.zero;
            Vector2 direction = axis * Mathf.Sign(distance);
            float requestedDistance = Mathf.Abs(distance);
            if ((IgnoreDownwardCollisions && direction.y < 0f) ||
                (IgnoreUpwardCollisions && direction.y > 0f))
                return direction * requestedDistance;
            castHits.Clear();
            int hitCount = box.Cast(startPosition, body.rotation, direction, CreateFilter(), castHits, requestedDistance + skin, true);
            float allowedDistance = requestedDistance;
            bool foundBlockingHit = false;
            Vector2 blockingNormal = Vector2.zero;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = castHits[i];
                if (ShouldIgnoreCollider(hit.collider) || Vector2.Dot(hit.normal, -direction) <= blockingNormalDotThreshold) continue;
                float availableDistance = Mathf.Max(0f, hit.distance - skin);
                if (availableDistance >= allowedDistance) continue;
                allowedDistance = availableDistance;
                blockingNormal = hit.normal;
                foundBlockingHit = true;
            }
            if (foundBlockingHit && allowedDistance < requestedDistance - tolerance)
            {
                if (direction.y < 0f && blockingNormal.y > 0.5f) grounded = true;
                axisVelocity = 0f;
            }
            return direction * allowedDistance;
        }

        private ContactFilter2D CreateFilter() => new ContactFilter2D { useLayerMask = true, layerMask = collisionMask, useTriggers = false };
        private bool ShouldIgnoreCollider(Collider2D other) => other == null || other == box || Physics2D.GetIgnoreCollision(box, other);
    }
}
