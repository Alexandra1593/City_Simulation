using DX12GameProgramming.Enums;
using ProiectSPG.Structs;
using SharpDX;
using SharpDX.Direct3D12;
using SharpDX.DXGI;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Media.Media3D;
using Resource = SharpDX.Direct3D12.Resource;
using ShaderResourceViewDimension = SharpDX.Direct3D12.ShaderResourceViewDimension;

namespace ProiectSPG
{
    public class MyApp : D3DApp
    {
        private readonly IList<FrameResource> frameResources = new List<FrameResource>(NUMBER_OF_FRAME_RESOURCES);
        private readonly IList<AutoResetEvent> fenceEvents = new List<AutoResetEvent>(NUMBER_OF_FRAME_RESOURCES);
        private int currentFrameResourceIndex;

        private DescriptorHeap srvDescriptorHeap;
        private DescriptorHeap[] descriptorHeaps;

        private RootSignature rootSignature;

        private readonly IDictionary<string, ShaderBytecode> shaders = new Dictionary<string, ShaderBytecode>();
        private readonly IDictionary<string, PipelineState> pipelineStates = new Dictionary<string, PipelineState>();
        private readonly IDictionary<string, MeshGeometry> geometries = new Dictionary<string, MeshGeometry>();
        private readonly IDictionary<string, Material> materials = new Dictionary<string, Material>();
        private readonly IDictionary<string, Texture> textures = new Dictionary<string, Texture>();
        

        private InputLayoutDescription inputLayout;

        private readonly IList<RenderItem> allRenderItems = new List<RenderItem>();

        // Render items divided by PSO.
        private readonly IDictionary<RenderLayer, IList<RenderItem>> renderItemLayers = new Dictionary<RenderLayer, IList<RenderItem>>
        {
            [RenderLayer.Opaque] = new List<RenderItem>(),
            [RenderLayer.Sky] = new List<RenderItem>(),
            [RenderLayer.Transparent] = new List<RenderItem>()
        };

        private int skyTexHeapIndex;

        private PassConstants mainPassCB = PassConstants.Default;

        private readonly Camera camera = new Camera();

        private Point lastMousePosition;

        public MyApp()
        {
            MainWindowCaption = "My App";
        }

        private FrameResource CurrentFrameResource => frameResources[currentFrameResourceIndex];
        private AutoResetEvent CurrentFenceEvent => fenceEvents[currentFrameResourceIndex];

        public override void Initialize()
        {
            base.Initialize();

            // Reset the command list to prep for initialization commands.
            CommandList.Reset(DirectCmdListAlloc, null);
            camera.Position = new Vector3(-40.0f, 25.0f, -120.0f);

            camera.LookAt(
                new Vector3(-30.0f,10.0f, -10.0f),
                new Vector3(40.0f, -10.0f, 13.0f),
                Vector3.UnitY
            );

            camera.UpdateViewMatrix();

            LoadTextures();
            CreateRootSignature();
            CreateDescriptorHeaps();
            CreateShadersAndInputLayout();
            CreateShapeGeometries();
            CreateMaterials();
            CreateRenderItems();
            CreateFrameResources();
            CreatePipelineStateObjects();

            // Execute the initialization commands.
            CommandList.Close();
            CommandQueue.ExecuteCommandList(CommandList);

            // Wait until initialization is complete.
            FlushCommandQueue();
        }

        protected override void OnResize()
        {
            base.OnResize();

            // The window resized, so update the aspect ratio and recompute the projection matrix.
            camera.SetLens(MathUtil.PiOverFour, AspectRatio, 1.0f, 1000.0f);
        }

        protected override void Update(GameTimer gameTimer)
        {
            //OnKeyboardInput(gameTimer);

            // Cycle through the circular frame resource array.
            currentFrameResourceIndex = (currentFrameResourceIndex + 1) % NUMBER_OF_FRAME_RESOURCES;

            // Has the GPU finished processing the commands of the current frame resource?
            // If not, wait until the GPU has completed commands up to this fence point.
            if (CurrentFrameResource.Fence != 0 && Fence.CompletedValue < CurrentFrameResource.Fence)
            {
                Fence.SetEventOnCompletion(CurrentFrameResource.Fence, CurrentFenceEvent.SafeWaitHandle.DangerousGetHandle());
                CurrentFenceEvent.WaitOne();
            }

            UpdateObjectCBs();
            UpdateMaterialBuffer();
            UpdateMainPassCB(gameTimer);
        }

        protected override void Draw(GameTimer gameTimer)
        {
            CommandAllocator cmdListAlloc = CurrentFrameResource.CommandAllocator;

            // Reuse the memory associated with command recording.
            cmdListAlloc.Reset();

            // A command list can be reset after it has been added to the command queue via ExecuteCommandList.
            // Reusing the command list reuses memory.
            CommandList.Reset(cmdListAlloc, pipelineStates["opaque"]);

            CommandList.SetViewport(Viewport);
            CommandList.SetScissorRectangles(ScissorRectangle);

            // Indicate a state transition on the resource usage.
            CommandList.ResourceBarrierTransition(CurrentBackBuffer, ResourceStates.Present, ResourceStates.RenderTarget);

            // Clear the back buffer and depth buffer.
            CommandList.ClearRenderTargetView(CurrentBackBufferView, Color.LightSteelBlue);
            CommandList.ClearDepthStencilView(DepthStencilView, ClearFlags.FlagsDepth | ClearFlags.FlagsStencil, 1.0f, 0);

            // Specify the buffers we are going to render to.
            CommandList.SetRenderTargets(CurrentBackBufferView, DepthStencilView);

            CommandList.SetDescriptorHeaps(descriptorHeaps.Length, descriptorHeaps);

            CommandList.SetGraphicsRootSignature(rootSignature);

            Resource passCB = CurrentFrameResource.PassCB.Resource;
            CommandList.SetGraphicsRootConstantBufferView(1, passCB.GPUVirtualAddress);

            // Bind all the materials used in this scene. For structured buffers, we can bypass the heap and
            // set as a root descriptor.
            Resource matBuffer = CurrentFrameResource.MaterialBuffer.Resource;
            CommandList.SetGraphicsRootShaderResourceView(2, matBuffer.GPUVirtualAddress);

            GpuDescriptorHandle skyTexDescriptor = srvDescriptorHeap.GPUDescriptorHandleForHeapStart;
            skyTexDescriptor += skyTexHeapIndex * CbvSrvUavDescriptorSize;
            CommandList.SetGraphicsRootDescriptorTable(3, skyTexDescriptor);

            // Bind all the textures used in this scene. 
            CommandList.SetGraphicsRootDescriptorTable(4, srvDescriptorHeap.GPUDescriptorHandleForHeapStart);

            CommandList.PipelineState = pipelineStates["opaque"];
            DrawRenderItems(CommandList, renderItemLayers[RenderLayer.Opaque]);

            CommandList.PipelineState = pipelineStates["transparent"];
            DrawRenderItems(CommandList, renderItemLayers[RenderLayer.Transparent]);

            CommandList.PipelineState = pipelineStates["sky"];
            DrawRenderItems(CommandList, renderItemLayers[RenderLayer.Sky]);

            // Indicate a state transition on the resource usage.
            CommandList.ResourceBarrierTransition(CurrentBackBuffer, ResourceStates.RenderTarget, ResourceStates.Present);

            // Done recording commands.
            CommandList.Close();

            // Add the command list to the queue for execution.
            CommandQueue.ExecuteCommandList(CommandList);

            // Present the buffer to the screen. Presenting will automatically swap the back and front buffers.
            SwapChain.Present(0, PresentFlags.None);

            // Advance the fence value to mark commands up to this fence point.
            CurrentFrameResource.Fence = ++CurrentFence;

            // Add an instruction to the command queue to set a new fence point.
            // Because we are on the GPU timeline, the new fence point won't be
            // set until the GPU finishes processing all the commands prior to this Signal().
            CommandQueue.Signal(Fence, CurrentFence);
        }

        protected override void OnMouseDown(MouseButtons button, Point location)
        {
            base.OnMouseDown(button, location);
            lastMousePosition = location;
        }

       
        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                rootSignature.Dispose();
                srvDescriptorHeap?.Dispose();
                foreach (Texture texture in textures.Values)
                {
                    texture.Dispose();
                }
                foreach (FrameResource frameResource in frameResources)
                {
                    frameResource.Dispose();
                }
                foreach (MeshGeometry geometry in geometries.Values)
                {
                    geometry.Dispose();
                }
                foreach (PipelineState pipelineState in pipelineStates.Values)
                {
                    pipelineState.Dispose();
                }
            }
            base.Dispose(isDisposing);
        }

        private void UpdateObjectCBs()
        {
            foreach (RenderItem renderItem in allRenderItems)
            {
                // Only update the cbuffer data if the constants have changed.
                // This needs to be tracked per frame resource.
                if (renderItem.NumFramesDirty > 0)
                {
                    var objConstants = new ObjectConstants
                    {
                        World = Matrix.Transpose(renderItem.World),
                        TexTransform = Matrix.Transpose(renderItem.TexTransform),
                        MaterialIndex = renderItem.Material.MaterialConstantBufferIndex
                    };
                    CurrentFrameResource.ObjectCB.CopyData(renderItem.ObjCBIndex, ref objConstants);

                    // Next FrameResource need to be updated too.
                    renderItem.NumFramesDirty--;
                }
            }
        }

        private void UpdateMaterialBuffer()
        {
            UploadBuffer<MaterialData> currentMaterialCB = CurrentFrameResource.MaterialBuffer;
            foreach (Material material in materials.Values)
            {
                // Only update the cbuffer data if the constants have changed. If the cbuffer
                // data changes, it needs to be updated for each FrameResource.
                if (material.NumberOfFramesDirty > 0)
                {
                    var materialConstants = new MaterialData
                    {
                        DiffuseAlbedo = material.DiffuseAlbedo,
                        FresnelR0 = material.FresnelR0,
                        Roughness = material.Roughness,
                        MatTransform = Matrix.Transpose(material.MatTransform),
                        DiffuseMapIndex = material.DiffuseSrvHeapIndex
                    };

                    currentMaterialCB.CopyData(material.MaterialConstantBufferIndex, ref materialConstants);

                    // Next FrameResource need to be updated too.
                    material.NumberOfFramesDirty--;
                }
            }
        }

        private void UpdateMainPassCB(GameTimer gameTimer)
        {
            Matrix view = camera.View;
            Matrix proj = camera.Proj;

            Matrix viewProj = view * proj;
            Matrix invView = Matrix.Invert(view);
            Matrix invProj = Matrix.Invert(proj);
            Matrix invViewProj = Matrix.Invert(viewProj);

            mainPassCB.View = Matrix.Transpose(view);
            mainPassCB.InvView = Matrix.Transpose(invView);
            mainPassCB.Proj = Matrix.Transpose(proj);
            mainPassCB.InvProj = Matrix.Transpose(invProj);
            mainPassCB.ViewProj = Matrix.Transpose(viewProj);
            mainPassCB.InvViewProj = Matrix.Transpose(invViewProj);
            mainPassCB.EyePosW = camera.Position;
            mainPassCB.RenderTargetSize = new Vector2(WindowWidth, WindowHeight);
            mainPassCB.InvRenderTargetSize = 1.0f / mainPassCB.RenderTargetSize;
            mainPassCB.NearZ = 1.0f;
            mainPassCB.FarZ = 1000.0f;
            mainPassCB.TotalTime = gameTimer.TotalTime;
            mainPassCB.DeltaTime = gameTimer.DeltaTime;
            mainPassCB.AmbientLight = new Vector4(0.25f, 0.25f, 0.35f, 1.0f); 
            mainPassCB.Lights[0].Direction = new Vector3(0.57735f, -0.57735f, 0.57735f);
            mainPassCB.Lights[0].Strength = new Vector3(0.6f);
            mainPassCB.Lights[1].Direction = new Vector3(-0.57735f, -0.57735f, 0.57735f);
            mainPassCB.Lights[1].Strength = new Vector3(0.3f);
            mainPassCB.Lights[2].Direction = new Vector3(0.0f, -0.707f, -0.707f);
            mainPassCB.Lights[2].Strength = new Vector3(0.15f);

            CurrentFrameResource.PassCB.CopyData(0, ref mainPassCB);
        }

        private void LoadTextures()
        {
            AddTexture("house1", "house1.dds");
            AddTexture("house2", "house2.dds");
            AddTexture("house3", "house3.dds");
            AddTexture("house4", "house4.dds");
            AddTexture("house5", "house5.dds");
            AddTexture("tree1", "treearray.dds");
            AddTexture("tree2", "treeArray2.dds");
            AddTexture("tree3", "tree02S.dds");
            AddTexture("street", "street1.dds");
            AddTexture("grass", "grass.dds");
            AddTexture("pavement", "pavement.dds");
            AddTexture("roof", "roof.dds");
            AddTexture("skyCubeMap", "nightSky.dds");
            AddTexture("terrain", "rocks.dds");
            AddTexture("water", "water.dds");
        }

        private void AddTexture(string name, string fileName)
        {
            var texture = new Texture
            {
                Name = name,
                FileName = $"Textures\\{fileName}"
            };
            texture.Resource = TextureUtilities.CreateTextureFromDDS(Device, texture.FileName);
            textures[texture.Name] = texture;
        }

        private void CreateRootSignature()
        {
            var slotRootParameters = new[]
            {
                new RootParameter(ShaderVisibility.All, new RootDescriptor(0, 0), RootParameterType.ConstantBufferView),
                new RootParameter(ShaderVisibility.All, new RootDescriptor(1, 0), RootParameterType.ConstantBufferView),
                new RootParameter(ShaderVisibility.All, new RootDescriptor(0, 1), RootParameterType.ShaderResourceView),
                new RootParameter(ShaderVisibility.All, new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 0)),
                new RootParameter(ShaderVisibility.All, new DescriptorRange(DescriptorRangeType.ShaderResourceView, 5, 1))
            };

            // Create the root signature, which is an array of root parameters.
            var rootSignatureDescription = new RootSignatureDescription(
                RootSignatureFlags.AllowInputAssemblerInputLayout,
                slotRootParameters,
                GetStaticSamplers());

            rootSignature = Device.CreateRootSignature(rootSignatureDescription.Serialize());
        }

        private void CreateDescriptorHeaps()
        {
            // Create the SRV heap.
            var srvHeapDescription = new DescriptorHeapDescription
            {
                DescriptorCount = 20,
                Type = DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
                Flags = DescriptorHeapFlags.ShaderVisible
            };
            srvDescriptorHeap = Device.CreateDescriptorHeap(srvHeapDescription);
            descriptorHeaps = new[] { srvDescriptorHeap };

            // Fill out the heap with the descriptors.
            CpuDescriptorHandle cpuDescriptor = srvDescriptorHeap.CPUDescriptorHandleForHeapStart;

            Resource[] textures2D =
            {
                textures["house1"].Resource,
                textures["house2"].Resource,
                textures["house3"].Resource,
                textures["house4"].Resource,
                textures["house5"].Resource,
                textures["tree1"].Resource,
                textures["tree2"].Resource,
                textures["tree3"].Resource,
                textures["street"].Resource,
                textures["grass"].Resource,
                textures["pavement"].Resource,
                textures["roof"].Resource,
                textures["terrain"].Resource,
                textures["water"].Resource
            };
            Resource skyTexture = textures["skyCubeMap"].Resource;

            var shaderResourceViewDescription = new ShaderResourceViewDescription
            {
                Shader4ComponentMapping = D3DHelper.DefaultShader4ComponentMapping,
                Dimension = ShaderResourceViewDimension.Texture2D,
                Texture2D = new ShaderResourceViewDescription.Texture2DResource
                {
                    MostDetailedMip = 0,
                    ResourceMinLODClamp = 0.0f
                }
            };

            foreach (Resource texture2D in textures2D)
            {
                shaderResourceViewDescription.Format = texture2D.Description.Format;
                shaderResourceViewDescription.Texture2D.MipLevels = texture2D.Description.MipLevels;
                Device.CreateShaderResourceView(texture2D, shaderResourceViewDescription, cpuDescriptor);

                // Next descriptor.
                cpuDescriptor += CbvSrvUavDescriptorSize;
            }

            shaderResourceViewDescription.Dimension = ShaderResourceViewDimension.TextureCube;
            shaderResourceViewDescription.TextureCube = new ShaderResourceViewDescription.TextureCubeResource
            {
                MostDetailedMip = 0,
                MipLevels = skyTexture.Description.MipLevels,
                ResourceMinLODClamp = 0.0f
            };
            shaderResourceViewDescription.Format = skyTexture.Description.Format;
            Device.CreateShaderResourceView(skyTexture, shaderResourceViewDescription, cpuDescriptor);

            skyTexHeapIndex = 14;
        }

        private void CreateShadersAndInputLayout()
        {
            shaders["standardVS"] = D3DHelper.CompileShader("Shaders\\Default.hlsl", "VS", "vs_5_1");
            shaders["opaquePS"] = D3DHelper.CompileShader("Shaders\\Default.hlsl", "PS", "ps_5_1");

            shaders["skyVS"] = D3DHelper.CompileShader("Shaders\\Sky.hlsl", "VS", "vs_5_1");
            shaders["skyPS"] = D3DHelper.CompileShader("Shaders\\Sky.hlsl", "PS", "ps_5_1");

            inputLayout = new InputLayoutDescription(new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElement("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
                new InputElement("TEXCOORD", 0, Format.R32G32_Float, 24, 0)
            });
        }

        private void CreateShapeGeometries()
        {
            // Concatenate all the geometries into one big vertex/index buffer. 
            // Define the regions in the buffer each submesh covers.

            var vertices = new List<Vertex>();
            var indices = new List<short>();

            SubmeshGeometry box = AppendMeshData(GeometryGenerator.CreateBox(3.0f, 8f, 3.0f, 3), vertices, indices);
            SubmeshGeometry grid = AppendMeshData(GeometryGenerator.CreateGrid(7.0f, 250.0f, 60, 40), vertices, indices); 
            SubmeshGeometry sphere = AppendMeshData(GeometryGenerator.CreateSphere(0.5f, 20, 20), vertices, indices);
            SubmeshGeometry rectangle = AppendMeshData(GeometryGenerator.CreateRectangle(3.0f, 3.0f, 0), vertices, indices);
            SubmeshGeometry grassGrid = AppendMeshData(GeometryGenerator.CreateBox(3.0f, 0f, 3.0f, 3), vertices, indices);
            SubmeshGeometry pavementGrid = AppendMeshData(GeometryGenerator.CreateGrid(6.0f, 250.0f, 10, 10), vertices, indices);
            SubmeshGeometry roofBox = AppendMeshData(GeometryGenerator.CreateBox(3.0f, 0.5f, 3.0f, 3), vertices, indices);
            SubmeshGeometry terrainGrid = AppendMeshData(GeometryGenerator.CreateGrid(100.0f, 100.0f, 100, 100), vertices, indices);
            SubmeshGeometry riverGrid = AppendMeshData(GeometryGenerator.CreateGrid(10.0f, 250.0f, 20, 80), vertices, indices);
           
            var geo = MeshGeometry.New(Device, CommandList, vertices, indices.ToArray(), "shapeGeo");

            geo.DrawArguments["box"] = box;
            geo.DrawArguments["grid"] = grid;
            geo.DrawArguments["sphere"] = sphere;
            geo.DrawArguments["rectangle"] = rectangle;
            geo.DrawArguments["grassGrid"] = grassGrid;
            geo.DrawArguments["pavementGrid"] = pavementGrid;
            geo.DrawArguments["roofBox"] = roofBox;
            geo.DrawArguments["terrainGrid"] = terrainGrid;
            geo.DrawArguments["riverGrid"] = riverGrid;

            geometries[geo.Name] = geo;
        }

        private SubmeshGeometry AppendMeshData(GeometryGenerator.MeshData meshData, List<Vertex> vertices, List<short> indices)
        {
            // Define the SubmeshGeometry that cover different
            // regions of the vertex/index buffers.
            var submesh = new SubmeshGeometry
            {
                IndexCount = meshData.Indices32.Count,
                StartIndexLocation = indices.Count,
                BaseVertexLocation = vertices.Count
            };

            // Extract the vertex elements we are interested in and pack the
            // vertices and indices of all the meshes into one vertex/index buffer.
            vertices.AddRange(meshData.Vertices.Select(vertex => new Vertex
            {
                Pos = vertex.Position,
                Normal = vertex.Normal,
                TexC = vertex.TexC
            }));
            indices.AddRange(meshData.GetIndices16());

            return submesh;
        }

        private void CreatePipelineStateObjects()
        {
            // Pipeline State for opaque objects.
            var opaquePipelineStateDescription = new GraphicsPipelineStateDescription
            {
                InputLayout = inputLayout,
                RootSignature = rootSignature,
                VertexShader = shaders["standardVS"],
                PixelShader = shaders["opaquePS"],
                RasterizerState = RasterizerStateDescription.Default(),
                BlendState = BlendStateDescription.Default(),
                DepthStencilState = DepthStencilStateDescription.Default(),
                SampleMask = unchecked((int)uint.MaxValue),
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                RenderTargetCount = 1,
                SampleDescription = new SampleDescription(MsaaCount, MsaaQuality),
                DepthStencilFormat = DepthStencilFormat
            };
            opaquePipelineStateDescription.RenderTargetFormats[0] = BackBufferFormat;
            pipelineStates["opaque"] = Device.CreateGraphicsPipelineState(opaquePipelineStateDescription);

            // Pipeline State for transparent objects.
            GraphicsPipelineStateDescription transparentPipelineStateDescription = opaquePipelineStateDescription.Copy();

            var transparencyBlendDescription = new RenderTargetBlendDescription
            {
                IsBlendEnabled = true,
                LogicOpEnable = false,
                SourceBlend = BlendOption.SourceAlpha,
                DestinationBlend = BlendOption.InverseSourceAlpha,
                BlendOperation = BlendOperation.Add,
                SourceAlphaBlend = BlendOption.One,
                DestinationAlphaBlend = BlendOption.Zero,
                AlphaBlendOperation = BlendOperation.Add,
                LogicOp = LogicOperation.Noop,
                RenderTargetWriteMask = ColorWriteMaskFlags.All
            };
            transparentPipelineStateDescription.BlendState.RenderTarget[0] = transparencyBlendDescription;

            pipelineStates["transparent"] = Device.CreateGraphicsPipelineState(transparentPipelineStateDescription);

            // Pipeline State for sky.
            GraphicsPipelineStateDescription skyPipelineStateDescription = opaquePipelineStateDescription.Copy();
            skyPipelineStateDescription.RasterizerState.CullMode = CullMode.None;
            skyPipelineStateDescription.DepthStencilState.DepthComparison = Comparison.LessEqual;
            skyPipelineStateDescription.RootSignature = rootSignature;
            skyPipelineStateDescription.VertexShader = shaders["skyVS"];
            skyPipelineStateDescription.PixelShader = shaders["skyPS"];
            pipelineStates["sky"] = Device.CreateGraphicsPipelineState(skyPipelineStateDescription);
        }

        private void CreateFrameResources()
        {
            for (int i = 0; i < NUMBER_OF_FRAME_RESOURCES; i++)
            {
                frameResources.Add(new FrameResource(Device, 1, allRenderItems.Count, materials.Count));
                fenceEvents.Add(new AutoResetEvent(false));
            }
        }

        private void CreateMaterials()
        {
            AddMaterial(new Material
            {
                Name = "house1Material",
                MaterialConstantBufferIndex = 0,
                DiffuseSrvHeapIndex = 0,
                DiffuseAlbedo = Vector4.One,
                FresnelR0 = new Vector3(0.01f),
                Roughness = 0.5f
            });
            AddMaterial(new Material
            {
                Name = "house2Material",
                MaterialConstantBufferIndex = 1,
                DiffuseSrvHeapIndex = 1,
                DiffuseAlbedo = Vector4.One,
                FresnelR0 = new Vector3(0.01f),
                Roughness = 0.5f
            });
            AddMaterial(new Material
            {
                Name = "house3Material",
                MaterialConstantBufferIndex = 2,
                DiffuseSrvHeapIndex = 2,
                DiffuseAlbedo = Vector4.One,
                FresnelR0 = new Vector3(0.01f),
                Roughness = 0.5f
            });
            AddMaterial(new Material
            {
                Name = "house4Material",
                MaterialConstantBufferIndex = 3,
                DiffuseSrvHeapIndex = 3,
                DiffuseAlbedo = Vector4.One,
                FresnelR0 = new Vector3(0.01f),
                Roughness = 0.5f
            });
            AddMaterial(new Material
            {
                Name = "house5Material",
                MaterialConstantBufferIndex = 4,
                DiffuseSrvHeapIndex = 4,
                DiffuseAlbedo = Vector4.One,
                FresnelR0 = new Vector3(0.01f),
                Roughness = 0.5f
            });
            AddMaterial(new Material
            {
                Name = "tree1Material",
                MaterialConstantBufferIndex = 5,
                DiffuseSrvHeapIndex = 5,
                DiffuseAlbedo = Vector4.One,
                FresnelR0 = new Vector3(0.01f),
                Roughness = 0.5f
            });
            AddMaterial(new Material
            {
                Name = "tree2Material",
                MaterialConstantBufferIndex = 6,
                DiffuseSrvHeapIndex = 6,
                DiffuseAlbedo = Vector4.One,
                FresnelR0 = new Vector3(0.01f),
                Roughness = 0.5f
            });
            AddMaterial(new Material
            {
                Name = "tree3Material",
                MaterialConstantBufferIndex = 7,
                DiffuseSrvHeapIndex = 7,
                DiffuseAlbedo = Vector4.One,
                FresnelR0 = new Vector3(0.01f),
                Roughness = 0.5f
            });
            AddMaterial(new Material
            {
                Name = "streetMaterial",
                MaterialConstantBufferIndex = 8,
                DiffuseSrvHeapIndex = 8,
                DiffuseAlbedo = new Vector4(1.0f, 1.0f, 1.0f, 1.0f),
                FresnelR0 = new Vector3(0.01f),
                Roughness = 1.0f
            });
            AddMaterial(new Material
            {
                Name = "grassMaterial",
                MaterialConstantBufferIndex = 9,
                DiffuseSrvHeapIndex = 9,
                DiffuseAlbedo = new Vector4(1.0f, 1.0f, 1.0f, 1.0f),
                FresnelR0 = new Vector3(0.01f),
                Roughness = 1.0f
            });
            AddMaterial(new Material
            {
                Name = "pavementMaterial",
                MaterialConstantBufferIndex = 10,
                DiffuseSrvHeapIndex = 10,
                DiffuseAlbedo = new Vector4(1.0f, 1.0f, 1.0f, 1.0f),
                FresnelR0 = new Vector3(0.01f),
                Roughness = 1.0f
            });
            AddMaterial(new Material
            {
                Name = "roofMaterial",
                MaterialConstantBufferIndex = 11,
                DiffuseSrvHeapIndex = 11,
                DiffuseAlbedo = new Vector4(1.0f, 1.0f, 1.0f, 1.0f),
                FresnelR0 = new Vector3(0.01f),
                Roughness = 1.0f
            });
            AddMaterial(new Material
            {
                Name = "sky",
                MaterialConstantBufferIndex = 12,
                DiffuseSrvHeapIndex = 14,
                DiffuseAlbedo = Vector4.One,
                FresnelR0 = new Vector3(0.1f),
                Roughness = 1.0f
            });
            AddMaterial(new Material
            {
                Name = "terrainMaterial",
                MaterialConstantBufferIndex = 13,
                DiffuseSrvHeapIndex = 12,
                DiffuseAlbedo = new Vector4(1, 1, 1, 1),
                FresnelR0 = new Vector3(0.02f),
                Roughness = 1.0f
            });
            AddMaterial(new Material
            {
                Name = "waterMaterial",
                MaterialConstantBufferIndex = 14,
                DiffuseSrvHeapIndex = 13,
                DiffuseAlbedo = new Vector4(1, 1, 1, 0.8f),
                FresnelR0 = new Vector3(0.1f),
                Roughness = 0.0f
            });
        }

        private void AddMaterial(Material material)
        {
            materials[material.Name] = material;
        }

        private void CreateRenderItems()
        {

            int objectCBIndex = 0;

            AddRenderItem(
            RenderLayer.Opaque,
            objectCBIndex++,
            "terrainMaterial",
            "shapeGeo",
            "terrainGrid",
            world: Matrix.Translation(13.0f, -0.2f, 0.0f),
            textureTransform: Matrix.Scaling(12.0f, 12.0f, 1.0f)
            );

            AddRenderItem(RenderLayer.Sky, objectCBIndex++, "sky", "shapeGeo", "sphere", world: Matrix.Scaling(5000.0f));
            objectCBIndex = CreateHouses(objectCBIndex);
            
            objectCBIndex = CreateRoofs(objectCBIndex);
            objectCBIndex = CreateTrees(objectCBIndex);
            objectCBIndex = CreateStreets(objectCBIndex);
            objectCBIndex = CreatePavement(objectCBIndex);
            CreateGrassAroundTrees(objectCBIndex);
           objectCBIndex = CreateRiver(objectCBIndex);
        }

        private int CreateStreets(int objectCBIndex)
        {
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "streetMaterial", "shapeGeo", "grid",
                textureTransform: Matrix.Scaling(1.0f, 20.0f, 2.0f));
            //AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "streetMaterial", "shapeGeo", "grid",
            //    textureTransform: Matrix.Scaling(1.0f, 20.0f, 2.0f), world: Matrix.Translation(13.0f, 0.0f, 0));
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "streetMaterial", "shapeGeo", "grid",
                textureTransform: Matrix.Scaling(1.0f, 20.0f, 2.0f), world: Matrix.Translation(26.0f, 0.0f, 0));

            return objectCBIndex;
        }

        private int CreatePavement(int objectCBIndex)
        {
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "pavementMaterial", "shapeGeo", "pavementGrid",
                textureTransform: Matrix.Scaling(1.0f, 100.0f, 2.0f), world: Matrix.Translation(-6.5f, 0.0f, 0));
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "pavementMaterial", "shapeGeo", "pavementGrid",
                textureTransform: Matrix.Scaling(1.0f, 100.0f, 2.0f), world: Matrix.Translation(+6.5f, 0.0f, 0));
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "pavementMaterial", "shapeGeo", "pavementGrid",
                textureTransform: Matrix.Scaling(1.0f, 100.0f, 2.0f), world: Matrix.Translation(19.5f, 0.0f, 0));
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "pavementMaterial", "shapeGeo", "pavementGrid",
                textureTransform: Matrix.Scaling(1.0f, 100.0f, 2.0f), world: Matrix.Translation(32.5f, 0.0f, 0));
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "pavementMaterial", "shapeGeo", "pavementGrid",
                textureTransform: Matrix.Scaling(1.0f, 100.0f, 2.0f), world: Matrix.Translation(-12.5f, 0.0f, 0));
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "pavementMaterial", "shapeGeo", "pavementGrid",
                textureTransform: Matrix.Scaling(1.0f, 100.0f, 2.0f), world: Matrix.Translation(38.5f, 0.0f, 0));

            return objectCBIndex;
        }

        private int CreateHouses(int objectCBIndex)
        {
            for (int i = 0; i < 6; ++i)
            {
                //there are 4 rows with buildings
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "house4Material", "shapeGeo", "box",
                  world: Matrix.Translation(-7.0f, 4.0f, -60.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "house1Material", "shapeGeo", "box",
                    world: Matrix.Translation(+7.0f, 4.0f, -60.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "house2Material", "shapeGeo", "box",
                   world: Matrix.Translation(+19.0f, 4.0f, -60.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "house4Material", "shapeGeo", "box",
                  world: Matrix.Translation(+32.5f, 4.0f, -60.0f + i * 40.0f));
 }

            return objectCBIndex;
        }

        private int CreateRoofs(int objectCBIndex)
        {
            for (int i = 0; i < 5; ++i)
            {
                // there are 4 rows with buildings
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "roofMaterial", "shapeGeo", "roofBox",
                  world: Matrix.Translation(-7.0f, 8.2f, -60.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "roofMaterial", "shapeGeo", "roofBox",
                    world: Matrix.Translation(+7.0f, 8.2f, -60.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "roofMaterial", "shapeGeo", "roofBox",
                   world: Matrix.Translation(+19.0f, 8.2f, -60.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "roofMaterial", "shapeGeo", "roofBox",
                  world: Matrix.Translation(+32.5f, 8.2f, -60.0f + i * 40.0f));


              
            }

            return objectCBIndex;
        }

        private int CreateTrees(int objectCBIndex)
        {
            for (int i = 0; i < 5; ++i)
            {
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree1Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(-7.0f, 1.5f, -87.9f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree1Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(+7.0f, 1.5f, -87.9f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree1Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(+19.0f, 1.5f, -87.9f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree1Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(+32.5f, 1.5f, -87.9f + i * 40.0f));

                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(-7.0f, 1.5f, -80.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(+7.0f, 1.5f, -80.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(+19.0f, 1.5f, -80.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                   world: Matrix.Translation(+32.5f, 1.5f, -80.0f + i * 40.0f));

                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree3Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(-7.0f, 1.5f, -72.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree3Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(+7.0f, 1.5f, -72.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree3Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(+19.0f, 1.5f, -72.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree3Material", "shapeGeo", "rectangle",
                   world: Matrix.Translation(+32.5f, 1.5f, -72.0f + i * 40.0f));

                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree3Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(-7.0f, 1.5f, -64.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree3Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(+7.0f, 1.5f, -64.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree3Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(+19.0f, 1.5f, -64.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree3Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(+32.5f, 1.5f, -64.0f + i * 40.0f));

                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(-7.0f, 1.5f, -55.8f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(+7.0f, 1.5f, -55.8f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(+19.0f, 1.5f, -55.8f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(+32.5f, 1.5f, -55.8f + i * 40.0f));
            }

            for(int i = 0; i < 70; i++)
            {
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(-12.5f, 1.5f, -95.8f + i * 3.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(-12.5f, 1.5f, -95.8f + i * 3.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(-12.5f, 1.5f, -95.8f + i * 3.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(-12.5f, 1.5f, -95.8f + i * 3.0f));

                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                   world: Matrix.Translation(38.5f, 1.5f, -95.8f + i * 3.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(38.5f, 1.5f, -95.8f + i * 3.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(38.5f, 1.5f, -95.8f + i * 3.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(38.5f, 1.5f, -95.8f + i * 3.0f));
            }
            return objectCBIndex;
        }

        private int CreateGrassAroundTrees(int objectCBIndex)
        {
            for (int i = 0; i < 5; ++i)
            {
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(-7.0f, 0.1f, -87.9f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+7.0f, 0.1f, -87.9f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+19.0f, 0.1f, -87.9f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+32.5f, 0.1f, -87.9f + i * 40.0f));

                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(-7.0f, 0.1f, -80.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+7.0f, 0.1f, -80.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+19.0f, 0.1f, -80.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+32.5f, 0.1f, -80.0f + i * 40.0f));

                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(-7.0f, 0.1f, -72.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+7.0f, 0.1f, -72.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+19.0f, 0.1f, -72.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+32.5f, 0.1f, -72.0f + i * 40.0f));

                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(-7.0f, 0.1f, -64.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+7.0f, 0.1f, -64.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+19.0f, 0.1f, -64.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+32.5f, 0.1f, -64.0f + i * 40.0f));

                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(-7.0f, 0.1f, -55.8f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+7.0f, 0.1f, -55.8f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+19.0f, 0.1f, -55.8f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+32.5f, 0.1f, -55.8f + i * 40.0f));
            }

            for(int i = 0; i < 70; i++)
            {
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(-12.5f, 0.1f, -95.8f + i * 3.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(-12.5f, 0.1f, -95.8f + i * 3.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(-12.5f, 0.1f, -95.8f + i * 3.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(-12.5f, 0.1f, -95.8f + i * 3.0f));

                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                   world: Matrix.Translation(38.5f, 0.1f, -95.8f + i * 3.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(38.5f, 0.1f, -95.8f + i * 3.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(38.5f, 0.1f, -95.8f + i * 3.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(38.5f, 0.1f, -95.8f + i * 3.0f));
            }

            return objectCBIndex;
        }



        private int CreateRiver(int objectCBIndex)
        {
            AddRenderItem(
                RenderLayer.Opaque,
                objectCBIndex++,
                "waterMaterial",
                "shapeGeo",
                "riverGrid",
                world: Matrix.Translation(13.0f, -0.15f, 0.0f),
                textureTransform: Matrix.Scaling(3.0f, 25.0f, 1.0f));

            return objectCBIndex;
        }
        private void AddRenderItem(RenderLayer layer, int objCBIndex, string materialName, string geometryName, string submeshName,
            Matrix? world = null, Matrix? textureTransform = null)
        {
            MeshGeometry meshGeometry = geometries[geometryName];
            SubmeshGeometry submesh = meshGeometry.DrawArguments[submeshName];
            var renderItem = new RenderItem
            {
                ObjCBIndex = objCBIndex,
                Material = materials[materialName],
                MeshGeometry = meshGeometry,
                IndexCount = submesh.IndexCount,
                StartIndexLocation = submesh.StartIndexLocation,
                BaseVertexLocation = submesh.BaseVertexLocation,
                World = world ?? Matrix.Identity,
                TexTransform = textureTransform ?? Matrix.Identity
            };
            renderItemLayers[layer].Add(renderItem);
            allRenderItems.Add(renderItem);
        }

        private void DrawRenderItems(GraphicsCommandList cmdList, IList<RenderItem> renderItems)
        {
            int objCBByteSize = D3DHelper.ComputeConstantBufferByteSize<ObjectConstants>();

            Resource objectCB = CurrentFrameResource.ObjectCB.Resource;

            foreach (RenderItem renderItem in renderItems)
            {
                cmdList.SetVertexBuffer(0, renderItem.MeshGeometry.VertexBufferView);
                cmdList.SetIndexBuffer(renderItem.MeshGeometry.IndexBufferView);
                cmdList.PrimitiveTopology = renderItem.PrimitiveType;

                long objCBAddress = objectCB.GPUVirtualAddress + renderItem.ObjCBIndex * objCBByteSize;

                cmdList.SetGraphicsRootConstantBufferView(0, objCBAddress);

                cmdList.DrawIndexedInstanced(renderItem.IndexCount, 1, renderItem.StartIndexLocation, renderItem.BaseVertexLocation, 0);
            }
        }

        private static StaticSamplerDescription[] GetStaticSamplers() => new[]
        {
            // PointWrap
            new StaticSamplerDescription(ShaderVisibility.All, 0, 0)
            {
                Filter = Filter.MinMagMipPoint,
                AddressUVW = TextureAddressMode.Wrap
            },
            // PointClamp
            new StaticSamplerDescription(ShaderVisibility.All, 1, 0)
            {
                Filter = Filter.MinMagMipPoint,
                AddressUVW = TextureAddressMode.Clamp
            },
            // LinearWrap
            new StaticSamplerDescription(ShaderVisibility.All, 2, 0)
            {
                Filter = Filter.MinMagMipLinear,
                AddressUVW = TextureAddressMode.Wrap
            },
            // LinearClamp
            new StaticSamplerDescription(ShaderVisibility.All, 3, 0)
            {
                Filter = Filter.MinMagMipLinear,
                AddressUVW = TextureAddressMode.Clamp
            },
            // AnisotropicWrap
            new StaticSamplerDescription(ShaderVisibility.All, 4, 0)
            {
                Filter = Filter.Anisotropic,
                AddressUVW = TextureAddressMode.Wrap,
                MipLODBias = 0.0f,
                MaxAnisotropy = 8
            },
            // AnisotropicClamp
            new StaticSamplerDescription(ShaderVisibility.All, 5, 0)
            {
                Filter = Filter.Anisotropic,
                AddressUVW = TextureAddressMode.Clamp,
                MipLODBias = 0.0f,
                MaxAnisotropy = 8
            }
        };
    }


    }






