using UnityEngine;

namespace TerrainCollapse
{
    [CreateAssetMenu(fileName = "NewMineData", menuName = "Data/Mine", order = 1)]
    public class MineData : ScriptableObject
    {
        [Header("Arming Settings")]
        [Tooltip("설치 직후 비활성화 시간")]
        [SerializeField] private float armDelay = 1.5f;

        [Header("Detection Settings")]
        [Tooltip("감지 반경")]
        [SerializeField] private float detectionRadius = 1.5f;
        [Tooltip("감지 판정 주기(초)")]
        [SerializeField] private float detectionInterval = 0.1f;

        [Header("Explosion Settings")]
        [SerializeField] private int power = 3;
        [SerializeField] private float explosionRadius = 2.5f;

        public float ArmDelay => armDelay;
        public float DetectionRadius => detectionRadius;
        public float DetectionInterval => detectionInterval;
        public int Power => power;
        public float ExplosionRadius => explosionRadius;
    }
}