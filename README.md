# A Simple NATS Microservice written in C#

This is a really simple example of a scalable .NET microservice using [NATS](https://nats.io).  In this example service,
we'll serve weather data from [OpenWeatherMap.org](https://openweathermap.org/) to NATS clients.  Data will be cached
for a short period of time to lower latency, providing a better quality of service.

## Requirements for this example

- [.NET Core](https://dotnet.microsoft.com/download) 3.1 or higher.
- [Docker](https://docker.com) or a version of nats-req from NATS examples.
- A NATS server
- An OpenWeatherMap.org API key

It takes about 2 minutes to create an OpenWeatherMap API key.  [Signup](https://home.openweathermap.org/users/sign_up)
and create a [api key](https://home.openweathermap.org/api_keys).

## Design

The design is simple.  We create a connection to NATS, listen to requests on the `weather.current` subject, and when 
we receive requests we serve up the data.  This is achieved using the [request/reply](https://docs.nats.io/nats-concepts/reqreply)
pattern with a [queue subscription](https://docs.nats.io/nats-concepts/queue) used here to enable scaling. For bonus points,
the `Drain()` API is used at exit to gracefully handle incoming requests.  This is a good thing to do if you plan to
scale down or someday perform a rolling upgrade.

Note, in order to scale up the **only** thing you have to do is launch another instance. NATS will do the rest with zero
configuration changes.

There are just three things we do with NATS in our service.

1) Setup options and connect
2) Subscribe and provide a handler to process messages
3) Drain on exit *(optional but good practice)*

Take a look at the [code](./SimpleWeatherService/Program.cs).  It's documented to describe what is happening when, and why.

### Starting the NATS server

There are a few options.  You can run locally with docker, connect to `demo.nats.io`, or connect to [NGS](https://synadia.com/ngs).
There's plenty of documentation [here](https://docs.nats.io/nats-server/installation).  If you use `demo.nats.io` I'd suggest altering the subject the service listens on.

To run locally one way to launch the NATS server is through docker.

`$ docker run --rm -p 4222:4222 nats`
 
If you are using default ports the NATS url is simply the IP address of your computer.

### From the source directly

Building is simple.  From the the repo directory, run:

```text
cd SimpleWeatherService
dotnet run <your openweatherorg api key> <nats url> <optional credentials>
```

## Testing

First, I'll get my IP.  Depending on your system, you may need to use a different API.

```$ ipconfig getifaddr en1
192.168.0.29
```

Next we'll start our service.

```
$ dotnet bin/Debug/netcoreapp3.1/netcoreapp3.1/SimpleWeatherService.dll `cat apikey.txt` 192.168.0.29
Connected and listening for requests.
```

Using the [nats-box](https://hub.docker.com/r/synadia/nats-box) utility, we'll send a request for the current
weather in Denver.

```bash
$ docker run --rm -it synadia/nats-box
             _             _               
 _ __   __ _| |_ ___      | |__   _____  __
| '_ \ / _` | __/ __|_____| '_ \ / _ \ \/ /
| | | | (_| | |_\__ \_____| |_) | (_) >  < 
|_| |_|\__,_|\__|___/     |_.__/ \___/_/\_\
                                           
nats-box v0.3.0
8f24d7467cdf:~# nats-req -s 192.168.0.29 weather.current Denver
{"coord":{"lon":-104.98,"lat":39.74},"weather":[{"id":800,"main":"Clear","description":"clear sky","icon":"01n"}],"base":"stations","main":{"temp":275.18,"feels_like":271.62,"temp_min":271.48,"temp_max":278.15,"pressure":1011,"humidity":64},"visibility":16093,"wind":{"speed":1.5,"deg":60},"clouds":{"all":1},"dt":1581907351,"sys":{"type":1,"id":3958,"country":"US","sunrise":1581861111,"sunset":1581899787},"timezone":-25200,"id":5419384,"name":"Denver","cod":200}
8f24d7467cdf:~#
```

There we go...  weather data from a scalable .NET service!  Short but sweet.

## Enjoy!

For more on NATS in .NET, check out the NATS .NET client [here](https://github.com/nats-io/nats.net).
