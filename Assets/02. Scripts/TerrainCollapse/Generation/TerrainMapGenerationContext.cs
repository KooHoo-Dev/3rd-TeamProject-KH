namespace TerrainCollapse
{
    // 동일한 맵 시드에서 모든 지형 생성 규칙이 공유하는 난수 컨텍스트

    public sealed class TerrainMapGenerationContext
    {
        public int Seed { get; }
        public System.Random Random { get; }

        public TerrainMapGenerationContext(int seed)
        {
            Seed = seed;
            Random = new System.Random(seed);
        }
    }
}
