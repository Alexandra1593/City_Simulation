using System;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D12;
using Device = SharpDX.Direct3D12.Device;
using Resource = SharpDX.Direct3D12.Resource;
using ShaderBytecode = SharpDX.Direct3D12.ShaderBytecode;

namespace ProiectSPG
{
    public static class D3DHelper
    {
        public const int DefaultShader4ComponentMapping = 5768;

        public static Resource CreateDefaultBuffer<T>(
            Device device,
            GraphicsCommandList cmdList,
            T[] initData,
            long byteSize,
            out Resource uploadBuffer) where T : struct
        {
            // Create the actual default buffer resource.
            Resource defaultBuffer = device.CreateCommittedResource(
                new HeapProperties(HeapType.Default),
                HeapFlags.None,
                ResourceDescription.Buffer(byteSize),
                ResourceStates.Common);

            // In order to copy CPU memory data into the default buffer, create an intermediate upload heap.
            uploadBuffer = device.CreateCommittedResource(
                new HeapProperties(HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(byteSize),
                ResourceStates.GenericRead);

            // Copy the data to the upload buffer.
            IntPtr pointer = uploadBuffer.Map(0);
            Utilities.Write(pointer, initData, 0, initData.Length);
            uploadBuffer.Unmap(0);

            // Schedule to copy the data to the default buffer resource.
            cmdList.ResourceBarrierTransition(defaultBuffer, ResourceStates.Common, ResourceStates.CopyDestination);
            cmdList.CopyResource(defaultBuffer, uploadBuffer);
            cmdList.ResourceBarrierTransition(defaultBuffer, ResourceStates.CopyDestination, ResourceStates.GenericRead);

            // Note: uploadBuffer has to be kept alive after the above function calls because
            // the command list has not been executed yet that performs the actual copy.
            // The caller can Release the uploadBuffer after it knows the copy has been executed.

            return defaultBuffer;
        }

        // Constant buffers must be a multiple of the minimum hardware
        // allocation size (usually 256 bytes). So round up to nearest
        // multiple of 256. This is done by adding 255 and then masking off
        // the lower 2 bytes which store all bits < 256.
        public static int ComputeConstantBufferByteSize<T>() where T : struct => (Marshal.SizeOf(typeof(T)) + 255) & ~255;

        public static ShaderBytecode CompileShader(string fileName, string entryPoint, string profile, ShaderMacro[] defines = null)
        {
            var shaderFlags = ShaderFlags.None;
#if DEBUG
            shaderFlags |= ShaderFlags.Debug | ShaderFlags.SkipOptimization;
#endif
            CompilationResult result = SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile(
                fileName,
                entryPoint,
                profile,
                shaderFlags,
                include: FileIncludeHandler.Default,
                defines: defines);
            return new ShaderBytecode(result);
        }
    }
}