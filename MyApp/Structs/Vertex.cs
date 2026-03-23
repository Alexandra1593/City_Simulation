using SharpDX;
using System.Runtime.InteropServices;

namespace ProiectSPG.Structs
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct Vertex
    {
        public Vector3 Pos;
        public Vector3 Normal;
        public Vector2 TexC;
    }
}
