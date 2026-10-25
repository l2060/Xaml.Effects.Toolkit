param(
    [string]$OutputDirectory,
    [int]$Directories = 1000,
    [int]$Files = 500000,
    [int]$Rounds = 3
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repository ("artifacts\package-benchmark-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) {
    throw 'Choose a new output directory so existing measurements are not overwritten.'
}

Push-Location $repository
$previousTiering = $env:DOTNET_TieredCompilation
try {
    dotnet build benchmarks/GameResource.Package.Benchmarks/GameResource.Package.Benchmarks.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark build failed.' }
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
    $cpu = Get-CimInstance Win32_Processor
    $computer = Get-CimInstance Win32_ComputerSystem
    $os = Get-CimInstance Win32_OperatingSystem
    $driveLetter = [IO.Path]::GetPathRoot($OutputDirectory).Substring(0, 1)
    $disk = Get-Partition -DriveLetter $driveLetter | Get-Disk
    $volume = Get-Volume -DriveLetter $driveLetter
    [ordered]@{
        Timestamp = (Get-Date).ToString('o')
        Cpu = $cpu.Name
        PhysicalCores = $cpu.NumberOfCores
        LogicalProcessors = $cpu.NumberOfLogicalProcessors
        RamBytes = $computer.TotalPhysicalMemory
        AvailableRamKiB = $os.FreePhysicalMemory
        OS = $os.Caption
        OSVersion = $os.Version
        Disk = $disk.FriendlyName
        BusType = $disk.BusType.ToString()
        FileSystem = $volume.FileSystem
        FreeBytesBefore = $volume.SizeRemaining
        Sdk = (dotnet --version)
        PowerScheme = (powercfg /getactivescheme)
        LibrarySources = @(Get-ChildItem GameResource.Package -Recurse -File -Filter '*.cs' |
            Where-Object FullName -NotMatch '\\(bin|obj)\\' |
            ForEach-Object {
                [ordered]@{ File = $_.FullName; SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
            })
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'environment.json') -Encoding UTF8

    # Process-local setting; do not modify machine-wide runtime configuration.
    $env:DOTNET_TieredCompilation = '0'
    $executable = 'benchmarks/GameResource.Package.Benchmarks/bin/Release/net10.0/GameResource.Package.Benchmarks.dll'
    foreach ($profile in @('none', 'lz4')) {
        dotnet $executable --phase prepare --profile $profile --output $OutputDirectory --directories $Directories --files $Files --rounds $Rounds
        if ($LASTEXITCODE -ne 0) { throw "Preparation failed: $profile" }
    }
    foreach ($profile in @('none', 'lz4')) {
        foreach ($phase in @('open', 'read', 'enumerate', 'update', 'modern')) {
            dotnet $executable --phase $phase --profile $profile --output $OutputDirectory --directories $Directories --files $Files --rounds $Rounds
            if ($LASTEXITCODE -ne 0) { throw "Benchmark failed: $profile/$phase" }
        }
    }
}
finally {
    $env:DOTNET_TieredCompilation = $previousTiering
    Pop-Location
}
