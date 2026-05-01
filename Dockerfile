FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/KokoroApi/KokoroApi.csproj src/KokoroApi/
RUN dotnet restore src/KokoroApi/KokoroApi.csproj
COPY src/KokoroApi/ src/KokoroApi/
RUN dotnet publish src/KokoroApi/KokoroApi.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
 && apt-get install -y --no-install-recommends libopenal1 ca-certificates curl \
 && rm -rf /var/lib/apt/lists/*
RUN mkdir -p /app/bin /app/cache
COPY --from=build /out /app/bin
WORKDIR /app/cache
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "/app/bin/KokoroApi.dll"]
