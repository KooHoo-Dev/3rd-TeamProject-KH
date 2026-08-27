namespace Character
{
    // 외부 시스템에서 공통 피해를 전달하기 위한 인터페이스

    public interface IDamageable
    {
        bool IsDead { get; }

        void TakeDamage(DamageInfo damageInfo);
    }
}
