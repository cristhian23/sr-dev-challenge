FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Refidomsa.Api/Refidomsa.Api.csproj Refidomsa.Api/
RUN dotnet restore Refidomsa.Api/Refidomsa.Api.csproj
COPY src/Refidomsa.Api/ Refidomsa.Api/
RUN dotnet publish Refidomsa.Api/Refidomsa.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Refidomsa.Api.dll"]
