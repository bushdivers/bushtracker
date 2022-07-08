﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BushDiversTracker.Services
{
    class HelperService
    {
        protected static string BasePath { get; set; }

        /// <summary>
        /// Convert to/from string and Version class
        /// </summary>
        internal class VersionJsonConverter : System.Text.Json.Serialization.JsonConverter<Version>
        {
            public override Version Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                string ver = reader.GetString();
                try 
                {
                    return new Version(ver);
                }
                catch { }

                if (ver.Contains('-'))
                {
                    ver = ver.Substring(0, ver.IndexOf('-'));
                    try
                    {
                        return new Version(ver);
                    }
                    catch { }
                }
                return new Version();
            }
            public override void Write(Utf8JsonWriter writer, Version value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.ToString());
            }
        }

        public static readonly JsonSerializerOptions SerializerOptions = new()
        {
            Converters =
            {
                new VersionJsonConverter()
            }
        };

        /// <summary>
        /// Calculates the distance between two points
        /// </summary>
        /// <param name="latFrom">double</param>
        /// <param name="lonFrom">double</param>
        /// <param name="latTo">double</param>
        /// <param name="lonTo">double</param>
        /// <param name="endRad">if end lat/ln is a radian already</param>
        /// <returns>
        /// Double of calculated distance
        /// </returns>
        public static double CalculateDistance(double latFrom, double lonFrom, double latTo, double lonTo, bool endRad = false)
        {
            latTo = DegToRad(latTo);
            lonTo = DegToRad(lonTo);

            latFrom = DegToRad(latFrom);
            lonFrom = DegToRad(lonFrom);

            double earthRadius = 3440.1;

            var thetaLat = latTo - latFrom;
            var thetaLon = lonTo - lonFrom;

            var a = Math.Sin(thetaLat / 2) * Math.Sin(thetaLat / 2) + Math.Cos(latFrom) * Math.Cos(latTo) * Math.Sin(thetaLon / 2) * Math.Sin(thetaLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            var d = earthRadius * c;

            return d;
        }

        /// <summary>
        /// Converts deg lat/lon to radian
        /// </summary>
        /// <param name="deg">Double of degrees</param>
        /// <returns>double of radian</returns>
        public static double DegToRad(double deg)
        {
            double rad = (Math.PI / 180) * deg;
            return rad;
        }

        /// <summary>
        /// Takes time in seconds and converts to datetime
        /// </summary>
        /// <param name="zuluTimeInSecs">int seconds in time</param>
        /// <returns>DateTime of zulu</returns>
        public static DateTime SetZuluTime(int zuluTimeInSecs)
        {

            return DateTime.Today.Add(TimeSpan.FromSeconds((double)zuluTimeInSecs));
        }

        /// <summary>
        /// Checks if there has been a valid increase or decrease in altitude
        /// </summary>
        /// <param name="currentAlt">The current/previous alt</param>
        /// <param name="newAlt">The new alt</param>
        /// <returns>true if change is within zone</returns>
        public static bool CheckForAltChange(double currentAlt, double newAlt)
        {
            var increasedAlt = newAlt >= (currentAlt + 200) ? true : false;
            var decreasedAlt = newAlt <= (currentAlt - 200) ? true : false;
            if (increasedAlt || decreasedAlt) return true;
            else return false;
        }

        /// <summary>
        /// Checks if there has been a valid change in direction
        /// </summary>
        /// <param name="currentHdg">last heading</param>
        /// <param name="newHdg">new heading</param>
        /// <returns>true if heading change is 7 degrees or more</returns>
        public static bool CheckForHeadingChange(double currentHdg, double newHdg)
        {
            var left = currentHdg - newHdg;
            var right = newHdg - currentHdg;

            if (left < 0) left += 360;
            if (right < 0) right += 360;

            var headingChange = left < right ? left : right;

            if (headingChange >= 7) return true;
            else return false;
        }

        /// <summary>
        /// Sends information to log file
        /// </summary>
        /// <param name="msg">String to send to log file</param>
        public static void WriteToLog(string msg)
        {
            var fileName = DateTime.Now.Date.ToString();
            using (StreamWriter w = File.AppendText("log.txt"))
            {
                w.WriteLine($"{DateTime.Now.ToLongTimeString()} {DateTime.Now.ToLongDateString()}");
                w.WriteLine($"{msg}");
                w.WriteLine("-------------");
            }
        }

        /// <summary>
        /// Processes flight cancellation
        /// </summary>
        public static async void CancelFlightOnExit()
        {
            var _api = new APIService();
            await _api.CancelTrackingAsync();
        }

        /// <summary>
        /// Convert gallons to litres
        /// </summary>
        /// <param name="gal">Gallons</param>
        /// <returns>Litres</returns>
        public static decimal GalToLitre(decimal gal)
        {
            return gal * new decimal(3.785412);
        }

        /// <summary>
        /// Convert pounds to kilograms
        /// </summary>
        /// <param name="lbs">Pounds</param>
        /// <returns>Kilograms</returns>
        public static decimal LbsToKG(decimal lbs)
        {
            return lbs * new decimal(0.453592);
        }

        /// <summary>
        /// Find the local community package path
        /// </summary>
        /// <returns></returns>
        public static string GetPackagePath()
        {
            string path = Properties.Settings.Default.CommunityDir;

            if (Directory.Exists(path))
                return path;

            path = GetBasePath() + "\\Community";

            if (!Directory.Exists(path))
                return null; // Give up

            Properties.Settings.Default.CommunityDir = path;
            Properties.Settings.Default.Save();

            return path;
        }

        /// <summary>
        /// Find the local official package path
        /// </summary>
        /// <returns></returns>
        public static string GetOfficialPath()
        {
            string path = GetBasePath() + "\\Official";

            if (Directory.Exists(path + "\\Steam"))
            {
                path += "\\Steam";
            }
            else if (Directory.Exists(path + "\\OneStore"))
            {
                path += "\\OneStore";
            }
            else
            {
                var dirs = Directory.GetDirectories(path);
                if (dirs.Length == 1)
                    path = dirs[0];
            }

            return path;
        }

        /// <summary>
        /// Find the base FS package path
        /// </summary>
        /// <returns></returns>
        public static string GetBasePath()
        {
            if (BasePath?.Length > 0 && Directory.Exists(BasePath))
                return BasePath;

            // Go searching
            string path = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\Microsoft Flight Simulator\\UserCfg.opt";
            if (!File.Exists(path))
            {
                path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\Packages\\Microsoft.FlightSimulator_8wekyb3d8bbwe\\LocalCache\\UserCfg.opt";
                if (!File.Exists(path))
                    return null; // Couldn't find on Steam or Store
            }

            var lines = File.ReadAllLines(path);
            if (lines.Length == 0)
                return null;

            path = lines.FirstOrDefault(line => line.StartsWith("InstalledPackagesPath "));
            if (path.Length == 0)
                return null;

            path = path[23..^1];
            if (!Directory.Exists(path))
                return null;

            BasePath = path;

            return BasePath;
        }
    }
}
