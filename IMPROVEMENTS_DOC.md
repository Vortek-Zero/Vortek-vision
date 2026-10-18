# 🛠️ AR Metaverse - Fixes, Enhancements & Unity Setup Guide

## 📌 Executive Summary & Objective
This project enables **real-time desktop display mirroring on an Android smartphone placed inside a VR Box or Google Cardboard headset**. By combining Unity Render Streaming (WebRTC), stereoscopic rendering, and head tracking via smartphone gyroscope sensors, it creates a low-cost, budget-friendly Spatial Computing workstation.

This document records all code modifications made to solve connection instability, manual IP configuration bugs, and missing adaptive quality streaming, along with a **step-by-step guide for Unity Editor configuration**.

---

## 🛠️ Summary of Code Modifications & Bug Fixes

### 1. Manual IP Address Configuration Bug (`WebServerIPScript.cs`)
- **Bug Fixed:** Previously, `TryNormalizeHostPortInput` rejected input containing `://`, causing inputs like `ws://192.168.1.5:80` or `http://192.168.1.5:80` to fail with `"Invalid URL format"`.
- **Solution:** Rewrote URL parsing in `TryNormalizeWebSocketUrl` to handle raw IPs, host:port, `http://`, `https://`, `ws://`, and `wss://` seamlessly.
- **Auto-UI Binding:** Added automatic `onEndEdit` event subscription to `TMP_InputField` in `Start()`. Typing or submitting a new IP immediately triggers reconnection without needing manual UnityEvent wiring.
- **Signaling Auto-Discovery:** `WebServerIPScript` now automatically finds the active `SignalingManager` in the scene if unassigned.

### 2. Auto-Reconnection & Fault Tolerance (`WebServerIPScript.cs` & `StreamConnectionHandlerAndroidScript.cs`)
- **Bug Fixed:** If signaling or video stream dropped, the connection remained frozen indefinitely.
- **Solution:** 
  - Added `autoReconnectOnDisconnect` coroutine with exponential retry interval in `WebServerIPScript.cs`.
  - Added video frame freeze monitoring (`freezeTimeoutSeconds`) in `StreamConnectionHandlerAndroidScript.cs` to auto-refresh the WebRTC stream if texture updates stop.
  - Added dual rendering support to `StreamConnectionHandlerAndroidScript.cs` to handle both UI `RawImage` and 3D `MeshRenderer` (Quad display).

### 3. Dynamic Connection Quality & Bandwidth Adaptation (`DynamicStreamQualityManager.cs`)
- **New Feature:** Added `DynamicStreamQualityManager.cs` to solve the issue where poor initial connection permanently degraded stream quality.
- **How It Works:**
  - Continuously monitors frame render rate and network stability.
  - **Weak Connection:** Automatically steps down bitrate (down to 2 Mbps) to prevent input lag and stuttering.
  - **Recovered / Strong Connection:** Automatically steps up bitrate in increments (up to 15 Mbps / 60 FPS) for crisp, high-definition desktop display mirroring.
  - Works seamlessly with `VideoStreamSender` and `WebRTCOptimizerScript.cs`.

### 4. VR Headset Head-Tracking & Stereoscopic Alignment (`RotationDataSender.cs`, `RotationDataReceiver.cs`, `VRSplitScreenScript.cs`)
- **Enhancements:**
  - Added Quaternion **Slerp smoothing** (`smoothFactor`) in `RotationDataReceiver.cs` to eliminate micro-jitter when viewing through VR Box lenses.
  - Added legacy `Input.gyro` fallback in `RotationDataSender.cs` if new `AttitudeSensor` is unassigned on older Android hardware.
  - Added send rate limiting (60 Hz) to avoid saturating WebRTC DataChannels.
  - Updated `VRSplitScreenScript.cs` with auto-camera binding and runtime IPD (Interpupillary Distance ~64mm) adjustment controls.

### 5. Web Screen Sharing Web Application (`web app`)
- **`highSpeedScreenShare.html.html` & `screenshare.html` Upgrades:**
  - Added sleek modern dark UI dashboard with real-time status badges.
  - Added `localStorage` address memory so you don't have to re-enter your server IP every time.
  - Added automatic WebSocket reconnection backoff loops.
  - Added stream bitrate quality selector (15 Mbps Ultra, 10 Mbps High, 5 Mbps Balanced).

---

## 🎯 WHAT YOU NEED TO DO IN THE UNITY EDITOR (Step-by-Step Guide)

Follow these exact steps inside the Unity Editor to configure your scene:

### Step 1: Set Up `SignalingManager` & IP Script
1. Open your target scene (e.g. `SplitScreenScene2` or `AndroidAppScene 2`).
2. Select the GameObject containing `SignalingManager` (or create an empty GameObject named `[NetworkManager]`).
3. Ensure `SignalingManager` component is attached.
4. Attach `WebServerIPScript` component:
   - Assign **Server Address Input Field** to your UI `TMP_InputField` (if you have a canvas UI for entering IP).
   - Assign **Connection Status Text** to your `TMP_Text` element.
   - Set **Default Server Address** to your laptop's local IP (e.g. `192.168.1.50:80`).
   - Check **Load Saved Url On Start** and **Auto Reconnect On Disconnect**.

### Step 2: Add Dynamic Quality Management (For High Quality Upgrade)
1. In your Windows/Host streaming scene (`WindowsAppScene`), select the GameObject containing `VideoStreamSender` (or your WebRTC manager).
2. Add the **`DynamicStreamQualityManager`** script component (`Assets/Scripts/DynamicStreamQualityManager.cs`).
3. In the Inspector settings:
   - Set **Min Bitrate Kbps**: `2000` (2 Mbps)
   - Set **Max Bitrate Kbps**: `15000` (15 Mbps for ultra crisp desktop view)
   - Set **Initial Bitrate Kbps**: `5000`
   - Set **Evaluation Interval Seconds**: `3`
4. On `WebRTCOptimizerScript`, drag and drop `DynamicStreamQualityManager` into the `Dynamic Quality Manager` field.

### Step 3: Configure Android Video Receiver Display
1. In your Android app scene (`SplitScreenScene2` or `AndroidAppScene`), select your video receiver object.
2. Attach **`StreamConnectionHandlerAndroidScript`**:
   - Set **Connection Id**: `windowsStream` (or your configured stream key).
   - Drag your `RawImage` (for UI display) OR `MeshRenderer` (for 3D Quad wall display).
   - Set **Freeze Timeout Seconds**: `5`.

### Step 4: Configure VR Box / Google Cardboard Stereoscopic Cameras
1. In your VR scene (`SplitScreenScene2`), ensure you have dual cameras or an XR Camera setup:
   - **Left Eye Camera:** Main Camera tracked by AR Foundation or Gyro.
   - **Right Eye Camera:** Child object attached with `VRSplitScreenScript`.
2. Select Right Eye Camera:
   - Attach `VRSplitScreenScript`.
   - Drag Left Eye Camera transform into **Left Eye Camera** field.
   - Set **IPD**: `0.064` (64mm interpupillary distance).

### Step 5: Configure Head Rotation Sensors
1. **On Android Device (Sender):**
   - Attach `RotationDataSender` to your connection manager GameObject.
   - Set **Connection Id**: `InputStream`.
   - Set **Send Rate Hz**: `60`.
2. **On Desktop PC (Receiver):**
   - Attach `RotationDataReceiver` to your virtual camera root object.
   - Set **Connection Id**: `InputStream`.
   - Set **Initial Rotation Offset**: `(90, 0, 0)` (or `(-90, 0, 0)` depending on phone orientation inside VR Box).
   - Set **Smooth Factor**: `20` (provides smooth, jitter-free head tracking).

---

## 🚀 Suggested Future Features & Enhancements

1. **Virtual Multi-Monitor Setup (Multi-Screen AR Workspace):**
   - Extend signaling to support multiple WebRTC video tracks (`windowsStream1`, `windowsStream2`).
   - Display 2–3 virtual quads arranged in a curved arc around the user in VR/AR space.

2. **Smartphone Virtual Touchpad & Spatial Gestures:**
   - Use smartphone touchscreen as a laptop trackpad or 3DoF laser pointer when phone is removed from VR Box.
   - Send mouse clicks, scrolling, and dragging back to Windows PC over WebRTC DataChannel.

3. **AR Passthrough / VR Skybox Blend Toggle:**
   - Add a button in Unity to toggle between pure black background (VR Box mode) and AR Camera background (AR visor mode).

4. **mDNS Auto-Discovery (Zero-Configuration Connection):**
   - Implement UDP multicast / mDNS broadcasting from signaling server so Android app automatically discovers the laptop's IP address on the local Wi-Fi without manual typing.
