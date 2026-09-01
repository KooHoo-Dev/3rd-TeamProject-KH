using System;

namespace TerrainCollapse.Networking
{
    // 서버가 확정한 맵 생성 규칙과 데이터 버전 식별 정보

    [Serializable]
    public sealed class MapSessionDescriptor
    {
        public string MapSessionID { get; set; }
        public string ProfileID { get; set; }
        public int Seed { get; set; }
        public string TerrainDataVersion { get; set; }

        public bool IsValid()
        {
            return string.IsNullOrWhiteSpace(MapSessionID) == false &&
                   string.IsNullOrWhiteSpace(ProfileID) == false &&
                   string.IsNullOrWhiteSpace(TerrainDataVersion) == false;
        }
    }

    // 서버가 방 참가자에게 확정된 맵 세션을 전달하는 메시지

    [Serializable]
    public sealed class MapSessionMessage
    {
        public string Type { get; set; } = NetworkMessageTypes.MapSession;
        public MapSessionDescriptor Session { get; set; }
    }
}
