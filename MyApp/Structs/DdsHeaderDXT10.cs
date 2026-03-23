using SharpDX.DXGI;
using System.Runtime.InteropServices;

namespace ProiectSPG.Structs
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct DdsHeaderDXT10
    {
        public Format dxgiFormat;
        public int resourceDimension;
        public int miscFlag;
        public int arraySize;
        public int reserved;
    }
}
