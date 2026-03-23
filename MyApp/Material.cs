using SharpDX;

namespace ProiectSPG
{
    public class Material
    {
        public string Name { get; set; }

        // Index into constant buffer corresponding to this material.
        public int MaterialConstantBufferIndex { get; set; } = -1;

        // Index into SRV heap for diffuse texture.
        public int DiffuseSrvHeapIndex { get; set; } = -1;

        // Index into SRV heap for normal texture.
        public int NormalSrvHeapIndex { get; set; } = -1;

        // Dirty flag indicating the material has changed and the constant buffer needs to be updated.
        // Because we have a material constant buffer for each FrameResource, we have to apply the
        // update to each FrameResource. Thus, when we modify a material we should set
        // NumberOfFramesDirty = D3DApp.NUMBER_OF_FRAME_RESOURCES so that each frame resource gets the update.
        public int NumberOfFramesDirty { get; set; } = D3DApp.NUMBER_OF_FRAME_RESOURCES;


        // Material constant buffer data used for shading.
        public Vector4 DiffuseAlbedo { get; set; } = Vector4.One;
        public Vector3 FresnelR0 { get; set; } = new Vector3(0.01f);
        public float Roughness { get; set; } = 0.25f;
        public Matrix MatTransform { get; set; } = Matrix.Identity;
    }
}
