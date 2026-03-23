using System.Runtime.InteropServices;

namespace ProiectSPG.Structs
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct DdsHeader
    {
        public int size;
        public int flags;
        public int height;
        public int width;
        public int pitchOrLinearSize;
        public int depth; // only if DDS_HEADER_FLAGS_VOLUME is set in flags
        public int mipMapCount;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
        public int[] reserved1;

        public DdsPixelFormat ddspf;
        public int caps;
        public int caps2;
        public int caps3;
        public int caps4;
        public int reserved2;
    }
}
