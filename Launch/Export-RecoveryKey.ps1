param(
    [string]$State=(Join-Path $env:LOCALAPPDATA 'Career24-UP04-Case6'),
    [Parameter(Mandatory=$true)][string]$Destination
)
$ErrorActionPreference='Stop'
$auth=Get-Content -Raw -LiteralPath (Join-Path $State 'credentials.json') | ConvertFrom-Json
if ($auth.cipherKey -notmatch '^[a-fA-F0-9]{32}$') { throw 'В профиле отсутствует ключ восстановления.' }
$target=[IO.Path]::GetFullPath($Destination)
$stream=[IO.File]::Open($target,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try {
    $bytes=[Text.Encoding]::ASCII.GetBytes($auth.cipherKey)
    $stream.Write($bytes,0,$bytes.Length)
}
finally {$stream.Dispose()}
Write-Host 'Ключ сохранён. Храните его в защищённом каталоге отдельно от копии базы.'
