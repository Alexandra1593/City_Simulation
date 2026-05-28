using DX12GameProgramming.Enums;
using ProiectSPG.Structs;
using SharpDX;
using SharpDX.Direct3D;
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
        private readonly List<Vector3> streetLampLightPositions = new List<Vector3>();




        private Resource shadowMap = null;
        private DescriptorHeap dsvHeapShadow = null;
        private CpuDescriptorHandle shadowDsv;
        private GpuDescriptorHandle shadowSrv;
        private CpuDescriptorHandle shadowSrvCpu;

        private ViewportF shadowViewport;
        private Rectangle shadowScissorRect;

        private Matrix lightView = Matrix.Identity;
        private Matrix lightProj = Matrix.Identity;
        private Matrix shadowTransform = Matrix.Identity;




        private InputLayoutDescription inputLayout;

        private readonly IList<RenderItem> allRenderItems = new List<RenderItem>();

        // Render items divided by PSO.
        private readonly IDictionary<RenderLayer, IList<RenderItem>> renderItemLayers = new Dictionary<RenderLayer, IList<RenderItem>>
        {
            [RenderLayer.Opaque] = new List<RenderItem>(),
            [RenderLayer.Sky] = new List<RenderItem>(),
            [RenderLayer.Transparent] = new List<RenderItem>(),
           // [RenderLayer.Moon] = new List<RenderItem>()
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
            CreateShadowMap();
            CreateDescriptorHeaps();
            CreateShadersAndInputLayout();
            CreateShapeGeometries();
            CreateMaterials();
            CreateRenderItems();
            CreateFrameResources();
            CreatePipelineStateObjects();
            UpdateShadowTransform();

            // Execute the initialization commands.
            CommandList.Close();
            CommandQueue.ExecuteCommandList(CommandList);

            // Wait until initialization is complete.
            FlushCommandQueue();
        }

        protected override void OnResize()
        {
            base.OnResize();

            // The window resized, so update the a
            //
            //
            //
            // t ratio and recompute the projection matrix.
            camera.SetLens(MathUtil.PiOverFour, AspectRatio, 1.0f, 1000.0f);
        }

        protected override void Update(GameTimer gameTimer)
        {
            OnKeyboardInput(gameTimer);

            // Cycle through the circular frame resource array.
            currentFrameResourceIndex = (currentFrameResourceIndex + 1) % NUMBER_OF_FRAME_RESOURCES;

            // Has the GPU finished processing the commands of the current frame resource?
            // If not, wait until the GPU has completed commands up to this fence point.
            if (CurrentFrameResource.Fence != 0 && Fence.CompletedValue < CurrentFrameResource.Fence)
            {
                Fence.SetEventOnCompletion(CurrentFrameResource.Fence, CurrentFenceEvent.SafeWaitHandle.DangerousGetHandle());
                CurrentFenceEvent.WaitOne();
            }


            AnimateWaterMaterial(gameTimer);
            UpdateObjectCBs();
            UpdateMaterialBuffer();
            UpdateMainPassCB(gameTimer);
        }

        protected override void Draw(GameTimer gameTimer)
        {



            CommandAllocator cmdListAlloc = CurrentFrameResource.CommandAllocator;

            cmdListAlloc.Reset();

            CommandList.Reset(cmdListAlloc, pipelineStates["opaque"]);


        

            // 1) first render scene into the shadow map
            DrawSceneToShadowMap();

            // 2) now switch back to the normal screen viewport
            CommandList.SetViewport(Viewport);
            CommandList.SetScissorRectangles(ScissorRectangle);

            // 3) now render the normal scene to the back buffer
            //CommandAllocator cmdListAlloc = CurrentFrameResource.CommandAllocator;

            //// Reuse the memory associated with command recording.
            //cmdListAlloc.Reset();

            //// A command list can be reset after it has been added to the command queue via ExecuteCommandList.
            //// Reusing the command list reuses memory.
            //CommandList.Reset(cmdListAlloc, pipelineStates["opaque"]);

            //CommandList.SetViewport(Viewport);
            //CommandList.SetScissorRectangles(ScissorRectangle);

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

            //CommandList.PipelineState = pipelineStates["moon"];
            //DrawRenderItems(CommandList, renderItemLayers[RenderLayer.Moon]);

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
            //mainPassCB.AmbientLight = new Vector4(0.25f, 0.25f, 0.35f, 1.0f); 
            //mainPassCB.Lights[0].Direction = new Vector3(0.57735f, -0.57735f, 0.57735f);
            //mainPassCB.Lights[0].Strength = new Vector3(0.6f);
            //mainPassCB.Lights[1].Direction = new Vector3(-0.57735f, -0.57735f, 0.57735f);
            //mainPassCB.Lights[1].Strength = new Vector3(0.3f);
            //mainPassCB.Lights[2].Direction = new Vector3(0.0f, -0.707f, -0.707f);
            //mainPassCB.Lights[2].Strength = new Vector3(0.15f);

            Vector3 cityCenter = new Vector3(13.0f, 0.0f, 0.0f);
            Vector3 moonPosition = new Vector3(-300.0f, 200.0f, -400.0f);

            Vector3 moonDir = cityCenter - moonPosition;
            moonDir.Normalize();
            mainPassCB.AmbientLight = new Vector4(0.25f, 0.25f, 0.25f, 1.0f);

            mainPassCB.Lights[0].Direction = Vector3.Normalize(new Vector3(1.0f, -0.35f, 0.4f));
            mainPassCB.Lights[0].Strength = new Vector3(0.9f, 0.9f, 1.0f);


            mainPassCB.Lights[1].Strength = new Vector3(0.05f, 0.05f, 0.05f);
            mainPassCB.Lights[2].Strength = new Vector3(0.03f, 0.03f, 0.05f);

            UpdateShadowTransform();


            // clear remaining lights
            for (int i = 3; i < mainPassCB.Lights.Length; i++)
            {
                mainPassCB.Lights[i].Strength = Vector3.Zero;
                mainPassCB.Lights[i].Direction = Vector3.Zero;
                mainPassCB.Lights[i].Position = Vector3.Zero;
                mainPassCB.Lights[i].FalloffStart = 1.0f;
                mainPassCB.Lights[i].FalloffEnd = 10.0f;
                mainPassCB.Lights[i].SpotPower = 1.0f;
            }



            int lampCount = System.Math.Min(streetLampLightPositions.Count, mainPassCB.Lights.Length - 3);

            for (int i = 0; i < lampCount; i++)
            {
                int lightIndex = 3 + i;
                Vector3 lampPos = streetLampLightPositions[i];

                mainPassCB.Lights[lightIndex].Position = lampPos;
                mainPassCB.Lights[lightIndex].Strength = new Vector3(1.0f, 1.0f, 1.25f);
                //mainPassCB.Lights[lightIndex].Strength = Vector3.Zero;
                mainPassCB.Lights[lightIndex].FalloffEnd = 8.0f;

                mainPassCB.Lights[lightIndex].FalloffStart = 1.0f;
                //mainPassCB.Lights[lightIndex].FalloffEnd = 18.0f;

                mainPassCB.Lights[lightIndex].Direction = Vector3.Zero;
                mainPassCB.Lights[lightIndex].SpotPower = 0.0f;
            }



            mainPassCB.LightView = Matrix.Transpose(lightView);
            mainPassCB.LightProj = Matrix.Transpose(lightProj);
            mainPassCB.ShadowTransform = Matrix.Transpose(shadowTransform);

            CurrentFrameResource.PassCB.CopyData(0, ref mainPassCB);
            }






        private void LoadTextures()
        {
            AddTexture("house1", "images.dds"); //0
            AddTexture("house2", "house2.dds"); //1
            AddTexture("house3", "house3.dds"); //2
            AddTexture("house4", "house4.dds"); //3
            AddTexture("house5", "house5.dds"); //4
            AddTexture("tree1", "roses.dds");//5
            AddTexture("tree2", "tree2.dds");//6
            AddTexture("tree3", "tree.dds"); //7
            AddTexture("street", "street1.dds");//8
            AddTexture("grass", "grass.dds");//9
            AddTexture("pavement", "pavement.dds");//10
            AddTexture("roof", "roof.dds");//11
            AddTexture("skyCubeMap", "nightSky.dds");//12
            AddTexture("terrain", "rocks.dds");//13
            AddTexture("water", "water.dds");//14
            AddTexture("riverWalls", "riverWalls.dds");//15
            AddTexture("wood", "wood.dds");//16
            AddTexture("moon", "moon.dds"); //17

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
            //var slotRootParameters = new[]
            //{
            //    new RootParameter(ShaderVisibility.All, new RootDescriptor(0, 0), RootParameterType.ConstantBufferView),
            //    new RootParameter(ShaderVisibility.All, new RootDescriptor(1, 0), RootParameterType.ConstantBufferView),
            //    new RootParameter(ShaderVisibility.All, new RootDescriptor(0, 1), RootParameterType.ShaderResourceView),
            //    new RootParameter(ShaderVisibility.All, new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 0)),
            //    new RootParameter(ShaderVisibility.All, new DescriptorRange(DescriptorRangeType.ShaderResourceView, 5, 1))
            //};
            var slotRootParameters = new[]
{
    new RootParameter(ShaderVisibility.All, new RootDescriptor(0, 0), RootParameterType.ConstantBufferView), // b0
    new RootParameter(ShaderVisibility.All, new RootDescriptor(1, 0), RootParameterType.ConstantBufferView), // b1
    new RootParameter(ShaderVisibility.All, new RootDescriptor(0, 1), RootParameterType.ShaderResourceView), // t0 space1
    new RootParameter(ShaderVisibility.All, new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 0)),  // t0
    new RootParameter(ShaderVisibility.All, new DescriptorRange(DescriptorRangeType.ShaderResourceView, 21, 1))  // t1..t20
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
                DescriptorCount = 22,
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
                textures["water"].Resource,
                textures["riverWalls"].Resource,
                textures["wood"].Resource,
                textures["moon"].Resource
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

            skyTexHeapIndex = 17;







            var shadowSrvDesc = new ShaderResourceViewDescription
            {
                Shader4ComponentMapping = D3DHelper.DefaultShader4ComponentMapping,
                Format = Format.R24_UNorm_X8_Typeless,
                Dimension = ShaderResourceViewDimension.Texture2D,
                Texture2D = new ShaderResourceViewDescription.Texture2DResource
                {
                    MostDetailedMip = 0,
                    MipLevels = 1,
                    ResourceMinLODClamp = 0.0f
                }
            };

            CpuDescriptorHandle shadowSrvCpuHandle = srvDescriptorHeap.CPUDescriptorHandleForHeapStart;
            shadowSrvCpuHandle += 20 * CbvSrvUavDescriptorSize; // choose free slot
            Device.CreateShaderResourceView(shadowMap, shadowSrvDesc, shadowSrvCpuHandle);
        }

        private void CreateShadersAndInputLayout()
        {
            shaders["standardVS"] = D3DHelper.CompileShader("Shaders\\Default.hlsl", "VS", "vs_5_1");
            shaders["opaquePS"] = D3DHelper.CompileShader("Shaders\\Default.hlsl", "PS", "ps_5_1");

            shaders["skyVS"] = D3DHelper.CompileShader("Shaders\\Sky.hlsl", "VS", "vs_5_1");
            shaders["skyPS"] = D3DHelper.CompileShader("Shaders\\Sky.hlsl", "PS", "ps_5_1");

            //shaders["moonVS"] = D3DHelper.CompileShader("Shaders\\Moon.hlsl", "VS", "vs_5_1");
            //shaders["moonPS"] = D3DHelper.CompileShader("Shaders\\Moon.hlsl", "PS", "ps_5_1");

            shaders["shadowVS"] = D3DHelper.CompileShader("Shaders\\Shadow.hlsl", "VS", "vs_5_1");
            //shaders["shadowPS"] = D3DHelper.CompileShader("Shaders\\Shadow.hlsl", "PS", "ps_5_1");


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
            SubmeshGeometry terrainGrid = AppendMeshData(GeometryGenerator.CreateGrid(200.0f, 250.0f, 100, 100), vertices, indices);
            SubmeshGeometry riverGrid = AppendMeshData(GeometryGenerator.CreateGrid(10.0f, 250.0f, 20, 80), vertices, indices);
            SubmeshGeometry houseBox = AppendMeshData(GeometryGenerator.CreateBox(3.0f, 8.0f, 5.0f, 3), vertices, indices);
            SubmeshGeometry smallStreetGrid = AppendMeshData(GeometryGenerator.CreateGrid(22.0f, 5.5f, 4, 2), vertices, indices);

            SubmeshGeometry carBody = AppendMeshData(GeometryGenerator.CreateBox(1.6f, 0.7f, 6.4f, 0), vertices, indices);
            SubmeshGeometry carCabin = AppendMeshData(GeometryGenerator.CreateBox(0.6f, 0.7f, 2.5f, 0), vertices, indices);
            SubmeshGeometry wheel = AppendMeshData(GeometryGenerator.CreateCylinder(0.35f, 0.35f, 0.25f, 20, 20), vertices, indices);
            SubmeshGeometry headlight = AppendMeshData(GeometryGenerator.CreateSphere(0.12f, 16, 16), vertices, indices);



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
            geo.DrawArguments["houseBox"] = houseBox;
            geo.DrawArguments["smallStreetGrid"] = smallStreetGrid;
            geo.DrawArguments["carBody"] = carBody;
            geo.DrawArguments["carCabin"] = carCabin;
            geo.DrawArguments["wheel"] = wheel;
            geo.DrawArguments["headlight"] = headlight; 

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



            //// Pipeline State for shadow.
            GraphicsPipelineStateDescription shadowPsoDesc = new GraphicsPipelineStateDescription
            {
                InputLayout = inputLayout,
                RootSignature = rootSignature,
                VertexShader = shaders["shadowVS"],
                PixelShader = default(ShaderBytecode), // no PS
                RasterizerState = RasterizerStateDescription.Default(),
                BlendState = BlendStateDescription.Default(),
                DepthStencilState = DepthStencilStateDescription.Default(),
                SampleMask = unchecked((int)uint.MaxValue),
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                RenderTargetCount = 0,
                SampleDescription = new SampleDescription(1, 0),
                DepthStencilFormat = Format.D24_UNorm_S8_UInt
            };

            shadowPsoDesc.RasterizerState.DepthBias = 0;
            shadowPsoDesc.RasterizerState.DepthBiasClamp = 0.0f;
            shadowPsoDesc.RasterizerState.SlopeScaledDepthBias = 1.0f;

            pipelineStates["shadow"] = Device.CreateGraphicsPipelineState(shadowPsoDesc);

            //// Pipeline State for moon.
            //GraphicsPipelineStateDescription moonPipelineStateDescription = opaquePipelineStateDescription.Copy();
            //moonPipelineStateDescription.VertexShader = shaders["moonVS"];
            //moonPipelineStateDescription.PixelShader = shaders["moonPS"];
            //pipelineStates["moon"] = Device.CreateGraphicsPipelineState(moonPipelineStateDescription);
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
                DiffuseSrvHeapIndex = 18,
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
            AddMaterial(new Material
            {
                Name = "riverWallsMaterial",
                MaterialConstantBufferIndex = 15,
                DiffuseSrvHeapIndex = 14,
                DiffuseAlbedo = new Vector4(1, 1, 1, 1),
                FresnelR0 = new Vector3(0.02f),
                Roughness = 1.0f
            });
            AddMaterial(new Material
            {
                Name = "woodMaterial",
                MaterialConstantBufferIndex = 16,
                DiffuseSrvHeapIndex = 15,
                DiffuseAlbedo = new Vector4(1, 1, 1, 1),
                FresnelR0 = new Vector3(0.02f),
                Roughness = 1.0f
            });
            AddMaterial(new Material
            {
                Name = "lampPoleMaterial",
                MaterialConstantBufferIndex = 17,
                DiffuseSrvHeapIndex = 10, 
                DiffuseAlbedo = new Vector4(0.25f, 0.25f, 0.25f, 1.0f),
                FresnelR0 = new Vector3(0.03f),
                Roughness = 0.6f
            });

            AddMaterial(new Material
            {
                Name = "lampLightMaterial",
                MaterialConstantBufferIndex = 18,
                DiffuseSrvHeapIndex = 10,
                DiffuseAlbedo = new Vector4(6.0f, 5.5f, 4.0f, 1.0f),
                Roughness = 0.1f,
                FresnelR0 = new Vector3(0.2f),
            });
            AddMaterial(new Material
            {
                Name = "moonMaterial",
                MaterialConstantBufferIndex = 19,
                DiffuseSrvHeapIndex = 17,
                DiffuseAlbedo = new Vector4(3.0f, 2.9f, 2.5f, 1.0f),
                FresnelR0 = new Vector3(0.02f),
                Roughness = 0.05f
            });
         




            AddMaterial(new Material
            {
                Name = "carBodyMaterial",
                MaterialConstantBufferIndex = 20,
                DiffuseSrvHeapIndex = 10,
                DiffuseAlbedo = new Vector4(0.85f, 0.10f, 0.10f, 1.0f),
                FresnelR0 = new Vector3(0.04f),
                Roughness = 0.25f
            });

            AddMaterial(new Material
            {
                Name = "carCabinMaterial",
                MaterialConstantBufferIndex = 21,
                DiffuseSrvHeapIndex = 10,
                DiffuseAlbedo = new Vector4(0.20f, 0.25f, 0.35f, 1.0f),
                FresnelR0 = new Vector3(0.03f),
                Roughness = 0.35f
            });

            AddMaterial(new Material
            {
                Name = "carWheelMaterial",
                MaterialConstantBufferIndex = 22,
                DiffuseSrvHeapIndex = 10,
                DiffuseAlbedo = new Vector4(0.05f, 0.05f, 0.05f, 1.0f),
                FresnelR0 = new Vector3(0.02f),
                Roughness = 0.85f
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
            AddRenderItem(
   // RenderLayer.Moon,
   RenderLayer.Opaque,
    objectCBIndex++,
    "moonMaterial",
    "shapeGeo",
    "sphere",
    world: Matrix.Scaling(40.0f, 40.0f, 40.0f) * Matrix.Translation(-300.0f, 200.0f, -400.0f)
);

            AddRenderItem(RenderLayer.Sky, objectCBIndex++, "sky", "shapeGeo", "sphere", world: Matrix.Scaling(5000.0f));
            objectCBIndex = CreateHouses(objectCBIndex);
            
            objectCBIndex = CreateRoofs(objectCBIndex);
            objectCBIndex = CreateTrees(objectCBIndex);
            objectCBIndex = CreateStreets(objectCBIndex);
            objectCBIndex = CreatePavement(objectCBIndex);
            objectCBIndex = CreateGrassAroundTrees(objectCBIndex);
            objectCBIndex = CreateRiver(objectCBIndex);
            objectCBIndex = CreateRiverMargins(objectCBIndex);
            objectCBIndex = CreateRiverWalls(objectCBIndex);
            objectCBIndex = CreateBridges(objectCBIndex);
            objectCBIndex = CreateStreetLamps(objectCBIndex);
            objectCBIndex = CreateCars(objectCBIndex);
        }
       
        private int CreateStreets(int objectCBIndex)
        {
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "streetMaterial", "shapeGeo", "grid",
                textureTransform: Matrix.Scaling(1.0f, 20.0f, 2.0f));
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "streetMaterial", "shapeGeo", "grid",
                textureTransform: Matrix.Scaling(1.0f, 20.0f, 2.0f), world: Matrix.Translation(26.0f, 0.0f, 0));
          
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "streetMaterial", "shapeGeo", "grid",
                textureTransform: Matrix.Scaling(1.0f, 20.0f, 2.0f), world: Matrix.Translation(-26.0f, 0.0f, 0));
           
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "streetMaterial", "shapeGeo", "grid",
                textureTransform: Matrix.Scaling(1.0f, 20.0f, 2.0f), world: Matrix.Translation(52.0f, 0.0f, 0));


            // left side pair: between -26 and 0
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "streetMaterial", "shapeGeo", "smallStreetGrid",
                textureTransform: Matrix.Scaling(10.0f, 1.0f, 2.0f), world: Matrix.Translation(-14.0f, 0.01f, -35.0f));


            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "streetMaterial", "shapeGeo", "smallStreetGrid",
                world: Matrix.Translation(-14.0f, 0.01f, 35.0f),
                textureTransform: Matrix.Scaling(10.0f, 1.0f, 2.0f));

            // right side pair: between 26 and 52
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "streetMaterial", "shapeGeo", "smallStreetGrid",
                world: Matrix.Translation(39.0f, 0.01f, -35.0f),
               textureTransform: Matrix.Scaling(10.0f, 1.0f, 2.0f));

            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "streetMaterial", "shapeGeo", "smallStreetGrid",
                world: Matrix.Translation(39.0f, 0.01f, 35.0f),
                textureTransform: Matrix.Scaling(10.0f, 1.0f, 2.0f));

            return objectCBIndex;
        }

        private int CreatePavement(int objectCBIndex)
        {
            //AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "pavementMaterial", "shapeGeo", "pavementGrid",
            //    textureTransform: Matrix.Scaling(1.0f, 100.0f, 2.0f), world: Matrix.Translation(-6.5f, 0.0f, 0));
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "pavementMaterial", "shapeGeo", "pavementGrid",
                textureTransform: Matrix.Scaling(1.0f, 100.0f, 3.0f), world: Matrix.Translation(+6.5f, 0.0f, 0));
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "pavementMaterial", "shapeGeo", "pavementGrid",
                textureTransform: Matrix.Scaling(1.0f, 100.0f, 3.0f), world: Matrix.Translation(19.5f, 0.0f, 0));
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "pavementMaterial", "shapeGeo", "pavementGrid",
            //    textureTransform: Matrix.Scaling(1.0f, 100.0f, 2.0f), world: Matrix.Translation(32.5f, 0.0f, 0));
            //AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "pavementMaterial", "shapeGeo", "pavementGrid",
                textureTransform: Matrix.Scaling(10.0f, 100.0f, 3.0f), world: Matrix.Scaling(3.2f, 100.0f, 1.0f) *  Matrix.Translation(-13.1f, 0.0f, 0));
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "pavementMaterial", "shapeGeo", "pavementGrid",
                textureTransform: Matrix.Scaling(10.0f, 100.0f, 3.0f), world: Matrix.Scaling(3.2f, 100.0f, 1.0f) * Matrix.Translation(39.0f, 0.0f, 0));

            return objectCBIndex;
        }

        private int CreateHouses(int objectCBIndex)
        {
            for (int i = 0; i < 5; ++i)
            {
                //there are 4 rows with buildings
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "house4Material", "shapeGeo", "box",
                  world: Matrix.Translation(-7.0f, 4.0f, -60.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "house1Material", "shapeGeo", "houseBox",
                   world: Matrix.Translation(7.3f, 4.0f, -60.0f + i * 40.0f));
             
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
                    world: Matrix.Scaling(1.0f, 1.0f, 1.7f)* Matrix.Translation(+7.3f, 8.2f, -60.0f + i * 40.0f));
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
                //AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree1Material", "shapeGeo", "rectangle",
                //    world: Matrix.Translation(-7.0f, 1.5f, -87.9f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree1Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(+7.0f, 1.5f, -87.9f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree1Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(+19.0f, 1.5f, -87.9f + i * 40.0f));
                //AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree1Material", "shapeGeo", "rectangle",
                //    world: Matrix.Translation(+32.5f, 1.5f, -87.9f + i * 40.0f));

                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(-7.0f, 1.5f, -80.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(+7.0f, 1.5f, -80.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(+19.0f, 1.5f, -80.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                   world: Matrix.Translation(+32.5f, 1.5f, -80.0f + i * 40.0f));

                //AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree3Material", "shapeGeo", "rectangle",
                //    world: Matrix.Translation(-7.0f, 1.5f, -72.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree3Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(+7.0f, 1.5f, -72.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree3Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(+19.0f, 1.5f, -72.0f + i * 40.0f));
                //AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree3Material", "shapeGeo", "rectangle",
                //   world: Matrix.Translation(+32.5f, 1.5f, -72.0f + i * 40.0f));

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

            // part 1
            for (int i = 0; i < 15; i++)
            {
                float z = -120.8f + i * 5.0f;

                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(-12.5f, 1.5f, z));

                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(38.5f, 1.5f, z));
            }

            // part 2
            for (int i = 0; i < 10; i++)
            {
                float z = -25.0f + i * 5.0f;

                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(-12.0f, 1.5f, z));

                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(38.5f, 1.5f, z));
            }

            // part 3
            for (int i = 0; i <15; i++)
            {
                float z = 40.0f + i * 5.0f;

                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(-12.5f, 1.5f, z));

                AddRenderItem(RenderLayer.Transparent, objectCBIndex++, "tree2Material", "shapeGeo", "rectangle",
                    world: Matrix.Translation(38.5f, 1.5f, z));
            }
            return objectCBIndex;
        }

        private int CreateGrassAroundTrees(int objectCBIndex)
        {
            for (int i = 0; i < 5; ++i)
            {
                //AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                //    world: Matrix.Translation(-7.0f, 0.1f, -87.9f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+7.0f, 0.1f, -87.9f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+19.0f, 0.1f, -87.9f + i * 40.0f));
                //AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                //    world: Matrix.Translation(+32.5f, 0.1f, -87.9f + i * 40.0f));

                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(-7.0f, 0.1f, -80.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+7.0f, 0.1f, -80.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+19.0f, 0.1f, -80.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+32.5f, 0.1f, -80.0f + i * 40.0f));

                //AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                //    world: Matrix.Translation(-7.0f, 0.1f, -72.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+7.0f, 0.1f, -72.0f + i * 40.0f));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(+19.0f, 0.1f, -72.0f + i * 40.0f));
                //AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                //    world: Matrix.Translation(+32.5f, 0.1f, -72.0f + i * 40.0f));

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

           
            // part 1
            for (int i = 0; i < 15; i++)
            {
                float z = -120.8f + i * 5.0f;

                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                      world: Matrix.Translation(-12.5f, 0.1f, z));

                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                     world: Matrix.Translation(38.5f, 0.1f, z));
            }

            // part 2
            for (int i = 0; i < 10; i++)
            {
                float z = -25.0f + i * 5.0f;
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(-12.5f, 0.1f, z));
                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(38.5f, 0.1f, z));
            }

            // part 3
            for (int i = 0; i < 15; i++)
            {
                float z = 40.0f + i * 5.0f;

                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                     world: Matrix.Translation(-12.5f, 0.1f, z));

                AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "grassMaterial", "shapeGeo", "grassGrid",
                    world: Matrix.Translation(38.5f, 0.1f, z));
            }

            return objectCBIndex;
        }


  //RIVER pieces
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

        private void AnimateWaterMaterial(GameTimer gameTimer)
        {
            Material waterMat = materials["waterMaterial"];

            float uOffset = 0.0f;
            float vOffset = (float)(0.2f * gameTimer.TotalTime); // flow speed

            waterMat.MatTransform =
                Matrix.Scaling(1.0f, 3.0f, 1.0f) *
                Matrix.Translation(uOffset, vOffset, 0.0f);

            waterMat.NumberOfFramesDirty = NUMBER_OF_FRAME_RESOURCES;
        }

        private int CreateRiverMargins(int objectCBIndex)
        {
            // left margin
            AddRenderItem( RenderLayer.Opaque,objectCBIndex++,"terrainMaterial","shapeGeo","grid",
                world: Matrix.Scaling(0.7f, 7.0f, 1.0f) * Matrix.Translation(8.0f, -0.05f, 0.0f),
                textureTransform: Matrix.Scaling(1.0f, 80.0f, 1.0f)
            );

            // right margin
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++,"terrainMaterial","shapeGeo","grid",
                world: Matrix.Scaling(0.7f, 7.0f, 1.0f) * Matrix.Translation(18.0f, -0.05f, 0.0f),
                textureTransform: Matrix.Scaling(1.0f, 80.0f, 80.0f)
            ); 

            return objectCBIndex;
        }

        private int CreateRiverWalls(int objectCBIndex)
        {
            // Left wall
            AddRenderItem(
                RenderLayer.Opaque,
                objectCBIndex++,
                "riverWallsMaterial",
                "shapeGeo",
                "box",
                world: Matrix.Scaling(0.1f, 0.1f, 80.0f) *
               Matrix.Translation(9.8f, 0.1f, 0.0f),
               textureTransform: Matrix.Scaling(1.0f, 80.0f, 1.0f)
            );

            // Right wall
            AddRenderItem(
                RenderLayer.Opaque,
                objectCBIndex++,
                "riverWallsMaterial",
                "shapeGeo",
                "box",
               world: Matrix.Scaling(0.1f, 0.1f, 80.0f) *
               Matrix.Translation(16.2f, 0.1f, 0.0f),
               textureTransform: Matrix.Scaling(1.0f, 80.0f, 800.0f)
            );

            return objectCBIndex;
        }

        
        private int CreateBridge(int objectCBIndex, float x, float y, float z)
        {
            // deck
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "woodMaterial", "shapeGeo", "roofBox",
                world: Matrix.Scaling(4.0f, 0.8f, 1.0f) *
                       Matrix.Translation(x, y, z),
                textureTransform: Matrix.Scaling(4.0f, 1.0f, 1.0f)
            );

            // left rail
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "woodMaterial", "shapeGeo", "roofBox",
                world: Matrix.Scaling(4.0f, 0.4f, 0.12f) *
                       Matrix.Translation(x, y + 0.35f, z - 1.35f),
                textureTransform: Matrix.Scaling(4.0f, 1.0f, 1.0f)
            );

            // right rail
            AddRenderItem(RenderLayer.Opaque, objectCBIndex++, "woodMaterial", "shapeGeo", "roofBox",
                world: Matrix.Scaling(4.0f, 0.4f, 0.12f) *
                       Matrix.Translation(x, y + 0.35f, z + 1.35f),
                textureTransform: Matrix.Scaling(4.0f, 1.0f, 1.0f)
            );

            return objectCBIndex;
        }

        private int CreateBridges(int objectCBIndex)
        {
            float bridgeX = 13.0f;
            float bridgeY = 0.35f;

            objectCBIndex = CreateBridge(objectCBIndex, bridgeX, bridgeY, -36.0f);
            objectCBIndex = CreateBridge(objectCBIndex, bridgeX, bridgeY, -12.0f);
            objectCBIndex = CreateBridge(objectCBIndex, bridgeX, bridgeY, 12.0f);
            objectCBIndex = CreateBridge(objectCBIndex, bridgeX, bridgeY, 36.0f);
            objectCBIndex = CreateBridge(objectCBIndex, bridgeX, bridgeY, -52.0f);
            objectCBIndex = CreateBridge(objectCBIndex, bridgeX, bridgeY, 52.0f);

            return objectCBIndex;
        }
    



        private int CreateStreetLamp(int objectCBIndex, float x, float z)
        {
            float sphereY = 3.6f;

            // base
            AddRenderItem(
                RenderLayer.Opaque,
                objectCBIndex++,
                "lampPoleMaterial",
                "shapeGeo",
                "box",
                world: Matrix.Scaling(0.18f, 0.06f, 0.18f) *
                       Matrix.Translation(x, 0.2f, z)
            );

            // pole
            AddRenderItem(
                RenderLayer.Opaque,
                objectCBIndex++,
                "lampPoleMaterial",
                "shapeGeo",
                "box",
                world: Matrix.Scaling(0.05f, 0.50f, 0.05f) *
                       Matrix.Translation(x, 1.8f, z)
            );

            // glowing sphere = lamp head
            AddRenderItem(
                RenderLayer.Opaque,
                objectCBIndex++,
                "lampLightMaterial",
                "shapeGeo",
                "sphere",
                world: Matrix.Scaling(0.45f, 0.45f, 0.45f) *
                       Matrix.Translation(x, sphereY, z)
            );

            streetLampLightPositions.Add(new Vector3(x, sphereY, z));

            return objectCBIndex;
        }


        private int CreateStreetLamps(int objectCBIndex)
        {
            for (int i = 0; i < 5; i++)
            {
                float z = 8.0f + i * 10.0f;

                // lamps near first street
                objectCBIndex = CreateStreetLamp(objectCBIndex, -3.5f, z);
                objectCBIndex = CreateStreetLamp(objectCBIndex, 3.5f, z);

                // lamps near second street
                objectCBIndex = CreateStreetLamp(objectCBIndex, 22.5f, z);
                objectCBIndex = CreateStreetLamp(objectCBIndex, 28.5f, z);

                
            }
            for (int i = 0; i < 6; i++)
            {
                float z = -8.0f + i * -10.0f;

                // lamps near first street
                objectCBIndex = CreateStreetLamp(objectCBIndex, -3.5f, z);
                objectCBIndex = CreateStreetLamp(objectCBIndex, 3.5f, z);

                // lamps near second street
                objectCBIndex = CreateStreetLamp(objectCBIndex, 22.5f, z);
                objectCBIndex = CreateStreetLamp(objectCBIndex, 28.5f, z);


            }
         
             for (int i = 0; i < 5; i++)
            {
                float z = 60.0f + i * 10.0f;

                // lamps near first street
                objectCBIndex = CreateStreetLamp(objectCBIndex, -3.5f, z);
                objectCBIndex = CreateStreetLamp(objectCBIndex, 3.5f, z);

                // lamps near second street
                objectCBIndex = CreateStreetLamp(objectCBIndex, 22.5f, z);
                objectCBIndex = CreateStreetLamp(objectCBIndex, 28.5f, z);


            }
            return objectCBIndex;
        }




        private void CreateShadowMap()
        {
            int shadowWidth = 2048;
            int shadowHeight = 2048;

            shadowViewport = new ViewportF(0, 0, shadowWidth, shadowHeight, 0.0f, 1.0f);
            shadowScissorRect = new Rectangle(0, 0, shadowWidth, shadowHeight);

            var shadowMapDesc = ResourceDescription.Texture2D(
                Format.R24G8_Typeless,
                shadowWidth,
                shadowHeight,
                1, 1, 1, 0,
                ResourceFlags.AllowDepthStencil);

            var optClear = new ClearValue
            {
                Format = Format.D24_UNorm_S8_UInt,
                DepthStencil = new DepthStencilValue { Depth = 1.0f, Stencil = 0 }
            };

            shadowMap = Device.CreateCommittedResource(
                new HeapProperties(HeapType.Default),
                HeapFlags.None,
                shadowMapDesc,
                ResourceStates.GenericRead,
                optClear);

            // DSV heap
            var dsvHeapDesc = new DescriptorHeapDescription
            {
                DescriptorCount = 1,
                Type = DescriptorHeapType.DepthStencilView,
                Flags = DescriptorHeapFlags.None
            };

            dsvHeapShadow = Device.CreateDescriptorHeap(dsvHeapDesc);
            shadowDsv = dsvHeapShadow.CPUDescriptorHandleForHeapStart;

            var dsvDesc = new DepthStencilViewDescription
            {
                Dimension = DepthStencilViewDimension.Texture2D,
                Format = Format.D24_UNorm_S8_UInt
            };

            Device.CreateDepthStencilView(shadowMap, dsvDesc, shadowDsv);
        }

        //private void UpdateShadowTransform()
        //{
        //    Vector3 lightDir = -mainPassCB.Lights[0].Direction;
        //    Vector3 lightPos = -150.0f * lightDir;

        //    Vector3 targetPos = new Vector3(13.0f, 0.0f, 0.0f);
        //    Vector3 up = Vector3.UnitY;

        //    lightView = Matrix.LookAtLH(lightPos, targetPos, up);

        //    // orthographic projection for directional light
        //    float left = -120.0f;
        //    float right = 120.0f;
        //    float bottom = -120.0f;
        //    float top = 120.0f;
        //    float nearZ = 1.0f;
        //    float farZ = 300.0f;

        //    lightProj = Matrix.OrthoOffCenterLH(left, right, bottom, top, nearZ, farZ);

        //    Matrix T = new Matrix(
        //        0.5f, 0.0f, 0.0f, 0.0f,
        //        0.0f, -0.5f, 0.0f, 0.0f,
        //        0.0f, 0.0f, 1.0f, 0.0f,
        //        0.5f, 0.5f, 0.0f, 1.0f);

        //    shadowTransform = lightView * lightProj * T;
        //}

        private void UpdateShadowTransform()
        {
            Vector3 targetPos = new Vector3(13.0f, 0.0f, 0.0f);

            Vector3 lightDir = Vector3.Normalize(mainPassCB.Lights[0].Direction);
            Vector3 lightPos = targetPos - 180.0f * lightDir;

            lightView = Matrix.LookAtLH(lightPos, targetPos, Vector3.UnitY);

            lightProj = Matrix.OrthoOffCenterLH(
                -120.0f, 120.0f,
                -120.0f, 120.0f,
                1.0f, 400.0f
            );

            Matrix T = new Matrix(
                0.5f, 0.0f, 0.0f, 0.0f,
                0.0f, -0.5f, 0.0f, 0.0f,
                0.0f, 0.0f, 1.0f, 0.0f,
                0.5f, 0.5f, 0.0f, 1.0f);

            shadowTransform = lightView * lightProj * T;
        }

        //CAR
        private int CreateCar(int objectCBIndex, float carX, float carY, float carZ)
        {
            float wheelY = carY - 0.35f;
            float wheelOffsetX = 1.0f;
            float wheelOffsetZ = 0.78f;

            Matrix carRotation = Matrix.Identity;

            // body
            AddRenderItem(
                RenderLayer.Opaque,
                objectCBIndex++,
                "carBodyMaterial",
                "shapeGeo",
                "carBody",
                world: carRotation * Matrix.Translation(carX, carY, carZ)
            );

            // cabin
            AddRenderItem(
                RenderLayer.Opaque,
                objectCBIndex++,
                "carCabinMaterial",
                "shapeGeo",
                "carCabin",
                world: carRotation * Matrix.Translation(carX + 0.2f, carY + 0.55f, carZ)
            );

            // wheel rotation
            Matrix wheelRotation = Matrix.RotationZ(MathUtil.PiOverTwo);
            // If the wheels stand upright, replace with:
            // Matrix wheelRotation = Matrix.RotationX(MathUtil.PiOverTwo);

            // front-left
            AddRenderItem(
                RenderLayer.Opaque,
                objectCBIndex++,
                "carWheelMaterial",
                "shapeGeo",
                "wheel",
                world: wheelRotation * carRotation *
                       Matrix.Translation(carX - wheelOffsetX, wheelY, carZ - wheelOffsetZ)
            );

            // front-right
            AddRenderItem(
                RenderLayer.Opaque,
                objectCBIndex++,
                "carWheelMaterial",
                "shapeGeo",
                "wheel",
                world: wheelRotation * carRotation *
                       Matrix.Translation(carX - wheelOffsetX, wheelY, carZ + wheelOffsetZ)
            );

            // back-left
            AddRenderItem(
                RenderLayer.Opaque,
                objectCBIndex++,
                "carWheelMaterial",
                "shapeGeo",
                "wheel",
                world: wheelRotation * carRotation *
                       Matrix.Translation(carX + wheelOffsetX, wheelY, carZ - wheelOffsetZ)
            );

            // back-right
            AddRenderItem(
                RenderLayer.Opaque,
                objectCBIndex++,
                "carWheelMaterial",
                "shapeGeo",
                "wheel",
                world: wheelRotation * carRotation *
                       Matrix.Translation(carX + wheelOffsetX, wheelY, carZ + wheelOffsetZ)
            );

            return objectCBIndex;
        }

        private int CreateCars(int objectCBIndex)
        {
            objectCBIndex = CreateCar(objectCBIndex, -1.5f, 0.8f, -20.0f);
            objectCBIndex = CreateCar(objectCBIndex, -1.5f, 0.8f, 20.0f);

            objectCBIndex = CreateCar(objectCBIndex, 24.5f, 0.8f, -35.0f);
            objectCBIndex = CreateCar(objectCBIndex, 24.5f, 0.8f, 35.0f);

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


        private void OnKeyboardInput(GameTimer gameTimer)
        {
            float dt = gameTimer.DeltaTime;
            float moveSpeed = 10.0f * dt;
            float rollSpeed = 1.5f * dt;

            if (IsKeyDown(Keys.W) || IsKeyDown(Keys.Up))
            {
                camera.Walk(moveSpeed);
            }
            if (IsKeyDown(Keys.S) || IsKeyDown(Keys.Down))
            {
                camera.Walk(-moveSpeed);
            }
            if (IsKeyDown(Keys.A) || IsKeyDown(Keys.Left))
            {
                camera.Strafe(-moveSpeed);
            }
            if (IsKeyDown(Keys.D) || IsKeyDown(Keys.Right))
            {
                camera.Strafe(moveSpeed);
            }

            // move up/down on Y axis
            if (IsKeyDown(Keys.Q))
            {
                camera.Rise(moveSpeed);
            }
            if (IsKeyDown(Keys.E))
            {
                camera.Rise(-moveSpeed);
            }

            // optional roll
            if (IsKeyDown(Keys.Z))
            {
                camera.Roll(-rollSpeed);
            }
            if (IsKeyDown(Keys.C))
            {
                camera.Roll(rollSpeed);
            }

            if (IsKeyDown(Keys.R))
            {
                camera.LookAt(
                    new Vector3(-40.0f, 25.0f, -100.0f),
                    new Vector3(40.0f, -10.0f, 13.0f),
                    Vector3.UnitY
                );
            }

            camera.UpdateViewMatrix();
        }
        //MOUSE

        protected override void OnMouseMove(MouseButtons button, Point location)
        {
            if ((button & MouseButtons.Left) != 0)
            {
                // Make each pixel correspond to a quarter of a degree.
                float dx = MathUtil.DegreesToRadians(0.25f * (location.X - lastMousePosition.X));
                float dy = MathUtil.DegreesToRadians(0.25f * (location.Y - lastMousePosition.Y));

                camera.Pitch(dy);
                camera.RotateY(dx);
            }

            lastMousePosition = location;
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





        private void DrawSceneToShadowMap()
        {
            // Set shadow-map viewport/scissor instead of the screen ones
            CommandList.SetViewport(shadowViewport);
            CommandList.SetScissorRectangles(shadowScissorRect);

            // Shadow map: GenericRead -> DepthWrite
            CommandList.ResourceBarrierTransition(
                shadowMap,
                ResourceStates.GenericRead,
                ResourceStates.DepthWrite);

            // No color target, only depth
            CommandList.ClearDepthStencilView(
                shadowDsv,
                ClearFlags.FlagsDepth,
                1.0f,
                0);

            CommandList.SetRenderTargets((CpuDescriptorHandle)default, shadowDsv);

            // Use shadow PSO
            CommandList.PipelineState = pipelineStates["shadow"];

            // Root signature stays the same
            CommandList.SetGraphicsRootSignature(rootSignature);

            // Pass constants
            Resource passCB = CurrentFrameResource.PassCB.Resource;
            CommandList.SetGraphicsRootConstantBufferView(1, passCB.GPUVirtualAddress);

            // Material buffer, only if your shadow shader/root signature still expects it
            Resource matBuffer = CurrentFrameResource.MaterialBuffer.Resource;
            CommandList.SetGraphicsRootShaderResourceView(2, matBuffer.GPUVirtualAddress);

            // Draw opaque objects into shadow map
            DrawRenderItems(CommandList, renderItemLayers[RenderLayer.Opaque]);

            // Back to GenericRead so main pass can sample shadow map
            CommandList.ResourceBarrierTransition(
                shadowMap,
                ResourceStates.DepthWrite,
                ResourceStates.GenericRead);
        }

        private static StaticSamplerDescription[] GetStaticSamplers()
        {
            return new[]
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
            },
          new StaticSamplerDescription(ShaderVisibility.All, 6, 0)
{
    Filter = Filter.ComparisonMinMagLinearMipPoint,
    AddressUVW = TextureAddressMode.Border,
    ComparisonFunc = Comparison.LessEqual,
    BorderColor = StaticBorderColor.OpaqueWhite
}




        };
        }
    }




   

    }






