using System;

namespace TerrainCollapse.Networking
{
    [Serializable]
    public sealed class ErrorMessage
    {
        public string Type { get; set; }
        public string RequestId { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
    }
}
