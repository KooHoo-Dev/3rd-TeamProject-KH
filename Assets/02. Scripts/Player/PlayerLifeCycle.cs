using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>사망/리스폰 상태와 죽은 뒤 낙하 여부를 소유</summary>
    public sealed class PlayerLifeCycle
    {
        public bool IsDead { get; private set; }
        public bool IsDeadFalling { get; private set; }

        public void EnterDead(bool isPositionLocked, bool wasBuried)
        {
            if (IsDead) return;
            IsDead = true;
            IsDeadFalling = !isPositionLocked && !wasBuried;
        }

        public void StopDeadFall() => IsDeadFalling = false;
        public void Respawn() { IsDead = false; IsDeadFalling = false; }
    }
}
