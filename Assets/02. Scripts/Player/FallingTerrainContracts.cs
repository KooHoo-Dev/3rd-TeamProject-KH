using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>낙하 지형과 캐릭터 접촉 처리 결과</summary>
    public enum FallingTerrainContactResult
    {
        Ignored,
        Escaped,
        NoEscapeSpace
    }

    /// <summary>낙하 지형 접촉 시 탈출 또는 매몰 여부를 결정하는 대상</summary>
    public interface IFallingTerrainEscapeTarget
    {
        // 실제 타일 형상을 확인하기 위해 FallingChunk 객체 자체를 전달
        FallingTerrainContactResult ResolveFallingTerrainContact(
            FallingChunk fallingChunk,
            float impactSpeed);
    }

    /// <summary>FallingTerrainInteraction이 PlayerController 이동 상태를 제어하기 위한 대상</summary>
    public interface IFallingTerrainCharacter
    {
        Bounds CollisionBounds { get; }
        Vector2 Position { get; }
        LayerMask CollisionMask { get; }
        float CollisionSkin { get; }

        void CancelClimbing();
        void ApplyFallingVelocity(Vector2 velocity);
        void StartTerrainEscape(float targetX, float speed);
        void StopTerrainEscape();
        void SetTerrainInteractionLocked(bool isLocked, Vector2 lockedPosition);
        void SetTerrainBuried(bool isBuried, Vector2 lockedPosition);
        void SetPosition(Vector2 position);
    }

    /// <summary>기존 TestPlayerController와 새 PlayerController가 공통으로 제공하는 게임플레이 상태/수명 계약.</summary>
    public interface IPlayerController : IFallingTerrainCharacter
    {
        bool IsBuried { get; }
        bool IsClimbing { get; }
        bool IsDead { get; }
        bool IsGrounded { get; }
        bool IsInVillage { get; }
        Vector2 Velocity { get; }
        Vector2 CurrentPosition { get; }
        TerrainManager TerrainManager { get; }
        IItemInventory Inventory { get; }
        void EnterDeadState(bool isPositionLocked = false);
        void Respawn(Vector2 spawnPosition);
    }

    /// <summary>낙하 지형 최초 충돌 피해 계산에 필요한 붕괴 식별자와 타일 정보</summary>
    public readonly struct FallingTerrainImpactInfo
    {
        public readonly long CollapseId;
        public readonly int TileCount;
        public readonly GameObject Source;

        public FallingTerrainImpactInfo(
            long collapseId,
            int tileCount,
            GameObject source)
        {
            CollapseId = collapseId;
            TileCount = tileCount;
            Source = source;
        }
    }
}
