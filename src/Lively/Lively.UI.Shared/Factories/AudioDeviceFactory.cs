using Lively.Common;
using Lively.Common.Helpers.Pinvoke;
using Lively.Models;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Lively.UI.Shared.Factories;

public class AudioDeviceFactory : IAudioDeviceFactory
{
    private readonly string cacheDir = Path.Combine(Constants.CommonPaths.TempDir, "icons");

    public AudioDevice? GetDefaultRenderDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
        return ToAudioDevice(device);
    }

    public IEnumerable<AudioDevice> GetRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        return devices.Select(ToAudioDevice).ToList();
    }

    public AudioDevice? GetDeviceById(string id)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(id);
            return ToAudioDevice(device);
        }
        catch {
            // device not found or unavailable
            return null;
        }
    }

    private AudioDevice ToAudioDevice(MMDevice device)
    {
        return new AudioDevice(device.ID, device.FriendlyName, GetAndCacheDeviceIcon(device));
    }

    public string GetAndCacheDeviceIcon(MMDevice device)
    {
        Directory.CreateDirectory(cacheDir);

        var fileName = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(device.ID))) + ".png";
        var cachedIconPath = Path.Combine(cacheDir, fileName);
        var iconPath = device.IconPath;

        if (File.Exists(cachedIconPath))
            return cachedIconPath;

        try
        {
            if (iconPath.Contains(","))
            {
                // DLL with resource ID, example: "%windir%\system32\mmres.dll,-3017"
                var parts = iconPath.Split(',');
                string dllPath = Environment.ExpandEnvironmentVariables(parts[0]);
                int id = int.Parse(parts[1]);

                IntPtr[] largeIcons = new IntPtr[1];
                var ret = NativeMethods.ExtractIconEx(dllPath, id, largeIcons, null, 1);
                IntPtr hIcon = largeIcons[0];

                // Error, ref: https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-extracticonexa
                if (ret == uint.MaxValue || hIcon == IntPtr.Zero)
                {
                    // Fallback to small
                    IntPtr[] smallIcons = new IntPtr[1];
                    ret = NativeMethods.ExtractIconEx(dllPath, id, null, smallIcons, 1);
                    hIcon = smallIcons[0];
                }

                if (ret == uint.MaxValue || hIcon == IntPtr.Zero)
                    throw new Win32Exception();

                try
                {
                    using var icon = Icon.FromHandle(hIcon);
                    using var bmp = icon.ToBitmap();
                    bmp.Save(cachedIconPath, ImageFormat.Png);
                }
                finally
                {
                    NativeMethods.DestroyIcon(hIcon);
                }
            }
            else
            {
                // Direct .ico/.exe/.dll path
                string expanded = Environment.ExpandEnvironmentVariables(iconPath);
                using var icon = Icon.ExtractAssociatedIcon(expanded);
                using var bmp = icon.ToBitmap();
                bmp.Save(cachedIconPath, ImageFormat.Png);
            }
        }
        catch {
            return null;
        }

        return cachedIconPath;
    }
}