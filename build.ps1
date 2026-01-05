param(
    [Parameter(Mandatory = $true)]
    [Alias("v")]
    [string]$Version
)

<#
.SYNOPSIS
  Build script for SbModbus: pack libraries, publish SbModbus.Tool as self-contained single-file
  for win-x64, remove .xml/.pdb files and compress publish artifacts with maximum compression.

.DESCRIPTION
  - Packs library projects to NuGet packages into `build/`
  - Publishes `SbModbus.Tool` as self-contained single-file executable for win-x64
  - Removes all .xml and .pdb files under `build/`
  - Compresses each publish output into `<ProjName>.win-x64.<Version>.7z` (uses 7z if available with max compression),
    otherwise falls back to `.zip` using `Compress-Archive`.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Projects to pack (NuGet)
$projects = @(
    "SbModbus\SbModbus.csproj",
    "SbModbus.SerialPortStream\SbModbus.SerialPortStream.csproj",
    "SbModbus.TcpStream\SbModbus.TcpStream.csproj"
)

# Tools to publish (self-contained single-file win-x64)
$tools = @(
    "SbModbus.Tool\SbModbus.Tool.csproj"
)

$outputDir = "build"

function Ensure-Directory
{
    param([string]$Path)
    if (-not (Test-Path $Path))
    {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Find-7Zip
{
    <#
    Attempts to locate a 7-Zip executable. Returns full path if found, $null otherwise.
    Checks:
      - PATH via Get-Command for common names (7z, 7za, 7zr)
      - Common install locations in Program Files
    #>
    $candidates = @('7z','7za','7zr')
    foreach ($name in $candidates)
    {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue
        if ($cmd)
        { return $cmd.Path
        }
    }

    $possiblePaths = @(
        Join-Path $env:ProgramFiles '7-Zip\7z.exe',
        Join-Path $env:ProgramFiles '7-Zip\7za.exe',
        Join-Path $env:ProgramFiles '(x86)\7-Zip\7z.exe',
        Join-Path $env:ProgramFiles '(x86)\7-Zip\7za.exe'
    )

    foreach ($p in $possiblePaths)
    {
        if ($p -and (Test-Path $p))
        { return $p
        }
    }

    return $null
}

function Remove-XmlPdbFiles
{
    param([string]$Root)
    if (-not (Test-Path $Root))
    {
        Write-Host "Directory not found: $Root" -ForegroundColor Yellow
        return
    }
    Write-Host "Removing .xml and .pdb files under: $Root" -ForegroundColor Green
    try
    {
        $files = Get-ChildItem -Path $Root -Recurse -File -Include *.xml, *.pdb -ErrorAction SilentlyContinue
        if (-not $files -or $files.Count -eq 0)
        {
            Write-Host "No .xml/.pdb files found." -ForegroundColor Cyan
            return
        }
        $count = 0
        foreach ($f in $files)
        {
            try
            {
                Remove-Item -LiteralPath $f.FullName -Force -ErrorAction Stop
                $count++
            } catch
            {
                Write-Host "Failed to delete $($f.FullName): $($_.Exception.Message)" -ForegroundColor Yellow
            }
        }
        Write-Host "Deleted $count .xml/.pdb files." -ForegroundColor Green
    } catch
    {
        Write-Host "Error scanning/deleting files: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# Ensure output directory exists
Ensure-Directory $outputDir

# 1) Pack library projects
foreach ($proj in $projects)
{
    if (-not (Test-Path $proj))
    {
        Write-Host "Project not found: $proj" -ForegroundColor Yellow
        continue
    }
    Write-Host "Packing: $proj (Version: $Version)" -ForegroundColor Green
    try
    {
        dotnet pack $proj -o $outputDir --configuration Release -p:PackageVersion=$Version
        if ($LASTEXITCODE -ne 0)
        {
            Write-Host "dotnet pack returned exit code $LASTEXITCODE for $proj" -ForegroundColor Red
        } else
        {
            Write-Host "Packed: $proj" -ForegroundColor Cyan
        }
    } catch
    {
        Write-Host "Error packing $proj $($_.Exception.Message)" -ForegroundColor Red
    }
}

# 2) Publish tools (self-contained single-file for win-x64)
$published = @()
foreach ($tool in $tools)
{
    if (-not (Test-Path $tool))
    {
        Write-Host "Tool project not found: $tool" -ForegroundColor Yellow
        continue
    }
    $projFileName = Split-Path $tool -Leaf
    $projName = [System.IO.Path]::GetFileNameWithoutExtension($projFileName)

    $publishOut = Join-Path $outputDir (Join-Path "publish" (Join-Path $projName (Join-Path "win-x64" $Version)))
    Ensure-Directory $publishOut

    Write-Host "Publishing: $projName -> $publishOut" -ForegroundColor Green
    Write-Host "Fixed params: runtime=win-x64, self-contained=true, single-file=true" -ForegroundColor Green

    try
    {
        dotnet publish $tool -c Release -r win-x64 --self-contained true -o $publishOut -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
        if ($LASTEXITCODE -ne 0)
        {
            Write-Host "dotnet publish returned exit code $LASTEXITCODE for $projName" -ForegroundColor Red
        } else
        {
            Write-Host "Published: $projName" -ForegroundColor Cyan
            $published += [PSCustomObject]@{ Name = $projName; Path = $publishOut }
        }
    } catch
    {
        Write-Host "Error publishing $projName $($_.Exception.Message)" -ForegroundColor Red
    }
}

# 3) Remove .xml and .pdb files under build/
Remove-XmlPdbFiles $outputDir

# 4) Compress published outputs:
#    Prefer 7z with maximum compression (LZMA2, solid, multithread), fallback to zip.
$publishRoot = Join-Path $outputDir "publish"
Ensure-Directory $publishRoot

$sevenExe = Find-7Zip
if ($sevenExe)
{
    Write-Host "7-Zip found: $sevenExe (will use .7z with max compression)" -ForegroundColor Green
} else
{
    Write-Host "7-Zip not found: falling back to ZIP via Compress-Archive" -ForegroundColor Yellow
}

foreach ($p in $published)
{
    $projName = $p.Name
    $srcFolder = $p.Path

    if (-not (Test-Path $srcFolder))
    {
        Write-Host "Publish folder missing, skipping: $srcFolder" -ForegroundColor Yellow
        continue
    }

    $archiveName7z = "{0}-win-x64-{1}.7z" -f $projName, $Version
    $archivePath7z = Join-Path $publishRoot $archiveName7z
    $archivePathZip = [System.IO.Path]::ChangeExtension($archivePath7z, '.zip')

    # Remove existing archives if any
    foreach ($existing in @($archivePath7z, $archivePathZip))
    {
        if (Test-Path $existing)
        {
            try
            { Remove-Item -LiteralPath $existing -Force -ErrorAction Stop
            } catch
            {
            }
        }
    }

    if ($sevenExe)
    {
        # Use 7z with LZMA2, solid archive, max compression and multi-threading
        Write-Host "Creating 7z: $archivePath7z (source: $srcFolder)" -ForegroundColor Green
        try
        {
            # Quote paths to handle spaces
            $srcPattern = Join-Path $srcFolder '*'
            $proc = Start-Process -FilePath $sevenExe -ArgumentList @('a','-t7z','-mx=9','-m0=lzma2','-ms=on','-mmt=on', $archivePath7z, $srcPattern) -NoNewWindow -Wait -PassThru
            if ($proc.ExitCode -eq 0)
            {
                Write-Host "7z created: $archivePath7z" -ForegroundColor Cyan
                continue
            } else
            {
                Write-Host "7z failed (exit $($proc.ExitCode)), falling back to ZIP" -ForegroundColor Yellow
            }
        } catch
        {
            Write-Host "7z exception: $($_.Exception.Message). Falling back to ZIP." -ForegroundColor Yellow
        }
    }

    # Fallback: use Compress-Archive to produce a zip
    Write-Host "Creating ZIP (fallback): $archivePathZip" -ForegroundColor Green
    try
    {
        # Compress-Archive expects a list of files or a folder wildcard.
        $pattern = Join-Path $srcFolder '*'
        Compress-Archive -Path $pattern -DestinationPath $archivePathZip -CompressionLevel Optimal -Force -ErrorAction Stop
        Write-Host "ZIP created: $archivePathZip" -ForegroundColor Cyan
    } catch
    {
        Write-Host "ZIP compression failed: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "Build and publish complete. Output directory: $((Get-Location).Path)\$outputDir" -ForegroundColor Green
