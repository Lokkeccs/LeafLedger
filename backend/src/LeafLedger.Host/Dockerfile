# Multi-stage build: SDK build → aspnet runtime
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["LeafLedger.sln", "."]
COPY ["src/LeafLedger.Host/LeafLedger.Host.csproj", "src/LeafLedger.Host/"]
COPY ["src/LeafLedger.SharedKernel/LeafLedger.SharedKernel.csproj", "src/LeafLedger.SharedKernel/"]
COPY ["Directory.Build.props", "."]

# Restore only the Host project (and its SharedKernel dependency); the test
# projects referenced by the solution are intentionally not part of the image.
RUN dotnet restore "src/LeafLedger.Host/LeafLedger.Host.csproj"

# Copy the rest of the source code
COPY . .

# Build the release
WORKDIR "/src/src/LeafLedger.Host"
RUN dotnet build -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# Create non-root user
RUN useradd -m -u 1000 appuser && chown -R appuser:appuser /app
USER appuser

COPY --from=publish /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "LeafLedger.Host.dll"]
