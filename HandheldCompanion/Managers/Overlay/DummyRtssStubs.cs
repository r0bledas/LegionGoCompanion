using System;

namespace RTSSSharedMemoryNET
{
    public class AppEntry
    {
        public int ProcessId { get; set; }
        public string? Name { get; set; }
    }

    public class OSD : IDisposable
    {
        public OSD(string? name = null) { }
        public void Update(string content) { }
        public void Dispose() { }
        public uint EmbedGraphDirect(
            uint offset,
            uint processId,
            uint historySize,
            int width,
            int height,
            int margin,
            float minMs,
            float maxMs,
            EMBEDDED_OBJECT_GRAPH flags,
            out float minFt,
            out float avgFt,
            out float maxFt)
        {
            minFt = 0f;
            avgFt = 0f;
            maxFt = 0f;
            return 0;
        }
    }

    public enum EMBEDDED_OBJECT_GRAPH : uint
    {
        None = 0
    }
}
