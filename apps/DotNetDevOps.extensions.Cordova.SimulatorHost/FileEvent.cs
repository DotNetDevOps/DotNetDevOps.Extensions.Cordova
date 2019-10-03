namespace DotNetDevOps.extensions.Cordova.SimulatorHost
{
    public class FileEvent
    {
        public long FileLength { get; set; }
        public string FileHash { get; set; }
        public string Event { get; set; }
        public string Path { get; set; }
    }
}
