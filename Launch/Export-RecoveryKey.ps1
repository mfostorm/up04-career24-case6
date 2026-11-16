param(
    [string]$State=(Join-Path $env:LOCALAPPDATA 'Career24-UP04-Case5'),
    [Parameter(Mandatory=$true)][string]$Destination
)
$ErrorActionPreference='Stop'
$auth=Get-Content -Raw -LiteralPath (Join-Path $State 'credentials.json') | ConvertFrom-Json
if ($auth.cipherKey -notmatch '^[a-fA-F0-9]{32}$') { throw 'The Case 5 key is missing.' }
$target=[IO.Path]::GetFullPath($Destination)
$stream=[IO.File]::Open($target,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try {
    $bytes=[Text.Encoding]::ASCII.GetBytes($auth.cipherKey)
    $stream.Write($bytes,0,$bytes.Length)
}
finally {$stream.Dispose()}
Write-Host 'Recovery key exported. Store this file in a separately protected location.'
