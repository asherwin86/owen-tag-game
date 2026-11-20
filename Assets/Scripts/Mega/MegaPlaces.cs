using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using TagGame.Maps;

namespace TagGame.Mega
{
    /// <summary>
    /// Every place in the World, with a name, worked out from the same layout the cities are built from:
    /// cities (one every 10 km in the big worlds), airports, schools, malls, parks, lakes, and a street address
    /// for every house. Used by the car's map, search box and navigation.
    /// </summary>
    public static class MegaPlaces
    {
        public enum Kind { Country, City, Airport, School, Mall, Park, Lake, Address }

        public struct Place
        {
            public string Name, Detail;
            public Kind Kind;
            public int Country;
            public double LX, LZ;     // relative to the country's centre
            public Vector3 Local => MegaWorld.ToLocal(MegaWorld.Countries[Country].CX + LX, MegaWorld.Countries[Country].CZ + LZ);
        }

        public const double CityCell = 10000.0;

        private static readonly string[] Pre = { "Brook", "Maple", "Silver", "Red", "Oak", "Sun", "Clear", "Stone", "Green", "Bright", "North", "South", "East", "West", "River", "Lake", "Pine", "Gold", "Fair", "High", "Cedar", "Rose", "Ash", "Elm", "Star", "Moon", "Fox", "Bear", "Eagle", "Crystal" };
        private static readonly string[] Post = { "haven", "field", "ton", "ville", "wood", "port", "bridge", "dale", "view", "side", "ford", "crest", "gate", "bury", "hill", "bay", "springs", "falls", "grove", "point" };
        private static readonly string[] StreetNames = { "Maple", "Oak", "Cedar", "Pine", "Elm", "Birch", "Willow", "Spruce", "Ash", "Hill", "Lake", "River", "Sunset", "Park", "Ocean", "Meadow", "Forest", "Valley", "Highland", "Bay", "Cherry", "Orchard", "Station", "Harbor", "Garden", "Mill", "Church", "Market", "Bridge", "Spring" };
        private static readonly string[] SchoolKinds = { "Primary School", "High School", "Middle School", "Elementary", "Academy", "College" };

        private static uint H(int country, long a, long b, int salt)
        {
            uint h = (uint)(a * 73856093L ^ b * 19349663L ^ (long)salt * 83492791L) ^ MegaWorld.Countries[country].Seed * 2654435761u;
            h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16;
            return h;
        }

        // ------------------------------------------------------------------ names

        public static long CellX(double l) => (long)System.Math.Floor(l / CityCell);

        public static string CityName(int country, long cx, long cz)
        {
            var c = MegaWorld.Countries[country];
            if (!c.Huge) return c.Name;
            uint h = H(country, cx, cz, 1);
            return Pre[h % Pre.Length] + Post[(h >> 8) % Post.Length];
        }

        public static string CityAt(int country, double lx, double lz) => CityName(country, CellX(lx), CellX(lz));

        public static string SchoolName(int country, int ci, int cj)
        {
            uint h = H(country, ci, cj, 2);
            return StreetNames[h % StreetNames.Length] + " " + SchoolKinds[(h >> 8) % SchoolKinds.Length];
        }

        private static string MallName(int country, int ci, int cj) { uint h = H(country, ci, cj, 3); return StreetNames[h % StreetNames.Length] + ((h >> 8) % 2 == 0 ? " Mall" : " Plaza"); }
        private static string ParkName(int country, int ci, int cj) { uint h = H(country, ci, cj, 4); return StreetNames[h % StreetNames.Length] + ((h >> 8) % 2 == 0 ? " Park" : " Gardens"); }
        private static string LakeName(int country, int ci, int cj) { uint h = H(country, ci, cj, 5); return "Lake " + Pre[h % Pre.Length]; }

        /// <summary>Street names: lines along x are Avenues/Roads, lines along z are Streets/Lanes. k = 100 m line index from the country centre.</summary>
        public static string StreetName(int country, int axis, long k)
        {
            long cell = (long)System.Math.Floor(k * 100.0 / CityCell);
            uint h = H(country, k, axis, 6);
            string n = StreetNames[h % StreetNames.Length];
            long within = k - cell * (long)(CityCell / 100.0);
            string num = within != 0 && (h >> 12) % 3 == 0 ? (Ordinal(System.Math.Abs(within)) + " ") : "";
            return axis == 0 ? num + n + ((h >> 8) % 2 == 0 ? " Avenue" : " Road") : num + n + ((h >> 8) % 2 == 0 ? " Street" : " Lane");
        }

        private static string Ordinal(long n)
        {
            long t = n % 100;
            string s = (t >= 11 && t <= 13) ? "th" : (n % 10 == 1 ? "st" : n % 10 == 2 ? "nd" : n % 10 == 3 ? "rd" : "th");
            return n + s;
        }

        /// <summary>The street address of a spot (country-relative), like "214 Maple Avenue, Brookhaven".</summary>
        public static string AddressAt(int country, double lx, double lz)
        {
            long kx = (long)System.Math.Round(lx / 100.0), kz = (long)System.Math.Round(lz / 100.0);
            double dz = System.Math.Abs(lz - kz * 100.0), dx = System.Math.Abs(lx - kx * 100.0);
            int axis = dz <= dx ? 0 : 1;               // nearest street: along x (constant z) or along z
            long k = axis == 0 ? kz : kx;
            double along = axis == 0 ? lx : lz;
            double side = axis == 0 ? lz - kz * 100.0 : lx - kx * 100.0;
            long number = HouseNumber(along, side);
            return number + " " + StreetName(country, axis, k) + ", " + CityAt(country, lx, lz);
        }

        private static long HouseNumber(double along, double side)
        {
            double cellStart = System.Math.Floor(along / CityCell) * CityCell;
            long n = (long)((along - cellStart) / 10.0) * 2 + 1;
            if (side > 0) n++;
            return n;
        }

        private static double AlongFromNumber(long cellStartIdx, long number) => cellStartIdx * CityCell + ((number - 1) / 2) * 10.0 + 5.0;

        // ------------------------------------------------------------------ places near a point

        /// <summary>Named places (schools, malls, parks, lakes, airports) within radius r of a country-relative point.</summary>
        public static void PlacesNear(int country, double lx, double lz, double r, List<Place> into, int max = 400)
        {
            var c = MegaWorld.Countries[country];
            double S = MegaWorld.ChunkSize;
            int a0 = (int)System.Math.Floor((lx - r + c.Half) / S), a1 = (int)System.Math.Floor((lx + r + c.Half) / S);
            int b0 = (int)System.Math.Floor((lz - r + c.Half) / S), b1 = (int)System.Math.Floor((lz + r + c.Half) / S);
            int n = (int)System.Math.Round(c.Half * 2.0 / S);
            a0 = System.Math.Max(a0, 0); b0 = System.Math.Max(b0, 0); a1 = System.Math.Min(a1, n - 1); b1 = System.Math.Min(b1, n - 1);
            for (int ci = a0; ci <= a1 && into.Count < max; ci++)
                for (int cj = b0; cj <= b1 && into.Count < max; cj++)
                {
                    var kind = MapManager.KindOf(c, ci, cj);
                    double x0 = -c.Half + (double)ci * S, z0 = -c.Half + (double)cj * S;
                    switch (kind)
                    {
                        case MapManager.ChunkKind.Park: Add(into, ParkName(country, ci, cj), Kind.Park, country, x0 + 250, z0 + 250); break;
                        case MapManager.ChunkKind.Lake: Add(into, LakeName(country, ci, cj), Kind.Lake, country, x0 + 250, z0 + 25); break;
                        case MapManager.ChunkKind.City:
                        case MapManager.ChunkKind.Town:
                            MapManager.ChunkFeatures(c, ci, cj, kind, out int si, out int sj, out int mi, out int mj);
                            if (si >= 0) Add(into, SchoolName(country, ci, cj), Kind.School, country, x0 + 100.0 * (si + 1), z0 + 100.0 * (sj + 1) - 60.0);
                            if (mi >= 0) Add(into, MallName(country, ci, cj), Kind.Mall, country, x0 + 100.0 * (mi + 1), z0 + 100.0 * (mj + 1) - 60.0);
                            break;
                    }
                }
            var aps = new List<MegaWorld.V2d>();
            MegaWorld.AirportsNear(c, lx, lz, r, aps);
            foreach (var a in aps) Add(into, CityAt(country, a.x, a.y) + " Airport", Kind.Airport, country, a.x + 250, a.y + 222);
        }

        private static void Add(List<Place> into, string name, Kind kind, int country, double lx, double lz)
        {
            into.Add(new Place { Name = name, Kind = kind, Country = country, LX = lx, LZ = lz, Detail = KindLabel(kind) + " · " + CityAt(country, lx, lz) });
        }

        public static string KindLabel(Kind k)
        {
            switch (k)
            {
                case Kind.Country: return "Country"; case Kind.City: return "City"; case Kind.Airport: return "Airport";
                case Kind.School: return "School"; case Kind.Mall: return "Shopping"; case Kind.Park: return "Park";
                case Kind.Lake: return "Lake"; default: return "Address";
            }
        }

        /// <summary>Cities (10 km cells) within radius r: their names and centres.</summary>
        public static void CitiesNear(int country, double lx, double lz, double r, List<Place> into, int max = 300)
        {
            var c = MegaWorld.Countries[country];
            if (!c.Huge) { into.Add(new Place { Name = c.Name, Kind = Kind.City, Country = country, LX = 0, LZ = 0, Detail = "City" }); return; }
            long x0 = CellX(lx - r), x1 = CellX(lx + r), z0 = CellX(lz - r), z1 = CellX(lz + r);
            long step = System.Math.Max(1, (x1 - x0) / 24);   // when zoomed far out, only some cities
            for (long cx = x0; cx <= x1 && into.Count < max; cx += step)
                for (long cz = z0; cz <= z1 && into.Count < max; cz += step)
                {
                    double px = (cx + 0.5) * CityCell, pz = (cz + 0.5) * CityCell;
                    if (System.Math.Abs(px) > c.Half || System.Math.Abs(pz) > c.Half) continue;
                    if (MapManager.Density(c, px, pz) < 0.3f) continue;   // only real towns get a city name on the map
                    into.Add(new Place { Name = CityName(country, cx, cz), Kind = Kind.City, Country = country, LX = px, LZ = pz, Detail = "City" });
                }
        }

        // ------------------------------------------------------------------ search

        private static readonly Regex AddressRx = new Regex(@"^\s*(\d+)\s+(.+?)\s*(?:,\s*(.+))?$");

        /// <summary>Finds places by name (and street addresses like "214 Maple Avenue") near a scene position.</summary>
        public static List<Place> Search(string query, Vector3 nearLocal, int max = 8)
        {
            var res = new List<Place>();
            if (string.IsNullOrWhiteSpace(query)) return res;
            string q = query.Trim().ToLowerInvariant();
            int here = MegaWorld.CountryAtLocal(nearLocal);
            if (here < 0) here = MegaWorld.CurrentCountry;
            var c = MegaWorld.Countries[here];
            double lx = MegaWorld.WX(nearLocal.x) - c.CX, lz = MegaWorld.WZ(nearLocal.z) - c.CZ;

            for (int k = 0; k < MegaWorld.Countries.Length; k++)
                if (MegaWorld.Countries[k].Name.ToLowerInvariant().Contains(q))
                {
                    var ap = MegaWorld.Countries[k].Airports[MegaWorld.Countries[k].Airports.Count / 2];
                    res.Add(new Place { Name = MegaWorld.Countries[k].Name, Kind = Kind.Country, Country = k, LX = ap.x + 250, LZ = ap.y + 222, Detail = "Country · " + MegaWorld.Countries[k].SizeKm.ToString("#,0") + " km wide" });
                }

            var m = AddressRx.Match(query);
            if (m.Success) FindAddress(here, lx, lz, long.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim().ToLowerInvariant(), m.Groups[3].Success ? m.Groups[3].Value.Trim().ToLowerInvariant() : null, res);

            var tmp = new List<Place>();
            PlacesNear(here, lx, lz, 15000, tmp, 4000);
            foreach (var p in tmp) if (p.Name.ToLowerInvariant().Contains(q)) res.Add(p);
            tmp.Clear();
            CitiesNear(here, lx, lz, c.Huge ? 300000 : 1, tmp, 4000);
            foreach (var p in tmp) if (p.Name.ToLowerInvariant().Contains(q)) res.Add(p);

            res.Sort((a, b) => Dist(a, here, lx, lz).CompareTo(Dist(b, here, lx, lz)));
            if (res.Count > max) res.RemoveRange(max, res.Count - max);
            return res;
        }

        private static double Dist(Place p, int here, double lx, double lz)
        {
            if (p.Country != here) return 1e15 + p.Country;
            return System.Math.Abs(p.LX - lx) + System.Math.Abs(p.LZ - lz);
        }

        private static void FindAddress(int country, double lx, double lz, long number, string street, string city, List<Place> res)
        {
            var c = MegaWorld.Countries[country];
            long cellX = CellX(lx), cellZ = CellX(lz);
            int found = 0;
            for (long cx = cellX - 1; cx <= cellX + 1 && found < 4; cx++)
                for (long cz = cellZ - 1; cz <= cellZ + 1 && found < 4; cz++)
                {
                    if (city != null && !CityName(country, cx, cz).ToLowerInvariant().StartsWith(city)) continue;
                    long per = (long)(CityCell / 100.0);
                    for (int axis = 0; axis < 2 && found < 4; axis++)
                        for (long i = 0; i < per && found < 4; i++)
                        {
                            long k = (axis == 0 ? cz : cx) * per + i;
                            string name = StreetName(country, axis, k);
                            if (!name.ToLowerInvariant().StartsWith(street)) continue;
                            long cellAlong = axis == 0 ? cx : cz;
                            double along = AlongFromNumber(cellAlong, number);
                            if (number > CityCell / 5.0) continue;
                            double side = (number % 2 == 0) ? 22.0 : -22.0;
                            double px = axis == 0 ? along : k * 100.0 + side, pz = axis == 0 ? k * 100.0 + side : along;
                            if (System.Math.Abs(px) > c.Half || System.Math.Abs(pz) > c.Half) continue;
                            res.Add(new Place { Name = number + " " + name, Kind = Kind.Address, Country = country, LX = px, LZ = pz, Detail = "Address · " + CityName(country, cx, cz) });
                            found++;
                        }
                }
        }

        /// <summary>What's at a spot (for tapping on the map): a nearby named place, or else the street address.</summary>
        public static Place At(int country, double lx, double lz, double pickRadius)
        {
            var tmp = new List<Place>();
            if (pickRadius < 3000) PlacesNear(country, lx, lz, pickRadius, tmp);
            Place best = default; double bd = double.MaxValue;
            foreach (var p in tmp) { double d = System.Math.Abs(p.LX - lx) + System.Math.Abs(p.LZ - lz); if (d < bd && d < pickRadius) { bd = d; best = p; } }
            if (best.Name != null) return best;
            return new Place { Name = AddressAt(country, lx, lz).Split(',')[0], Kind = Kind.Address, Country = country, LX = lx, LZ = lz, Detail = "Address · " + CityAt(country, lx, lz) };
        }
    }
}
