//using Microsoft.JSInterop;
//using SpawnDev.BlazorJS.JSObjects;

//namespace SpawnDev.BlazorJS.NexStar
//{
//    /// <summary>
//    /// Represents the position of the device at a given time.
//    /// </summary>
//    public class GeolocationPosition : JSObject
//    {
//        public GeolocationPosition(IJSInProcessObjectReference _ref) : base(_ref) { }
        
//        /// <summary>
//        /// A GeolocationCoordinates object representing the geographic location.
//        /// </summary>
//        public GeolocationCoordinates Coords => JSRef!.Get<GeolocationCoordinates>("coords");
        
//        /// <summary>
//        /// A DOMTimeStamp representing the time at which the location was retrieved.
//        /// </summary>
//        public long Timestamp => JSRef!.Get<long>("timestamp");
//    }

//    /// <summary>
//    /// Represents the position and altitude of the device on Earth, as well as the accuracy with which these properties are calculated.
//    /// </summary>
//    public class GeolocationCoordinates : JSObject
//    {
//        public GeolocationCoordinates(IJSInProcessObjectReference _ref) : base(_ref) { }
        
//        /// <summary>
//        /// Latitude in decimal degrees.
//        /// </summary>
//        public double Latitude => JSRef!.Get<double>("latitude");
        
//        /// <summary>
//        /// Longitude in decimal degrees.
//        /// </summary>
//        public double Longitude => JSRef!.Get<double>("longitude");
        
//        /// <summary>
//        /// Altitude in meters, relative to the sea level.
//        /// </summary>
//        public double? Altitude => JSRef!.Get<double?>("altitude");
        
//        /// <summary>
//        /// Accuracy of the latitude and longitude properties, expressed in meters.
//        /// </summary>
//        public double Accuracy => JSRef!.Get<double>("accuracy");
        
//        /// <summary>
//        /// Accuracy of the altitude expressed in meters.
//        /// </summary>
//        public double? AltitudeAccuracy => JSRef!.Get<double?>("altitudeAccuracy");
        
//        /// <summary>
//        /// Direction towards which the device is facing, in degrees (0 to 360).
//        /// </summary>
//        public double? Heading => JSRef!.Get<double?>("heading");
        
//        /// <summary>
//        /// Velocity of the device in meters per second.
//        /// </summary>
//        public double? Speed => JSRef!.Get<double?>("speed");
//    }

//    /// <summary>
//    /// Represents the reason of an error occurring when using the geolocating device.
//    /// </summary>
//    public class GeolocationPositionError : JSObject
//    {
//        public GeolocationPositionError(IJSInProcessObjectReference _ref) : base(_ref) { }
        
//        /// <summary>
//        /// Returns an unsigned short representing the error code.
//        /// </summary>
//        public int Code => JSRef!.Get<int>("code");
        
//        /// <summary>
//        /// Returns a human-readable DOMString describing the details of the error.
//        /// </summary>
//        public string Message => JSRef!.Get<string>("message");
        
//        public const int PERMISSION_DENIED = 1;
//        public const int POSITION_UNAVAILABLE = 2;
//        public const int TIMEOUT = 3;
//    }
//}
