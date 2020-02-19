using System;
using NATS.Client;
using System.Text;
using System.Net;

/// <summary>
/// The Simple
/// </summary>
namespace SimpleWeatherRequestor
{
    class Program
    {
        /// <summary>
        /// Checks the location.  If null, the local latitude and longitude
        /// will be returned.
        /// </summary>
        /// <param name="location"></param>
        /// <returns>a location </returns>
        static byte[] CheckDefaultLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                using var client = new WebClient();
                string content = client.DownloadString("https://ipapi.co/latlong");
                return Encoding.UTF8.GetBytes(content);
            }
            return Encoding.UTF8.GetBytes(location);
        }

        /// <summary>
        /// GetCurrentWeather connects to a NATS server and then queries the weather service
        /// for data on a specfic location. See https://openweathermap.org/current for
        /// location parameters.
        /// </summary>
        /// <param name="url">URL of the NATS server to use.  Default is localhost.</param>
        /// <param name="location">A city name or coordinates. See  </param>
        /// <param name="credentials">NATS (or NGS) credentials</param>
        /// <returns></returns>
        static string GetCurrentWeather(string url, string location, string credentials)
        {
            var opts = ConnectionFactory.GetDefaultOptions();
            opts.Url = url;
            if (credentials != null)
            {
                opts.SetUserCredentials(credentials);
                if (url == "connect.ngs.global")
                {
                    opts.Secure = true;
                }
            }

            using var nc = new ConnectionFactory().CreateConnection(opts);
            var response = nc.Request("weather.current", CheckDefaultLocation(location), 5000);
            return Encoding.UTF8.GetString(response.Data);
        }

        /// <summary>
        /// Main optionally accepts a location to check, a NATS url, and NATS credentials.
        /// If a location isn't specified, it's looked up based on IP to return weather
        /// data from the location of the application.
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            if (args.Length > 0 && args.Length % 2 != 0)
            {
                Console.WriteLine("Usage:  SimpleWeatherRequestor --location <city name or lat,long> --url <NATS url> --creds <NATS Credentials>");
                Console.WriteLine("    For more information about location, see https://openweathermap.org/current");
                Environment.Exit(1);
            }

            string url = "localhost";
            string location = null;
            string credentials = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--url") url = args[i + 1];
                if (args[i] == "--location") location = args[i + 1];
                if (args[i] == "--creds") credentials = args[i + 1];
            }

            try
            {
                Console.WriteLine(GetCurrentWeather(url, location, credentials));
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: " + e.Message);
            }
        }
    }
}
