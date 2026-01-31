using SpawnDev.BlazorJS.JSObjects;
using System.Text;

namespace SpawnDev.BlazorJS.NexStar;
/// <summary>
/// Prolific PL2303 USB-to-Serial adapter support.
/// </summary>
public class ProlificSerialWebUSB : ProlificSerial
{
    private readonly USBDevice _device;
    private USBEndpoint? _inEndpoint;
    private USBEndpoint? _outEndpoint;
    private bool _isOpen = false;

    public override bool Connected => _isOpen && _device?.Opened == true;

    // PL2303 Standard Requests
    private const byte SET_LINE_CODING = 0x20;
    private const byte GET_LINE_CODING = 0x21;
    private const byte SET_CONTROL_LINE_STATE = 0x22;
    /// <summary>
    /// New ProlificSerial instance for the specified USBDevice.
    /// </summary>
    /// <param name="device"></param>
    public ProlificSerialWebUSB(USBDevice device)
    {
        _device = device;
    }
    /// <summary>
    /// Open the PL2303 device with the specified baud rate.gg
    /// </summary>
    /// <param name="serialOptions"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public override async Task<bool> OpenAsync(SerialOptions? serialOptions = null)
    {
        if (_device == null) return false;
        if (Connected) return true;
        if (_isOpen) return false;
        _isOpen = true;
        try
        {
            serialOptions ??= DefaultSerialOptions;
            await _device.Open();

            // PL2303 usually has one configuration. Select it.
            await _device.SelectConfiguration(1);

            // Claim Interface 0 (The data interface)
            await _device.ClaimInterface(0);

            // --- 1. PROLIFIC INITIALIZATION SEQUENCE ---
            // The PL2303 (especially "Controller D" / HX revs) requires a specific 
            // vendor read/write "dance" to enable the serial engine.
            // Without this, the bulk endpoints will silently drop data.

            // Write 0 to Register 1 (Enable)
            await VendorWrite(1, 0);

            // Read/Write loop often seen in Linux drivers to wake up HX/D chips
            // We perform the standard "wakeup" calls.
            await VendorRead(0x8484, 0);
            await VendorWrite(0x0404, 0);
            await VendorRead(0x8484, 0);
            await VendorRead(0x8383, 0);
            await VendorRead(0x8484, 0);
            await VendorWrite(0x0404, 1);
            await VendorRead(0x8484, 0);
            await VendorRead(0x8383, 0);

            // Reset upstream data pipes
            await VendorWrite(0, 1);
            await VendorWrite(1, 0);

            // 2 (0x02) = 0x44 is a common "magic" value for PL2303HX
            await VendorWrite(2, 0x44);

            // --- 2. CONFIGURE LINE ---
            await SetSerialOptionsAsync(serialOptions);

            // --- 3. ENDPOINT DISCOVERY ---
            // We need to find the Bulk IN and Bulk OUT endpoints dynamically.
            // PL2303 usually uses Endpoints 2 (OUT) and 3 (IN) or 1 (IN INTERRUPT).
            // We specifically look for BULK types.
            var iface = _device.Configuration!.Interfaces![0];
            // Note: Alternates is an Array, we usually want the first one (Active)
            var alt = iface.Alternates![0];

            foreach (var ep in alt.Endpoints!)
            {
                if (ep.Type == "bulk")
                {
                    if (ep.Direction == "in") _inEndpoint = ep;
                    else if (ep.Direction == "out") _outEndpoint = ep;
                }
            }

            if (_inEndpoint == null || _outEndpoint == null)
                throw new Exception("PL2303 Bulk Endpoints not found. Interface might be claimed by OS driver?");

            return true;
        }
        catch (Exception ex)
        {
            _isOpen = false;
        }
        finally
        {
            if (!_isOpen)
            {
                await CloseAsync();
            }
        }
        return false;
    }
    /// <summary>
    /// Set the SerialOptions for the PL2303 device.
    /// </summary>
    /// <param name="serialOptions"></param>
    /// <returns></returns>
    private async Task SetSerialOptionsAsync(SerialOptions serialOptions)
    {
        // PL2303 uses the CDC 7-byte structure for SetLineCoding
        // [0-3] Baud Rate (Little Endian)
        // [4]   Stop Bits (0=1, 1=1.5, 2=2)
        // [5]   Parity (0=None, 1=Odd, 2=Even, 3=Mark, 4=Space)
        // [6]   Data Bits (5,6,7,8)

        var baudRate = serialOptions.BaudRate;

        var buffer = new byte[7];
        buffer[0] = (byte)(baudRate & 0xFF);
        buffer[1] = (byte)((baudRate >> 8) & 0xFF);
        buffer[2] = (byte)((baudRate >> 16) & 0xFF);
        buffer[3] = (byte)((baudRate >> 24) & 0xFF);
        buffer[4] = serialOptions.StopBits switch
        {
            1 => 0,
            2 => 2,
            _ => 0
        };
        buffer[5] = serialOptions.Parity?.String?.ToLower() switch
        {
            "none" => 0,
            "odd" => 1,
            "even" => 2,
            "mark" => 3,
            "space" => 4,
            _ => 0
        };
        buffer[6] = (byte)(serialOptions.DataBits ?? 8);

        // In SpawnDev 2.62, we use Uint8Array directly
        using var jsBuffer = new Uint8Array(buffer);

        // Note: PL2303 uses "Class" request type for Line Coding, NOT Vendor
        var setup = new USBControlTransferParameters
        {
            RequestType = "class",
            Recipient = "interface",
            Request = SET_LINE_CODING,
            Value = 0,
            Index = 0
        };

        await _device.ControlTransferOut(setup, jsBuffer);
    }
    /// <summary>
    /// Write data to the PL2303 device.
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    protected async Task WriteAsync(string data)
    {
        if (!_isOpen || _outEndpoint == null) throw new InvalidOperationException("Port not open");

        var bytes = Encoding.UTF8.GetBytes(data);
        using var jsBuffer = new Uint8Array(bytes);

        // transferOut returns USBOutTransferResult
        await _device.TransferOut(_outEndpoint.EndpointNumber, jsBuffer);
    }
    /// <summary>
    /// Read data from the PL2303 device.
    /// </summary>
    /// <param name="maxLen"></param>
    /// <returns></returns>
    protected async Task<byte[]?> ReadAsync(int maxLen = 64)
    {
        if (!_isOpen || _inEndpoint == null || _device == null) return null;
        try
        {
            // transferIn returns USBInTransferResult
            var result = await _device.TransferIn(_inEndpoint.EndpointNumber, maxLen);
            if (result.Status == "ok")
            {
                using var dataView = result.Data;
                return dataView?.ReadBytes();
            }
        }
        catch (Exception)
        {
            // Timeout or device disconnected
        }
        return null;
    }

    // --- Helpers for Vendor Control Transfers ---

    private async Task VendorWrite(int value, int index)
    {
        // 0x40 = Vendor | Request | Out
        var setup = new USBControlTransferParameters
        {
            RequestType = "vendor",
            Recipient = "device",
            Request = 0x01, // Vendor Request ID
            Value = value,
            Index = index
        };
        await _device.ControlTransferOut(setup);
    }

    private async Task VendorRead(int value, int index)
    {
        // 0xC0 = Vendor | Request | In
        var setup = new USBControlTransferParameters
        {
            RequestType = "vendor",
            Recipient = "device",
            Request = 0x01, // Vendor Request ID
            Value = value,
            Index = index
        };
        // We read 1 byte just to satisfy the handshake
        await _device.ControlTransferIn(setup, 1);
    }

    public override async Task CloseAsync()
    {
        _isOpen = false;
        if (_device != null)
        {
            if (_device.Opened)
            {
                try { await _device.Close(); } catch { }
            }
        }
    }

    public override Task<string?> SendStringCommandAsync(string data, int timeoutMs = 2000)
    {
        throw new NotImplementedException();
    }

    public override async Task<byte[]?> SendCommandAsync(byte[] command, int timeoutMs = 2000)
    {
        throw new NotImplementedException();
        //await WriteAsync(Encoding.UTF8.GetString(command));
    }
}