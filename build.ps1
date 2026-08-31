[CmdletBinding()]
param(
    [ValidateSet('x64', 'x86')]
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$frameworkFolder = if ($Platform -eq 'x64') { 'Framework64' } else { 'Framework' }
$compiler = Join-Path $env:WINDIR "Microsoft.NET\$frameworkFolder\v4.0.30319\csc.exe"

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "Windows C# compiler not found: $compiler"
}

$outputDir = Join-Path $projectRoot 'release'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$outputFile = Join-Path $outputDir 'ThreeFingerDrag.exe'
$manifestFile = Join-Path $projectRoot 'app.manifest'

$sources = @(
    (Join-Path $projectRoot 'src\Program.cs'),
    (Join-Path $projectRoot 'src\GestureEngine.cs'),
    (Join-Path $projectRoot 'src\RawTouchpad.cs'),
    (Join-Path $projectRoot 'src\Native.cs')
)

& $compiler /nologo /target:winexe /optimize+ /debug- "/platform:$Platform" `
    "/out:$outputFile" `
    "/win32manifest:$manifestFile" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    $sources

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$item = Get-Item -LiteralPath $outputFile
Write-Host ("Build completed: {0} ({1:N0} bytes)" -f $item.FullName, $item.Length)
