using SpawnDev.BlazorJS.JSObjects;
using System.Text;

namespace SpawnDev.BlazorJS.NexStar
{
    /// <summary>
    /// This service controls a horizontal camera slider using the Web Serial API<br/>
    /// Reference:
    /// https://developer.chrome.com/docs/capabilities/serial
    /// </summary>
    public class NexStarService : IAsyncBackgroundService
    {
        private CancellationTokenSource? CancelComsTokenSource = null;
        private Navigator navigator;
        private WritableStreamDefaultWriter? Writer = null;
        private Task? ReadingTask = null;
        private BlazorJSRuntime JS;
        private Task? _Ready = null;
        public Task Ready => _Ready ??= InitASync();
        public Serial? Serial { get; private set; }
        public bool SerialPortAvailable => SerialPort?.Connected == true;
        public bool SerialPortSelected => SerialPort != null;
        public SerialPort? SerialPort { get; private set; }
        public bool ComsEnabled { get; private set; }
        public event Action<SerialPort> OnConnected = default!;
        public event Action<SerialPort> OnDisconnected = default!;
        public event Action<Uint8Array> OnData = default!;
        // 1. Celestron Standard Connection Settings
        // The SLT usually connects via the Hand Controller at 9600 baud.
        SerialOptions SerialOptions = new SerialOptions
        {
            BaudRate = 9600,
            DataBits = 8,
            StopBits = 1,
            Parity = "none"
        };
        public NexStarService(BlazorJSRuntime js)
        {
            JS = js;
            if (!JS.IsBrowser)
            {
                return;
            }
            navigator = JS.Get<Navigator?>("navigator")!;
            Serial = navigator!.Serial;
            if (Serial != null)
            {
                Serial.OnConnect += Serial_OnConnect;
            }
            else
            {
                // Serial is not supported
                // fallback to serial.js polyfill
                // TODO: implement polyfill
            }
            JS.Set("_selectPort", SelectPort);
            JS.Set("_startComs", StartComs);
            JS.Set("_stopComs", StopComs);
            JS.Set("_write", SerialPortWrite);
        }
        public async Task<bool> SerialPortWrite(string value)
        {
            if (Writer != null)
            {
                try
                {
                    var bytes = Encoding.ASCII.GetBytes(value);
                    await Writer.Ready;
                    await Writer.Write(bytes);
                    return true;
                }
                catch (Exception ex)
                {
                    // continue
                    var nmt = true;
                }
            }
            return false;
        }
        public async Task StartComs()
        {
            var port = SerialPort;
            if (ComsEnabled || port == null) return;
            ComsEnabled = true;
            try
            {
                await port.Open(SerialOptions);
            }
            catch (Exception ex)
            {
                JS.Log("StartComs failed. Cannot open port.");
                ComsEnabled = false;
                return;
            }
            // get writer
            try
            {
                Writer = port.Writable.GetWriter();
            }
            catch (Exception ex)
            {
                JS.Log("GetWriter failed", ex.Message, ex.StackTrace);
                // close port...
                try
                {
                    await port.Close();
                }
                catch { }
                ComsEnabled = false;
                return;
            }
            // get signals
            try
            {
                var signals = await port.GetSignals();
                JS.Log($"Clear To Send:       {signals.ClearToSend}");
                JS.Log($"Data Carrier Detect: {signals.DataCarrierDetect}");
                JS.Log($"Data Set Ready:      {signals.DataSetReady}");
                JS.Log($"Ring Indicator:      {signals.RingIndicator}");
            }
            catch (Exception ex)
            {

                JS.Log("GetSignals failed", ex.Message, ex.StackTrace);
            }
            CancelComsTokenSource = new CancellationTokenSource();
            // start reading
            ReadingTask = ReadUntilClosed(port, CancelComsTokenSource.Token);
            JS.Set("_writer", Writer);
        }
        public async Task StopComs()
        {
            if (!ComsEnabled) return;
            ComsEnabled = false;
            var cts = CancelComsTokenSource;
            CancelComsTokenSource = null;
            cts?.Cancel();
            var rt = ReadingTask;
            ReadingTask = null;
            Writer?.ReleaseLock();
            Writer?.Dispose();
            Writer = null;
            if (rt != null)
            {
                await rt;
            }
            if (SerialPort != null && SerialPort.Connected)
            {
                try
                {
                    await SerialPort.Close();
                }
                catch (Exception ex)
                {
                    JS.Log("SerialPort.Close() failed", ex.Message, ex.StackTrace);
                }
            }
            cts?.Dispose();
        }
        public async Task DeselectPort()
        {
            if (SerialPort != null)
            {
                await StopComs();
                SerialPort!.OnDisconnect -= SerialPort_OnDisconnect;
                OnDisconnected?.Invoke(SerialPort);
                await SerialPort.Forget();
                SerialPort = null;
            }
        }
        public async Task<bool> SelectPort()
        {
            var ret = false;
            if (Serial != null)
            {
                SerialPort? serialPort = null;
                try
                {
                    serialPort = await Serial.RequestPort(
                        new()
                        {
                            Filters = new[]
                            {
                                new SerialPortRequestFilter { UsbVendorId = 0x067B }    // USB\VID_067B&PID_2303&REV_0400 -> COM 7 (currently used)
                            }
                        });
                }
                catch
                {
                    // continue;
                }
                if (serialPort != null)
                {
                    var isCelestron = await IsCelestronMount(serialPort);
                    if (isCelestron)
                    {
                        ret = true;
                        if (SerialPort != null)
                        {
                            await StopComs();
                            SerialPort!.OnDisconnect -= SerialPort_OnDisconnect;
                            OnDisconnected?.Invoke(SerialPort);
                            SerialPort = null;
                        }
                        SerialPort = serialPort;
                        SerialPort.OnDisconnect += SerialPort_OnDisconnect;
                        OnConnected?.Invoke(SerialPort);
                    }
                }
            }
            return ret;
        }

        public async Task<bool> IsCelestronMount(SerialPort port)
        {

            try
            {
                // 2. Open the port
                await port.Open(SerialOptions);

                // 3. Get Writer and Reader
                // Note: SpawnDev wrappers for ReadableStream/WritableStream 
                // usage may vary slightly depending on version, 
                // but the concept relies on getting the default reader/writer.

                using var writable = port.Writable;
                using var writer = writable.GetWriter();

                using var readable = port.Readable;
                using var reader = readable.GetReader();

                // 4. Send the Echo Command ('K' + 'A')
                // Command: 75 (K), 65 (A)
                byte[] command = new byte[] { 0x4B, 0x41 };
                await writer.Write(command);

                // 5. Read Response with a Timeout
                // We expect 'A' (65) followed by '#' (35)
                // Set a short timeout (e.g., 1000ms) to avoid hanging if it's the wrong device.

                var readBuffer = new List<byte>();
                var startTime = DateTime.Now;
                bool hashFound = false;

                while ((DateTime.Now - startTime).TotalMilliseconds < 1000)
                {
                    // ReadChunk returns { value: Uint8Array, done: bool }
                    var result = await reader.Read();

                    if (result.Done) break;
                    if (result.Value != null)
                    {
                        // Convert Uint8Array to byte[] and add to buffer
                        // Note: Ensure you handle the JS->C# array conversion correctly based on SpawnDev version
                        var chunk = result.Value;
                        readBuffer.AddRange(chunk.ToArray());

                        // Check if we received the terminator '#'
                        if (readBuffer.Contains(0x23)) // 0x23 is '#'
                        {
                            hashFound = true;
                            break;
                        }
                    }
                }

                // 6. Release lock so port can be closed
                reader.ReleaseLock();
                writer.ReleaseLock();

                // 7. Validate
                // We look for 'A' (0x41) followed immediately or eventually by '#' (0x23)
                if (hashFound && readBuffer.Contains(0x41))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Handshake failed: {ex.Message}");
            }
            finally
            {
                // Always close the port after testing
                // You would keep it open in the real app, but for a "Test" function, we close it.
                await port.Close();
            }

            return false;
        }
        public async Task Update()
        {
            JS.Log("Update");
            if (Serial != null)
            {
                JS.Log("Serial Update");
                var ports = (await Serial.GetPorts()).ToArray();
                foreach (var port in ports)
                {
                    Console.WriteLine($"Port: {port}");
                }
                var serialPort = ports.FirstOrDefault();
                if (serialPort != null)
                {
                    if (SerialPort == null)
                    {
                        SerialPort = serialPort;
                        SerialPort.OnDisconnect += SerialPort_OnDisconnect;
                        OnConnected?.Invoke(SerialPort);
                    }
                }
            }
        }
        private async Task ReadUntilClosed(SerialPort port, CancellationToken token)
        {
            ReadableStream? readable;
            ReadableStreamDefaultReader? reader = null;
            token.Register(() => reader?.Cancel());
            bool isClosed = false;
            while (!token.IsCancellationRequested && (readable = port.Readable) != null)
            {
                reader = readable.GetReader();
                var closed = reader.Closed;
                try
                {
                    while (true)
                    {
                        isClosed = closed.IsCompleted;
                        using var readResponse = await reader.Read();
                        if (readResponse.Done)
                        {
                            // Allow the serial port to be closed later.
                            // calling port.cancel() will cause it to be done
                            reader.ReleaseLock();
                            break;
                        }
                        var value = readResponse.Value;
                        if (value != null)
                        {
                            OnData?.Invoke(value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // TODO: Handle non-fatal read error.
                    var nmt = true;
                }
            }
        }
        private async Task InitASync()
        {
            await Update();
        }
        private void Serial_OnConnect(Event e)
        {
            var serialPort = e.TargetAs<SerialPort>();
            JS.Log("Serial_OnConnect", serialPort);
            if (SerialPort == null)
            {
                SerialPort = serialPort;
                JS.Log("SerialPort_OnConnect", SerialPort);
                SerialPort.OnDisconnect += SerialPort_OnDisconnect;
                OnConnected?.Invoke(SerialPort);
            }
        }
        private async void SerialPort_OnDisconnect(Event e)
        {
            JS.Log("SerialPort_OnDisconnect", SerialPort);
            await StopComs();
            SerialPort!.OnDisconnect -= SerialPort_OnDisconnect;
            OnDisconnected?.Invoke(SerialPort);
            SerialPort = null;
        }
    }
}
