using SpawnDev.BlazorJS.JSObjects;
using System.Text;

namespace SpawnDev.BlazorJS.NexStar
{
    /// <summary>
    /// Service for communicating with and controlling Celestron NexStar telescopes
    /// using the Web Serial API.<br/>
    /// Reference: https://developer.chrome.com/docs/capabilities/serial
    /// </summary>
    public class NexStarService : IAsyncBackgroundService
    {
        #region Private Fields

        private CancellationTokenSource? CancelComsTokenSource = null;
        private Navigator navigator;
        private WritableStreamDefaultWriter? Writer = null;
        private ReadableStreamDefaultReader? Reader = null;
        private Task? ReadingTask = null;
        private BlazorJSRuntime JS;
        private Task? _Ready = null;
        private List<byte> _responseBuffer = new();
        private TaskCompletionSource<byte[]>? _pendingResponse = null;
        private readonly object _responseLock = new object();

        #endregion

        #region Public Properties

        /// <summary>
        /// Ready task for async initialization
        /// </summary>
        public Task Ready => _Ready ??= InitAsync();

        /// <summary>
        /// Web Serial API interface
        /// </summary>
        public Serial? Serial { get; private set; }

        /// <summary>
        /// Currently selected serial port
        /// </summary>
        public SerialPort? SerialPort { get; private set; }

        /// <summary>
        /// Whether a serial port is connected and available
        /// </summary>
        public bool SerialPortAvailable => SerialPort?.Connected == true;

        /// <summary>
        /// Whether a serial port has been selected
        /// </summary>
        public bool SerialPortSelected => SerialPort != null;

        /// <summary>
        /// Whether communications are currently enabled
        /// </summary>
        public bool ComsEnabled { get; private set; }

        /// <summary>
        /// Telescope model (retrieved after connection)
        /// </summary>
        public TelescopeModel Model { get; private set; } = TelescopeModel.Unknown;

        /// <summary>
        /// Hand controller version string
        /// </summary>
        public string Version { get; private set; } = "";

        /// <summary>
        /// Major version number
        /// </summary>
        public int VersionMajor { get; private set; }

        /// <summary>
        /// Minor version number
        /// </summary>
        public int VersionMinor { get; private set; }

        /// <summary>
        /// Whether the telescope is aligned
        /// </summary>
        public bool IsAligned { get; private set; }

        /// <summary>
        /// Current tracking mode
        /// </summary>
        public TrackingMode CurrentTrackingMode { get; private set; } = TrackingMode.Off;

        /// <summary>
        /// Current RA/Dec position
        /// </summary>
        public RaDecCoordinates? CurrentRaDec { get; private set; }

        /// <summary>
        /// Current Az/Alt position
        /// </summary>
        public AzAltCoordinates? CurrentAzAlt { get; private set; }

        /// <summary>
        /// Current telescope location
        /// </summary>
        public GeoLocation? Location { get; private set; }

        #endregion

        #region Events

        /// <summary>
        /// Fired when a serial port is connected
        /// </summary>
        public event Action<SerialPort> OnConnected = default!;

        /// <summary>
        /// Fired when a serial port is disconnected
        /// </summary>
        public event Action<SerialPort> OnDisconnected = default!;

        /// <summary>
        /// Fired when raw data is received
        /// </summary>
        public event Action<byte[]> OnData = default!;

        /// <summary>
        /// Fired when telescope status is updated
        /// </summary>
        public event Action OnStatusChanged = default!;

        #endregion

        #region Serial Configuration

        /// <summary>
        /// Celestron standard serial settings: 9600 baud, 8N1
        /// </summary>
        private readonly SerialOptions SerialOptions = new SerialOptions
        {
            BaudRate = 9600,
            DataBits = 8,
            StopBits = 1,
            Parity = "none"
        };

        /// <summary>
        /// USB Vendor ID filter for Prolific USB-to-Serial adapters
        /// </summary>
        private const int ProlificVendorId = 0x067B;

        /// <summary>
        /// Command timeout in milliseconds
        /// </summary>
        private const int CommandTimeoutMs = 2000;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new NexStarService instance
        /// </summary>
        public NexStarService(BlazorJSRuntime js)
        {
            JS = js;
            if (!JS.IsBrowser)
            {
                navigator = null!;
                return;
            }
            navigator = JS.Get<Navigator?>("navigator")!;
            Serial = navigator!.Serial;
            if (Serial != null)
            {
                Serial.OnConnect += Serial_OnConnect;
            }
        }

        #endregion

        #region Port Selection & Connection

        /// <summary>
        /// Opens device picker and selects a serial port
        /// </summary>
        /// <returns>True if a valid NexStar port was selected</returns>
        public async Task<bool> SelectPortAsync()
        {
            if (Serial == null) return false;

            SerialPort? serialPort = null;
            try
            {
                serialPort = await Serial.RequestPort(new()
                {
                    Filters = new[]
                    {
                        new SerialPortRequestFilter { UsbVendorId = ProlificVendorId }
                    }
                });
            }
            catch
            {
                return false;
            }

            if (serialPort != null)
            {
                var isCelestron = await ValidateCelestronMountAsync(serialPort);
                if (isCelestron)
                {
                    if (SerialPort != null)
                    {
                        await StopComsAsync();
                        SerialPort!.OnDisconnect -= SerialPort_OnDisconnect;
                        OnDisconnected?.Invoke(SerialPort);
                        SerialPort = null;
                    }
                    SerialPort = serialPort;
                    SerialPort.OnDisconnect += SerialPort_OnDisconnect;
                    OnConnected?.Invoke(SerialPort);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Deselects the current port
        /// </summary>
        public async Task DeselectPortAsync()
        {
            if (SerialPort != null)
            {
                await StopComsAsync();
                SerialPort!.OnDisconnect -= SerialPort_OnDisconnect;
                OnDisconnected?.Invoke(SerialPort);
                await SerialPort.Forget();
                SerialPort = null;
                ResetTelescopeState();
            }
        }

        /// <summary>
        /// Validates that a port is connected to a Celestron mount using echo command
        /// </summary>
        private async Task<bool> ValidateCelestronMountAsync(SerialPort port)
        {
            try
            {
                await port.Open(SerialOptions);

                using var writable = port.Writable;
                using var writer = writable.GetWriter();
                using var readable = port.Readable;
                using var reader = readable.GetReader();

                // Send echo command: K + test char
                byte[] command = new byte[] { 0x4B, 0x41 }; // 'K', 'A'
                await writer.Write(command);

                var readBuffer = new List<byte>();
                var startTime = DateTime.Now;
                bool hashFound = false;

                while ((DateTime.Now - startTime).TotalMilliseconds < 1000)
                {
                    var result = await reader.Read();
                    if (result.Done) break;
                    if (result.Value != null)
                    {
                        readBuffer.AddRange(result.Value.ToArray());
                        if (readBuffer.Contains(0x23)) // '#'
                        {
                            hashFound = true;
                            break;
                        }
                    }
                }

                reader.ReleaseLock();
                writer.ReleaseLock();

                // Valid if we got 'A' followed by '#'
                return hashFound && readBuffer.Contains(0x41);
            }
            catch (Exception ex)
            {
                JS.Log($"Validation failed: {ex.Message}");
            }
            finally
            {
                try { await port.Close(); } catch { }
            }
            return false;
        }

        #endregion

        #region Communication Control

        /// <summary>
        /// Starts communication with the telescope
        /// </summary>
        public async Task StartComsAsync()
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
                JS.Log("StartComs failed. Cannot open port.", ex.Message);
                ComsEnabled = false;
                return;
            }

            try
            {
                Writer = port.Writable.GetWriter();
            }
            catch (Exception ex)
            {
                JS.Log("GetWriter failed", ex.Message);
                try { await port.Close(); } catch { }
                ComsEnabled = false;
                return;
            }

            CancelComsTokenSource = new CancellationTokenSource();
            ReadingTask = ReadLoopAsync(port, CancelComsTokenSource.Token);

            // Initialize telescope state
            await RefreshTelescopeInfoAsync();
            OnStatusChanged?.Invoke();
        }

        /// <summary>
        /// Stops communication with the telescope
        /// </summary>
        public async Task StopComsAsync()
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
                try { await rt; } catch { }
            }

            if (SerialPort != null && SerialPort.Connected)
            {
                try { await SerialPort.Close(); } catch { }
            }

            cts?.Dispose();
            ResetTelescopeState();
            OnStatusChanged?.Invoke();
        }

        private void ResetTelescopeState()
        {
            Model = TelescopeModel.Unknown;
            Version = "";
            VersionMajor = 0;
            VersionMinor = 0;
            IsAligned = false;
            CurrentTrackingMode = TrackingMode.Off;
            CurrentRaDec = null;
            CurrentAzAlt = null;
            Location = null;
        }

        #endregion

        #region Low-Level Communication

        /// <summary>
        /// Sends a command and waits for response terminated by '#'
        /// </summary>
        private async Task<byte[]?> SendCommandAsync(byte[] command, int timeoutMs = CommandTimeoutMs)
        {
            if (Writer == null || !ComsEnabled) return null;

            lock (_responseLock)
            {
                _responseBuffer.Clear();
                _pendingResponse = new TaskCompletionSource<byte[]>();
            }

            try
            {
                await Writer.Ready;
                await Writer.Write(command);

                using var cts = new CancellationTokenSource(timeoutMs);
                cts.Token.Register(() => _pendingResponse?.TrySetCanceled());

                return await _pendingResponse.Task;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                JS.Log($"SendCommand error: {ex.Message}");
                return null;
            }
            finally
            {
                lock (_responseLock)
                {
                    _pendingResponse = null;
                }
            }
        }


        /// <summary>
        /// Sends a string command and returns string response
        /// </summary>
        private async Task<string?> SendStringCommandAsync(string command)
        {
            var response = await SendCommandAsync(Encoding.ASCII.GetBytes(command));
            if (response == null) return null;
            return Encoding.ASCII.GetString(response).TrimEnd('#');
        }

        /// <summary>
        /// Background read loop
        /// </summary>
        private async Task ReadLoopAsync(SerialPort port, CancellationToken token)
        {
            ReadableStream? readable;
            ReadableStreamDefaultReader? reader = null;
            token.Register(() => reader?.Cancel());

            while (!token.IsCancellationRequested && (readable = port.Readable) != null)
            {
                reader = readable.GetReader();
                try
                {
                    while (true)
                    {
                        using var readResponse = await reader.Read();
                        if (readResponse.Done) break;

                        var value = readResponse.Value;
                        if (value != null)
                        {
                            var bytes = value.ToArray();
                            OnData?.Invoke(bytes);

                            lock (_responseLock)
                            {
                                _responseBuffer.AddRange(bytes);
                                
                                // Check for terminator
                                int terminatorIndex = _responseBuffer.IndexOf((byte)'#');
                                if (terminatorIndex >= 0 && _pendingResponse != null)
                                {
                                    var response = _responseBuffer.Take(terminatorIndex + 1).ToArray();
                                    _responseBuffer.RemoveRange(0, terminatorIndex + 1);
                                    _pendingResponse.TrySetResult(response);
                                }
                            }
                        }
                    }
                    reader.ReleaseLock();
                }
                catch
                {
                    try { reader.ReleaseLock(); } catch { }
                }
            }
        }

        #endregion

        #region Telescope Commands - Basic

        /// <summary>
        /// Refreshes telescope info (version, model, alignment)
        /// </summary>
        public async Task RefreshTelescopeInfoAsync()
        {
            await GetVersionAsync();
            await GetModelAsync();
            await GetAlignmentStatusAsync();
            await GetTrackingModeAsync();
        }

        /// <summary>
        /// Gets the hand controller version
        /// </summary>
        public async Task<string?> GetVersionAsync()
        {
            var response = await SendCommandAsync(new byte[] { (byte)'V' });
            if (response == null || response.Length < 2) return null;

            VersionMajor = response[0];
            VersionMinor = response[1];
            Version = $"{VersionMajor}.{VersionMinor}";
            OnStatusChanged?.Invoke();
            return Version;
        }

        /// <summary>
        /// Gets the telescope model
        /// </summary>
        public async Task<TelescopeModel> GetModelAsync()
        {
            var response = await SendCommandAsync(new byte[] { (byte)'m' });
            if (response == null || response.Length < 2) return TelescopeModel.Unknown;

            var modelId = response[0];
            Model = Enum.IsDefined(typeof(TelescopeModel), (int)modelId) 
                ? (TelescopeModel)modelId 
                : TelescopeModel.Unknown;
            OnStatusChanged?.Invoke();
            return Model;
        }

        /// <summary>
        /// Sends an echo command for testing
        /// </summary>
        public async Task<char?> EchoAsync(char testChar)
        {
            var response = await SendCommandAsync(new byte[] { (byte)'K', (byte)testChar });
            if (response == null || response.Length < 2) return null;
            return (char)response[0];
        }

        /// <summary>
        /// Gets alignment status
        /// </summary>
        public async Task<bool> GetAlignmentStatusAsync()
        {
            var response = await SendCommandAsync(new byte[] { (byte)'J' });
            if (response == null || response.Length < 2) return false;
            IsAligned = response[0] == 1;
            OnStatusChanged?.Invoke();
            return IsAligned;
        }

        /// <summary>
        /// Checks if a GoTo operation is in progress
        /// </summary>
        public async Task<bool> IsGotoInProgressAsync()
        {
            var response = await SendStringCommandAsync("L");
            return response == "1";
        }

        /// <summary>
        /// Cancels any GoTo operation in progress
        /// </summary>
        public async Task<bool> CancelGotoAsync()
        {
            var response = await SendStringCommandAsync("M");
            return response != null;
        }

        #endregion

        #region Telescope Commands - Position

        /// <summary>
        /// Gets current RA/Dec position
        /// </summary>
        /// <param name="precise">Use precise (32-bit) format for sub-arcsecond accuracy</param>
        public async Task<RaDecCoordinates?> GetRaDecAsync(bool precise = false)
        {
            var cmd = precise ? "e" : "E";
            var response = await SendStringCommandAsync(cmd);
            if (response == null) return null;

            var (ra, dec) = NexStarProtocol.ParsePositionResponse(response);
            CurrentRaDec = new RaDecCoordinates(ra, dec);
            OnStatusChanged?.Invoke();
            return CurrentRaDec;
        }

        /// <summary>
        /// Gets current Az/Alt position
        /// </summary>
        /// <param name="precise">Use precise (32-bit) format for sub-arcsecond accuracy</param>
        public async Task<AzAltCoordinates?> GetAzAltAsync(bool precise = false)
        {
            var cmd = precise ? "z" : "Z";
            var response = await SendStringCommandAsync(cmd);
            if (response == null) return null;

            var (az, alt) = NexStarProtocol.ParsePositionResponse(response);
            CurrentAzAlt = new AzAltCoordinates(az, alt);
            OnStatusChanged?.Invoke();
            return CurrentAzAlt;
        }

        /// <summary>
        /// Commands telescope to slew to RA/Dec position
        /// </summary>
        public async Task<bool> GotoRaDecAsync(double ra, double dec, bool precise = false)
        {
            if (ra < 0 || ra > 360 || dec < -90 || dec > 90) return false;
            var command = NexStarProtocol.FormatGotoRaDecCommand(ra, dec, precise);
            var response = await SendCommandAsync(command);
            return response != null;
        }

        /// <summary>
        /// Commands telescope to slew to Az/Alt position
        /// </summary>
        public async Task<bool> GotoAzAltAsync(double az, double alt, bool precise = false)
        {
            if (az < 0 || az > 360 || alt < -90 || alt > 90) return false;
            var command = NexStarProtocol.FormatGotoAzAltCommand(az, alt, precise);
            var response = await SendCommandAsync(command);
            return response != null;
        }

        /// <summary>
        /// Syncs telescope position to provided RA/Dec coordinates
        /// </summary>
        public async Task<bool> SyncRaDecAsync(double ra, double dec, bool precise = false)
        {
            if (ra < 0 || ra > 360 || dec < -90 || dec > 90) return false;
            var command = NexStarProtocol.FormatSyncRaDecCommand(ra, dec, precise);
            var response = await SendCommandAsync(command);
            return response != null;
        }

        #endregion

        #region Telescope Commands - Slewing

        /// <summary>
        /// Starts slewing at a fixed rate
        /// </summary>
        public async Task<bool> SlewFixedAsync(SlewAxis axis, SlewDirection direction, SlewRate rate)
        {
            var command = NexStarProtocol.FormatFixedSlewCommand(axis, direction, rate);
            var response = await SendCommandAsync(command);
            return response != null;
        }

        /// <summary>
        /// Starts slewing at a variable rate
        /// </summary>
        /// <param name="axis">Axis to slew</param>
        /// <param name="direction">Direction to slew</param>
        /// <param name="rateArcsecPerSec">Rate in arcseconds per second (max ~16000)</param>
        public async Task<bool> SlewVariableAsync(SlewAxis axis, SlewDirection direction, double rateArcsecPerSec)
        {
            var command = NexStarProtocol.FormatVariableSlewCommand(axis, direction, rateArcsecPerSec);
            var response = await SendCommandAsync(command);
            return response != null;
        }

        /// <summary>
        /// Stops slewing on a specific axis
        /// </summary>
        public async Task<bool> StopSlewAsync(SlewAxis axis)
        {
            return await SlewFixedAsync(axis, SlewDirection.Positive, SlewRate.Stop);
        }

        /// <summary>
        /// Stops slewing on both axes
        /// </summary>
        public async Task StopAllSlewAsync()
        {
            await StopSlewAsync(SlewAxis.RaAzm);
            await StopSlewAsync(SlewAxis.DecAlt);
        }

        #endregion

        #region Telescope Commands - Tracking

        /// <summary>
        /// Gets current tracking mode
        /// </summary>
        public async Task<TrackingMode> GetTrackingModeAsync()
        {
            var response = await SendCommandAsync(new byte[] { (byte)'t' });
            if (response == null || response.Length < 2) return TrackingMode.Off;

            CurrentTrackingMode = response[0] switch
            {
                0 => TrackingMode.Off,
                1 => TrackingMode.AltAz,
                2 => TrackingMode.EQNorth,
                3 => TrackingMode.EQSouth,
                _ => TrackingMode.Off
            };
            OnStatusChanged?.Invoke();
            return CurrentTrackingMode;
        }

        /// <summary>
        /// Sets tracking mode
        /// </summary>
        public async Task<bool> SetTrackingModeAsync(TrackingMode mode)
        {
            var command = new byte[] { (byte)'T', (byte)mode };
            var response = await SendCommandAsync(command);
            if (response != null)
            {
                CurrentTrackingMode = mode;
                OnStatusChanged?.Invoke();
            }
            return response != null;
        }

        #endregion

        #region Telescope Commands - Time/Location

        /// <summary>
        /// Gets telescope time
        /// </summary>
        public async Task<TelescopeTime?> GetTimeAsync()
        {
            var response = await SendCommandAsync(new byte[] { (byte)'h' });
            if (response == null || response.Length < 9) return null;
            return NexStarProtocol.ParseTimeResponse(response);
        }

        /// <summary>
        /// Sets telescope time
        /// </summary>
        public async Task<bool> SetTimeAsync(DateTime time, int tzOffset, bool dst)
        {
            var command = NexStarProtocol.FormatSetTimeCommand(time, tzOffset, dst);
            var response = await SendCommandAsync(command);
            return response != null;
        }

        /// <summary>
        /// Sets telescope time to current browser time
        /// </summary>
        public async Task<bool> SyncTimeAsync()
        {
            var now = DateTime.Now;
            var offset = TimeZoneInfo.Local.GetUtcOffset(now);
            var isDst = TimeZoneInfo.Local.IsDaylightSavingTime(now);
            return await SetTimeAsync(now, (int)offset.TotalHours, isDst);
        }

        /// <summary>
        /// Gets telescope location
        /// </summary>
        public async Task<GeoLocation?> GetLocationAsync()
        {
            var response = await SendCommandAsync(new byte[] { (byte)'w' });
            if (response == null || response.Length < 9) return null;
            Location = NexStarProtocol.ParseLocationResponse(response);
            OnStatusChanged?.Invoke();
            return Location;
        }

        /// <summary>
        /// Sets telescope location
        /// </summary>
        public async Task<bool> SetLocationAsync(double lat, double lon)
        {
            if (lat < -90 || lat > 90 || lon < -180 || lon > 180) return false;
            var command = NexStarProtocol.FormatSetLocationCommand(lat, lon);
            var response = await SendCommandAsync(command);
            if (response != null)
            {
                Location = new GeoLocation(lat, lon);
                OnStatusChanged?.Invoke();
            }
            return response != null;
        }

        #endregion

        #region Event Handlers

        private async Task InitAsync()
        {
            await UpdateAsync();
        }

        private async Task UpdateAsync()
        {
            if (Serial == null) return;

            var ports = (await Serial.GetPorts()).ToArray();
            var serialPort = ports.FirstOrDefault();
            if (serialPort != null && SerialPort == null)
            {
                SerialPort = serialPort;
                SerialPort.OnDisconnect += SerialPort_OnDisconnect;
                OnConnected?.Invoke(SerialPort);
            }
        }

        private void Serial_OnConnect(Event e)
        {
            var serialPort = e.TargetAs<SerialPort>();
            if (SerialPort == null && serialPort != null)
            {
                SerialPort = serialPort;
                SerialPort.OnDisconnect += SerialPort_OnDisconnect;
                OnConnected?.Invoke(SerialPort);
            }
        }

        private async void SerialPort_OnDisconnect(Event e)
        {
            await StopComsAsync();
            if (SerialPort != null)
            {
                SerialPort.OnDisconnect -= SerialPort_OnDisconnect;
                OnDisconnected?.Invoke(SerialPort);
                SerialPort = null;
            }
        }

        #endregion

        #region Legacy Compatibility

        /// <summary>
        /// Legacy: Start communications
        /// </summary>
        [Obsolete("Use StartComsAsync instead")]
        public Task StartComs() => StartComsAsync();

        /// <summary>
        /// Legacy: Stop communications
        /// </summary>
        [Obsolete("Use StopComsAsync instead")]
        public Task StopComs() => StopComsAsync();

        /// <summary>
        /// Legacy: Select port
        /// </summary>
        [Obsolete("Use SelectPortAsync instead")]
        public Task<bool> SelectPort() => SelectPortAsync();

        /// <summary>
        /// Legacy: Deselect port
        /// </summary>
        [Obsolete("Use DeselectPortAsync instead")]
        public Task DeselectPort() => DeselectPortAsync();

        #endregion
    }
}
