# Baut BISClientSetup.exe, prüft die Größe und schreibt client-feed/version.json.
# Committet und pusht nicht. Aufruf über den Skill @c-u.
param(
    [string]$Version,
    [string]$Notes
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root 'src\BISClient\BISClient.csproj'
$iss = Join-Path $root 'installer\BISClient.iss'
$feedDir = Join-Path $root 'client-feed'
$manifestPath = Join-Path $feedDir 'version.json'
$publishedSetup = Join-Path $root 'installer\output\BISClientSetup.exe'
$feedSetup = Join-Path $feedDir 'BISClientSetup.exe'
$utf8 = New-Object System.Text.UTF8Encoding $false

function Read-Text([string]$Path) {
    return [System.IO.File]::ReadAllText($Path)
}

function Write-Text([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText($Path, $Text, $utf8)
}

function Get-CsprojVersion([string]$Text) {
    $match = [regex]::Match($Text, '<Version>([^<]+)</Version>')
    if (-not $match.Success) {
        throw 'Version in BISClient.csproj nicht gefunden.'
    }

    return $match.Groups[1].Value.Trim()
}

function Get-IssVersion([string]$Text) {
    $match = [regex]::Match($Text, '#define MyAppVersion "([^"]+)"')
    if (-not $match.Success) {
        throw 'MyAppVersion in BISClient.iss nicht gefunden.'
    }

    return $match.Groups[1].Value.Trim()
}

function Assert-Version([string]$Value) {
    if ($Value -notmatch '^\d+\.\d+\.\d+$') {
        throw "Version '$Value' muss die Form Major.Minor.Patch haben."
    }
}

function Set-SourceVersion([string]$Value) {
    $csprojText = Read-Text $csproj
    $csprojRegex = New-Object System.Text.RegularExpressions.Regex '<Version>[^<]+</Version>'
    $updatedCsproj = $csprojRegex.Replace($csprojText, "<Version>$Value</Version>", 1)
    if ($updatedCsproj -eq $csprojText) {
        throw 'Version in BISClient.csproj konnte nicht gesetzt werden.'
    }

    Write-Text $csproj $updatedCsproj

    $issText = Read-Text $iss
    $issRegex = New-Object System.Text.RegularExpressions.Regex '#define MyAppVersion "[^"]+"'
    $updatedIss = $issRegex.Replace($issText, "#define MyAppVersion ""$Value""", 1)
    if ($updatedIss -eq $issText) {
        throw 'MyAppVersion in BISClient.iss konnte nicht gesetzt werden.'
    }

    Write-Text $iss $updatedIss
}

function Get-FeedManifest {
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        return [pscustomobject]@{
            version    = '0.0.0'
            minVersion = '1.0.0'
            notes      = ''
        }
    }

    return (Read-Text $manifestPath) | ConvertFrom-Json
}

$branch = git -C $root rev-parse --abbrev-ref HEAD
if ($LASTEXITCODE -ne 0) {
    throw 'Git-Branch konnte nicht ermittelt werden.'
}

if ($branch -ne 'main') {
    throw "Aktueller Branch ist '$branch'. Der Feed wird nur auf main veröffentlicht."
}

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    Assert-Version $Version.Trim()
    Set-SourceVersion $Version.Trim()
}

$sourceVersion = Get-CsprojVersion (Read-Text $csproj)
$issVersion = Get-IssVersion (Read-Text $iss)
Assert-Version $sourceVersion
Assert-Version $issVersion
if ($sourceVersion -ne $issVersion) {
    throw "Versionen weichen ab: csproj $sourceVersion, iss $issVersion."
}

$existing = Get-FeedManifest
$feedVersion = [string]$existing.version
if ([string]::IsNullOrWhiteSpace($feedVersion)) {
    $feedVersion = '0.0.0'
}

Assert-Version $feedVersion
if ([version]$sourceVersion -le [version]$feedVersion) {
    throw "Version $sourceVersion ist nicht neuer als der Feed ($feedVersion)."
}

Write-Host "Veröffentliche $sourceVersion (Feed bisher $feedVersion)"

Push-Location $root
try {
    dotnet publish 'src\BISClient\BISClient.csproj' -c Release -p:PublishProfile=Win64
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish fehlgeschlagen ($LASTEXITCODE)."
    }
}
finally {
    Pop-Location
}

$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (-not (Test-Path -LiteralPath $iscc)) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        $iscc = $command.Source
    }
}

if (-not (Test-Path -LiteralPath $iscc)) {
    throw 'ISCC.exe nicht gefunden. Inno Setup 6 wird erwartet.'
}

& $iscc $iss
if ($LASTEXITCODE -ne 0) {
    throw "ISCC fehlgeschlagen ($LASTEXITCODE)."
}

if (-not (Test-Path -LiteralPath $publishedSetup)) {
    throw "Setup fehlt: $publishedSetup"
}

$setup = Get-Item -LiteralPath $publishedSetup
$warnAt = 50MB
$rejectAt = 100MB
if ($setup.Length -ge $rejectAt) {
    throw "BISClientSetup.exe ist $($setup.Length) Bytes und erreicht 100 MB. GitHub lehnt die Datei ab. client-feed wurde nicht verändert."
}

if ($setup.Length -ge $warnAt) {
    Write-Warning "BISClientSetup.exe ist $([math]::Round($setup.Length / 1MB, 1)) MB. GitHub warnt ab 50 MB."
}

$hash = (Get-FileHash -LiteralPath $publishedSetup -Algorithm SHA256).Hash.ToLowerInvariant()
$minVersion = [string]$existing.minVersion
if ([string]::IsNullOrWhiteSpace($minVersion)) {
    $minVersion = '1.0.0'
}

$notesValue = [string]$existing.notes
if ($PSBoundParameters.ContainsKey('Notes')) {
    $notesValue = $Notes
}

New-Item -ItemType Directory -Force -Path $feedDir | Out-Null
Copy-Item -LiteralPath $publishedSetup -Destination $feedSetup -Force

$json = @"
{
  "version": $(ConvertTo-Json $sourceVersion -Compress),
  "minVersion": $(ConvertTo-Json $minVersion -Compress),
  "downloadUrl": "BISClientSetup.exe",
  "sha256": $(ConvertTo-Json $hash -Compress),
  "notes": $(ConvertTo-Json $notesValue -Compress)
}

"@
Write-Text $manifestPath $json

Write-Host "FEED_READY version=$sourceVersion sha256=$hash bytes=$($setup.Length)"
