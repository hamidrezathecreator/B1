using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public static class ControllerHaptics
{
    static readonly List<InputDevice> Devices = new List<InputDevice>();

    public static void Pulse(float amplitude, float durationSeconds)
    {
        if (amplitude <= 0f || durationSeconds <= 0f) return;

        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, Devices);
        foreach (var device in Devices)
        {
            if (device.TryGetHapticCapabilities(out var capabilities) && capabilities.supportsImpulse)
                device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), durationSeconds);
        }
    }

    public static void Stop()
    {
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, Devices);
        foreach (var device in Devices)
            device.StopHaptics();
    }
}
