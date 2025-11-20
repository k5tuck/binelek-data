# Multi-stage build for Binah Pipeline Service
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 8094
ENV ASPNETCORE_URLS=http://+:8094

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy library projects
COPY ["libraries/binah-contracts/Binah.Contracts.csproj", "libraries/binah-contracts/"]
COPY ["libraries/binah-core/Binah.Core.csproj", "libraries/binah-core/"]
COPY ["libraries/binah-infrastructure/Binah.Infrastructure.csproj", "libraries/binah-infrastructure/"]

# Copy service project
COPY ["services/binah-pipeline/Binah.Pipeline.csproj", "services/binah-pipeline/"]
RUN dotnet restore "services/binah-pipeline/Binah.Pipeline.csproj"

# Copy source files
COPY libraries/binah-contracts/ libraries/binah-contracts/
COPY libraries/binah-core/ libraries/binah-core/
COPY libraries/binah-infrastructure/ libraries/binah-infrastructure/
COPY services/binah-pipeline/ services/binah-pipeline/

WORKDIR "/src/services/binah-pipeline"
RUN dotnet build "Binah.Pipeline.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Binah.Pipeline.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Binah.Pipeline.dll"]
