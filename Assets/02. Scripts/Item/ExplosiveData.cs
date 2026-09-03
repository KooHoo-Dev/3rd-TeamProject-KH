using UnityEngine;

namespace TerrainCollapse
{
    [CreateAssetMenu(fileName = "NewExplosiveData", menuName = "Data/Explosive", order = 0)]
    public class ExplosiveData : ScriptableObject
    {
        [Header("Flight Settings")]
        [SerializeField] private float fuseTime = 5.0f;
        [SerializeField] private int power = 3;
        [SerializeField] private float radius = 2.5f;
        [SerializeField] private float throwSpeed = 10.0f;

        // 외부에서 읽을 수 있도록 읽기 전용 프로퍼티 노출 (캡슐화 보장)
        public float FuseTime => fuseTime;
        public int Power => power;
        public float Radius => radius;
        public float ThrowSpeed => throwSpeed;
    }
}