using SharpDX;
using SharpDX.Direct3D;

namespace ProiectSPG
{
    // Lightweight structure stores parameters to draw a shape. 
    internal class RenderItem
    {
        // World matrix of the shape that describes the object's local space
        // relative to the world space, which defines the position, orientation,
        // and scale of the object in the world.
        public Matrix World { get; set; } = Matrix.Identity;

        public Matrix TexTransform { get; set; } = Matrix.Identity;

        // Dirty flag indicates that the object data has changed and the constant buffer needs to be updated.
        // Because for each FrameResource there is an object cbuffer, each FrameResource needs to be updated.
        // Thus, when an object data gets modified, NumFramesDirty is set to gNumFrameResources so that each frame resource gets updated.
        public int NumFramesDirty { get; set; } = D3DApp.NUMBER_OF_FRAME_RESOURCES;

        // Index into GPU constant buffer corresponding to the ObjectCB for this render item.
        public int ObjCBIndex { get; set; } = -1;

        public Material Material { get; set; }
        public MeshGeometry MeshGeometry { get; set; }

        // Primitive topology.
        public PrimitiveTopology PrimitiveType { get; set; } = PrimitiveTopology.TriangleList;

        // DrawIndexedInstanced parameters.
        public int IndexCount { get; set; }
        public int StartIndexLocation { get; set; }
        public int BaseVertexLocation { get; set; }
    }
}
