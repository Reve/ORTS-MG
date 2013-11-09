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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;

namespace ORTS
{
    public abstract class Camera
    {
        protected double CommandStartTime;

        // 2.1 sets the limit at just under a right angle as get unwanted swivel at the full right angle.
        protected static CameraAngleClamper VerticalClamper = new CameraAngleClamper(-MathHelper.Pi / 2.1f, MathHelper.Pi / 2.1f);
        protected const int TerrainAltitudeMargin = 2;

        protected readonly Viewer3D Viewer;

        protected WorldLocation cameraLocation = new WorldLocation();
        public int TileX { get { return cameraLocation.TileX; } }
        public int TileZ { get { return cameraLocation.TileZ; } }
        public Vector3 Location { get { return cameraLocation.Location; } }
        public WorldLocation CameraWorldLocation { get { return cameraLocation; } }
        protected int MouseScrollValue;

        protected Matrix xnaView;
        public Matrix XNAView { get { return xnaView; } }

        Matrix xnaProjection;
        public Matrix XNAProjection { get { return xnaProjection; } }
        public static Matrix XNADMProjection;
        Vector3 frustumRightProjected;
        Vector3 frustumLeft;
        Vector3 frustumRight;

        // This sucks. It's really not camera-related at all.
        public static Matrix XNASkyProjection;

        // The following group of properties are used by other code to vary
        // behavior by camera; e.g. Style is used for activating sounds,
        // AttachedCar for rendering the train or not, and IsUnderground for
        // automatically switching to/from cab view in tunnels.
        public enum Styles { External, Cab, Passenger }
        public virtual Styles Style { get { return Styles.External; } }
        public virtual TrainCar AttachedCar { get { return null; } }
        public virtual bool IsAvailable { get { return true; } }
        public virtual bool IsUnderground { get { return false; } }

        // We need to allow different cameras to have different near planes.
        public virtual float NearPlane { get { return 1.0f; } }

        public float ReplaySpeed { get; set; }
        const int SpeedFactorFastSlow = 8;  // Use by GetSpeed
        protected const float SpeedAdjustmentForRotation = 0.1f;

        // Sound related properties
        public Vector3 Velocity { get; protected set; }

        protected Camera(Viewer3D viewer)
        {
            Viewer = viewer;
        }

        protected Camera(Viewer3D viewer, Camera previousCamera) // maintain visual continuity
            : this(viewer)
        {
            if (previousCamera != null)
                cameraLocation = previousCamera.CameraWorldLocation;
        }

        [CallOnThread("Updater")]
        protected internal virtual void Save(BinaryWriter outf)
        {
            cameraLocation.Save(outf);
        }

        [CallOnThread("Render")]
        protected internal virtual void Restore(BinaryReader inf)
        {
            cameraLocation.Restore(inf);
        }

        /// <summary>
        /// Resets a camera's position, location and attachment information.
        /// </summary>
        public virtual void Reset()
        {
        }

        /// <summary>
        /// Switches the <see cref="Viewer3D"/> to this camera, updating the view information.
        /// </summary>
        public void Activate()
        {
            ScreenChanged();
            OnActivate(Viewer.Camera == this);
            Viewer.Camera = this;
            Update(ElapsedTime.Zero);
            xnaView = GetCameraView();
        }

        /// <summary>
        /// A camera can use this method to handle any preparation when being activated.
        /// </summary>
        protected virtual void OnActivate(bool sameCamera)
        {
        }

        /// <summary>
        /// A camera can use this method to respond to user input.
        /// </summary>
        /// <param name="elapsedTime"></param>
        public virtual void HandleUserInput(ElapsedTime elapsedTime)
        {
        }

        /// <summary>
        /// A camera can use this method to update any calculated data that may have changed.
        /// </summary>
        /// <param name="elapsedTime"></param>
        public virtual void Update(ElapsedTime elapsedTime)
        {
        }

        /// <summary>
        /// A camera should use this method to return a unique view.
        /// </summary>
        protected abstract Matrix GetCameraView();

        /// <summary>
        /// Notifies the camera that the screen dimensions have changed.
        /// </summary>
        public void ScreenChanged()
        {
            var aspectRatio = (float)Viewer.DisplaySize.X / Viewer.DisplaySize.Y;
            var farPlaneDistance = SkyConstants.skyRadius + 100;  // so far the sky is the biggest object in view
            var fovWidthRadians = MathHelper.ToRadians(Viewer.Settings.ViewingFOV);
            if (Viewer.Settings.DistantMountains)
                XNADMProjection = Matrix.CreatePerspectiveFieldOfView(fovWidthRadians, aspectRatio, MathHelper.Clamp(Viewer.Settings.ViewingDistance - 500, 500, 1500), Viewer.Settings.DistantMountainsViewingDistance);
            xnaProjection = Matrix.CreatePerspectiveFieldOfView(fovWidthRadians, aspectRatio, NearPlane, Viewer.Settings.ViewingDistance);
            XNASkyProjection = Matrix.CreatePerspectiveFieldOfView(fovWidthRadians, aspectRatio, NearPlane, farPlaneDistance);    // TODO remove? 
            frustumRightProjected.X = (float)Math.Cos(fovWidthRadians / 2 * aspectRatio);  // Precompute the right edge of the view frustrum.
            frustumRightProjected.Z = (float)Math.Sin(fovWidthRadians / 2 * aspectRatio);
        }

        /// <summary>
        /// Updates view and projection from this camera's data.
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="elapsedTime"></param>
        public void PrepareFrame(RenderFrame frame, ElapsedTime elapsedTime)
        {
            xnaView = GetCameraView();
            frame.SetCamera(ref xnaView, ref xnaProjection);
            frustumLeft.X = -xnaView.M11 * frustumRightProjected.X + xnaView.M13 * frustumRightProjected.Z;
            frustumLeft.Y = -xnaView.M21 * frustumRightProjected.X + xnaView.M23 * frustumRightProjected.Z;
            frustumLeft.Z = -xnaView.M31 * frustumRightProjected.X + xnaView.M33 * frustumRightProjected.Z;
            frustumLeft.Normalize();
            frustumRight.X = xnaView.M11 * frustumRightProjected.X + xnaView.M13 * frustumRightProjected.Z;
            frustumRight.Y = xnaView.M21 * frustumRightProjected.X + xnaView.M23 * frustumRightProjected.Z;
            frustumRight.Z = xnaView.M31 * frustumRightProjected.X + xnaView.M33 * frustumRightProjected.Z;
            frustumRight.Normalize();
        }

        // Cull for fov
        public bool InFOV(Vector3 mstsObjectCenter, float objectRadius)
        {
            mstsObjectCenter.X -= cameraLocation.Location.X;
            mstsObjectCenter.Y -= cameraLocation.Location.Y;
            mstsObjectCenter.Z -= cameraLocation.Location.Z;
            // TODO: This *2 is a complete fiddle because some objects don't currently pass in a correct radius and e.g. track sections vanish.
            objectRadius *= 2;
            if (frustumLeft.X * mstsObjectCenter.X + frustumLeft.Y * mstsObjectCenter.Y - frustumLeft.Z * mstsObjectCenter.Z > objectRadius)
                return false;
            if (frustumRight.X * mstsObjectCenter.X + frustumRight.Y * mstsObjectCenter.Y - frustumRight.Z * mstsObjectCenter.Z > objectRadius)
                return false;
            return true;
        }

        // Cull for distance
        public bool InRange(Vector3 mstsObjectCenter, float objectRadius, float objectViewingDistance)
        {
            mstsObjectCenter.X -= cameraLocation.Location.X;
            mstsObjectCenter.Z -= cameraLocation.Location.Z;

            // An object cannot be visible further away than the viewing distance.
            if (objectViewingDistance > Viewer.Settings.ViewingDistance)
                objectViewingDistance = Viewer.Settings.ViewingDistance;

            var distanceSquared = mstsObjectCenter.X * mstsObjectCenter.X + mstsObjectCenter.Z * mstsObjectCenter.Z;

            return distanceSquared < (objectRadius + objectViewingDistance) * (objectRadius + objectViewingDistance);
        }

        /// <summary>
        /// If the nearest part of the object is within camera viewing distance
        /// and is within the object's defined viewing distance then
        /// we can see it.   The objectViewingDistance allows a small object
        /// to specify a cutoff beyond which the object can't be seen.
        /// </summary>
        public bool CanSee(Vector3 mstsObjectCenter, float objectRadius, float objectViewingDistance)
        {
            if (!InRange(mstsObjectCenter, objectRadius, objectViewingDistance))
                return false;

            if (!InFOV(mstsObjectCenter, objectRadius))
                return false;

            return true;
        }

        public bool CanSee(Matrix xnaMatrix, float objectRadius, float objectViewingDistance)
        {
            var mstsLocation = new Vector3(xnaMatrix.Translation.X, xnaMatrix.Translation.Y, -xnaMatrix.Translation.Z);
            return CanSee(mstsLocation, objectRadius, objectViewingDistance);
        }

        protected static float GetSpeed(ElapsedTime elapsedTime)
        {
            var speed = 5 * elapsedTime.RealSeconds;
            if (UserInput.IsDown(UserCommands.CameraMoveFast))
                speed *= SpeedFactorFastSlow;
            if (UserInput.IsDown(UserCommands.CameraMoveSlow))
                speed /= SpeedFactorFastSlow;
            return speed;
        }


        /// <summary>
        /// Returns a position in XNA space relative to the camera's tile
        /// </summary>
        /// <param name="worldLocation"></param>
        /// <returns></returns>
        public Vector3 XNALocation(WorldLocation worldLocation)
        {
            var xnaVector = worldLocation.Location;
            xnaVector.X += 2048 * (worldLocation.TileX - cameraLocation.TileX);
            xnaVector.Z += 2048 * (worldLocation.TileZ - cameraLocation.TileZ);
            xnaVector.Z *= -1;
            return xnaVector;
        }


        protected class CameraAngleClamper
        {
            readonly float Minimum;
            readonly float Maximum;

            public CameraAngleClamper(float minimum, float maximum)
            {
                Minimum = minimum;
                Maximum = maximum;
            }

            public float Clamp(float angle)
            {
                return MathHelper.Clamp(angle, Minimum, Maximum);
            }
        }

        public void UpdateListener()
        {
            float[] cameraPosition = new float[] {
                        CameraWorldLocation.Location.X,
                        CameraWorldLocation.Location.Y,
                        CameraWorldLocation.Location.Z};

            float[] cameraVelocity = new float[] { 0, 0, 0 };

            if (!(this is TracksideCamera) && !(this is FreeRoamCamera) && AttachedCar != null)
            {
                cameraVelocity = AttachedCar.Velocity;
            }

            float[] cameraOrientation = new float[] { 
                        XNAView.Backward.X, XNAView.Backward.Y, XNAView.Backward.Z,
                        XNAView.Down.X, XNAView.Down.Y, XNAView.Down.Z };

            OpenAL.alListenerfv(OpenAL.AL_POSITION, cameraPosition);
            OpenAL.alListenerfv(OpenAL.AL_VELOCITY, cameraVelocity);
            OpenAL.alListenerfv(OpenAL.AL_ORIENTATION, cameraOrientation);
        }
    }

    public abstract class LookAtCamera : Camera
    {
        protected WorldLocation targetLocation = new WorldLocation();
        public WorldLocation TargetWorldLocation { get { return targetLocation; } }

        public override bool IsUnderground
        {
            get
            {
                var elevationAtTarget = Viewer.Tiles.GetElevation(targetLocation);
                return targetLocation.Location.Y + TerrainAltitudeMargin < elevationAtTarget;
            }
        }

        protected LookAtCamera(Viewer3D viewer)
            : base(viewer)
        {
        }

        protected internal override void Save(BinaryWriter outf)
        {
            base.Save(outf);
            targetLocation.Save(outf);
        }

        protected internal override void Restore(BinaryReader inf)
        {
            base.Restore(inf);
            targetLocation.Restore(inf);
        }

        protected override Matrix GetCameraView()
        {
            return Matrix.CreateLookAt(XNALocation(cameraLocation), XNALocation(targetLocation), Vector3.UnitY);
        }
    }

    public abstract class RotatingCamera : Camera
    {
        // Current camera values
        protected float RotationXRadians;
        protected float RotationYRadians;
        protected float XRadians;
        protected float YRadians;
        protected float ZRadians;

        // Target camera values
        public float? RotationXTargetRadians;
        public float? RotationYTargetRadians;
        public float? XTargetRadians;
        public float? YTargetRadians;
        public float? ZTargetRadians;
        public double EndTime;

        protected float axisZSpeedBoost = 1.0f;

        protected RotatingCamera(Viewer3D viewer)
            : base(viewer)
        {
        }

        protected RotatingCamera(Viewer3D viewer, Camera previousCamera)
            : base(viewer, previousCamera)
        {
            if (previousCamera != null)
            {
                float h, a, b;
                ORTSMath.MatrixToAngles(previousCamera.XNAView, out h, out a, out b);
                RotationXRadians = -b;
                RotationYRadians = -h;
            }
        }

        protected internal override void Save(BinaryWriter outf)
        {
            base.Save(outf);
            outf.Write(RotationXRadians);
            outf.Write(RotationYRadians);
        }

        protected internal override void Restore(BinaryReader inf)
        {
            base.Restore(inf);
            RotationXRadians = inf.ReadSingle();
            RotationYRadians = inf.ReadSingle();
        }

        public override void Reset()
        {
            base.Reset();
            RotationXRadians = RotationYRadians = XRadians = YRadians = ZRadians = 0;
        }

        protected override Matrix GetCameraView()
        {
            var lookAtPosition = Vector3.UnitZ;
            lookAtPosition = Vector3.Transform(lookAtPosition, Matrix.CreateRotationX(RotationXRadians));
            lookAtPosition = Vector3.Transform(lookAtPosition, Matrix.CreateRotationY(RotationYRadians));
            lookAtPosition += cameraLocation.Location;
            lookAtPosition.Z *= -1;
            return Matrix.CreateLookAt(XNALocation(cameraLocation), lookAtPosition, Vector3.Up);
        }

        protected static float GetMouseDelta(int mouseMovementPixels)
        {
            // Ignore CameraMoveFast as that is too fast to be useful
            var delta = 0.01f;
            if (UserInput.IsDown(UserCommands.CameraMoveSlow))
                delta *= 0.1f;
            return delta * mouseMovementPixels;
        }

        protected void RotateByMouse()
        {
            if (UserInput.IsMouseRightButtonDown())
            {
                // Mouse movement doesn't use 'var speed' because the MouseMove 
                // parameters are already scaled down with increasing frame rates, 
                RotationXRadians += GetMouseDelta(UserInput.MouseMoveY());
                RotationYRadians += GetMouseDelta(UserInput.MouseMoveX());
            }
            // Support for replaying mouse movements
            if (UserInput.IsMouseRightButtonPressed())
            {
                Viewer.CheckReplaying();
                CommandStartTime = Viewer.Simulator.ClockTime;
            }
            if (UserInput.IsMouseRightButtonReleased())
            {
                var commandEndTime = Viewer.Simulator.ClockTime;
                new CameraMouseRotateCommand(Viewer.Log, CommandStartTime, commandEndTime, RotationXRadians, RotationYRadians);
            }
        }

        protected void UpdateRotation(ElapsedTime elapsedTime)
        {
            var replayRemainingS = EndTime - Viewer.Simulator.ClockTime;
            if (replayRemainingS > 0)
            {
                var replayFraction = elapsedTime.ClockSeconds / replayRemainingS;
                if (RotationXTargetRadians != null && RotationYTargetRadians != null)
                {
                    var replayRemainingX = RotationXTargetRadians - RotationXRadians;
                    var replayRemainingY = RotationYTargetRadians - RotationYRadians;
                    var replaySpeedX = (float)(replayRemainingX * replayFraction);
                    var replaySpeedY = (float)(replayRemainingY * replayFraction);

                    if (IsCloseEnough(RotationXRadians, RotationXTargetRadians, replaySpeedX))
                    {
                        RotationXTargetRadians = null;
                    }
                    else
                    {
                        RotateDown(replaySpeedX);
                    }
                    if (IsCloseEnough(RotationYRadians, RotationYTargetRadians, replaySpeedY))
                    {
                        RotationYTargetRadians = null;
                    }
                    else
                    {
                        RotateRight(replaySpeedY);
                    }
                }
                else
                {
                    if (RotationXTargetRadians != null)
                    {
                        var replayRemainingX = RotationXTargetRadians - RotationXRadians;
                        var replaySpeedX = (float)(replayRemainingX * replayFraction);
                        if (IsCloseEnough(RotationXRadians, RotationXTargetRadians, replaySpeedX))
                        {
                            RotationXTargetRadians = null;
                        }
                        else
                        {
                            RotateDown(replaySpeedX);
                        }
                    }
                    if (RotationYTargetRadians != null)
                    {
                        var replayRemainingY = RotationYTargetRadians - RotationYRadians;
                        var replaySpeedY = (float)(replayRemainingY * replayFraction);
                        if (IsCloseEnough(RotationYRadians, RotationYTargetRadians, replaySpeedY))
                        {
                            RotationYTargetRadians = null;
                        }
                        else
                        {
                            RotateRight(replaySpeedY);
                        }
                    }
                }
            }
        }

        protected virtual void RotateDown(float speed)
        {
            RotationXRadians += speed;
            RotationXRadians = VerticalClamper.Clamp(RotationXRadians);
            MoveCamera();
        }

        protected virtual void RotateRight(float speed)
        {
            RotationYRadians += speed;
            MoveCamera();
        }

        protected void MoveCamera()
        {
            MoveCamera(new Vector3(0, 0, 0));
        }

        protected void MoveCamera(Vector3 movement)
        {
            movement = Vector3.Transform(movement, Matrix.CreateRotationX(RotationXRadians));
            movement = Vector3.Transform(movement, Matrix.CreateRotationY(RotationYRadians));
            cameraLocation.Location += movement;
            cameraLocation.Normalize();
        }

        protected virtual void ZoomIn(float speed)
        {
        }

        // <CJComment> To Do: Add a way to record this zoom operation. </CJComment>
        protected void ZoomByMouseWheel(float speed, float factor)
        {
            // Will not zoom-in-out when help windows is up.
            // TODO: Propery input processing through WindowManager.
            if (UserInput.IsMouseWheelChanged() && !Viewer.HelpWindow.Visible)
                ZoomIn(Math.Sign(UserInput.MouseWheelChange()) * speed * factor);
        }

        /// <summary>
        /// A margin of half a step (increment/2) is used to prevent hunting once the target is reached.
        /// </summary>
        /// <param name="current"></param>
        /// <param name="target"></param>
        /// <param name="increment"></param>
        /// <returns></returns>
        protected static bool IsCloseEnough(float current, float? target, float increment)
        {
            Trace.Assert(target != null, "Camera target position must not be null");
            // If a pause interrupts a camera movement, then the increment will become zero.
            if (increment == 0)
            {  // To avoid divide by zero error, just kill the movement.
                return true;
            }
            else
            {
                var error = (float)target - current;
                return error / increment < 0.5;
            }
        }
    }

    public class FreeRoamCamera : RotatingCamera
    {
        const float maxCameraHeight = 1000f;
        const float ZoomFactor = 2f;

        public FreeRoamCamera(Viewer3D viewer, Camera previousCamera)
            : base(viewer, previousCamera)
        {
        }

        public void SetLocation(WorldLocation location)
        {
            cameraLocation = location;
        }

        public override void Reset()
        {
            // Intentionally do nothing at all.
        }

        public override void HandleUserInput(ElapsedTime elapsedTime)
        {
            if (UserInput.IsDown(UserCommands.CameraZoomIn) || UserInput.IsDown(UserCommands.CameraZoomOut))
            {
                var elevation = Viewer.Tiles.GetElevation(cameraLocation);
                if (cameraLocation.Location.Y < elevation)
                    axisZSpeedBoost = 1;
                else
                {
                    cameraLocation.Location.Y = MathHelper.Min(cameraLocation.Location.Y, elevation + maxCameraHeight);
                    float cameraRelativeHeight = cameraLocation.Location.Y - elevation;
                    axisZSpeedBoost = ((cameraRelativeHeight / maxCameraHeight) * 50) + 1;
                }
            }

            var speed = GetSpeed(elapsedTime);

            // Pan and zoom camera
            if (UserInput.IsDown(UserCommands.CameraPanRight)) PanRight(speed);
            if (UserInput.IsDown(UserCommands.CameraPanLeft)) PanRight(-speed);
            if (UserInput.IsDown(UserCommands.CameraPanUp)) PanUp(speed);
            if (UserInput.IsDown(UserCommands.CameraPanDown)) PanUp(-speed);
            if (UserInput.IsDown(UserCommands.CameraZoomIn)) ZoomIn(speed * ZoomFactor);
            if (UserInput.IsDown(UserCommands.CameraZoomOut)) ZoomIn(-speed * ZoomFactor);
            ZoomByMouseWheel(speed, ZoomFactor * 2);  // * 2 to get similar speed as with keypress

            if (UserInput.IsPressed(UserCommands.CameraPanRight) || UserInput.IsPressed(UserCommands.CameraPanLeft))
            {
                Viewer.CheckReplaying();
                CommandStartTime = Viewer.Simulator.ClockTime;
            }
            if (UserInput.IsReleased(UserCommands.CameraPanRight) || UserInput.IsReleased(UserCommands.CameraPanLeft))
                new CameraXCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, XRadians);

            if (UserInput.IsPressed(UserCommands.CameraPanUp) || UserInput.IsPressed(UserCommands.CameraPanDown))
            {
                Viewer.CheckReplaying();
                CommandStartTime = Viewer.Simulator.ClockTime;
            }
            if (UserInput.IsReleased(UserCommands.CameraPanUp) || UserInput.IsReleased(UserCommands.CameraPanDown))
                new CameraYCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, YRadians);

            if (UserInput.IsPressed(UserCommands.CameraZoomIn) || UserInput.IsPressed(UserCommands.CameraZoomOut))
            {
                Viewer.CheckReplaying();
                CommandStartTime = Viewer.Simulator.ClockTime;
            }
            if (UserInput.IsReleased(UserCommands.CameraZoomIn) || UserInput.IsReleased(UserCommands.CameraZoomOut))
                new CameraZCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, ZRadians);

            speed *= SpeedAdjustmentForRotation;
            RotateByMouse();

            // Rotate camera
            if (UserInput.IsDown(UserCommands.CameraRotateUp)) RotateDown(-speed);
            if (UserInput.IsDown(UserCommands.CameraRotateDown)) RotateDown(speed);
            if (UserInput.IsDown(UserCommands.CameraRotateLeft)) RotateRight(-speed);
            if (UserInput.IsDown(UserCommands.CameraRotateRight)) RotateRight(speed);

            // Support for replaying camera rotation movements
            if (UserInput.IsPressed(UserCommands.CameraRotateUp) || UserInput.IsPressed(UserCommands.CameraRotateDown))
            {
                Viewer.CheckReplaying();
                CommandStartTime = Viewer.Simulator.ClockTime;
            }
            if (UserInput.IsReleased(UserCommands.CameraRotateUp) || UserInput.IsReleased(UserCommands.CameraRotateDown))
                new CameraRotateUpDownCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, RotationXRadians);

            if (UserInput.IsPressed(UserCommands.CameraRotateLeft) || UserInput.IsPressed(UserCommands.CameraRotateRight))
            {
                Viewer.CheckReplaying();
                CommandStartTime = Viewer.Simulator.ClockTime;
            }
            if (UserInput.IsReleased(UserCommands.CameraRotateLeft) || UserInput.IsReleased(UserCommands.CameraRotateRight))
                new CameraRotateLeftRightCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, RotationYRadians);
        }

        public override void Update(ElapsedTime elapsedTime)
        {
            UpdateRotation(elapsedTime);

            var replayRemainingS = EndTime - Viewer.Simulator.ClockTime;
            if (replayRemainingS > 0)
            {
                var replayFraction = elapsedTime.ClockSeconds / replayRemainingS;
                // Panning
                if (XTargetRadians != null)
                {
                    var replayRemainingX = XTargetRadians - XRadians;
                    var replaySpeedX = Math.Abs((float)(replayRemainingX * replayFraction));
                    if (IsCloseEnough(XRadians, XTargetRadians, replaySpeedX))
                    {
                        XTargetRadians = null;
                    }
                    else
                    {
                        PanRight(replaySpeedX);
                    }
                }
                if (YTargetRadians != null)
                {
                    var replayRemainingY = YTargetRadians - YRadians;
                    var replaySpeedY = Math.Abs((float)(replayRemainingY * replayFraction));
                    if (IsCloseEnough(YRadians, YTargetRadians, replaySpeedY))
                    {
                        YTargetRadians = null;
                    }
                    else
                    {
                        PanUp(replaySpeedY);
                    }
                }
                // Zooming
                if (ZTargetRadians != null)
                {
                    var replayRemainingZ = ZTargetRadians - ZRadians;
                    var replaySpeedZ = Math.Abs((float)(replayRemainingZ * replayFraction));
                    if (IsCloseEnough(ZRadians, ZTargetRadians, replaySpeedZ))
                    {
                        ZTargetRadians = null;
                    }
                    else
                    {
                        ZoomIn(replaySpeedZ);
                    }
                }
            }
            UpdateListener();
        }

        protected virtual void PanRight(float speed)
        {
            var movement = new Vector3(0, 0, 0);
            movement.X += speed;
            XRadians += movement.X;
            MoveCamera(movement);
        }

        protected virtual void PanUp(float speed)
        {
            var movement = new Vector3(0, 0, 0);
            movement.Y += speed;
            movement.Y = VerticalClamper.Clamp(movement.Y);    // Only the vertical needs to be clamped
            YRadians += movement.Y;
            MoveCamera(movement);
        }

        protected override void ZoomIn(float speed)
        {
            var movement = new Vector3(0, 0, 0);
            movement.Z += speed;
            ZRadians += movement.Z;
            MoveCamera(movement);
        }
    }

    public abstract class AttachedCamera : RotatingCamera
    {
        protected TrainCar attachedCar;
        public override TrainCar AttachedCar { get { return attachedCar; } }
        public bool tiltingLand;
        protected Vector3 attachedLocation;

        protected AttachedCamera(Viewer3D viewer)
            : base(viewer)
        {
        }

        protected internal override void Save(BinaryWriter outf)
        {
            base.Save(outf);
            if (attachedCar != null && attachedCar.Train != null && attachedCar.Train == Viewer.SelectedTrain)
                outf.Write(Viewer.SelectedTrain.Cars.IndexOf(attachedCar));
            else
                outf.Write((int)-1);
            outf.Write(attachedLocation.X);
            outf.Write(attachedLocation.Y);
            outf.Write(attachedLocation.Z);
        }

        protected internal override void Restore(BinaryReader inf)
        {
            base.Restore(inf);
            var carIndex = inf.ReadInt32();
            if (carIndex != -1 && Viewer.SelectedTrain != null)
                attachedCar = Viewer.SelectedTrain.Cars[carIndex];
            attachedLocation.X = inf.ReadSingle();
            attachedLocation.Y = inf.ReadSingle();
            attachedLocation.Z = inf.ReadSingle();
        }

        protected override void OnActivate(bool sameCamera)
        {
            if (attachedCar == null || attachedCar.Train != Viewer.SelectedTrain)
            {
                if (Viewer.SelectedTrain.MUDirection != Direction.Reverse)
                    SetCameraCar(GetCameraCars().First());
                else
                    SetCameraCar(GetCameraCars().Last());
            }
            base.OnActivate(sameCamera);
        }

        protected virtual List<TrainCar> GetCameraCars()
        {
            return Viewer.SelectedTrain.Cars;
        }

        protected virtual void SetCameraCar(TrainCar car)
        {
            attachedCar = car;
        }

        protected virtual bool IsCameraFlipped()
        {
            return false;
        }

        protected void MoveCar()
        {
            if (UserInput.IsPressed(UserCommands.CameraCarNext))
                new NextCarCommand(Viewer.Log);
            else if (UserInput.IsPressed(UserCommands.CameraCarPrevious))
                new PreviousCarCommand(Viewer.Log);
            else if (UserInput.IsPressed(UserCommands.CameraCarFirst))
                new FirstCarCommand(Viewer.Log);
            else if (UserInput.IsPressed(UserCommands.CameraCarLast))
                new LastCarCommand(Viewer.Log);
        }

        public void NextCar()
        {
            var trainCars = GetCameraCars();
            SetCameraCar(attachedCar == trainCars.First() ? attachedCar : trainCars[trainCars.IndexOf(attachedCar) - 1]);
        }

        public void PreviousCar()
        {
            var trainCars = GetCameraCars();
            SetCameraCar(attachedCar == trainCars.Last() ? attachedCar : trainCars[trainCars.IndexOf(attachedCar) + 1]);
        }

        public void FirstCar()
        {
            var trainCars = GetCameraCars();
            SetCameraCar(trainCars.First());
        }

        public void LastCar()
        {
            var trainCars = GetCameraCars();
            SetCameraCar(trainCars.Last());
        }

        public void UpdateLocation()
        {
            if (attachedCar != null)
            {
                cameraLocation.TileX = attachedCar.WorldPosition.TileX;
                cameraLocation.TileZ = attachedCar.WorldPosition.TileZ;
                if (IsCameraFlipped())
                {
                    cameraLocation.Location.X = -attachedLocation.X;
                    cameraLocation.Location.Y = attachedLocation.Y;
                    cameraLocation.Location.Z = -attachedLocation.Z;
                }
                else
                {
                    cameraLocation.Location.X = attachedLocation.X;
                    cameraLocation.Location.Y = attachedLocation.Y;
                    cameraLocation.Location.Z = attachedLocation.Z;
                }
                cameraLocation.Location.Z *= -1;
                cameraLocation.Location = Vector3.Transform(cameraLocation.Location, attachedCar.GetXNAMatrix());
                cameraLocation.Location.Z *= -1;
            }
        }

        private void FixCameraLocation()
        {
            var elevationAtCamera = Viewer.Tiles.GetElevation(cameraLocation);

            Console.WriteLine(elevationAtCamera.ToString() + " : " + cameraLocation.Location.Y.ToString());
            if (elevationAtCamera > cameraLocation.Location.Y)
            {
                cameraLocation.Location.Y = elevationAtCamera;
            }
        }

        protected override Matrix GetCameraView()
        {
            var flipped = IsCameraFlipped();
            var lookAtPosition = Vector3.UnitZ;
            lookAtPosition = Vector3.Transform(lookAtPosition, Matrix.CreateRotationX(RotationXRadians));
            lookAtPosition = Vector3.Transform(lookAtPosition, Matrix.CreateRotationY(RotationYRadians + (flipped ? MathHelper.Pi : 0)));
            if (flipped)
            {
                lookAtPosition.X -= attachedLocation.X;
                lookAtPosition.Y += attachedLocation.Y;
                lookAtPosition.Z -= attachedLocation.Z;
            }
            else
            {
                lookAtPosition.X += attachedLocation.X;
                lookAtPosition.Y += attachedLocation.Y;
                lookAtPosition.Z += attachedLocation.Z;
            }
            lookAtPosition.Z *= -1;
            lookAtPosition = Vector3.Transform(lookAtPosition, attachedCar.GetXNAMatrix());
            if (tiltingLand && Program.Simulator.CabRotating > 0)
            {
				if (attachedCar.HasFreightAnim)//cars with freight animation will rotate only camera, the land will rotate with the camera, so the FA can follow
				{
					var up = (Matrix.CreateRotationZ( Program.Simulator.CabRotating * attachedCar.totalRotationZ) * attachedCar.GetXNAMatrix()).Up;
					return Matrix.CreateLookAt(XNALocation(cameraLocation), lookAtPosition, up);//Vector3.Transform(Vector3.Up, Matrix.CreateRotationZ(3 * attachedCar.totalRotationZ)));
				}
				else
				{
					var up = (Matrix.CreateRotationZ((Program.Simulator.CabRotating - 4) * attachedCar.totalRotationZ) * attachedCar.GetXNAMatrix()).Up;
					return Matrix.CreateLookAt(XNALocation(cameraLocation), lookAtPosition, up);//Vector3.Transform(Vector3.Up, Matrix.CreateRotationZ(3 * attachedCar.totalRotationZ)));
				}
            }
            else
                return Matrix.CreateLookAt(XNALocation(cameraLocation), lookAtPosition, Vector3.Up);
        }

        public override void Update(ElapsedTime elapsedTime)
        {
            if (attachedCar != null)
            {
                cameraLocation.TileX = attachedCar.WorldPosition.TileX;
                cameraLocation.TileZ = attachedCar.WorldPosition.TileZ;
                if (IsCameraFlipped())
                {
                    cameraLocation.Location.X = -attachedLocation.X;
                    cameraLocation.Location.Y = attachedLocation.Y;
                    cameraLocation.Location.Z = -attachedLocation.Z;
                }
                else
                {
                    cameraLocation.Location.X = attachedLocation.X;
                    cameraLocation.Location.Y = attachedLocation.Y;
                    cameraLocation.Location.Z = attachedLocation.Z;
                }
                cameraLocation.Location.Z *= -1;
                cameraLocation.Location = Vector3.Transform(cameraLocation.Location, attachedCar.GetXNAMatrix());
                cameraLocation.Location.Z *= -1;
            }
            UpdateRotation(elapsedTime);
            UpdateListener();
        }
    }

    public class TrackingCamera : AttachedCamera
    {
        const float StartPositionDistance = 20;
        const float StartPositionXRadians = 0.399f;
        const float StartPositionYRadians = 0.387f;

        protected readonly bool Front;
        public enum AttachedTo { Front, Rear }
        const float ZoomFactor = 0.1f;

        protected float PositionDistance = StartPositionDistance;
        protected float PositionXRadians = StartPositionXRadians;
        protected float PositionYRadians = StartPositionYRadians;
        public float? PositionDistanceTargetMetres;
        public float? PositionXTargetRadians;
        public float? PositionYTargetRadians;

        public override bool IsUnderground
        {
            get
            {
                var elevationAtTrain = Viewer.Tiles.GetElevation(attachedCar.WorldPosition.WorldLocation);
                var elevationAtCamera = Viewer.Tiles.GetElevation(cameraLocation);
                return attachedCar.WorldPosition.WorldLocation.Location.Y + TerrainAltitudeMargin < elevationAtTrain || cameraLocation.Location.Y + TerrainAltitudeMargin < elevationAtCamera;
            }
        }

        public TrackingCamera(Viewer3D viewer, AttachedTo attachedTo)
            : base(viewer)
        {
            Front = attachedTo == AttachedTo.Front;
            PositionYRadians = StartPositionYRadians + (Front ? 0 : MathHelper.Pi);
            RotationXRadians = PositionXRadians;
            RotationYRadians = PositionYRadians - MathHelper.Pi;
        }

        protected internal override void Save(BinaryWriter outf)
        {
            base.Save(outf);
            outf.Write(PositionDistance);
            outf.Write(PositionXRadians);
            outf.Write(PositionYRadians);
        }

        protected internal override void Restore(BinaryReader inf)
        {
            base.Restore(inf);
            PositionDistance = inf.ReadSingle();
            PositionXRadians = inf.ReadSingle();
            PositionYRadians = inf.ReadSingle();
        }

        public override void Reset()
        {
            base.Reset();
            PositionDistance = StartPositionDistance;
            PositionXRadians = StartPositionXRadians;
            PositionYRadians = StartPositionYRadians + (Front ? 0 : MathHelper.Pi);
            RotationXRadians = PositionXRadians;
            RotationYRadians = PositionYRadians - MathHelper.Pi;
        }

        protected override void OnActivate(bool sameCamera)
        {
            if (attachedCar == null || attachedCar.Train != Viewer.SelectedTrain)
            {
                if (Front)
                    SetCameraCar(GetCameraCars().First());
                else
                    SetCameraCar(GetCameraCars().Last());
            }
            base.OnActivate(sameCamera);
        }

        protected override bool IsCameraFlipped()
        {
            return attachedCar.Flipped;
        }

        public override void HandleUserInput(ElapsedTime elapsedTime)
        {
            MoveCar();

            var speed = GetSpeed(elapsedTime) * ZoomFactor;

            if (UserInput.IsDown(UserCommands.CameraZoomOut)) ZoomIn(speed);
            if (UserInput.IsDown(UserCommands.CameraZoomIn)) ZoomIn(-speed);
            ZoomByMouseWheel(speed, ZoomFactor * 20);// * 20 to get similar speed as with keypress

            if (UserInput.IsPressed(UserCommands.CameraZoomOut) || UserInput.IsPressed(UserCommands.CameraZoomIn))
            {
                Viewer.CheckReplaying();
                CommandStartTime = Viewer.Simulator.ClockTime;
            }
            if (UserInput.IsReleased(UserCommands.CameraZoomOut) || UserInput.IsReleased(UserCommands.CameraZoomIn))
                new TrackingCameraZCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, PositionDistance);

            speed = GetSpeed(elapsedTime) * SpeedAdjustmentForRotation;

            // Pan camera
            if (UserInput.IsDown(UserCommands.CameraPanUp)) PanUp(speed);
            if (UserInput.IsDown(UserCommands.CameraPanDown)) PanUp(-speed);
            if (UserInput.IsDown(UserCommands.CameraPanLeft)) PanRight(speed);
            if (UserInput.IsDown(UserCommands.CameraPanRight)) PanRight(-speed);

            // Support for replaying camera pan movements
            if (UserInput.IsPressed(UserCommands.CameraPanUp) || UserInput.IsPressed(UserCommands.CameraPanDown))
            {
                Viewer.CheckReplaying();
                CommandStartTime = Viewer.Simulator.ClockTime;
            }
            if (UserInput.IsReleased(UserCommands.CameraPanUp) || UserInput.IsReleased(UserCommands.CameraPanDown))
                new TrackingCameraXCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, PositionXRadians);

            if (UserInput.IsPressed(UserCommands.CameraPanLeft) || UserInput.IsPressed(UserCommands.CameraPanRight))
            {
                Viewer.CheckReplaying();
                CommandStartTime = Viewer.Simulator.ClockTime;
            }
            if (UserInput.IsReleased(UserCommands.CameraPanLeft) || UserInput.IsReleased(UserCommands.CameraPanRight))
                new TrackingCameraYCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, PositionYRadians);

            RotateByMouse();

            // Rotate camera
            if (UserInput.IsDown(UserCommands.CameraRotateUp)) RotateDown(-speed);
            if (UserInput.IsDown(UserCommands.CameraRotateDown)) RotateDown(speed);
            if (UserInput.IsDown(UserCommands.CameraRotateLeft)) RotateRight(-speed);
            if (UserInput.IsDown(UserCommands.CameraRotateRight)) RotateRight(speed);

            // Support for replaying camera rotation movements
            if (UserInput.IsPressed(UserCommands.CameraRotateUp) || UserInput.IsPressed(UserCommands.CameraRotateDown))
            {
                Viewer.CheckReplaying();
                CommandStartTime = Viewer.Simulator.ClockTime;
            }
            if (UserInput.IsReleased(UserCommands.CameraRotateUp) || UserInput.IsReleased(UserCommands.CameraRotateDown))
                new CameraRotateUpDownCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, RotationXRadians);

            if (UserInput.IsPressed(UserCommands.CameraRotateLeft) || UserInput.IsPressed(UserCommands.CameraRotateRight))
            {
                Viewer.CheckReplaying();
                CommandStartTime = Viewer.Simulator.ClockTime;
            }
            if (UserInput.IsReleased(UserCommands.CameraRotateLeft) || UserInput.IsReleased(UserCommands.CameraRotateRight))
                new CameraRotateLeftRightCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, RotationYRadians);
        }

        public override void Update(ElapsedTime elapsedTime)
        {
            var replayRemainingS = EndTime - Viewer.Simulator.ClockTime;
            if (replayRemainingS > 0)
            {
                var replayFraction = elapsedTime.ClockSeconds / replayRemainingS;
                // Panning
                if (PositionXTargetRadians != null)
                {
                    var replayRemainingX = PositionXTargetRadians - PositionXRadians;
                    var replaySpeedX = (float)(replayRemainingX * replayFraction);
                    if (IsCloseEnough(PositionXRadians, PositionXTargetRadians, replaySpeedX))
                    {
                        PositionXTargetRadians = null;
                    }
                    else
                    {
                        PanUp(replaySpeedX);
                    }
                }
                if (PositionYTargetRadians != null)
                {
                    var replayRemainingY = PositionYTargetRadians - PositionYRadians;
                    var replaySpeedY = (float)(replayRemainingY * replayFraction);
                    if (IsCloseEnough(PositionYRadians, PositionYTargetRadians, replaySpeedY))
                    {
                        PositionYTargetRadians = null;
                    }
                    else
                    {
                        PanRight(replaySpeedY);
                    }
                }
                // Zooming
                if (PositionDistanceTargetMetres != null)
                {
                    var replayRemainingZ = PositionDistanceTargetMetres - PositionDistance;
                    var replaySpeedZ = (float)(replayRemainingZ * replayFraction);
                    if (IsCloseEnough(PositionDistance, PositionDistanceTargetMetres, replaySpeedZ))
                    {
                        PositionDistanceTargetMetres = null;
                    }
                    else
                    {
                        ZoomIn(replaySpeedZ / PositionDistance);
                    }
                }
            }

            // Rotation
            UpdateRotation(elapsedTime);

            // Update location of attachment
            attachedLocation.X = 0;
            attachedLocation.Y = 2;
            attachedLocation.Z = PositionDistance;
            attachedLocation = Vector3.Transform(attachedLocation, Matrix.CreateRotationX(-PositionXRadians));
            attachedLocation = Vector3.Transform(attachedLocation, Matrix.CreateRotationY(PositionYRadians));
            attachedLocation.Z += attachedCar.LengthM / 2.0f * (Front ? 1 : -1);

            // Update location of camera
            UpdateLocation();
            UpdateListener();
        }

        protected void PanUp(float speed)
        {
            PositionXRadians += speed;
            PositionXRadians = VerticalClamper.Clamp(PositionXRadians);
            RotationXRadians += speed;
            RotationXRadians = VerticalClamper.Clamp(RotationXRadians);
        }

        protected void PanRight(float speed)
        {
            PositionYRadians += speed;
            RotationYRadians += speed;
        }

        protected override void ZoomIn(float speed)
        {
            // Speed depends on distance, slows down when zooming in, speeds up zooming out.
            PositionDistance += speed * PositionDistance;
            PositionDistance = MathHelper.Clamp(PositionDistance, 1, 100);
        }
    }

    public abstract class NonTrackingCamera : AttachedCamera
    {
        public NonTrackingCamera(Viewer3D viewer)
            : base(viewer)
        {
        }

        public override void HandleUserInput(ElapsedTime elapsedTime)
        {
            MoveCar();

            RotateByMouse();

            var speed = GetSpeed(elapsedTime) * SpeedAdjustmentForRotation;

            // Rotate camera
            if (UserInput.IsDown(UserCommands.CameraRotateUp) || UserInput.IsDown(UserCommands.CameraPanUp)) RotateDown(-speed);
            if (UserInput.IsDown(UserCommands.CameraRotateDown) || UserInput.IsDown(UserCommands.CameraPanDown)) RotateDown(speed);
            if (UserInput.IsDown(UserCommands.CameraRotateLeft) || UserInput.IsDown(UserCommands.CameraPanLeft)) RotateRight(-speed);
            if (UserInput.IsDown(UserCommands.CameraRotateRight) || UserInput.IsDown(UserCommands.CameraPanRight)) RotateRight(speed);

            // Support for replaying camera rotation movements
            if (UserInput.IsPressed(UserCommands.CameraRotateUp) || UserInput.IsPressed(UserCommands.CameraRotateDown)
                || UserInput.IsPressed(UserCommands.CameraPanUp) || UserInput.IsPressed(UserCommands.CameraPanDown))
                CommandStartTime = Viewer.Simulator.ClockTime;
            if (UserInput.IsReleased(UserCommands.CameraRotateUp) || UserInput.IsReleased(UserCommands.CameraRotateDown)
                || UserInput.IsReleased(UserCommands.CameraPanUp) || UserInput.IsReleased(UserCommands.CameraPanDown))
                new CameraRotateUpDownCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, RotationXRadians);

            if (UserInput.IsPressed(UserCommands.CameraRotateLeft) || UserInput.IsPressed(UserCommands.CameraRotateRight)
                || UserInput.IsPressed(UserCommands.CameraPanLeft) || UserInput.IsPressed(UserCommands.CameraPanRight))
                CommandStartTime = Viewer.Simulator.ClockTime;
            if (UserInput.IsReleased(UserCommands.CameraRotateLeft) || UserInput.IsReleased(UserCommands.CameraRotateRight)
                || UserInput.IsReleased(UserCommands.CameraPanLeft) || UserInput.IsReleased(UserCommands.CameraPanRight))
                new CameraRotateLeftRightCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, RotationYRadians);
        }
    }

    public class BrakemanCamera : NonTrackingCamera
    {
        protected bool attachedToRear;

        public override float NearPlane { get { return 0.25f; } }

        public BrakemanCamera(Viewer3D viewer)
            : base(viewer)
        {
        }

        protected override List<TrainCar> GetCameraCars()
        {
            var cars = base.GetCameraCars();
            return new List<TrainCar>(new[] { cars.First(), cars.Last() });
        }

        protected override void SetCameraCar(TrainCar car)
        {
            base.SetCameraCar(car);
            attachedLocation = new Vector3(1.8f, 2.0f, attachedCar.LengthM / 2 - 0.3f);
            attachedToRear = car.Train.Cars[0] != car;
        }

        protected override bool IsCameraFlipped()
        {
            return attachedToRear ^ attachedCar.Flipped;
        }

    }

    public class PassengerCamera : NonTrackingCamera
    {
        public override Styles Style { get { return Styles.Passenger; } }
        public override bool IsAvailable { get { return Viewer.SelectedTrain != null && Viewer.SelectedTrain.Cars.Any(c => c.PassengerViewpoints.Count > 0); } }
        public override float NearPlane { get { return 0.1f; } }

        public PassengerCamera(Viewer3D viewer)
            : base(viewer)
        {
        }

        protected override List<TrainCar> GetCameraCars()
        {
            return base.GetCameraCars().Where(c => c.PassengerViewpoints.Count > 0).ToList();
        }

        protected override void SetCameraCar(TrainCar car)
        {
            base.SetCameraCar(car);
            var viewPoint = attachedCar.PassengerViewpoints[0];
            attachedLocation = viewPoint.Location;
            // <CJComment> More useful without resetting. </CJComment>
            //RotationXRadians = MSTSMath.M.Radians( viewPoint.StartDirection.X );
            //RotationYRadians = MSTSMath.M.Radians( viewPoint.StartDirection.Y );
        }
    }

    public class HeadOutCamera : NonTrackingCamera
    {
        protected readonly bool Forwards;
        public enum HeadDirection { Forward, Backward }
        protected int CurrentViewpointIndex;
        protected bool PrevCabWasRear;

        // Head-out camera is only possible on the player train.
        public override bool IsAvailable { get { return Viewer.PlayerTrain != null && Viewer.PlayerTrain.Cars.Any(c => c.HeadOutViewpoints.Count > 0); } }
        public override float NearPlane { get { return 0.25f; } }

        public HeadOutCamera(Viewer3D viewer, HeadDirection headDirection)
            : base(viewer)
        {
            Forwards = headDirection == HeadDirection.Forward;
            RotationYRadians = Forwards ? 0 : -MathHelper.Pi;
        }

        protected override List<TrainCar> GetCameraCars()
        {
            // Head-out camera is only possible on the player train.
            return Viewer.PlayerTrain.Cars.Where(c => c.HeadOutViewpoints.Count > 0).ToList();
        }

        protected override void SetCameraCar(TrainCar car)
        {
            base.SetCameraCar(car);
            if (attachedCar.HeadOutViewpoints.Count > 0)
                attachedLocation = attachedCar.HeadOutViewpoints[CurrentViewpointIndex].Location;

            if (!Forwards)
                attachedLocation.X *= -1;
        }

        public void ChangeCab(TrainCar newCar)
        {
            var mstsLocomotive = newCar as MSTSLocomotive;
            if (PrevCabWasRear != mstsLocomotive.UsingRearCab)
                RotationYRadians += MathHelper.Pi;
            CurrentViewpointIndex = mstsLocomotive.UsingRearCab ? 1 : 0;
            PrevCabWasRear = mstsLocomotive.UsingRearCab;
            SetCameraCar(newCar);
        }
    }

    public class CabCamera : NonTrackingCamera
    {
        protected int sideLocation;
        public int SideLocation { get { return sideLocation; } }

        public override Styles Style { get { return Styles.Cab; } }
        // Cab camera is only possible on the player train.
        public override bool IsAvailable { get { return Viewer.PlayerLocomotive != null && Viewer.PlayerLocomotive.HasFrontCab; } }

        public override bool IsUnderground
        {
            get
            {
                // Camera is underground if target (base) is underground or
                // track location is underground. The latter means we switch
                // to cab view instead of putting the camera above the tunnel.
                if (base.IsUnderground)
                    return true;
                var elevationAtCameraTarget = Viewer.Tiles.GetElevation(attachedCar.WorldPosition.WorldLocation);
                return attachedCar.WorldPosition.Location.Y + TerrainAltitudeMargin < elevationAtCameraTarget;
            }
        }

        public CabCamera(Viewer3D viewer)
            : base(viewer)
        {
        }

        protected internal override void Save(BinaryWriter outf)
        {
            base.Save(outf);
            outf.Write(sideLocation);
        }

        protected internal override void Restore(BinaryReader inf)
        {
            base.Restore(inf);
            sideLocation = inf.ReadInt32();
        }

        public override void Reset()
        {
            base.Reset();
            Viewer.CabYOffsetPixels = (Viewer.DisplaySize.Y - Viewer.CabHeightPixels) / 2;
            OnActivate(true);
        }

        protected override void OnActivate(bool sameCamera)
        {
            // Cab camera is only possible on the player locomotive.
            SetCameraCar(GetCameraCars().First());
            tiltingLand = false;
            if (Viewer.Simulator.UseSuperElevation > 0 || Viewer.Simulator.CarVibrating > 0) tiltingLand = true;
            var car = attachedCar;
            if (car != null && car.Train != null && car.Train.tilted == true) tiltingLand = true;
            base.OnActivate(sameCamera);
        }

        protected override List<TrainCar> GetCameraCars()
        {
            // Cab camera is only possible on the player locomotive.
            return new List<TrainCar>(new[] { Viewer.PlayerLocomotive });
        }

        protected override void SetCameraCar(TrainCar car)
        {
            base.SetCameraCar(car);
            if (car != null)
            {
                var loco = car as MSTSLocomotive;
                var viewpoints = (loco.UsingRearCab)
                ? loco.CabViewList[(int)CabViewType.Rear].ViewPointList
                : loco.CabViewList[(int)CabViewType.Front].ViewPointList;
                attachedLocation = viewpoints[sideLocation].Location;
            }
            InitialiseRotation(attachedCar);
        }

        /// <summary>
        /// Switches to another cab view (e.g. side view).
        /// Applies the inclination of the previous external view due to PanUp() to the new external view. 
        /// </summary>
        void ShiftView(int index)
        {
            var loco = attachedCar as MSTSLocomotive;

            // Get inclination offset due to PanUp() from previous view. 
            // Inclination or up/down angle is a rotation about the X axis.
            var viewpointList = (loco.UsingRearCab)
            ? loco.CabViewList[(int)CabViewType.Rear].ViewPointList
            : loco.CabViewList[(int)CabViewType.Front].ViewPointList;
            var rotationXRadiansOffset = RotationXRadians - MSTSMath.M.Radians(viewpointList[sideLocation].StartDirection.X);

            sideLocation += index;

            var count = (loco.UsingRearCab)
                ? loco.CabViewList[(int)CabViewType.Rear].ViewPointList.Count
                : loco.CabViewList[(int)CabViewType.Front].ViewPointList.Count;
            // Wrap around
            if (sideLocation < 0)
                sideLocation = count - 1;
            else if (sideLocation >= count)
                sideLocation = 0;

            SetCameraCar(attachedCar);
            // Apply inclination offset due to PanUp() from previous view.
            RotationXRadians += rotationXRadiansOffset;
        }

        /// <summary>
        /// Where cabview image doesn't fit the display exactly, this method mimics the player looking up
        /// and pans the image down to reveal details at the top of the cab.
        /// The external view also moves down by a similar amount.
        /// </summary>
        void PanUp(bool up, float speed)
        {
            int max = 0;
            int min = Viewer.DisplaySize.Y - Viewer.CabHeightPixels; // -ve value
            int cushionPixels = 40;
            int slowFactor = 4;

            // Cushioned approach to limits of travel. Within 40 pixels, travel at 1/4 speed
            if (up && Math.Abs(Viewer.CabYOffsetPixels - max) < cushionPixels)
                speed /= slowFactor;
            if (!up && Math.Abs(Viewer.CabYOffsetPixels - min) < cushionPixels)
                speed /= slowFactor;
            Viewer.CabYOffsetPixels += (up) ? (int)speed : -(int)speed;
            // Enforce limits to travel
            if (Viewer.CabYOffsetPixels >= max)
            {
                Viewer.CabYOffsetPixels = max;
                return;
            }
            if (Viewer.CabYOffsetPixels <= min)
            {
                Viewer.CabYOffsetPixels = min;
                return;
            }
            // Adjust inclination (up/down angle) of external view to match.
            var viewSpeed = speed * 0.00105f; // factor found by trial and error.
            RotationXRadians -= (up) ? viewSpeed : -viewSpeed;
        }

        /// <summary>
        /// Sets direction for view out of cab front window. Also called when toggling between full screen and windowed.
        /// </summary>
        /// <param name="attachedCar"></param>
        public void InitialiseRotation(TrainCar attachedCar)
        {
            if (attachedCar == null) return;

            var loco = attachedCar as MSTSLocomotive;
            var viewpoints = (loco.UsingRearCab)
            ? loco.CabViewList[(int)CabViewType.Rear].ViewPointList
            : loco.CabViewList[(int)CabViewType.Front].ViewPointList;

            RotationXRadians = MSTSMath.M.Radians(viewpoints[sideLocation].StartDirection.X);
            RotationYRadians = MSTSMath.M.Radians(viewpoints[sideLocation].StartDirection.Y);
        }

        public override void HandleUserInput(ElapsedTime elapsedTime)
        {
            var speedFactor = 500;  // Gives a fairly smart response.
            var speed = speedFactor * elapsedTime.RealSeconds; // Independent of framerate

            if (UserInput.IsPressed(UserCommands.CameraPanLeft))
                ShiftView(+1);
            if (UserInput.IsPressed(UserCommands.CameraPanRight))
                ShiftView(-1);
            if (UserInput.IsDown(UserCommands.CameraPanUp))
                PanUp(true, speed);
            if (UserInput.IsDown(UserCommands.CameraPanDown))
                PanUp(false, speed);
        }
    }

    public class TracksideCamera : LookAtCamera
    {
        const int MaximumDistance = 100;
        const float SidewaysScale = MaximumDistance / 10;
        // Heights above the terrain for the camera.
        const float CameraNormalAltitude = 2;
        const float CameraBridgeAltitude = 8;
        // Height above the coordinate center of target.
        const float TargetAltitude = TerrainAltitudeMargin;
        // Max altitude of terrain below coordinate center of train car before bridge-mode.
        const float BridgeCutoffAltitude = 1;

        protected TrainCar attachedCar;
        public override TrainCar AttachedCar { get { return attachedCar; } }

        protected TrainCar LastCheckCar;
        protected readonly Random Random;
        protected WorldLocation TrackCameraLocation;
        protected float CameraAltitudeOffset;

        public override bool IsUnderground
        {
            get
            {
                // Camera is underground if target (base) is underground or
                // track location is underground. The latter means we switch
                // to cab view instead of putting the camera above the tunnel.
                if (base.IsUnderground)
                    return true;
                if (TrackCameraLocation == null) return false;
                var elevationAtCameraTarget = Viewer.Tiles.GetElevation(TrackCameraLocation);
                return TrackCameraLocation.Location.Y + TerrainAltitudeMargin < elevationAtCameraTarget;
            }
        }

        public TracksideCamera(Viewer3D viewer)
            : base(viewer)
        {
            Random = new Random();
        }

        public override void Reset()
        {
            base.Reset();
            cameraLocation.Location.Y -= CameraAltitudeOffset;
            CameraAltitudeOffset = 0;
        }

        protected override void OnActivate(bool sameCamera)
        {
            if (sameCamera)
            {
                cameraLocation.TileX = 0;
                cameraLocation.TileZ = 0;
            }
            if (attachedCar == null || attachedCar.Train != Viewer.SelectedTrain)
            {
                if (Viewer.SelectedTrain.MUDirection != Direction.Reverse)
                    attachedCar = Viewer.SelectedTrain.Cars.First();
                else
                    attachedCar = Viewer.SelectedTrain.Cars.Last();
            }
            base.OnActivate(sameCamera);
        }

        public override void HandleUserInput(ElapsedTime elapsedTime)
        {
            var speed = GetSpeed(elapsedTime);

            if (UserInput.IsDown(UserCommands.CameraPanUp))
            {
                CameraAltitudeOffset += speed;
                cameraLocation.Location.Y += speed;
            }
            if (UserInput.IsDown(UserCommands.CameraPanDown))
            {
                CameraAltitudeOffset -= speed;
                cameraLocation.Location.Y -= speed;
                if (CameraAltitudeOffset < 0)
                {
                    cameraLocation.Location.Y -= CameraAltitudeOffset;
                    CameraAltitudeOffset = 0;
                }
            }

            var trainCars = Viewer.SelectedTrain.Cars;
            if (UserInput.IsPressed(UserCommands.CameraCarNext))
                attachedCar = attachedCar == trainCars.First() ? attachedCar : trainCars[trainCars.IndexOf(attachedCar) - 1];
            else if (UserInput.IsPressed(UserCommands.CameraCarPrevious))
                attachedCar = attachedCar == trainCars.Last() ? attachedCar : trainCars[trainCars.IndexOf(attachedCar) + 1];
            else if (UserInput.IsPressed(UserCommands.CameraCarFirst))
                attachedCar = trainCars.First();
            else if (UserInput.IsPressed(UserCommands.CameraCarLast))
                attachedCar = trainCars.Last();
        }

        public override void Update(ElapsedTime elapsedTime)
        {
            var train = attachedCar.Train;

            // TODO: What is this code trying to do?
            //if (train != Viewer.PlayerTrain && train.LeadLocomotive == null) train.ChangeToNextCab();
            if (train.LeadLocomotive == null)
            {
                return;
            }

            var trainForwards = (train.LeadLocomotive.SpeedMpS >= 0) ^ train.LeadLocomotive.Flipped;
            targetLocation = attachedCar.WorldPosition.WorldLocation;

            // Train is close enough if the last car we used is part of the same train and still close enough.
            var trainClose = (LastCheckCar != null) && (LastCheckCar.Train == train) && (WorldLocation.GetDistance2D(LastCheckCar.WorldPosition.WorldLocation, cameraLocation).Length() < MaximumDistance);

            // Otherwise, let's check out every car and remember which is the first one close enough for next time.
            if (!trainClose)
            {
                foreach (var car in train.Cars)
                {
                    if (WorldLocation.GetDistance2D(car.WorldPosition.WorldLocation, cameraLocation).Length() < MaximumDistance)
                    {
                        LastCheckCar = car;
                        trainClose = true;
                        break;
                    }
                }
            }

            // Switch to new position.
            if (!trainClose || (TrackCameraLocation == null))
            {
                var tdb = trainForwards ? new Traveller(train.FrontTDBTraveller) : new Traveller(train.RearTDBTraveller, Traveller.TravellerDirection.Backward);
                tdb.Move(MaximumDistance * 0.75f);
                var newLocation = tdb.WorldLocation;
                TrackCameraLocation = new WorldLocation(newLocation);
                var directionForward = WorldLocation.GetDistance((trainForwards ? train.FirstCar : train.LastCar).WorldPosition.WorldLocation, newLocation);
                if (Random.Next(2) == 0)
                {
                    newLocation.Location.X += -directionForward.Z / SidewaysScale; // Use swapped -X and Z to move to the left of the track.
                    newLocation.Location.Z += directionForward.X / SidewaysScale;
                }
                else
                {
                    newLocation.Location.X += directionForward.Z / SidewaysScale; // Use swapped X and -Z to move to the right of the track.
                    newLocation.Location.Z += -directionForward.X / SidewaysScale;
                }
                newLocation.Normalize();

                var newLocationElevation = Viewer.Tiles.GetElevation(newLocation);
                if (newLocationElevation > newLocation.Location.Y - BridgeCutoffAltitude)
                {
                    cameraLocation = newLocation;
                    cameraLocation.Location.Y = newLocationElevation + CameraNormalAltitude + CameraAltitudeOffset;
                }
                else
                {
                    cameraLocation = new WorldLocation(tdb.TileX, tdb.TileZ, tdb.X, tdb.Y + CameraBridgeAltitude + CameraAltitudeOffset, tdb.Z);
                }
            }

            targetLocation.Location.Y += TargetAltitude;
            UpdateListener();
        }
    }
}
