using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SharpDX;
using SharpDX.Direct3D12;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D12.Device;
using Resource = SharpDX.Direct3D12.Resource;

namespace ProiectSPG
{
    public class MeshGeometry : IDisposable
    {
        private readonly List<IDisposable> toBeDisposed = new List<IDisposable>();

        private MeshGeometry() { }

        public string Name { get; set; }

        public Resource VertexBufferGPU { get; set; }
        public Resource IndexBufferGPU { get; set; }

        public object VertexBufferCPU { get; set; }
        public object IndexBufferCPU { get; set; }

        // Data about buffers.
        public int VertexByteStride { get; set; }
        public int VertexBufferByteSize { get; set; }
        public Format IndexFormat { get; set; }
        public int IndexBufferByteSize { get; set; }
        public int IndexCount { get; set; }

        // A MeshGeometry may store multiple geometries in one vertex/index buffer.
        // This container is used to define the Submesh geometries to draw the Submeshes individually.
        public Dictionary<string, SubmeshGeometry> DrawArguments { get; } = new Dictionary<string, SubmeshGeometry>();

        public VertexBufferView VertexBufferView => new VertexBufferView
        {
            BufferLocation = VertexBufferGPU.GPUVirtualAddress,
            StrideInBytes = VertexByteStride,
            SizeInBytes = VertexBufferByteSize
        };

        public IndexBufferView IndexBufferView => new IndexBufferView
        {
            BufferLocation = IndexBufferGPU.GPUVirtualAddress,
            Format = IndexFormat,
            SizeInBytes = IndexBufferByteSize
        };

        public void Dispose()
        {
            foreach (IDisposable disposable in toBeDisposed)
            {
                disposable.Dispose();
            }
        }

        public static MeshGeometry New<TVertex, TIndex>(
            Device device,
            GraphicsCommandList commandList,
            IEnumerable<TVertex> vertices,
            IEnumerable<TIndex> indices,
            string name = "Default")
            where TVertex : struct
            where TIndex : struct
        {
            TVertex[] vertexArray = vertices.ToArray();
            TIndex[] indexArray = indices.ToArray();

            int vertexBufferByteSize = Utilities.SizeOf(vertexArray);
            Resource vertexBuffer = D3DHelper.CreateDefaultBuffer(
                device,
                commandList,
                vertexArray,
                vertexBufferByteSize,
                out Resource vertexBufferUploader);

            int indexBufferByteSize = Utilities.SizeOf(indexArray);
            Resource indexBuffer = D3DHelper.CreateDefaultBuffer(
                device, commandList,
                indexArray,
                indexBufferByteSize,
                out Resource indexBufferUploader);

            return new MeshGeometry
            {
                Name = name,
                VertexByteStride = Utilities.SizeOf<TVertex>(),
                VertexBufferByteSize = vertexBufferByteSize,
                VertexBufferGPU = vertexBuffer,
                VertexBufferCPU = vertexArray,
                IndexCount = indexArray.Length,
                IndexFormat = GetIndexFormat<TIndex>(),
                IndexBufferByteSize = indexBufferByteSize,
                IndexBufferGPU = indexBuffer,
                IndexBufferCPU = indexArray,
                toBeDisposed =
                {
                    vertexBuffer, vertexBufferUploader,
                    indexBuffer, indexBufferUploader
                }
            };
        }

        private static Format GetIndexFormat<TIndex>()
        {
            var format = Format.Unknown;
            if (typeof(TIndex) == typeof(int))
            {
                format = Format.R32_UInt;
            }
            else if (typeof(TIndex) == typeof(short))
            {
                format = Format.R16_UInt;
            }

            Debug.Assert(format != Format.Unknown);

            return format;
        }
    }
}
