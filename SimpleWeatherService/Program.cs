// Copyright 2020 Colin Sullivan
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Net;
using System.Text;
using System.Threading;
using NATS.Client;

namespace SimpleWeatherService
{
    /// <summary>
    /// This is an example weather service connects to a NATS server and listens
    /// for requests on the "weathcer.current" subject for a message containing an
    /// OpenWeatherOrg API "city name" string. When it receives a request It will
    /// return a response, and then cache requests for period of time for performance
    /// and to avoid blasting the OpenWeatherAPI server.
    /// </summary>
    /// <seealso cref="https://openweathermap.org/current"/>
    class WeatherService
    {
        // The NATS connection and options
        private IConnection conn = null;
        private readonly Options opts = null;
        private readonly string OpenWeatherMapAPIKey = null;

        // A WebClient to poll the OpenWeatherOrg API
        private readonly WebClient client = new WebClient();

        // Cache our results
        private readonly ResultCache resultsCache = new ResultCache();

        private readonly AutoResetEvent shutdown = new AutoResetEvent(false);

        /// <summary>
        /// Adding EventHandlers are optional but highly recommended. This
        /// could save you loads of time troubleshooting.
        /// </summary>
        /// <param name="opts">NATS options</param>
        private void AddEventHandlers(Options opts)
        {
            opts.AsyncErrorEventHandler = (obj, args) =>
            {
                Console.WriteLine($"NATS error: { args.Error }");
            };
            opts.ClosedEventHandler = (obj, args) =>
            {
                Console.WriteLine("NATS Connection closed.");
                if (args.Error != null)
                {
                    Console.WriteLine($"  Error: { args.Error.Message }");
                }
            };
            opts.DisconnectedEventHandler = (obj, args) =>
            {
                Console.WriteLine("Disconnected from the NATS server.");
            };
            opts.ReconnectedEventHandler = (obj, args) =>
            {
                Console.WriteLine("Reconnected to the NATS server.");
            };
        }

        /// <summary>
        /// Create our weather service object that will use OpenWeatherMap.org
        /// as a data source.
        /// </summary>
        /// <param name="apiKey">OpenWeather API key</param>
        /// <param name="url">NATS Connect url</param>
        /// <param name="credentialsFile">Optional credentials file.</param>
        public WeatherService(string apiKey, string url, string credentialsFile = null)
        {
            OpenWeatherMapAPIKey = apiKey;

            // Create options then set the NATS url and credentials if specified.
            opts = ConnectionFactory.GetDefaultOptions();
            opts.Url = url;

            if (credentialsFile != null)
            {
                opts.SetUserCredentials(credentialsFile);
                if (url == "connect.ngs.global")
                {
                    opts.Secure = true;
                }
            }

            AddEventHandlers(opts);
        }

        /// <summary>
        /// This method is the message event handler.  It is invoked on every
        /// message received and here is we'll handle message requests.  There are
        /// only two salient NATS calls in this callback: One to get the message
        /// content and other to reply to the request.
        /// </summary>
        /// <param name="obj">The connection was invoked on.</param>
        /// <param name="args">The event handler args.</param>
        void HandleRequest(object obj, MsgHandlerEventArgs args)
        {
            string content;

            // Get the NATS message
            var msg = args.Message;

            // Extract the location from the message.  Very simple here,
            // just a string.  In a more complex microservice this might
            // be serialized as JSON or protobuf.
            string location = Encoding.Default.GetString(msg.Data);

            // Check the cache for data.  See notes on the resultsCache below.
            content = resultsCache.Get(location);
            if (content == null)
            {
                try
                {
                    // Request data from OpenWeatherOrg and cache the result.
                    content = client.DownloadString(
                        "http://api.openweathermap.org/data/2.5/weather?q=" +
                        location + "&APPID=" +
                        OpenWeatherMapAPIKey);

                    resultsCache.Add(location, content);
                }
                catch (Exception ex)
                {
                    // return a JSON formatted error to stay consistent.
                    Console.WriteLine($"Couldn't get data from OpenWeatherMap.org: {ex.Message}");
                    content = "{ \"error\": \"" + ex.Message + "\"}";
                }
            }

            // Respond to the requestor with the content.
            msg.Respond(Encoding.UTF8.GetBytes(content));
        }

        /// <summary>
        /// Runs the WeatherService.
        /// </summary>
        public void Run()
        {
            // Create a connection to the NATS server.
            conn = new ConnectionFactory().CreateConnection(opts);

            // Subscribe and handle requests.  Add ourselves to the "cw" group
            // so NATS will automatically load balance as new instances are added.
            // To scale, simply launch more instances.  That's it.
            conn.SubscribeAsync("weather.current", "cw", HandleRequest);


            Console.WriteLine("Connected and listening for requests.");

            // Block until we're signaled to stop.
            shutdown.WaitOne();
        }

        /// <summary>
        /// Shuts down the service. Idempotent but caller must ensure threadsafety.
        /// </summary>
        public void Shutdown()
        {
            if (conn == null)
                return;

            Console.WriteLine("Shutting down.");

            // Drain the connection while we're exiting to handle outstanding requests.
            // Drain will unsubscribe and close the connection, processing any messages
            // that have been received but not processed yet.  It is a graceful way
            // to exit and minimize timeouts on the service requestor if we are scaling
            // down.
            try
            {
                conn?.Drain();
            }
            catch { }

            conn = null;

            // Unblock Run()
            shutdown.Set();
        }

        /// <summary>
        /// A simple result cache utilizing a memory cache under the hood. In this example,
        /// we don't want to hit the OpenWeatherOrg API too often - it's expensive in terms
        /// of latency when compared to NATS.
        ///
        /// Also, how quickly does current weather change?  As the joke goes in Colorado,
        /// "If you don't like the weather, wait 20 minutes."  ...but we'll use 5 minutes.
        ///
        /// Ohter types of services that require up to date information wouldn't use a cache,
        /// and services provding very static data could longer expirations (or none at all)
        /// </summary>
        private class ResultCache
        {
            private readonly MemoryCache cache = new MemoryCache(new MemoryCacheOptions());

            public string Get(String key)
            {
                if (cache.TryGetValue(key, out string result))
                {
                    return result;
                }
                return null;
            }

            public void Add(string key, string value)
            {
                var o = new MemoryCacheEntryOptions
                {
                    // Expire cache entries after 20 minutes.
                    AbsoluteExpiration = new DateTimeOffset(
                        DateTime.Now.Add(new TimeSpan(0, 5, 0)))
                };
                _ = cache.Set(key, value, o);
            }
        }
    }

    class MainClass
    {
        public static void Main(string[] args)
        {
            string apikey = null;
            string url = "127.0.0.1";
            string creds = null;

            if (args.Length == 0)
            {
                Console.WriteLine("Usage:  {0}: <API key> <NATS url> <NATS Credentials>");
                Environment.Exit(1);
            }

            apikey = args[0];

            if (args.Length > 1) url = args[1];
            if (args.Length > 2) creds = args[2];

            var ws = new WeatherService(apikey, url, creds);

            // Setup a graceful exit for ctrl+c and SIGTERM.
            Console.CancelKeyPress += (o, a) => ws.Shutdown();
            AppDomain.CurrentDomain.ProcessExit += (o, a) => ws.Shutdown();

            try
            {
                ws.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error:  { ex.Message }");
                Environment.Exit(1);
            }
        }
    }
}
