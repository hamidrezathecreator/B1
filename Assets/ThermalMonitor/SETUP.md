# Thermal Monitor – setup

For: URP, camera -> RenderTexture -> RawImage on a canvas.
Written for Unity 6.3 (URP 17, Render Graph). Not compatible with the old Execute()/cmd.Blit path.

## 1. Import
Drop the `ThermalMonitor` folder into Assets/. Keep the .meta files (GUIDs match your original heat shaders/materials).

## 2. Renderer (one-time)
1. Assets > Create > Rendering > URP Universal Renderer -> name it `ThermalMonitorRenderer`.
2. Select it > Add Renderer Feature > **Thermal Monitor Render Feature**.
3. Add it to the Renderer List of your URP Pipeline Asset.

Use a separate renderer so your main camera never touches this feature. (Even on a shared renderer the feature
does nothing for cameras without the component, but URP creates an intermediate colour texture for any renderer
that has features, so a dedicated one is cheaper for the main camera.)

## 3. Monitor camera
1. Create a Camera (remove its AudioListener). Set **Renderer = ThermalMonitorRenderer**.
2. Background Type: Solid Color, black. Set the Culling Mask to what the monitor should see.
3. Add **ThermalMonitorCamera**. (Add it via the component menu so Reset() fills in the shaders; otherwise assign
   `Hidden/ThermalMonitor/PostProcess` and `Hidden/ThermalObjectReplacement`.)
4. Output, pick one:
   - Auto Create Texture (default 640x480) + drag your RawImage into **Target Raw Image**, or
   - Assign your own RenderTexture (ARGB32, depth 24, no MSAA) and set the RawImage's texture to it.

## 3b. Which objects show up
- Objects with a heat shader (Custom/HeatmapObject_Mobile, particles, outline, decals...) are drawn through their own
  `ThermalVision` pass – these are the "native" layers.
- Anything else (plain Lit/Unlit) is invisible to the thermal data unless you put it on a layer and set that layer
  as **Ambient Layers**. Those are drawn with the flat replacement shader (base heat + gradient + normals + noise).

## 4. Scripting
```csharp
var thermal = monitorCamera.GetComponent<ThermalMonitorCamera>();
thermal.SetPower(true/false);   // off = camera stops rendering, screen goes black
thermal.CyclePalette();
thermal.SetPalette(ThermalMonitorCamera.ThermalPalette.Ironbow);
thermal.ToggleInvert();         // white-hot <-> black-hot
```

## What changed vs. the original package
- No static/global on-off state: only cameras with ThermalMonitorCamera are affected.
- ThermalVisionController / UI / Audio / Crosshair / ButtonHover / old RenderFeature are not needed (and not included).
- Pass is a Render Graph pass (3 raster passes); post shader uses Blit.hlsl.
- Post shader reads scene detail from the camera colour, so "Opaque Texture" is NOT required.
- Depth texture is requested by the pass itself (particles & decals need it).
- Camera auto-setup turns off HDR, MSAA, post-processing on the monitor camera.
- Heat shaders, replacement shader, particles, decals: unchanged.

## Troubleshooting
- Screen shows the normal (non-thermal) image: the camera's Renderer doesn't have the feature, or the component is disabled.
- Screen is flat grey/black: nothing in the culling mask has a ThermalVision pass and no Ambient Layer is set.
- Compile errors in ThermalMonitorRenderFeature.cs: send me the exact message (Render Graph APIs shift slightly between 6.x releases).
- Particles/decals missing: check the Pipeline Asset allows depth texture; if "Native RenderPass" is on, try turning it off for the monitor renderer.

## Meta Quest 3 notes
- The monitor camera is forced to a plain mono camera (`stereoTargetEye = None`), so it never renders as an XR eye.
  It renders into its RenderTexture before the eyes, like any RT camera.
- **Keep the RenderTexture small.** 512x384 is the default; 384x288 still reads fine on a prop monitor. Cost scales with its pixels.
- Assign the **Target Raw Image** (your monitor canvas image) and keep *Render Only When Visible* on: the camera renders only while that image is inside the Viewer Camera's frustum (empty = Camera.main; set it to your XR camera if needed). Frustum test only, no occlusion check. A disabled/inactive RawImage also stops rendering.
- Thermal heat texture is a single 8-bit channel; the camera has shadows, HDR, MSAA, post-processing and Opaque Texture off.
- Turn off *Use Scene Depth* if you don't use thermal particles/decals (saves a depth copy).
- Give the monitor camera a tight Culling Mask and Far Clip: it draws the scene twice (normal colour for the detail blend, plus thermal).
- Use Vulkan + the URP Quest settings you already have. Depth Priming off is recommended for the monitor renderer.
- Not tested on device. Profile with RenderDoc / OVR Metrics (GPU % and fill) with the monitor in view.
