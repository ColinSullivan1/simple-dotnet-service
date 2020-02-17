FROM mcr.microsoft.com/dotnet/core/runtime:3.1-buster-slim AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/core/sdk:3.1-buster AS build
WORKDIR /src
COPY SimpleWeatherService.csproj ./
RUN dotnet restore "./SimpleWeatherService.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "SimpleWeatherService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SimpleWeatherService.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SimpleWeatherService.dll"]
