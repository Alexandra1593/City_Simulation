using System.Runtime.InteropServices;

namespace ProiectSPG.Structs
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct DdsPixelFormat
    {
        public int size;
        public int flags;
        public int fourCC;
        public int RGBBitCount;
        public uint RBitMask;
        public uint GBitMask;
        public uint BBitMask;
        public uint ABitMask;
    };
}
