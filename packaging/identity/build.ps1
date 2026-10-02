param(
    [Parameter(Mandatory = $true)]
    [string] $OutputDir,

    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $SdkBinDir
)

$ErrorActionPreference = "Stop"

function Convert-ToFourPartVersion([string] $value) {
    $parts = ($value -split "[^0-9]") | Where-Object { $_ -ne "" }
    while ($parts.Count -lt 4) {
        $parts += "0"
    }
    return "{0}.{1}.{2}.{3}" -f $parts[0], $parts[1], $parts[2], $parts[3]
}

function Save-ResizedPng([string] $Source, [string] $Dest, [int] $Width, [int] $Height) {
    $src = [System.Drawing.Image]::FromFile($Source)
    try {
        $bmp = New-Object System.Drawing.Bitmap $Width, $Height
        try {
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try {
                $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $g.Clear([System.Drawing.Color]::FromArgb(255, 27, 58, 75))
                $g.DrawImage($src, 0, 0, $Width, $Height)
                $dir = Split-Path $Dest -Parent
                if (-not (Test-Path $dir)) {
                    New-Item -ItemType Directory -Path $dir | Out-Null
                }
                $bmp.Save($Dest, [System.Drawing.Imaging.ImageFormat]::Png)
            }
            finally {
                $g.Dispose()
            }
        }
        finally {
            $bmp.Dispose()
        }
    }
    finally {
        $src.Dispose()
    }
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $root "..\..")
$icon = Join-Path $repoRoot "src\BISClient\Resources\bis-client-icon.png"
$template = Join-Path $root "AppxManifest.xml"
$staging = Join-Path $root "staging"
$keys = Join-Path $root ".keys"
$msixName = "BIS.BISClient.msix"
$cerName = "BIS.Identity.cer"
$pfxPath = Join-Path $keys "BIS.Identity.pfx"
$pfxPassword = "BISClient-Identity"
$makeAppx = Join-Path $SdkBinDir "makeappx.exe"
$signTool = Join-Path $SdkBinDir "signtool.exe"

if (-not (Test-Path $makeAppx)) { throw "makeappx.exe nicht gefunden: $makeAppx" }
if (-not (Test-Path $signTool)) { throw "signtool.exe nicht gefunden: $signTool" }
if (-not (Test-Path $icon)) { throw "App-Icon nicht gefunden: $icon" }
if (-not (Test-Path $template)) { throw "AppxManifest-Vorlage nicht gefunden: $template" }

Add-Type -AssemblyName System.Drawing

if (Test-Path $staging) {
    Remove-Item $staging -Recurse -Force
}
New-Item -ItemType Directory -Path $staging | Out-Null
New-Item -ItemType Directory -Path (Join-Path $staging "Images") | Out-Null
if (-not (Test-Path $keys)) {
    New-Item -ItemType Directory -Path $keys | Out-Null
}
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

Save-ResizedPng $icon (Join-Path $staging "Images\StoreLogo.png") 50 50
Save-ResizedPng $icon (Join-Path $staging "Images\Square44x44Logo.png") 44 44
Save-ResizedPng $icon (Join-Path $staging "Images\Square150x150Logo.png") 150 150

$fourPart = Convert-ToFourPartVersion $Version
$manifestText = [System.IO.File]::ReadAllText($template).Replace("__VERSION__", $fourPart)
[System.IO.File]::WriteAllText((Join-Path $staging "AppxManifest.xml"), $manifestText)

if (-not (Test-Path $pfxPath)) {
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject "CN=BIS" `
        -KeyUsage DigitalSignature `
        -FriendlyName "BIS Client Identity" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
        -NotAfter (Get-Date).AddYears(10)
    $secure = ConvertTo-SecureString $pfxPassword -AsPlainText -Force
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $secure | Out-Null
    Remove-Item (Join-Path "Cert:\CurrentUser\My" $cert.Thumbprint)
}

$msixPath = Join-Path $OutputDir $msixName
$cerPath = Join-Path $OutputDir $cerName
if (Test-Path $msixPath) {
    Remove-Item $msixPath -Force
}

& $makeAppx pack /d $staging /p $msixPath /nv
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx ist fehlgeschlagen ($LASTEXITCODE)"
}

& $signTool sign /fd SHA256 /a /f $pfxPath /p $pfxPassword $msixPath
if ($LASTEXITCODE -ne 0) {
    throw "SignTool ist fehlgeschlagen ($LASTEXITCODE)"
}

$secure = ConvertTo-SecureString $pfxPassword -AsPlainText -Force
$signingCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfxPath, $secure)
[System.IO.File]::WriteAllBytes($cerPath, $signingCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))

Write-Host "Identity-Paket erstellt: $msixPath"
