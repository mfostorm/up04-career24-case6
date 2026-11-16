param(
    [string]$State = (Join-Path $env:LOCALAPPDATA 'Career24-UP04-Case5'),
    [Parameter(Mandatory=$true)][string]$SecondCopy,
    [Parameter(Mandatory=$true)][string]$OffsiteCopy
)
$ErrorActionPreference='Stop'
$State=(Resolve-Path -LiteralPath $State).ProviderPath
$mysqlHome=Get-Content -Raw -LiteralPath (Join-Path $State 'mysql-home.txt')
$mysqldump=Join-Path $mysqlHome.Trim() 'bin\mysqldump.exe'
$ini=Join-Path $State 'backup-client.ini'

function Destination([string]$root) {
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    return (Resolve-Path -LiteralPath $root).ProviderPath.TrimEnd('\','/')
}

$local=Destination (Join-Path $State 'Backups')
$second=Destination $SecondCopy
$remote=Destination $OffsiteCopy
if (@(@($local,$second,$remote) | Sort-Object -Unique).Count -ne 3) {
    throw 'Choose three different backup locations.'
}
$stamp=(Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8)
$filename="career24-$stamp.sql"
$source=Join-Path $local $filename
$partial="$source.partial"
$clock=[Diagnostics.Stopwatch]::StartNew()
try {
    $dumpOptions=@("--defaults-file=$ini",'--single-transaction','--no-tablespaces','--skip-lock-tables',
      '--set-gtid-purged=OFF','--column-statistics=0','--default-character-set=utf8mb4','--hex-blob',
      "--result-file=$partial",'career24_up04')
    & $mysqldump @dumpOptions
    if($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $partial)){throw 'mysqldump failed.'}
    Move-Item -LiteralPath $partial -Destination $source
} finally {
    if(Test-Path -LiteralPath $partial){Remove-Item -LiteralPath $partial}
}
$hash=(Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash.ToLowerInvariant()
$paths=@($source)
foreach($directory in @($second,$remote)) {
    $target=Join-Path $directory $filename
    $temp="$target.partial"
    try {
        Copy-Item -LiteralPath $source -Destination $temp
        if((Get-FileHash -Algorithm SHA256 -LiteralPath $temp).Hash.ToLowerInvariant() -ne $hash){
            throw "Copy verification failed: $directory"
        }
        Move-Item -LiteralPath $temp -Destination $target
        $paths+=$target
    }
    finally { if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp} }
}
foreach($target in $paths) {
    "$hash  $filename" | Set-Content -LiteralPath "$target.sha256" -Encoding ASCII
}
$clock.Stop()
[pscustomobject]@{
    local=$paths[0]; second=$paths[1]; offsite=$paths[2]
    sha256=$hash; bytes=(Get-Item -LiteralPath $source).Length
    elapsedMs=[math]::Round($clock.Elapsed.TotalMilliseconds,1)
}
