$ErrorActionPreference = 'Stop'
$packageDirectory = Split-Path -Parent $PSScriptRoot
$sourceDirectory = Join-Path $packageDirectory 'build\runtime-sources'
New-Item -ItemType Directory -Path $sourceDirectory -Force | Out-Null
$pins = Get-Content -LiteralPath (Join-Path $packageDirectory 'licenses\SOURCES.json') -Raw | ConvertFrom-Json
foreach ($pin in $pins) {
    $filename = [IO.Path]::GetFileName(([Uri]$pin.url).AbsolutePath)
    # Tag archive URLs have ambiguous names; keep a component prefix.
    $destination = Join-Path $sourceDirectory ($pin.name + '-' + $filename)
    if (-not (Test-Path -LiteralPath $destination)) { Invoke-WebRequest -Uri $pin.url -OutFile $destination }
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $pin.sha256) { throw ('Source checksum mismatch for ' + $pin.name) }
    Write-Output ('Verified ' + $pin.name + ' ' + $pin.version)
}
