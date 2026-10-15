using Lively.Models;
using System.Collections.Generic;

namespace Lively.UI.Shared.Factories;

public interface IAudioDeviceFactory
{
    AudioDevice GetDefaultRenderDevice();
    IEnumerable<AudioDevice> GetRenderDevices();
    AudioDevice? GetDeviceById(string id);
}