namespace Lively.Models
{
    public class AudioDevice
    {
        public AudioDevice(string id, string name, string deviceIcon)
        {
            Id = id;
            Name = name;
            DeviceIcon = deviceIcon;
        }

        public string Id { get; }
        public string Name { get; }
        public string DeviceIcon { get; }
    }
}
