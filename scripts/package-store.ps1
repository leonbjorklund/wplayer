$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "WPlayer.csproj"
$manifestSource = Join-Path $root "store\AppxManifest.xml"
$layout = Join-Path $root "artifacts\store\layout"
[xml]$projectXml = Get-Content -LiteralPath $project
$version = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
$outputDir = Join-Path $root "artifacts\store"

$parsedVersion = [Version]$version
$packageVersion = "$($parsedVersion.Major).$($parsedVersion.Minor).$($parsedVersion.Build).0"
$package = Join-Path $outputDir "WPlayer-$packageVersion-x64.msix"
$makeAppxPath = (Get-Command makeappx.exe -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($makeAppxPath)) {
    $windowsSdkRoot = $env:WindowsSdkDir
    if ([string]::IsNullOrWhiteSpace($windowsSdkRoot)) {
        $windowsSdkRoot = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots" -Name KitsRoot10 -ErrorAction SilentlyContinue).KitsRoot10
    }

    if (-not [string]::IsNullOrWhiteSpace($windowsSdkRoot)) {
        $makeAppxPath = Get-ChildItem (Join-Path $windowsSdkRoot "bin\*\x64\makeappx.exe") -ErrorAction SilentlyContinue |
            Sort-Object { [Version]$_.Directory.Parent.Name } -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
}
if ([string]::IsNullOrWhiteSpace($makeAppxPath)) {
    throw "MakeAppx.exe was not found. Install the Windows SDK."
}

if (Test-Path -LiteralPath $layout) {
    Remove-Item -LiteralPath $layout -Recurse -Force
}
New-Item -ItemType Directory -Path $layout | Out-Null
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
if (Test-Path -LiteralPath $package) {
    Remove-Item -LiteralPath $package -Force
}

dotnet publish $project -c Release -r win-x64 --self-contained true -o $layout `
    -p:Version=$version `
    -p:SatelliteResourceLanguages=en-US
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

[xml]$manifest = Get-Content -LiteralPath $manifestSource
$manifest.Package.Identity.Version = $packageVersion
$manifest.Save((Join-Path $layout "AppxManifest.xml"))

Copy-Item -LiteralPath (Join-Path $root "store\Assets") -Destination $layout -Recurse

& $makeAppxPath pack /o /d $layout /p $package
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx failed with exit code $LASTEXITCODE."
}

Get-Item -LiteralPath $package | Select-Object FullName, Length
