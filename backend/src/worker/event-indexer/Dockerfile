FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore src/worker/event-indexer/event-indexer.csproj
RUN dotnet publish src/worker/event-indexer/event-indexer.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
ENV DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /out .

ENTRYPOINT ["dotnet", "event-indexer.dll"]
