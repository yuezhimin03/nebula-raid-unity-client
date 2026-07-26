$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot

Push-Location $projectRoot
try {
    dotnet build NebulaRaid.sln -c Release
    dotnet run --project tests/NebulaRaid.Tests/NebulaRaid.Tests.csproj -c Release --no-build
    dotnet run --project src/NebulaRaid.Demo/NebulaRaid.Demo.csproj -c Release --no-build -- --ticks 900
    dotnet run --project benchmarks/NebulaRaid.Benchmarks/NebulaRaid.Benchmarks.csproj -c Release --no-build -- --entities 1024 --ticks 300
}
finally {
    Pop-Location
}

