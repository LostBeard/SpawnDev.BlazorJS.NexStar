using SpawnDev.BlazorJS.JSObjects;

namespace SpawnDev.BlazorJS.NexStar;

/// <summary>
/// USBDevice extension methods.
/// </summary>
public static class USBDeviceExtensions
{

    /// <summary>
    /// Controls a transfer to the USB device.
    /// </summary>
    /// <param name="_this">USB device</param>
    /// <param name="setup">The setup packet for the control transfer.</param>
    /// <returns>A promise that resolves with the result of the transfer.</returns>
    public static Task<USBOutTransferResult> ControlTransferOut(this USBDevice _this, USBControlTransferParameters setup) => _this.JSRef!.CallAsync<USBOutTransferResult>("controlTransferOut", setup);
}
