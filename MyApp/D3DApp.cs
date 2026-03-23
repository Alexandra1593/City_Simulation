using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Input;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D12;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D12.Device;
using Feature = SharpDX.Direct3D12.Feature;
using Point = SharpDX.Point;
using Resource = SharpDX.Direct3D12.Resource;
using RectangleF = SharpDX.RectangleF;

namespace ProiectSPG
{
    /// <summary>
    /// The D3DApp class is the base Direct3D application class which provides functions for creating the main application window, running
    /// the application message loop, handling window messages, and initializing Direct3D.
    /// </summary>
    public class D3DApp : IDisposable
    {
        public const int NUMBER_OF_FRAME_RESOURCES = 3;
        public const int SWAP_CHAIN_BUFFER_COUNT = 2;

        private Form window;             
        private bool isAppPaused;         
        private bool isWindowMinimized;          
        private bool isWindowMaximized;          
        private bool isWindowResizing;           
        private bool isAppRunning;            

        private bool m4xMsaaState;       // 4X MSAA(Multisample anti-aliasing) enabled.
        private int m4xMsaaQuality;      // Quality level of 4X MSAA.

        private FormWindowState lastWindowState = FormWindowState.Normal;

        private Factory4 factory;
        private readonly Resource[] swapChainBuffers = new Resource[SWAP_CHAIN_BUFFER_COUNT];

        private AutoResetEvent fenceEvent;

        public bool M4xMsaaState
        {
            get { return m4xMsaaState; }
            set
            {
                if (m4xMsaaState != value)
                {
                    m4xMsaaState = value;

                    if (isAppRunning)
                    {
                        // Recreate the swapchain and buffers with new multisample settings.
                        CreateSwapChain();
                        OnResize();
                    }
                }
            }
        }

        protected DescriptorHeap RtvHeap { get; private set; }
        protected DescriptorHeap DsvHeap { get; private set; }

        protected int MsaaCount => M4xMsaaState ? 4 : 1;
        protected int MsaaQuality => M4xMsaaState ? m4xMsaaQuality - 1 : 0;

        protected GameTimer Timer { get; } = new GameTimer();

        protected Device Device { get; private set; }

        protected Fence Fence { get; private set; }
        protected long CurrentFence { get; set; }

        protected int RtvDescriptorSize { get; private set; }
        protected int DsvDescriptorSize { get; private set; }

        //SRV - Shader Resource View
        //CBV - Constant Buffer View
        //UAV - Unordered Access View
        protected int CbvSrvUavDescriptorSize { get; private set; }

        protected CommandQueue CommandQueue { get; private set; }
        protected CommandAllocator DirectCmdListAlloc { get; private set; }
        protected GraphicsCommandList CommandList { get; private set; }

        protected SwapChain3 SwapChain { get; private set; }
        protected Resource DepthStencilBuffer { get; private set; }

        protected ViewportF Viewport { get; set; }
        protected RectangleF ScissorRectangle { get; set; }

        protected string MainWindowCaption { get; set; } = "Proiect SPG";
        protected int WindowWidth { get; set; } = 1280;
        protected int WindowHeight { get; set; } = 720;
        
        protected float AspectRatio => (float)WindowWidth / WindowHeight;

        protected Format BackBufferFormat { get; } = Format.R8G8B8A8_UNorm;
        protected Format DepthStencilFormat { get; } = Format.D24_UNorm_S8_UInt;

        protected Resource CurrentBackBuffer => swapChainBuffers[SwapChain.CurrentBackBufferIndex];
        protected CpuDescriptorHandle CurrentBackBufferView
            => RtvHeap.CPUDescriptorHandleForHeapStart + SwapChain.CurrentBackBufferIndex * RtvDescriptorSize;
        protected CpuDescriptorHandle DepthStencilView => DsvHeap.CPUDescriptorHandleForHeapStart;

        public virtual void Initialize()
        {
            InitMainWindow();
            InitDirect3D();

            OnResize();

            isAppRunning = true;
        }

        /// <summary>
        /// This method wraps the application message loop.
        /// </summary>
        public void Run()
        {
            Timer.Reset();
            while (isAppRunning)
            {
                Application.DoEvents();
                Timer.Tick();
                if (!isAppPaused)
                {
                    Update(Timer);
                    Draw(Timer);
                }
                else
                {
                    Thread.Sleep(100);
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                FlushCommandQueue();

                RtvHeap?.Dispose();
                DsvHeap?.Dispose();
                SwapChain?.Dispose();
                foreach (Resource buffer in swapChainBuffers)
                {
                    buffer?.Dispose();
                }
                DepthStencilBuffer?.Dispose();
                CommandList?.Dispose();
                DirectCmdListAlloc?.Dispose();
                CommandQueue?.Dispose();
                Fence?.Dispose();
                Device?.Dispose();
            }
        }

        protected virtual void OnResize()
        {
            Debug.Assert(Device != null);
            Debug.Assert(SwapChain != null);
            Debug.Assert(DirectCmdListAlloc != null);

            // Flush before changing any resources.
            FlushCommandQueue();

            CommandList.Reset(DirectCmdListAlloc, null);

            // Release the previous resources we will be recreating.
            foreach (Resource buffer in swapChainBuffers)
            {
                buffer?.Dispose();
            }
            DepthStencilBuffer?.Dispose();

            // Resize the swap chain.
            SwapChain.ResizeBuffers(
                SWAP_CHAIN_BUFFER_COUNT,
                WindowWidth, WindowHeight,
                BackBufferFormat,
                SwapChainFlags.AllowModeSwitch);

            CpuDescriptorHandle rtvHeapHandle = RtvHeap.CPUDescriptorHandleForHeapStart;
            for (int i = 0; i < SWAP_CHAIN_BUFFER_COUNT; i++)
            {
                Resource backBuffer = SwapChain.GetBackBuffer<Resource>(i);
                swapChainBuffers[i] = backBuffer;
                Device.CreateRenderTargetView(backBuffer, null, rtvHeapHandle);
                rtvHeapHandle += RtvDescriptorSize;
            }

            // Create the depth/stencil buffer and view.
            var depthStencilDesc = new ResourceDescription
            {
                Dimension = ResourceDimension.Texture2D,
                Alignment = 0,
                Width = WindowWidth,
                Height = WindowHeight,
                DepthOrArraySize = 1,
                MipLevels = 1,
                Format = Format.R24G8_Typeless,
                SampleDescription = new SampleDescription
                {
                    Count = MsaaCount,
                    Quality = MsaaQuality
                },
                Layout = TextureLayout.Unknown,
                Flags = ResourceFlags.AllowDepthStencil
            };
            var optClear = new ClearValue
            {
                Format = DepthStencilFormat,
                DepthStencil = new DepthStencilValue
                {
                    Depth = 1.0f,
                    Stencil = 0
                }
            };
            DepthStencilBuffer = Device.CreateCommittedResource(
                new HeapProperties(HeapType.Default),
                HeapFlags.None,
                depthStencilDesc,
                ResourceStates.Common,
                optClear);

            var depthStencilViewDesc = new DepthStencilViewDescription
            {
                Dimension = M4xMsaaState 
                    ? DepthStencilViewDimension.Texture2DMultisampled
                    : DepthStencilViewDimension.Texture2D,
                Format = DepthStencilFormat
            };
            // Create descriptor to mip level 0 of entire resource using a depth stencil format.
            CpuDescriptorHandle dsvHeapHandle = DsvHeap.CPUDescriptorHandleForHeapStart;
            Device.CreateDepthStencilView(DepthStencilBuffer, depthStencilViewDesc, dsvHeapHandle);

            // Transition the resource from its initial state to be used as a depth buffer.
            CommandList.ResourceBarrierTransition(DepthStencilBuffer, ResourceStates.Common, ResourceStates.DepthWrite);

            // Execute the resize commands.
            CommandList.Close();
            CommandQueue.ExecuteCommandList(CommandList);

            // Wait until resize is complete.
            FlushCommandQueue();

            Viewport = new ViewportF(0, 0, WindowWidth, WindowHeight, 0.0f, 1.0f);
            ScissorRectangle = new RectangleF(0, 0, WindowWidth, WindowHeight);
        }

        protected virtual void Update(GameTimer gameTimer) { }
        protected virtual void Draw(GameTimer gameTimer) { }

        /// <summary>
        /// Initializes the main application window
        /// </summary>
        protected void InitMainWindow()
        {
            window = new Form
            {
                Text = MainWindowCaption,
                Name = "Proiect SPG",
                FormBorderStyle = FormBorderStyle.Sizable,
                ClientSize = new Size(WindowWidth, WindowHeight),
                StartPosition = FormStartPosition.CenterScreen,
                MinimumSize = new Size(200, 200)
            };

            window.MouseDown += (sender, e) => OnMouseDown(e.Button, new Point(e.X, e.Y));
            window.MouseUp += (sender, e) => OnMouseUp(e.Button, new Point(e.X, e.Y));
            window.MouseMove += (sender, e) => OnMouseMove(e.Button, new Point(e.X, e.Y));
            window.KeyDown += (sender, e) => OnKeyDown(e.KeyCode);
            window.KeyUp += (sender, e) => OnKeyUp(e.KeyCode);
            window.ResizeBegin += (sender, e) =>
            {
                isAppPaused = true;
                isWindowResizing = true;
                Timer.Stop();
            };
            window.ResizeEnd += (sender, e) =>
            {
                isAppPaused = false;
                isWindowResizing = false;
                Timer.Start();
                OnResize();
            };
            window.Activated += (sender, e) =>
            {
                isAppPaused = false;
                Timer.Start();
            };
            window.Deactivate += (sender, e) =>
            {
                isAppPaused = true;
                Timer.Stop();
            };
            window.HandleDestroyed += (sender, e) => isAppRunning = false;
            window.Resize += (sender, e) =>
            {
                WindowWidth = window.ClientSize.Width;
                WindowHeight = window.ClientSize.Height;
                // When window state changes.
                if (window.WindowState != lastWindowState)
                {
                    lastWindowState = window.WindowState;
                    if (window.WindowState == FormWindowState.Maximized)
                    {
                        isAppPaused = false;
                        isWindowMinimized = false;
                        isWindowMaximized = true;
                        OnResize();
                    }
                    else if (window.WindowState == FormWindowState.Minimized)
                    {
                        isAppPaused = true;
                        isWindowMinimized = true;
                        isWindowMaximized = false;
                    }
                    else if (window.WindowState == FormWindowState.Normal)
                    {
                        if (isWindowMinimized) // Restoring from minimized state?
                        {
                            isAppPaused = false;
                            isWindowMinimized = false;
                            OnResize();
                        }
                        else if (isWindowMaximized) // Restoring from maximized state?
                        {
                            isAppPaused = false;
                            isWindowMaximized = false;
                            OnResize();
                        }
                        else if (isWindowResizing)
                        {
                            // If user is dragging the resize bars, we do not resize
                            // the buffers here because as the user continuously
                            // drags the resize bars, a stream of WM_SIZE messages are
                            // sent to the window, and it would be pointless (and slow)
                            // to resize for each WM_SIZE message received from dragging
                            // the resize bars.  So instead, we reset after the user is
                            // done resizing the window and releases the resize bars, which
                            // sends a WM_EXITSIZEMOVE message.
                        }
                        else // API call such as SetWindowPos or mSwapChain->SetFullscreenState.
                        {
                            OnResize();
                        }
                    }
                }
                else if (!isWindowResizing) // Resize due to snapping.
                {
                    OnResize();
                }
            };

            window.Show();
            window.Update();
        }

        protected virtual void OnMouseDown(MouseButtons button, Point location)
        {
            window.Capture = true;
        }

        protected virtual void OnMouseUp(MouseButtons button, Point location)
        {
            window.Capture = false;
        }

        protected virtual void OnMouseMove(MouseButtons button, Point location)
        {
        }

        protected virtual void OnKeyDown(Keys keyCode)
        {
        }

        protected virtual void OnKeyUp(Keys keyCode)
        {
            switch (keyCode)
            {
                case Keys.Escape:
                    isAppRunning = false;
                    break;
                case Keys.F2:
                    M4xMsaaState = !M4xMsaaState;
                    break;
            }
        }

        protected bool IsKeyDown(Keys keyCode) => Keyboard.IsKeyDown(KeyInterop.KeyFromVirtualKey((int)keyCode));

        /// <summary>
        /// Initializes Direct3D.
        /// </summary>
        protected void InitDirect3D()
        { 
            factory = new Factory4();

            try
            {
                Device = new Device(null, FeatureLevel.Level_11_0);
            }
            catch (SharpDXException)
            {
                Adapter warpAdapter = factory.GetWarpAdapter();
                Device = new Device(warpAdapter, FeatureLevel.Level_11_0);
            }

            Fence = Device.CreateFence(0, FenceFlags.None);
            fenceEvent = new AutoResetEvent(false);

            RtvDescriptorSize = Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
            DsvDescriptorSize = Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.DepthStencilView);
            CbvSrvUavDescriptorSize = Device.GetDescriptorHandleIncrementSize(
                DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);

            // Check 4X MSAA quality support for our back buffer format.
            // All Direct3D 11 capable devices support 4X MSAA for all render 
            // target formats, so we only need to check quality support.

            FeatureDataMultisampleQualityLevels msQualityLevels;
            msQualityLevels.Format = BackBufferFormat;
            msQualityLevels.SampleCount = 4;
            msQualityLevels.Flags = MultisampleQualityLevelFlags.None;
            msQualityLevels.QualityLevelCount = 0;
            Debug.Assert(Device.CheckFeatureSupport(Feature.MultisampleQualityLevels, ref msQualityLevels));
            m4xMsaaQuality = msQualityLevels.QualityLevelCount;

            CreateCommandObjects();
            CreateSwapChain();
            CreateRtvAndDsvDescriptorHeaps();
        }

        protected void FlushCommandQueue()
        {
            // Advance the fence value to mark commands up to this fence point.
            CurrentFence++;

            // Add an instruction to the command queue to set a new fence point.  Because we
            // are on the GPU timeline, the new fence point won't be set until the GPU finishes
            // processing all the commands prior to this Signal().
            CommandQueue.Signal(Fence, CurrentFence);

            // Wait until the GPU has completed commands up to this fence point.
            if (Fence.CompletedValue < CurrentFence)
            {
                // Fire event when GPU hits current fence.
                Fence.SetEventOnCompletion(CurrentFence, fenceEvent.SafeWaitHandle.DangerousGetHandle());

                // Wait until the GPU hits current fence event is fired.
                fenceEvent.WaitOne();
            }
        }

        protected virtual int RtvDescriptorCount => SWAP_CHAIN_BUFFER_COUNT;
        protected virtual int DsvDescriptorCount => 1;

        private void CreateCommandObjects()
        {
            var queueDesc = new CommandQueueDescription(CommandListType.Direct);
            CommandQueue = Device.CreateCommandQueue(queueDesc);

            DirectCmdListAlloc = Device.CreateCommandAllocator(CommandListType.Direct);

            CommandList = Device.CreateCommandList(
                0,
                CommandListType.Direct,
                DirectCmdListAlloc, // Associated command allocator.
                null);              // Initial PipelineStateObject.

            // Start off in a closed state.  This is because the first time we refer
            // to the command list we will Reset it, and it needs to be closed before
            // calling Reset.
            CommandList.Close();
        }

        private void CreateSwapChain()
        {
            // Release the previous swapchain we will be recreating.
            SwapChain?.Dispose();

            var sd = new SwapChainDescription
            {
                ModeDescription = new ModeDescription
                {
                    Width = WindowWidth,
                    Height = WindowHeight,
                    Format = BackBufferFormat,
                    RefreshRate = new Rational(60, 1),
                    Scaling = DisplayModeScaling.Unspecified,
                    ScanlineOrdering = DisplayModeScanlineOrder.Unspecified
                },
                SampleDescription = new SampleDescription
                {
                    Count = 1,
                    Quality = 0
                },
                Usage = Usage.RenderTargetOutput,
                BufferCount = SWAP_CHAIN_BUFFER_COUNT,
                SwapEffect = SwapEffect.FlipDiscard,
                Flags = SwapChainFlags.AllowModeSwitch,
                OutputHandle = window.Handle,
                IsWindowed = true
            };

            using (var tempSwapChain = new SwapChain(factory, CommandQueue, sd))
            {
                SwapChain = tempSwapChain.QueryInterface<SwapChain3>();
            }
        }

        private void CreateRtvAndDsvDescriptorHeaps()
        {
            var rtvHeapDesc = new DescriptorHeapDescription
            {
                DescriptorCount = RtvDescriptorCount,
                Type = DescriptorHeapType.RenderTargetView
            };
            RtvHeap = Device.CreateDescriptorHeap(rtvHeapDesc);

            var dsvHeapDesc = new DescriptorHeapDescription
            {
                DescriptorCount = DsvDescriptorCount,
                Type = DescriptorHeapType.DepthStencilView
            };
            DsvHeap = Device.CreateDescriptorHeap(dsvHeapDesc);
        }
    }
}
