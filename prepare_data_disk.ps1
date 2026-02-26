param(
    [Parameter(Mandatory = $true)]
    [string]$ImagePath,
    [int]$SizeMB = 128,
    [switch]$ForceFormat
)

$ErrorActionPreference = "Stop"

function Resolve-QemuImg {
    $candidates = @(
        (Join-Path $env:ProgramFiles "qemu\qemu-img.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "qemu\qemu-img.exe")
    )

    for ($i = 0; $i -lt $candidates.Count; $i++) {
        $path = $candidates[$i]
        if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path $path)) {
            return $path
        }
    }

    $cmd = Get-Command qemu-img.exe -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Path) {
        return $cmd.Path
    }

    throw "qemu-img.exe not found. Install QEMU first."
}

function Test-SectorLooksFat {
    param([byte[]]$Sector)

    if ($Sector.Length -lt 512) {
        return $false
    }
    if ($Sector[510] -ne 0x55 -or $Sector[511] -ne 0xAA) {
        return $false
    }

    $fat16 = [System.Text.Encoding]::ASCII.GetString($Sector, 54, 8)
    $fat32 = [System.Text.Encoding]::ASCII.GetString($Sector, 82, 8)
    return $fat16.Contains("FAT") -or $fat32.Contains("FAT")
}

function Test-IsFatImage {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return $false
    }

    $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        if ($fs.Length -lt 512) {
            return $false
        }

        $sector0 = New-Object byte[] 512
        $read = $fs.Read($sector0, 0, 512)
        if ($read -lt 512) {
            return $false
        }

        # Superfloppy FAT (filesystem starts at sector 0).
        if (Test-SectorLooksFat -Sector $sector0) {
            return $true
        }

        # MBR with first partition containing FAT.
        if ($sector0[510] -ne 0x55 -or $sector0[511] -ne 0xAA) {
            return $false
        }

        $partitionType = $sector0[450]
        if ($partitionType -eq 0) {
            return $false
        }

        $startLba = [System.BitConverter]::ToUInt32($sector0, 454)
        if ($startLba -eq 0) {
            return $false
        }

        $offset = [int64]$startLba * 512L
        if ($offset + 512L -gt $fs.Length) {
            return $false
        }

        $fs.Position = $offset
        $partitionBoot = New-Object byte[] 512
        $read = $fs.Read($partitionBoot, 0, 512)
        if ($read -lt 512) {
            return $false
        }

        return (Test-SectorLooksFat -Sector $partitionBoot)
    }
    finally {
        $fs.Dispose()
    }
}

function Ensure-WslDiskTools {
    $wsl = Get-Command wsl.exe -ErrorAction SilentlyContinue
    if (-not $wsl) {
        throw "wsl.exe not found. WSL is required to format FAT image."
    }

    & wsl.exe -e sh -lc "command -v sfdisk >/dev/null 2>&1 && command -v losetup >/dev/null 2>&1 && (command -v mkfs.vfat >/dev/null 2>&1 || command -v mkfs.fat >/dev/null 2>&1)"
    if ($LASTEXITCODE -ne 0) {
        throw "Missing required WSL tools (sfdisk, losetup, mkfs.vfat/mkfs.fat)."
    }
}

function Convert-ToWslPath {
    param([string]$WindowsPath)

    $out = & wsl.exe -e wslpath -a $WindowsPath
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($out)) {
        throw "Failed to convert Windows path to WSL path: $WindowsPath"
    }
    return $out.Trim()
}

function Format-FatImage {
    param([string]$Path)

    Ensure-WslDiskTools
    $wslPath = Convert-ToWslPath -WindowsPath $Path

    $script = @'
set -eu
img="$1"

/sbin/sfdisk --wipe always "$img" >/dev/null <<'SFDISK_EOF'
label: dos
unit: sectors

2048,,0c,*
SFDISK_EOF

loop_dev=$(/sbin/losetup --find --show --partscan "$img")
cleanup() {
    /sbin/losetup -d "$loop_dev" >/dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM

part_dev="${loop_dev}p1"
i=0
while [ ! -b "$part_dev" ] && [ $i -lt 40 ]; do
    sleep 0.1
    i=$((i + 1))
done

if [ ! -b "$part_dev" ]; then
    echo "partition device not found: $part_dev" >&2
    exit 1
fi

if command -v mkfs.vfat >/dev/null 2>&1; then
    mkfs.vfat -F 32 -n AXOSDATA "$part_dev" >/dev/null
else
    mkfs.fat -F 32 -n AXOSDATA "$part_dev" >/dev/null
fi

sync
'@

    $script = $script -replace "`r`n", "`n"
    $tempScript = [System.IO.Path]::GetTempFileName() + ".sh"
    try {
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($tempScript, $script, $utf8NoBom)
        $wslScriptPath = Convert-ToWslPath -WindowsPath $tempScript

        & wsl.exe -e sh $wslScriptPath $wslPath
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create partitioned FAT image in WSL."
        }
    }
    finally {
        if (Test-Path $tempScript) {
            Remove-Item -Path $tempScript -Force -ErrorAction SilentlyContinue
        }
    }
}

$fullPath = [System.IO.Path]::GetFullPath($ImagePath)
$parent = [System.IO.Path]::GetDirectoryName($fullPath)
if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path $parent)) {
    New-Item -ItemType Directory -Path $parent | Out-Null
}

$qemuImg = Resolve-QemuImg
$created = $false
if (-not (Test-Path $fullPath)) {
    & $qemuImg create -f raw $fullPath ("{0}M" -f $SizeMB) | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "qemu-img failed to create image: $fullPath"
    }
    $created = $true
}

$needsFormat = $ForceFormat.IsPresent -or $created -or (-not (Test-IsFatImage -Path $fullPath))
if ($needsFormat) {
    Format-FatImage -Path $fullPath
}

Write-Output ("Data image ready: {0}" -f $fullPath)
