# 📓 Development Log

## 🗓️ 28-03-2026

**What I Did:**
- Initial commit of the AR-Metaverse repository
- working with render streaming and its webserver
- created an android app to stream app screen on web-browser
- manualStartScript.cs for space key or touch on screen to establish connection to webserver
- used Button.onClick.RemoveListene(/action/) for exit button
- duplicated ARMetaverseScene => ARMetaverseScene1 : this will have stereoscopic rendering (two cameras) on canvasDisplay
- duplicated ARMetaverseScene => ARMetaverseScene2 : this will have stereoscopic rendering (two cameras) without canvasDisplay
- TestStreaming Scene : for unity render streaming tutorial [https://docs.unity3d.com/Packages/com.unity.renderstreaming@3.1/manual/tutorial.html]


## 🗓️ 29-03-2026

**What I Did:**
- Input Action to control the movement of the cube : for remotely controlling the cube from browser using Render Streaming
- taking input from browser using Input Receiver 
- using quad to display the laptop's screen in unity
- setup done : laptop screen display, screen capture using screenshare.html and unity render stream receiving the share to display over quad


## 🗓️ 01-04-2026

**What I Did:**
- Optimization started!
- removed all the updates from ARMetaverse Scene as right targeting optimization of that scene only.
- set target FPS to 30
- tried dynamic fps (not impressed!)
- reduced number of game object present in the ARMetaverse Scene.
- PlaceDisplayOnWallScript.cs script will turn off right after the placement of the quad (display screen)
- reducing resolution of screen to reducing heating issue of the android device!
- DPI changed to FIXED DPI = 320 (for testing!)


## 🗓️ 01-04-2026

**What I Did:**
- ARResolutionControllerscript : to reduce the resolution of the camera feed to reduce cpu usage little (testing!)
- optimizated enough to work for 5 mins (in between temp changes from 37 to 40 deg celius, on Motorola Edge 60 stylus)


## 🗓️ 08-04-2026

**What I Did:**
- fixed some issue in connection of web-browser and unity android app (ARMetaverse app)
- added realtime connection status display box in screenshare.html (public1)
- changed name of ARMetaverseScene1 and ARMetaverseScene1 to SplitScreenScene1 and SplitScreenScene2
- Created new scene ARMetaverseSceneModified : AR Session Tracking mode = Rotation Only & No plane detection! (highly optimizated for less cpu usage)

## 🗓️ 09-04-2026

**What I Did:**
- creating VRMetaverseScene : duplicating ARMetaverseSceneModified : to remove AR Camera Background and test its effect on processing

## 🗓️ 12-04-2026

**What I Did:**
- Demo video added in repo 
- GIF of demo video in README

## 🗓️ 13-04-2026

**What I Did:**
- deleting XR Origin Prefab from all scenes, provided new XR Origin Component in each scene

## 🗓️ 15-04-2026

**What I Did:**
- modified SplitScreenScene2 for testing on google cardboard

## 🗓️ 16-04-2026 - 18-04-2026

**What I Did:**
- created new scenes AndroidAppScene and WindowsAppScene for remote rendering
- commiting for some progress but not success in building Android and windows app communication

## 🗓️ 24-04-2026 

**What I Did:**
- AndroidAppScene and WindowsAppScene : one connection at a time working ie either browser to windowsApp or windowsApp to androidApp
- Creating and testing in scenes - (AndroidAppScene 1) and (WindowsAppScene 1) _____ Working : Build Android App name = AndroidAppScene.apk

## 🗓️ 02-05-2026 

**What I Did:**
- Updating WindowsAppScene & AndroidAppScene for vr experience (including gyro input from phone) extension of the work in AndroidAppScene 2 and WindowsAppScene 1

(not tested!)-----------------------
Since InputRemoting is failing you completely, I have bypassed it and hooked directly into Unity Render Streaming's raw lowest-latency WebRTC DataChannel.
-RotationDataSender.cs
-RotationDataReceiver.cs
(not tested!)----------------------


 _____________


## 🗓️ 08-05-2026 

**What I Did:**
- Testing RotationDataSender.cs and RotationDataReceiver.cs in AndroidAppScene 2 and windowsAppScene 2
- (Issue of inputSender and inputReceiver is still not solved, no receiving of data in windowsAppScene)


## 🗓️ 09-05-2026 

**What I Did:**
- In AndroidAppScene 2 and windowsAppScene 3, using gyrosensor of android phone to control sterocamera on windows successfully implemented 
- How? Created another single connection base for one way communication (only for input communication)

## 🗓️ 28-08-2026 

**What I Did:**
- Testing and debugging AR MetaVerse scene for windows and android app with Unity Render Streaming


<!-- Keep adding entries as you work -->