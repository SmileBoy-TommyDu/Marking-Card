

namespace DrSoft.Drawing.DTO
{
   
    public class CanvasStorageDocumentDto
    {
     
        public CanvasSnapshotDto CanvasSnapshot { get; set; } = new();


        public Dictionary<int, byte[]> LayerPayloads { get; set; } = new();


        public Dictionary<string, byte[]> ExtensionPayloads { get; set; } = new();
    }
}
