param(
    [Parameter(Mandatory=$true)][string]$App,
    [Parameter(Mandatory=$true)][string]$Output,
    [switch]$Measure
)

$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$appPath=(Resolve-Path -LiteralPath $App).ProviderPath
$proof=(New-Item -ItemType Directory -Path $Output -Force).FullName
$state=Join-Path ([IO.Path]::GetTempPath()) ('Career24-Case6-'+[guid]::NewGuid().ToString('N'))
$service=Join-Path $root 'Launch/Service.ps1'
$dll=Join-Path $PSScriptRoot 'bin/Release/net8.0-windows/Verify.dll'
$port=33445

function Run-Service([string]$Action) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $service -Action $Action -App $appPath -State $state -Port $port
    if ($LASTEXITCODE) { throw "Сценарий MySQL завершился ошибкой на действии $Action." }
}

function Run-Verifier([string]$Mode, [string]$ResultPath) {
    & dotnet.exe $dll $Mode $state $ResultPath
    if ($LASTEXITCODE) { throw "Ошибка в режиме проверки $Mode." }
}

$start=[Diagnostics.Stopwatch]::StartNew()
try {
    Run-Service Prepare
    $start.Stop()
    Run-Verifier ci $proof
    if ($Measure) {
        Run-Verifier live (Join-Path $proof 'desktop')
        Run-Verifier measure $proof
        $restarts=@()
        for ($i=1;$i -le 3;$i++) {
            Run-Service Stop
            $clock=[Diagnostics.Stopwatch]::StartNew()
            Run-Service Prepare
            Run-Verifier measure (Join-Path $proof "restart-$i")
            $restarts+=[math]::Round($clock.Elapsed.TotalMilliseconds,2)
        }
        $os=Get-CimInstance -ClassName Win32_OperatingSystem
        @{
            os=$os.Caption; build=$os.BuildNumber; architecture=$os.OSArchitecture
            prepareMs=[math]::Round($start.Elapsed.TotalMilliseconds,2)
            sdk=(& dotnet.exe --version); restartAndCheckMs=$restarts
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $proof 'environment.json') -Encoding UTF8
    }
}
finally {
    if (Test-Path -LiteralPath (Join-Path $state 'credentials.json')) {
        Run-Service Stop
    }
}
