using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>지형별 셀 Tween 관리. DOTween은 렌더링 Offset만 생성한다.</summary>
    [DisallowMultipleComponent]
    public sealed class TerrainTileHitEffect : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float shakeDuration = 0.12f;
        [Tooltip("셀 크기에 대한 진동 비율. 1 유닛 셀에서 0.04 유닛.")]
        [SerializeField, Range(0f, 0.1f)] private float shakeStrength = 0.04f;
        [SerializeField, Min(1)] private int shakeVibrato = 8;
        [SerializeField, Range(0f, 180f)] private float shakeRandomness = 20f;

        private readonly Dictionary<Vector2Int, Tween> activeShakes = new();

        public void Play(TerrainGridWorld world, Vector2Int cell)
        {
            if (isActiveAndEnabled == false || world == null || world.gameObject != gameObject ||
                world.IsOccupied(cell) == false) return;
            StopCell(cell);
            world.TrySetCellVisualOffset(cell, Vector3.zero);

            Random.State randomState = Random.state;
            try
            {
                float strength = Mathf.Clamp(shakeStrength, 0f, 0.1f) * world.CellSize;
                Tween tween = DOTween.Shake(
                        () => Vector3.zero,
                        offset => world.TrySetCellVisualOffset(cell, offset),
                        Mathf.Max(0.01f, shakeDuration),
                        new Vector3(strength, strength, 0f),
                        Mathf.Max(1, shakeVibrato),
                        Mathf.Clamp(shakeRandomness, 0f, 180f))
                    .SetTarget(this)
                    .SetUpdate(true)
                    .SetAutoKill(true)
                    .SetRecyclable(false)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                    .OnKill(() =>
                    {
                        activeShakes.Remove(cell);
                        if (world != null) world.TrySetCellVisualOffset(cell, Vector3.zero);
                    });
                activeShakes[cell] = tween;
                tween.ForceInit();
            }
            finally
            {
                Random.state = randomState;
            }
        }

        public void StopCell(Vector2Int cell)
        {
            if (activeShakes.TryGetValue(cell, out Tween tween)) tween.Kill();
        }

        public void Stop()
        {
            // OnKill에서 Dictionary를 변경하므로 활성 Tween만 복사한다.
            foreach (Tween tween in new List<Tween>(activeShakes.Values)) tween.Kill();
            activeShakes.Clear();
        }

        private void OnDisable() => Stop();
        private void OnDestroy() => Stop();
    }
}
