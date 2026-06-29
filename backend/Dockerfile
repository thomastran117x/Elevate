FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore
RUN dotnet build -c Release
RUN dotnet publish -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV PORT=8090
ENV ASPNETCORE_URLS=http://+:8090

COPY --from=build /out .

EXPOSE 8090
ENTRYPOINT ["dotnet", "backend.dll", "--urls", "http://0.0.0.0:8090"]
