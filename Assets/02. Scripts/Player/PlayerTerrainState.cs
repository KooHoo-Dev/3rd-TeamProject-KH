using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>매몰, 위치 잠금, 탈출, 등반 상태의 모순 없는 단일 소유자</summary>
    public sealed class PlayerTerrainState
    {
        public bool IsBuried { get; private set; }
        public bool IsInteractionLocked { get; private set; }
        public bool IsEscaping { get; private set; }
        public bool IsClimbing { get; private set; }
        public Vector2 LockedPosition { get; private set; }
        public float EscapeTargetX { get; private set; }
        public float EscapeSpeed { get; private set; }

        public void SetBuried(bool buried, Vector2 position)
        {
            IsBuried = buried;
            if (!buried) return;
            LockedPosition = position;
            IsInteractionLocked = false;
            StopEscape();
            StopClimbing();
        }

        public void SetInteractionLocked(bool locked, Vector2 position)
        {
            IsInteractionLocked = locked;
            if (!locked) return;
            LockedPosition = position;
            StopEscape();
            StopClimbing();
        }

        public void StartEscape(float targetX, float speed)
        {
            if (IsBuried || IsInteractionLocked) return;
            EscapeTargetX = targetX;
            EscapeSpeed = speed;
            IsEscaping = true;
        }

        public void StopEscape() => IsEscaping = false;
        public void StartClimbing() => IsClimbing = true;
        public void StopClimbing() => IsClimbing = false;

        public void ResetState()
        {
            IsBuried = false;
            IsInteractionLocked = false;
            IsEscaping = false;
            IsClimbing = false;
            LockedPosition = Vector2.zero;
            EscapeTargetX = 0f;
            EscapeSpeed = 0f;
        }
    }
}
