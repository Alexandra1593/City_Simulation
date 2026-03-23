using SharpDX;
using System.Runtime.InteropServices;

namespace ProiectSPG.Structs
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct ObjectConstants
    {
        public Matrix World;
        public Matrix TexTransform;
        public int MaterialIndex;
        public int ObjPad0;
        public int ObjPad1;
        public int ObjPad2;

        public static ObjectConstants Default => new ObjectConstants
        {
            World = Matrix.Identity,
            TexTransform = Matrix.Identity
        };
    }
}
