$ErrorActionPreference = 'Stop'
$packageDirectory = Split-Path -Parent $PSScriptRoot
$testDirectory = Join-Path $packageDirectory 'build\tests'
$reportDirectory = Join-Path $packageDirectory 'build\reports'
New-Item -ItemType Directory -Path $testDirectory,$reportDirectory -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$sdk = Get-ChildItem -LiteralPath 'C:\Program Files (x86)\Windows Kits\10\UnionMetadata' -Directory | Where-Object { $_.Name -match '^\d+\.' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'Windows.winmd')) } | Sort-Object Name -Descending | Select-Object -First 1
if (-not $sdk) { throw 'The optional live capture test requires the Windows 10/11 SDK metadata.' }
$references = @(
    '/reference:System.Windows.Forms.dll',
    ('/reference:' + (Join-Path $sdk.FullName 'Windows.winmd')),
    ('/reference:' + (Join-Path $framework 'System.Runtime.WindowsRuntime.dll')),
    ('/reference:' + (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll')),
    ('/reference:' + (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Runtime.InteropServices.WindowsRuntime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.InteropServices.WindowsRuntime.dll'))
)
$probe = Join-Path $testDirectory 'screen-sharing-capture-tests.exe'
$targetExecutable = Join-Path $testDirectory 'sdl-capture-target.exe'
$manifest = '/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')
& $compiler /nologo /target:exe /platform:x64 $manifest ('/out:' + $probe) @references (Join-Path $PSScriptRoot 'tests\ScreenSharingCaptureTests.cs') (Join-Path $PSScriptRoot 'ScreenSharingWindow.cs')
if ($LASTEXITCODE -ne 0) { throw 'Capture test build failed.' }
& $compiler /nologo /target:winexe /platform:x64 $manifest ('/out:' + $targetExecutable) (Join-Path $PSScriptRoot 'tests\SdlCaptureTarget.cs')
if ($LASTEXITCODE -ne 0) { throw 'SDL capture fixture build failed.' }
$handleFile = Join-Path $testDirectory ('capture-' + [Guid]::NewGuid().ToString('N') + '.hwnd')
$target = Start-Process -FilePath $targetExecutable -ArgumentList ('"' + $handleFile + '"') -WorkingDirectory $packageDirectory -WindowStyle Hidden -PassThru
try {
    for ($attempt = 0; $attempt -lt 50 -and -not (Test-Path -LiteralPath $handleFile); $attempt++) { Start-Sleep -Milliseconds 100 }
    if (-not (Test-Path -LiteralPath $handleFile)) { throw 'The temporary SDL window did not start.' }
    & $probe (Get-Content -LiteralPath $handleFile) $target.Id | Tee-Object -FilePath (Join-Path $reportDirectory 'screen-sharing-capture.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Live screen sharing capture checks failed.' }
} finally {
    if (-not $target.HasExited) { $target.CloseMainWindow() | Out-Null; if (-not $target.WaitForExit(3000)) { $target.Kill() } }
    if (Test-Path -LiteralPath $handleFile) { Remove-Item -LiteralPath $handleFile }
}
