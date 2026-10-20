# Baut die App (Release, eigenstaendig inkl. .NET-Laufzeit) und daraus den Installer.
# Ergebnis: artifacts\ICP-Monitor-Setup-<Version>.exe
# Voraussetzung: .NET 10 SDK und Inno Setup 6 (winget install JRSoftware.InnoSetup)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\IcpMonitor\IcpMonitor.csproj'
$publish = Join-Path $root 'artifacts\publish'

$version = (Select-Xml -Path $project -XPath '//Version').Node.InnerText
Write-Host "ICP Monitor $version" -ForegroundColor Cyan

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true -o $publish -p:DebugType=None -nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish fehlgeschlagen.' }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 nicht gefunden (winget install JRSoftware.InnoSetup).' }

& $iscc "/DAppVersion=$version" "/DPublishDir=$publish" (Join-Path $root 'installer\IcpMonitor.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup fehlgeschlagen.' }

# Pruefsumme fuer das Auto-Update (die App installiert nur, wenn sie passt).
# Beim Release beide Dateien hochladen: Setup-.exe und .exe.sha256
$setup = Join-Path $root "artifacts\ICP-Monitor-Setup-$version.exe"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$setup.sha256", "$hash  $(Split-Path $setup -Leaf)`n")

Get-Item $setup, "$setup.sha256" | Select-Object FullName, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
