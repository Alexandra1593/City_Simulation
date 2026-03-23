using SharpDX.Direct3D12;
using System;

namespace ProiectSPG
{
    public class Texture : IDisposable
    {
        public string Name { get; set; }
        public string FileName { get; set; }
        public Resource Resource { get; set; }
        public Resource UploadHeap { get; set; }

        public void Dispose()
        {
            Resource?.Dispose();
            UploadHeap?.Dispose();
        }
    }
}
