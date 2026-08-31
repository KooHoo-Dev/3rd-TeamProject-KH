using System;
using System.Collections.Generic;

namespace TerrainCollapse
{
    // 서버 전송 및 중간 참여 복원에 사용하는 탐험 상태 데이터

    [Serializable]
    public sealed class ExplorationSnapshot
    {
        public int MapWidth;
        public int MapHeight;
        public List<int> ExploredCellIndices = new();
    }
}
