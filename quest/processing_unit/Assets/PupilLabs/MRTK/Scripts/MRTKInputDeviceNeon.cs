// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.XR;
using UnityEngine.Scripting;
using TrackingState = UnityEngine.XR.InputTrackingState;

namespace PupilLabs.MRTK
{

    [InputControlLayout(
        displayName = "Eye Gaze (MRTK)",
        isGenericTypeOfDevice = false),
        Preserve]
    public class MRTKInputDeviceNeon : InputDevice
    {
        [Preserve, InputControl(offset = 0, usages = new[] { "Device", "gaze" })]
        public PoseControl pose { get; private set; }

        /// <inheritdoc/>
        protected override void FinishSetup()
        {
            base.FinishSetup();
            pose = GetChildControl<PoseControl>(nameof(pose));
        }
    }

    internal class MRTKNeonEyeGaze : IDisposable
    {
        private MRTKInputDeviceNeon eyeDevice = null;

        // We don't need our own custom state struct because we are not adding
        // any additional information to the default device layout.
        private PoseState poseState;

        public MRTKNeonEyeGaze()
        {
#if !USE_INPUT_SYSTEM_POSE_CONTROL
            InputSystem.RegisterLayout<PoseControl>("InputSystemPose");
#endif

            eyeDevice = InputSystem.AddDevice<MRTKInputDeviceNeon>();
            if (eyeDevice == null)
            {
                Debug.LogError("Failed to create the simulated eye gaze device.");
                return;
            }
        }

        ~MRTKNeonEyeGaze()
        {
#if !USE_INPUT_SYSTEM_POSE_CONTROL
            // Remove/unregister the layout that we added as a workaround for the Unity bug.
            InputSystem.RemoveLayout("InputSystemPose");
#endif
            Dispose();
        }

        public void Dispose()
        {
            if (eyeDevice != null)
            {
                InputSystem.RemoveDevice(eyeDevice);
            }
            GC.SuppressFinalize(this);
        }

        public void Update(
            bool isTracked,
            Quaternion lookRotation,
            Vector3 origin)
        {
            if (eyeDevice == null) { return; }

            if (!eyeDevice.added)
            {
                eyeDevice = InputSystem.GetDeviceById(eyeDevice.deviceId) as MRTKInputDeviceNeon;
                if (eyeDevice == null) { return; }
            }

            poseState.isTracked = isTracked;
            poseState.trackingState = poseState.isTracked ?
                TrackingState.Position | TrackingState.Rotation :
                TrackingState.None;

            poseState.position = origin;
            // todo - saccade support
            poseState.rotation = lookRotation;

            InputState.Change(eyeDevice.pose, poseState);
        }
    }
}
