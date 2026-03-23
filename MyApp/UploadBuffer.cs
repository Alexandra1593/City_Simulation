using System;
using System.Runtime.InteropServices;
using SharpDX.Direct3D12;

namespace ProiectSPG
{
    public class UploadBuffer<T> : IDisposable where T : struct
    {
        private readonly int elementByteSize;
        private readonly IntPtr resourcePointer;

        public UploadBuffer(Device device, int elementCount, bool isConstantBuffer)
        {
            // Constant buffer elements need to be multiples of 256 bytes.
            // This is because the hardware can only view constant data
            // at m*256 byte offsets and of n*256 byte lengths.
            // typedef struct D3D12_CONSTANT_BUFFER_VIEW_DESC {
            // UINT64 OffsetInBytes; // multiple of 256
            // UINT   SizeInBytes;   // multiple of 256
            // } D3D12_CONSTANT_BUFFER_VIEW_DESC;
            elementByteSize = isConstantBuffer
                ? D3DHelper.ComputeConstantBufferByteSize<T>()
                : Marshal.SizeOf(typeof(T));

            Resource = device.CreateCommittedResource(
                new HeapProperties(HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(elementByteSize * elementCount),
                ResourceStates.GenericRead);

            resourcePointer = Resource.Map(0);
        }

        public Resource Resource { get; }

        public void CopyData(int elementIndex, ref T data)
        {
            Marshal.StructureToPtr(data, resourcePointer + elementIndex * elementByteSize, true);
        }

        public void Dispose()
        {
            Resource.Unmap(0);
            Resource.Dispose();
        }
    }
}
