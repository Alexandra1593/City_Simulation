using SharpDX;
using System.Runtime.InteropServices;

namespace ProiectSPG.Structs
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MaterialData
    {
        public Vector4 DiffuseAlbedo;
        public Vector3 FresnelR0;
        public float Roughness;

        // Used in texture mapping.
        public Matrix MatTransform;

        public int DiffuseMapIndex;
        public int MaterialPad0;
        public int MaterialPad1;
        public int MaterialPad2;

        public static MaterialData Default => new MaterialData
        {
            DiffuseAlbedo = Vector4.One,
            FresnelR0 = new Vector3(0.01f),
            Roughness = 64.0f,
            MatTransform = Matrix.Identity
        };
    }
}
