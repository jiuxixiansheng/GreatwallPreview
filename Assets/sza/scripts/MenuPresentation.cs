using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace Sza.Menu
{
    /// <summary>Scene-local lobby viewer. The gameplay scene creates its own network player.</summary>
    [DefaultExecutionOrder(-1000), DisallowMultipleComponent]
    public sealed class MenuPresentation : MonoBehaviour
    {
        public Camera desktopCamera;
        public XROrigin vrOrigin;
        public MenuIntroScreen vrIntroScreen;

        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        private bool initialized, vrMode;
        private float nextCheck;

        private void Awake() => RefreshMode();

        private void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.25f;
            RefreshMode();
        }

        private void RefreshMode()
        {
            displays.Clear();
            SubsystemManager.GetSubsystems(displays);
            bool running = XRSettings.isDeviceActive;
            foreach (var display in displays) running |= display.running;
            SetPresentationMode(running);
        }

        public void SetPresentationMode(bool useVR)
        {
            if (initialized && vrMode == useVR) return;
            initialized = true;
            vrMode = useVR;
            Camera headset = vrOrigin != null ? vrOrigin.Camera : null;
            bool headsetActive = useVR && headset != null;
            if (headsetActive)
            {
                SetView(desktopCamera, true, false);
                SetView(headset, true, true);
            }
            else
            {
                SetView(headset, false, false);
                SetView(desktopCamera, true, true);
            }
            if (vrIntroScreen != null) vrIntroScreen.gameObject.SetActive(headsetActive);
        }

        private static void SetView(Camera camera, bool render, bool primary)
        {
            if (camera == null) return;
            camera.enabled = render;
            camera.tag = primary ? "MainCamera" : "Untagged";
            var listener = camera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = primary;
        }
    }
}
