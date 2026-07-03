param(
    [switch]$SkipModelDownload
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}

$env:DOTNET_CLI_HOME = $root
$env:APPDATA = Join-Path $root '.appdata'
$env:NUGET_PACKAGES = Join-Path $root '.nuget\packages'
New-Item -ItemType Directory -Force (Join-Path $env:APPDATA 'NuGet') | Out-Null

$modelName = 'vosk-model-small-cn-0.22'
$modelRoot = Join-Path $root "src\MovieTimestampNotes.App\models"
$modelPath = Join-Path $modelRoot $modelName
if (-not (Test-Path $modelPath)) {
    if ($SkipModelDownload) {
        throw "Missing speech model: $modelPath"
    }
    New-Item -ItemType Directory -Force $modelRoot | Out-Null
    $zip = Join-Path $env:TEMP "$modelName.zip"
    Write-Host 'Downloading the official Vosk Chinese small model (about 42 MB)...'
    Invoke-WebRequest -UseBasicParsing "https://alphacephei.com/vosk/models/$modelName.zip" -OutFile $zip
    Expand-Archive -LiteralPath $zip -DestinationPath $modelRoot -Force
    Remove-Item -LiteralPath $zip -Force
}

$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'portable'
$archive = Join-Path $artifacts 'MovieTimestampNotes-win-x64.zip'
if (Test-Path $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
if (Test-Path $archive) { Remove-Item -LiteralPath $archive -Force }
New-Item -ItemType Directory -Force $publish | Out-Null

& $dotnet restore (Join-Path $root 'MovieTimestampNotes.slnx') --configfile (Join-Path $root 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }

& $dotnet restore (Join-Path $root 'src\MovieTimestampNotes.App\MovieTimestampNotes.App.csproj') `
    -r win-x64 --configfile (Join-Path $root 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'Windows runtime restore failed.' }

& $dotnet test (Join-Path $root 'tests\MovieTimestampNotes.Tests\MovieTimestampNotes.Tests.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

& $dotnet publish (Join-Path $root 'src\MovieTimestampNotes.App\MovieTimestampNotes.App.csproj') `
    -c Release -r win-x64 --self-contained true --no-restore -o $publish `
    -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $archive -CompressionLevel Optimal
Write-Host "Portable package created: $archive"
