using UnityEngine;

namespace TerrainCollapse
{
    // 광물 군집과 동굴 등 독립적인 절차 생성 단계를 추가하기 위한 확장 규칙

    public abstract class TerrainMapGenerationRule : ScriptableObject
    {
        public abstract void Apply(TerrainMapFile map, TerrainMapGenerationContext context);
    }
}
