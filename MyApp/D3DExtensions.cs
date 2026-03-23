using SharpDX.Direct3D12;

namespace ProiectSPG
{
    /// <summary>
    /// Class used to add a copy constructor to GraphicsPipelineStateDescription. Copy() is an extension method.
    /// </summary>
    public static class D3DExtensions
    {
        public static GraphicsPipelineStateDescription Copy(this GraphicsPipelineStateDescription pipelineStateDescriptor)
        {
            var newPipelineStateDescription = new GraphicsPipelineStateDescription
            {
                BlendState = pipelineStateDescriptor.BlendState,
                CachedPSO = pipelineStateDescriptor.CachedPSO,
                DepthStencilFormat = pipelineStateDescriptor.DepthStencilFormat,
                DepthStencilState = pipelineStateDescriptor.DepthStencilState,
                SampleDescription = pipelineStateDescriptor.SampleDescription,
                DomainShader = pipelineStateDescriptor.DomainShader,
                Flags = pipelineStateDescriptor.Flags,
                GeometryShader = pipelineStateDescriptor.GeometryShader,
                HullShader = pipelineStateDescriptor.HullShader,
                IBStripCutValue = pipelineStateDescriptor.IBStripCutValue,
                InputLayout = pipelineStateDescriptor.InputLayout,
                NodeMask = pipelineStateDescriptor.NodeMask,
                PixelShader = pipelineStateDescriptor.PixelShader,
                PrimitiveTopologyType = pipelineStateDescriptor.PrimitiveTopologyType,
                RasterizerState = pipelineStateDescriptor.RasterizerState,
                RenderTargetCount = pipelineStateDescriptor.RenderTargetCount,
                SampleMask = pipelineStateDescriptor.SampleMask,
                StreamOutput = pipelineStateDescriptor.StreamOutput,
                VertexShader = pipelineStateDescriptor.VertexShader,
                RootSignature = pipelineStateDescriptor.RootSignature
            };
            for (int i = 0; i < pipelineStateDescriptor.RenderTargetFormats.Length; i++)
            {
                newPipelineStateDescription.RenderTargetFormats[i] = pipelineStateDescriptor.RenderTargetFormats[i];
            }
            return newPipelineStateDescription;
        }
    }
}
