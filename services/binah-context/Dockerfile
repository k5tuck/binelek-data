# Multi-stage build for Binah Context Service
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 8089
ENV ASPNETCORE_URLS=http://+:8089

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy library projects
COPY ["libraries/binah-contracts/Binah.Contracts.csproj", "libraries/binah-contracts/"]
COPY ["libraries/binah-core/Binah.Core.csproj", "libraries/binah-core/"]

# Copy service project
COPY ["services/binah-context/Binah.Context.csproj", "services/binah-context/"]
RUN dotnet restore "services/binah-context/Binah.Context.csproj"

# Copy source files
COPY libraries/binah-contracts/ libraries/binah-contracts/
COPY libraries/binah-core/ libraries/binah-core/
COPY services/binah-context/ services/binah-context/

WORKDIR "/src/services/binah-context"
RUN dotnet build "Binah.Context.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Binah.Context.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Binah.Context.dll"]
