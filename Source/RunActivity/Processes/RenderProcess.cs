﻿// COPYRIGHT 2009, 2010, 2011, 2012, 2013 by the Open Rails project.
// 
// This file is part of Open Rails.
// 
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Open Rails is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with Open Rails.  If not, see <http://www.gnu.org/licenses/>.

// This file is the responsibility of the 3D & Environment Team. 

using System;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ORTS
{
    public class ElapsedTime
    {
        public float ClockSeconds;
        public float RealSeconds;

        public static ElapsedTime Zero = new ElapsedTime();

        public static ElapsedTime operator +(ElapsedTime a, ElapsedTime b)
        {
            return new ElapsedTime(a.ClockSeconds + b.ClockSeconds, a.RealSeconds + b.RealSeconds);
        }

        public ElapsedTime()
            : this(0, 0)
        {
        }

        public ElapsedTime(float clockSeconds, float realSeconds)
        {
            ClockSeconds = clockSeconds;
            RealSeconds = realSeconds;
        }

        public void Reset()
        {
            ClockSeconds = 0;
            RealSeconds = 0;
        }
    }

    [CallOnThread("Render")]
    public class RenderProcess : Microsoft.Xna.Framework.Game
    {
        public const int ShadowMapCountMaximum = 4;
        public const int ShadowMapMipCount = 1;

        public readonly Profiler Profiler = new Profiler("Render");
        public readonly Viewer3D Viewer;

        public Vector2 WindowSize = new Vector2(1024, 768);

        public GraphicsDeviceManager GraphicsDeviceManager;

        RenderFrame CurrentFrame;   // a frame contains a list of primitives to draw at a specified time
        RenderFrame NextFrame;      // we prepare the next frame in the background while the current one is rendering,

        public bool Stopped;        // use for shutdown

        public new bool IsMouseVisible;  // handles cross thread issues by signalling RenderProcess of a change

        // Diagnostic information
        public readonly SmoothedData FrameRate = new SmoothedData();
        public readonly SmoothedDataWithPercentiles FrameTime = new SmoothedDataWithPercentiles();
        public int[] PrimitiveCount = new int[(int)RenderPrimitiveSequence.Sentinel];
        public int[] PrimitivePerFrame = new int[(int)RenderPrimitiveSequence.Sentinel];
        public int[] ShadowPrimitiveCount;
        public int[] ShadowPrimitivePerFrame;

        // Dynamic shadow map setup.
        public static int ShadowMapCount = -1; // number of shadow maps
        public static int[] ShadowMapDistance; // distance of shadow map center from camera
        public static int[] ShadowMapDiameter; // diameter of shadow map
        public static float[] ShadowMapLimit; // diameter of shadow map far edge from camera

        public RenderProcess(Viewer3D viewer)
        {
            Viewer = viewer;
            Profiler.SetThread();

            Window.Title = "Open Rails";
            GraphicsDeviceManager = Viewer.GDM = new GraphicsDeviceManager(this);

            var windowSizeParts = Viewer.Settings.WindowSize.Split(new[] { 'x' }, 2);
            WindowSize.X = Convert.ToInt32(windowSizeParts[0]);
            WindowSize.Y = Convert.ToInt32(windowSizeParts[1]);

            IsFixedTimeStep = false;
            TargetElapsedTime = TimeSpan.FromMilliseconds(1);
            GraphicsDeviceManager.SynchronizeWithVerticalRetrace = Viewer.Settings.VerticalSync;
            GraphicsDeviceManager.PreferredBackBufferWidth = (int)WindowSize.X;
            GraphicsDeviceManager.PreferredBackBufferHeight = (int)WindowSize.Y;
            GraphicsDeviceManager.IsFullScreen = false;
            GraphicsDeviceManager.PreferMultiSampling = true;
            GraphicsDeviceManager.PreparingDeviceSettings += new EventHandler<PreparingDeviceSettingsEventArgs>(GDM_PreparingDeviceSettings);
        }

        void GDM_PreparingDeviceSettings(object sender, PreparingDeviceSettingsEventArgs e)
        {
            // This enables NVIDIA PerfHud to be run on Open Rails.
            foreach (var adapter in GraphicsAdapter.Adapters)
            {
                if (adapter.Description.Contains("PerfHUD"))
                {
                    e.GraphicsDeviceInformation.Adapter = adapter;
                    e.GraphicsDeviceInformation.DeviceType = DeviceType.Reference;
                    break;
                }
            }

            // This stops ResolveBackBuffer() clearing the back buffer.
            e.GraphicsDeviceInformation.PresentationParameters.RenderTargetUsage = RenderTargetUsage.PreserveContents;
            e.GraphicsDeviceInformation.PresentationParameters.AutoDepthStencilFormat = DepthFormat.Depth24Stencil8;
            Viewer.UpdateAdapterInformation(e.GraphicsDeviceInformation.Adapter);
        }

        /// <summary>
        /// Allows the game to perform any initialization it needs after the graphics device has started
        /// </summary>
        [ThreadName("Render")]
        protected override void Initialize()
        {
            Viewer.Initialize();

            ShadowMapCount = Viewer.Settings.ShadowMapCount;
            if (!Viewer.Settings.DynamicShadows)
                ShadowMapCount = 0;
            else if ((ShadowMapCount > 1) && (Viewer.Settings.ShaderModel < 3))
                ShadowMapCount = 1;
            else if (ShadowMapCount < 0)
                ShadowMapCount = 0;
            else if (ShadowMapCount > ShadowMapCountMaximum)
                ShadowMapCount = ShadowMapCountMaximum;
            if (ShadowMapCount < 1)
                Viewer.Settings.DynamicShadows = false;
            ShadowMapDistance = new int[ShadowMapCount];
            ShadowMapDiameter = new int[ShadowMapCount];
            ShadowMapLimit = new float[ShadowMapCount];

            ShadowPrimitiveCount = new int[ShadowMapCount];
            ShadowPrimitivePerFrame = new int[ShadowMapCount];

            InitializeShadowMapLocations(Viewer);

            CurrentFrame = new RenderFrame(this);
            NextFrame = new RenderFrame(this);
            base.Initialize();
            Viewer.Simulator.Paused = false;
        }

        internal static void InitializeShadowMapLocations(Viewer3D viewer)
        {
            var ratio = (float)viewer.DisplaySize.X / viewer.DisplaySize.Y;
            var fov = MathHelper.ToRadians(viewer.Settings.ViewingFOV);
            var n = (float)0.5;
            var f = (float)viewer.Settings.ShadowMapDistance;
            if (f == 0)
                f = viewer.Settings.ViewingDistance / 2;

            var m = (float)ShadowMapCount;
            var LastC = n;
            for (var shadowMapIndex = 0; shadowMapIndex < ShadowMapCount; shadowMapIndex++)
            {
                //     Clog  = split distance i using logarithmic splitting
                //         i
                // Cuniform  = split distance i using uniform splitting
                //         i
                //         n = near view plane
                //         f = far view plane
                //         m = number of splits
                //
                //                   i/m
                //     Clog  = n(f/n)
                //         i
                // Cuniform  = n+(f-n)i/m
                //         i

                // Calculate the two Cs and average them to get a good balance.
                var i = (float)(shadowMapIndex + 1);
                var Clog = n * (float)Math.Pow(f / n, i / m);
                var Cuniform = n + (f - n) * i / m;
                var C = (3 * Clog + Cuniform) / 4;

                // This shadow map goes from LastC to C; calculate the correct center and diameter for the sphere from the view frustum.
                var height1 = (float)Math.Tan(fov / 2) * LastC;
                var height2 = (float)Math.Tan(fov / 2) * C;
                var width1 = height1 * ratio;
                var width2 = height2 * ratio;
                var corner1 = new Vector3(height1, width1, LastC);
                var corner2 = new Vector3(height2, width2, C);
                var cornerCenter = (corner1 + corner2) / 2;
                var length = cornerCenter.Length();
                cornerCenter.Normalize();
                var center = length / Vector3.Dot(cornerCenter, Vector3.UnitZ);
                var diameter = 2 * (float)Math.Sqrt(height2 * height2 + width2 * width2 + (C - center) * (C - center));

                ShadowMapDistance[shadowMapIndex] = (int)center;
                ShadowMapDiameter[shadowMapIndex] = (int)diameter;
                ShadowMapLimit[shadowMapIndex] = C;
                LastC = C;
            }
        }

        /// <summary>
        /// Called regularly.   Used to update the simulator class when
        /// the window is minimized.
        /// </summary>
        [ThreadName("Render")]
        protected override void Update(GameTime gameTime)
        {
            if (IsMouseVisible != base.IsMouseVisible)
                base.IsMouseVisible = IsMouseVisible;

            if (ToggleFullScreenRequested)
            {
                GraphicsDeviceManager.ToggleFullScreen();
                ToggleFullScreenRequested = false;
            }

            if (Stopped)
            {
                Exit();
            }
            else if (gameTime.TotalRealTime.TotalSeconds > 0.001)
            {
                Viewer.UpdaterProcess.WaitTillFinished();

                // Must be done in XNA Game thread.
                UserInput.Update(Viewer);

                // Swap frames and start the next update (non-threaded updater does the whole update).
                SwapFrames(ref CurrentFrame, ref NextFrame);
                Viewer.UpdaterProcess.StartUpdate(NextFrame, gameTime.TotalRealTime.TotalSeconds);
            }

            base.Update(gameTime);
        }

        /// <summary>
        /// This is called once per frame when the game should draw itself.
        /// In a multiprocessor environement, it starts the background UpdateProcessor
        /// task preparing the next frame, while it renders this frame.
        /// In a single processor environment, it does the update/draw in
        /// sequence using this thread alone.
        /// </summary>
        int ProfileFrames = 0;
        [ThreadName("Render")]
        protected override void Draw(GameTime gameTime)
        {
            if (Viewer.Settings.Profiling)
                if ((Viewer.Settings.ProfilingFrameCount > 0 && ++ProfileFrames > Viewer.Settings.ProfilingFrameCount) || (Viewer.Settings.ProfilingTime > 0 && Viewer.RealTime >= Viewer.Settings.ProfilingTime))
                    Exit();

            Profiler.Start();

            // Sort-of hack to allow the NVIDIA PerfHud to display correctly.
            GraphicsDevice.RenderState.DepthBufferEnable = true;

            if ((Viewer.DisplaySize.X != GraphicsDevice.Viewport.Width) || (Viewer.DisplaySize.Y != GraphicsDevice.Viewport.Height))
            {
                Viewer.DisplaySize.X = GraphicsDevice.Viewport.Width;
                Viewer.DisplaySize.Y = GraphicsDevice.Viewport.Height;
                Viewer.WindowManager.ScreenChanged();
            }

            if (Debugger.IsAttached)
            {
                Draw();
                base.Draw(gameTime);
            }
            else
            {
                try
                {
                    Draw();
                    base.Draw(gameTime);
                }
                catch (Exception error)
                {
                    Viewer.ProcessReportError(error);
                }
            }

            // Sort-of hack to allow the NVIDIA PerfHud to display correctly.
            GraphicsDevice.RenderState.DepthBufferEnable = false;

            Profiler.Stop();
        }

        void Draw()
        {
            CurrentFrame.Draw(GraphicsDevice);

            for (var i = 0; i < (int)RenderPrimitiveSequence.Sentinel; i++)
            {
                PrimitivePerFrame[i] = PrimitiveCount[i];
                PrimitiveCount[i] = 0;
            }
            for (var shadowMapIndex = 0; shadowMapIndex < RenderProcess.ShadowMapCount; shadowMapIndex++)
            {
                ShadowPrimitivePerFrame[shadowMapIndex] = ShadowPrimitiveCount[shadowMapIndex];
                ShadowPrimitiveCount[shadowMapIndex] = 0;
            }
        }

        static void SwapFrames(ref RenderFrame frame1, ref RenderFrame frame2)
        {
            RenderFrame temp = frame1;
            frame1 = frame2;
            frame2 = temp;
        }

        bool ToggleFullScreenRequested = false;
        [CallOnThread("Updater")]
        public void ToggleFullScreen()
        {
            bool IsFullScreen = !GraphicsDeviceManager.IsFullScreen;
            if (IsFullScreen)
            {
                System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.PrimaryScreen;
                GraphicsDeviceManager.PreferredBackBufferWidth = screen.Bounds.Width;
                GraphicsDeviceManager.PreferredBackBufferHeight = screen.Bounds.Height;
                GraphicsDeviceManager.PreferredBackBufferFormat = SurfaceFormat.Color;
                GraphicsDeviceManager.PreferredDepthStencilFormat = DepthFormat.Depth32;
            }
            else
            {
                GraphicsDeviceManager.PreferredBackBufferWidth = (int)WindowSize.X;
                GraphicsDeviceManager.PreferredBackBufferHeight = (int)WindowSize.Y;
            }
            Viewer.AdjustCabHeight( GraphicsDeviceManager.PreferredBackBufferWidth, GraphicsDeviceManager.PreferredBackBufferHeight );
            ToggleFullScreenRequested = true;
        }

        /// <summary>
        /// Internal method - do not call! Use Viewer3D.Stop() instead.
        /// </summary>
        internal void Stop()
        {
            // Do not put shutdown code in here! Use RenderProcess.Terminate() instead.
            Stopped = true;
        }

        [ThreadName("Render")]
        void Terminate()
        {
            if (Viewer.Settings.Profiling)
                Viewer.Settings.ProfilingFrameCount = ProfileFrames;
            Viewer.UpdaterProcess.Stop();
            Viewer.LoaderProcess.Stop();
            Viewer.SoundProcess.Stop();
            Viewer.Terminate();
        }

        /// <summary>
        /// User closed the window without pressing the exit key
        /// </summary>
        [ThreadName("Render")]
        protected override void OnExiting(object sender, EventArgs args)
        {
            Terminate();
            base.OnExiting(sender, args);
        }

        [CallOnThread("Render")]
        [CallOnThread("Updater")]
        public void ComputeFPS(float elapsedRealTime)
        {
            if (elapsedRealTime < 0.001)
                return;

            FrameRate.Update(elapsedRealTime, 1f / elapsedRealTime);
            FrameTime.Update(elapsedRealTime, elapsedRealTime);
        }
    }
}
