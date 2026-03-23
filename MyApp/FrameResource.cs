using System;
using ProiectSPG.Structs;
using SharpDX.Direct3D12;

namespace ProiectSPG
{
    internal class FrameResource : IDisposable
    {
        public FrameResource(Device device, int passCount, int objectCount, int materialCount)
        {
            CommandAllocator = device.CreateCommandAllocator(CommandListType.Direct);

            PassCB = new UploadBuffer<PassConstants>(device, passCount, true);
            ObjectCB = new UploadBuffer<ObjectConstants>(device, objectCount, true);
            MaterialBuffer = new UploadBuffer<MaterialData>(device, materialCount, false);
        }

        // The allocator cannot be reset until the GPU is done processing the commands.
        // So each frame needs their own allocator.
        public CommandAllocator CommandAllocator { get; }

        // A cbuffer cannot be updated until the GPU is done processing the commands
        // that reference it. So each frame needs their own cbuffers.
        public UploadBuffer<PassConstants> PassCB { get; }
        public UploadBuffer<ObjectConstants> ObjectCB { get; }
        public UploadBuffer<MaterialData> MaterialBuffer { get; }

        // Fence value to mark commands up to this fence point.  This lets us
        // check if these frame resources are still in use by the GPU.
        public long Fence { get; set; }

        public void Dispose()
        {
            MaterialBuffer.Dispose();
            ObjectCB.Dispose();
            PassCB.Dispose();
            CommandAllocator.Dispose();
        }
    }
}
