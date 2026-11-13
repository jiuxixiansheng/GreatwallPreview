using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Sza.Menu
{
    /// <summary>Scene-local lobby viewer. The gameplay scene creates its own network player.</summary>
    [DefaultExecutionOrder(-1000), DisallowMultipleComponent]
    public sealed class MenuPresentation : MonoBehaviour
    {
        public Canvas desktopCanvas;
        public Canvas vrCanvas;
        public Camera desktopCamera;
        public XROrigin vrOrigin;
        public Button desktopHostButton, desktopClientButton;
        public Button vrHostButton, vrClientButton;

        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        private bool initialized, vrMode;
        private float nextCheck;

        private void Awake()
        {
            // Both views use the original menu's connection handlers.
            if (vrHostButton != null) vrHostButton.onClick.AddListener(HostFromVR);
            if (vrClientButton != null) vrClientButton.onClick.AddListener(ClientFromVR);
            RefreshMode();
        }

        private void HostFromVR() => desktopHostButton?.onClick.Invoke();
        private void ClientFromVR() => desktopClientButton?.onClick.Invoke();

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
            if (desktopCanvas != null) desktopCanvas.gameObject.SetActive(true);
            if (vrCanvas != null) vrCanvas.gameObject.SetActive(useVR);
            // URP renders Screen Space Overlay on the monitor, outside the HMD eyes.
            SetCamera(desktopCamera, !useVR);
            if (vrOrigin != null) SetCamera(vrOrigin.Camera, useVR);
        }

        private static void SetCamera(Camera camera, bool active)
        {
            if (camera == null) return;
            camera.enabled = active;
            camera.tag = active ? "MainCamera" : "Untagged";
            var listener = camera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = active;
        }

        private void OnDestroy()
        {
            if (vrHostButton != null) vrHostButton.onClick.RemoveListener(HostFromVR);
            if (vrClientButton != null) vrClientButton.onClick.RemoveListener(ClientFromVR);
        }
    }
}
