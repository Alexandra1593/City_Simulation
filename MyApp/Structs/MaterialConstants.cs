using SharpDX;
using System.Runtime.InteropServices;

namespace ProiectSPG.Structs
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MaterialConstants
    {
        public Vector4 DiffuseAlbedo;
        public Vector3 FresnelR0;
        public float Roughness;

        // Used in texture mapping.
        public Matrix MatTransform;

        public static MaterialConstants Default => new MaterialConstants
        {
            DiffuseAlbedo = Vector4.One,
            FresnelR0 = new Vector3(0.01f),
            Roughness = 0.25f,
            MatTransform = Matrix.Identity
        };
    };
}
