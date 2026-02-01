using SGPdotNET.TLE;
using SpawnDev.BlazorJS.NexStar.App.Services;
using static SpawnDev.BlazorJS.NexStar.App.Services.PlaneService;

namespace SpawnDev.BlazorJS.NexStar.App.Services;

/// <summary>
/// A unified sky object for display on the overhead map
/// </summary>
public class SkyObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = ""; // Satellite, Plane, Planet, Star, DSO
    public string Subcategory { get; set; } = ""; // e.g., "Space Station", "Starlink", "Messier"
    public double Azimuth { get; set; }
    public double Altitude { get; set; } // elevation angle above horizon
    public double? Magnitude { get; set; }
    public double? Distance { get; set; } // km for satellites/planes, light years for stars/DSOs
    public string? ExtraInfo { get; set; } // Additional info for tooltip
    
    // For tracking
    public double? Ra { get; set; }
    public double? Dec { get; set; }
    
    // Source reference
    public object? SourceObject { get; set; }
}

/// <summary>
/// Aggregates sky objects from all sources for the overhead view
/// </summary>
public class OverheadService
{
    private readonly SatelliteService SatelliteService;
    private readonly PlaneService PlaneService;
    private readonly LocationService LocationService;

    public OverheadService(SatelliteService satelliteService, PlaneService planeService, LocationService locationService)
    {
        SatelliteService = satelliteService;
        PlaneService = planeService;
        LocationService = locationService;
    }

    /// <summary>
    /// Filter settings for what to display
    /// </summary>
    public class FilterSettings
    {
        public bool ShowSatellites { get; set; } = true;
        public bool ShowPlanes { get; set; } = true;
        public bool ShowPlanets { get; set; } = true;
        public bool ShowStars { get; set; } = true;
        public bool ShowDSOs { get; set; } = true;
        
        // Subcategory filters
        public HashSet<string> SatelliteCategories { get; set; } = new() { "stations", "visual" };
    }

    /// <summary>
    /// Get all visible sky objects based on current filters
    /// </summary>
    public async Task<List<SkyObject>> GetVisibleObjectsAsync(FilterSettings filters)
    {
        var location = LocationService.Location;
        if (location == null) return new List<SkyObject>();

        var objects = new List<SkyObject>();
        var now = DateTime.UtcNow;

        // Satellites
        if (filters.ShowSatellites)
        {
            foreach (var category in filters.SatelliteCategories)
            {
                var tles = await SatelliteService.GetTlesAsync(category);
                foreach (var tle in tles)
                {
                    var pos = SatelliteService.CalculatePosition(tle, location);
                    if (pos != null && pos.TopocentricAltitude > 0) // Above horizon
                    {
                        objects.Add(new SkyObject
                        {
                            Id = $"sat_{tle.Name}",
                            Name = tle.Name,
                            Category = "Satellite",
                            Subcategory = GetSatelliteSubcategory(category),
                            Azimuth = pos.TopocentricAzimuth,
                            Altitude = pos.TopocentricAltitude,
                            Distance = pos.RangeKm,
                            ExtraInfo = $"Range: {pos.RangeKm:F0} km",
                            SourceObject = tle
                        });
                    }
                }
            }
        }

        // Planes
        if (filters.ShowPlanes)
        {
            var planes = await PlaneService.GetNearbyAircraftAsync(location);
            foreach (var plane in planes)
            {
                if (plane.Altitude.HasValue && plane.Altitude > 0 && plane.Azimuth.HasValue)
                {
                    objects.Add(new SkyObject
                    {
                        Id = $"plane_{plane.Icao24}",
                        Name = plane.Callsign ?? plane.Icao24,
                        Category = "Plane",
                        Subcategory = plane.OriginCountry ?? "Unknown",
                        Azimuth = plane.Azimuth.Value,
                        Altitude = plane.Altitude.Value,
                        Distance = plane.DistanceKm,
                        ExtraInfo = $"Alt: {plane.BaroAltitude:F0}m, Speed: {(plane.Velocity ?? 0) * 3.6:F0} km/h",
                        SourceObject = plane
                    });
                }
            }
        }

        // Planets
        if (filters.ShowPlanets)
        {
            objects.AddRange(GetPlanets(location, now));
        }

        // Stars
        if (filters.ShowStars)
        {
            objects.AddRange(GetVisibleStars(location, now));
        }

        // DSOs (Messier)
        if (filters.ShowDSOs)
        {
            objects.AddRange(GetVisibleDSOs(location, now));
        }

        return objects;
    }

    private string GetSatelliteSubcategory(string category) => category switch
    {
        "stations" => "Space Station",
        "starlink" => "Starlink",
        "visual" => "Bright",
        "weather" => "Weather",
        "gps-ops" => "GPS",
        _ => category
    };

    /// <summary>
    /// Calculate basic planetary positions (simplified)
    /// </summary>
    private List<SkyObject> GetPlanets(GeoLocation location, DateTime utc)
    {
        // Simplified planet positions - in reality you'd use VSOP87 or similar
        // For now, just return empty - would need proper ephemeris calculations
        return new List<SkyObject>();
    }

    /// <summary>
    /// Get visible alignment/bright stars
    /// </summary>
    private List<SkyObject> GetVisibleStars(GeoLocation location, DateTime utc)
    {
        var stars = new List<SkyObject>();

        foreach (var star in CelestialCatalogs.AlignmentStars)
        {
            var altAz = AstronomyMath.EquatorialToHorizontal(
                star.RightAscension, star.Declination, location.Latitude, location.Longitude, utc);
            if (altAz.Altitude > 5) // Above 5° horizon
            {
                stars.Add(new SkyObject
                {
                    Id = $"star_{star.Name}",
                    Name = star.Name,
                    Category = "Star",
                    Subcategory = star.Constellation,
                    Azimuth = altAz.Azimuth,
                    Altitude = altAz.Altitude,
                    Magnitude = star.Magnitude,
                    Ra = star.RightAscension,
                    Dec = star.Declination,
                    ExtraInfo = $"Mag: {star.Magnitude:F1}, {star.Constellation}",
                    SourceObject = star
                });
            }
        }
        return stars;
    }

    /// <summary>
    /// Get visible Messier objects
    /// </summary>
    private List<SkyObject> GetVisibleDSOs(GeoLocation location, DateTime utc)
    {
        var dsos = new List<SkyObject>();

        foreach (var m in CelestialCatalogs.Messier)
        {
            var altAz = AstronomyMath.EquatorialToHorizontal(
                m.RightAscension, m.Declination, location.Latitude, location.Longitude, utc);
            if (altAz.Altitude > 10) // Above 10° horizon
            {
                dsos.Add(new SkyObject
                {
                    Id = $"dso_{m.Id}",
                    Name = string.IsNullOrEmpty(m.Name) ? m.Id : $"{m.Id} {m.Name}",
                    Category = "DSO",
                    Subcategory = m.ObjectType,
                    Azimuth = altAz.Azimuth,
                    Altitude = altAz.Altitude,
                    Magnitude = m.Magnitude,
                    Ra = m.RightAscension,
                    Dec = m.Declination,
                    ExtraInfo = $"{m.ObjectType}, Mag: {m.Magnitude:F1}",
                    SourceObject = m
                });
            }
        }
        return dsos;
    }
}
