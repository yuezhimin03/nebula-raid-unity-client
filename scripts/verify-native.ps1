param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [int]$Entities = 4096,
    [int]$Ticks = 500
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$nativeRoot = Join-Path $projectRoot "native"
$outputRoot = Join-Path $projectRoot ".artifacts\native"

function Import-VisualStudioEnvironment {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} `
        "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswhere)) {
        throw "vswhere.exe was not found. Install Visual Studio Build Tools with C++."
    }

    $installation = & $vswhere `
        -latest `
        -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath
    if (-not $installation) {
        throw "MSVC x64 build tools were not found."
    }

    $developerCommand = Join-Path $installation "Common7\Tools\VsDevCmd.bat"
    if (-not (Test-Path -LiteralPath $developerCommand)) {
        throw "VsDevCmd.bat was not found at $developerCommand."
    }

    $commandLine = "call `"$developerCommand`" -no_logo -arch=x64 " `
        + "-host_arch=x64 >nul && set"
    $environmentLines = & $env:ComSpec /d /s /c $commandLine
    if ($LASTEXITCODE -ne 0) {
        throw "Visual Studio developer environment initialization failed."
    }

    foreach ($line in $environmentLines) {
        $parts = $line -split "=", 2
        if ($parts.Length -eq 2) {
            [Environment]::SetEnvironmentVariable(
                $parts[0],
                $parts[1],
                [EnvironmentVariableTarget]::Process)
        }
    }
}

function Invoke-Compiler {
    param([string[]]$Arguments)
    & cl.exe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "cl.exe failed with exit code $LASTEXITCODE."
    }
}

Import-VisualStudioEnvironment
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$include = Join-Path $nativeRoot "include"
$source = Join-Path $nativeRoot "src\nebula_native.cpp"
$testSource = Join-Path $nativeRoot "tests\native_tests.cpp"
$benchmarkSource = Join-Path $nativeRoot "benchmarks\native_benchmark.cpp"
$dll = Join-Path $outputRoot "NebulaNative.dll"
$importLibrary = Join-Path $outputRoot "NebulaNative.lib"
$testExecutable = Join-Path $outputRoot "nebula_native_tests.exe"
$benchmarkExecutable = Join-Path $outputRoot "nebula_native_benchmark.exe"

$compileFlags = @(
    "/nologo",
    "/std:c++17",
    "/permissive-",
    "/EHsc",
    "/W4",
    "/WX",
    "/Zc:__cplusplus",
    "/MD",
    "/I$include"
)
if ($Configuration -eq "Release") {
    $compileFlags += @("/O2", "/DNDEBUG")
} else {
    $compileFlags += @("/Od", "/Zi")
}

Write-Output "Building C++17 native plugin with MSVC..."
Invoke-Compiler ($compileFlags + @(
    "/LD",
    "/DNEBULA_NATIVE_EXPORTS",
    $source,
    "/Fo$(Join-Path $outputRoot 'nebula_native.obj')",
    "/Fe$dll",
    "/link",
    "/IMPLIB:$importLibrary",
    "/PDB:$(Join-Path $outputRoot 'NebulaNative.pdb')"
))

Invoke-Compiler ($compileFlags + @(
    $testSource,
    $importLibrary,
    "/Fo$(Join-Path $outputRoot 'native_tests.obj')",
    "/Fe$testExecutable",
    "/link",
    "/PDB:$(Join-Path $outputRoot 'nebula_native_tests.pdb')"
))

Invoke-Compiler ($compileFlags + @(
    $benchmarkSource,
    $importLibrary,
    "/Fo$(Join-Path $outputRoot 'native_benchmark.obj')",
    "/Fe$benchmarkExecutable",
    "/link",
    "/PDB:$(Join-Path $outputRoot 'nebula_native_benchmark.pdb')"
))

Write-Output "Running native C++ behavior tests..."
& $testExecutable
if ($LASTEXITCODE -ne 0) {
    throw "Native behavior tests failed with exit code $LASTEXITCODE."
}

Write-Output "Running real C# -> C ABI -> C++ interop smoke test..."
$interopProject = Join-Path `
    $projectRoot `
    "tests\NebulaRaid.NativeInterop.Tests\NebulaRaid.NativeInterop.Tests.csproj"
dotnet run `
    --project $interopProject `
    -c Release `
    -- `
    --library $dll
if ($LASTEXITCODE -ne 0) {
    throw "Managed/native interop test failed with exit code $LASTEXITCODE."
}

Write-Output "Running native simulation microbenchmark..."
& $benchmarkExecutable --entities $Entities --ticks $Ticks
if ($LASTEXITCODE -ne 0) {
    throw "Native benchmark failed with exit code $LASTEXITCODE."
}
