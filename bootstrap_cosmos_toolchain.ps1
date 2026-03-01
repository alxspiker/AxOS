param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$pinnedRoot = Join-Path $repoRoot "cosmos_pinned_20240311"
$packagesRoot = Join-Path $pinnedRoot "packages"
$stampPath = Join-Path $pinnedRoot "toolchain.stamp"

$userKitRoot = Join-Path $env:APPDATA "Cosmos User Kit"
$kernelDir = Join-Path $userKitRoot "Kernel"
$il2cpuDir = Join-Path $userKitRoot "Build\IL2CPU"
$userKitPackagesDir = Join-Path $userKitRoot "packages"

$packageVersion = "10.0.0"

$repoConfig = @(
    @{ Name = "Cosmos"; Url = "https://github.com/CosmosOS/Cosmos"; Ref = "764ceb0d5697024f35fe4ae9cae9f2f0d3662610" },
    @{ Name = "Common"; Url = "https://github.com/CosmosOS/Common"; Ref = "557617703272d2fac5bebcf7ca9684010d86f165" },
    @{ Name = "IL2CPU"; Url = "https://github.com/CosmosOS/IL2CPU"; Ref = "a48ad6552b136c6290b1e6c88ad157070b0ce9da" },
    @{ Name = "XSharp"; Url = "https://github.com/CosmosOS/XSharp"; Ref = "a7dd07032aa75740be0163ba0b7f5d8456413fc4" }
)

$requiredPackages = @(
    "Cosmos.Build.10.0.0.nupkg",
    "Cosmos.Common.10.0.0.nupkg",
    "Cosmos.Core.10.0.0.nupkg",
    "Cosmos.Core_Asm.10.0.0.nupkg",
    "Cosmos.Core_Plugs.10.0.0.nupkg",
    "Cosmos.Debug.Kernel.10.0.0.nupkg",
    "Cosmos.Debug.Kernel.Plugs.Asm.10.0.0.nupkg",
    "Cosmos.HAL2.10.0.0.nupkg",
    "Cosmos.Plugs.10.0.0.nupkg",
    "Cosmos.System2.10.0.0.nupkg",
    "Cosmos.System2_Plugs.10.0.0.nupkg",
    "IL2CPU.API.10.0.0.nupkg"
)

$requiredKernelFiles = @(
    "Cosmos.Common.dll",
    "Cosmos.Core.dll",
    "Cosmos.Core_Asm.dll",
    "Cosmos.Core_Plugs.dll",
    "Cosmos.HAL2.dll",
    "Cosmos.Plugs.dll",
    "Cosmos.System2.dll",
    "Cosmos.System2_Plugs.dll",
    "Cosmos.Debug.Kernel.dll",
    "Cosmos.Debug.Kernel.Plugs.Asm.dll"
)

$requiredIl2CpuFiles = @(
    "IL2CPU.dll",
    "Cosmos.IL2CPU.dll"
)

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory = ""
    )

    $displayArgs = ($Arguments | ForEach-Object {
            if ($_ -match "\s") { '"' + $_ + '"' } else { $_ }
        }) -join " "
    if ($WorkingDirectory) {
        Write-Host ">> [$WorkingDirectory] $FilePath $displayArgs"
    }
    else {
        Write-Host ">> $FilePath $displayArgs"
    }

    if ($WorkingDirectory) {
        Push-Location $WorkingDirectory
    }
    try {
        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$FilePath failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        if ($WorkingDirectory) {
            Pop-Location
        }
    }
}

function Get-StampContent {
    $lines = @("PackageVersion=$packageVersion")
    foreach ($repo in $repoConfig) {
        $lines += "$($repo.Name)=$($repo.Ref)"
    }
    return ($lines -join "`n")
}

function Test-RequiredArtifactsPresent {
    foreach ($package in $requiredPackages) {
        if (-not (Test-Path (Join-Path $packagesRoot $package))) {
            return $false
        }
    }

    foreach ($kernelFile in $requiredKernelFiles) {
        if (-not (Test-Path (Join-Path $kernelDir $kernelFile))) {
            return $false
        }
    }

    foreach ($il2cpuFile in $requiredIl2CpuFiles) {
        if (-not (Test-Path (Join-Path $il2cpuDir $il2cpuFile))) {
            return $false
        }
    }

    return $true
}

function Backup-IfExists {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Stamp
    )

    if (Test-Path $Path) {
        $backupPath = "$Path.backup_$Stamp"
        Move-Item -Path $Path -Destination $backupPath
        Write-Host "Backed up '$Path' to '$backupPath'"
    }
}

function Copy-BuildOutput {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$DestinationDir
    )

    if (-not (Test-Path $SourceDir)) {
        throw "Missing build output directory: $SourceDir"
    }

    Copy-Item -Path (Join-Path $SourceDir "*") -Destination $DestinationDir -Recurse -Force
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git is required but was not found on PATH."
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet is required but was not found on PATH."
}

$expectedStamp = Get-StampContent
if (-not $Force -and (Test-RequiredArtifactsPresent)) {
    New-Item -ItemType Directory -Force -Path $pinnedRoot | Out-Null
    $currentStamp = if (Test-Path $stampPath) { Get-Content $stampPath -Raw } else { "" }
    if ($currentStamp -ne $expectedStamp) {
        Set-Content -Path $stampPath -Value $expectedStamp -NoNewline
    }
    Write-Host "Cosmos toolchain already present. Skipping bootstrap."
    exit 0
}

New-Item -ItemType Directory -Force -Path $pinnedRoot | Out-Null
New-Item -ItemType Directory -Force -Path $packagesRoot | Out-Null

foreach ($repo in $repoConfig) {
    $repoDir = Join-Path $pinnedRoot $repo.Name
    if (-not (Test-Path (Join-Path $repoDir ".git"))) {
        Invoke-CheckedCommand -FilePath "git" -Arguments @("clone", "--filter=blob:none", $repo.Url, $repoDir)
    }

    Invoke-CheckedCommand -FilePath "git" -Arguments @("-C", $repoDir, "fetch", "origin")
    Invoke-CheckedCommand -FilePath "git" -Arguments @("-C", $repoDir, "checkout", $repo.Ref)
}

Get-ChildItem -Path $packagesRoot -Filter "*.nupkg" -ErrorAction SilentlyContinue | Remove-Item -Force

$projectsToPack = @(
    (Join-Path $pinnedRoot "Cosmos\source\Cosmos.Build.Tasks\Cosmos.Build.Tasks.csproj"),
    (Join-Path $pinnedRoot "Cosmos\source\Cosmos.Common\Cosmos.Common.csproj"),
    (Join-Path $pinnedRoot "Cosmos\source\Cosmos.Core\Cosmos.Core.csproj"),
    (Join-Path $pinnedRoot "Cosmos\source\Cosmos.Core_Plugs\Cosmos.Core_Plugs.csproj"),
    (Join-Path $pinnedRoot "Cosmos\source\Cosmos.Core_Asm\Cosmos.Core_Asm.csproj"),
    (Join-Path $pinnedRoot "Cosmos\source\Cosmos.HAL2\Cosmos.HAL2.csproj"),
    (Join-Path $pinnedRoot "Cosmos\source\Cosmos.System2\Cosmos.System2.csproj"),
    (Join-Path $pinnedRoot "Cosmos\source\Cosmos.System2_Plugs\Cosmos.System2_Plugs.csproj"),
    (Join-Path $pinnedRoot "Cosmos\source\Cosmos.Plugs\Cosmos.Plugs.csproj"),
    (Join-Path $pinnedRoot "Cosmos\source\Cosmos.Debug.Kernel\Cosmos.Debug.Kernel.csproj"),
    (Join-Path $pinnedRoot "Cosmos\source\Cosmos.Debug.Kernel.Plugs.Asm\Cosmos.Debug.Kernel.Plugs.Asm.csproj")
)

foreach ($project in $projectsToPack) {
    Invoke-CheckedCommand -FilePath "dotnet" -Arguments @("pack", $project, "-c", "Debug", "-p:PackageVersion=$packageVersion", "--output", $packagesRoot)
}

$il2cpuApiProject = Join-Path $pinnedRoot "IL2CPU\source\IL2CPU.API\IL2CPU.API.csproj"
Invoke-CheckedCommand -FilePath "dotnet" -Arguments @("pack", $il2cpuApiProject, "-c", "Debug", "-p:PackageVersion=$packageVersion", "--output", $packagesRoot)

$backupStamp = Get-Date -Format "yyyyMMdd_HHmmss"
Backup-IfExists -Path $kernelDir -Stamp $backupStamp
Backup-IfExists -Path $il2cpuDir -Stamp $backupStamp

New-Item -ItemType Directory -Force -Path (Join-Path $userKitRoot "Build") | Out-Null
New-Item -ItemType Directory -Force -Path $kernelDir | Out-Null
New-Item -ItemType Directory -Force -Path $il2cpuDir | Out-Null
New-Item -ItemType Directory -Force -Path $userKitPackagesDir | Out-Null

Copy-BuildOutput -SourceDir (Join-Path $pinnedRoot "IL2CPU\source\IL2CPU\bin\Debug\net6.0") -DestinationDir $il2cpuDir
Copy-BuildOutput -SourceDir (Join-Path $pinnedRoot "Cosmos\source\Cosmos.Common\bin\Debug\net6.0") -DestinationDir $kernelDir
Copy-BuildOutput -SourceDir (Join-Path $pinnedRoot "Cosmos\source\Cosmos.Core_Asm\bin\Debug\net6.0") -DestinationDir $kernelDir
Copy-BuildOutput -SourceDir (Join-Path $pinnedRoot "Cosmos\source\Cosmos.Core_Plugs\bin\Debug\net6.0") -DestinationDir $kernelDir
Copy-BuildOutput -SourceDir (Join-Path $pinnedRoot "Cosmos\source\Cosmos.Core\bin\Debug\net6.0") -DestinationDir $kernelDir
Copy-BuildOutput -SourceDir (Join-Path $pinnedRoot "Cosmos\source\Cosmos.HAL2\bin\Debug\net6.0") -DestinationDir $kernelDir
Copy-BuildOutput -SourceDir (Join-Path $pinnedRoot "Cosmos\source\Cosmos.System2\bin\Debug\net6.0") -DestinationDir $kernelDir
Copy-BuildOutput -SourceDir (Join-Path $pinnedRoot "Cosmos\source\Cosmos.System2_Plugs\bin\Debug\net6.0") -DestinationDir $kernelDir
Copy-BuildOutput -SourceDir (Join-Path $pinnedRoot "Cosmos\source\Cosmos.Plugs\bin\Debug\net6.0") -DestinationDir $kernelDir
Copy-BuildOutput -SourceDir (Join-Path $pinnedRoot "Cosmos\source\Cosmos.Debug.Kernel\bin\Debug\netstandard2.0") -DestinationDir $kernelDir
Copy-BuildOutput -SourceDir (Join-Path $pinnedRoot "Cosmos\source\Cosmos.Debug.Kernel.Plugs.Asm\bin\Debug\netstandard2.0") -DestinationDir $kernelDir

Get-ChildItem -Path $userKitPackagesDir -Filter "Cosmos.*.nupkg" -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem -Path $userKitPackagesDir -Filter "IL2CPU.API*.nupkg" -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item -Path (Join-Path $packagesRoot "*.nupkg") -Destination $userKitPackagesDir -Force

Set-Content -Path $stampPath -Value $expectedStamp -NoNewline

Write-Host "Cosmos toolchain bootstrap completed."
Write-Host "Packages: $packagesRoot"
Write-Host "User Kit Kernel: $kernelDir"
Write-Host "User Kit IL2CPU: $il2cpuDir"
