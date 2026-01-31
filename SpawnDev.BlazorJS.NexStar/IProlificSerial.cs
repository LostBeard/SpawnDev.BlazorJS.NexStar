
using SpawnDev.BlazorJS.JSObjects;

namespace SpawnDev.BlazorJS.NexStar
{
    public interface IProlificSerial : IAsyncDisposable
    {
        bool Connected { get; }
        Task<bool> OpenAsync(SerialOptions? serialOptions = null);
        Task CloseAsync();
        Task Forget();
        Task<string> SendStringCommandAsync(string data, int timeoutMs);
        Task<byte[]?> SendCommandAsync(byte[] command, int timeoutMs);
    }
}