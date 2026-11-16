param(
    [string]$State = (Join-Path $env:LOCALAPPDATA 'Career24-UP04-Case6'),
    [Parameter(Mandatory=$true)][string]$Dump,
    [ValidatePattern('^career24_recovery_[A-Za-z0-9_]{1,35}$')]
    [string]$Schema=('career24_recovery_'+[guid]::NewGuid().ToString('N').Substring(0,8))
)
$ErrorActionPreference='Stop'
$Dump=(Resolve-Path -LiteralPath $Dump).ProviderPath
$manifest=(Get-Content -Raw -LiteralPath "$Dump.sha256").Trim()
$expected=($manifest -split '\s+',2)[0]
$actual=(Get-FileHash -Algorithm SHA256 -LiteralPath $Dump).Hash
if ($expected -notmatch '^[a-fA-F0-9]{64}$' -or $expected -ne $actual) {
    throw 'Контрольная сумма SHA-256 копии не совпала.'
}
$mysqlHome=Get-Content -Raw -LiteralPath (Join-Path $State 'mysql-home.txt')
$client=Join-Path $mysqlHome.Trim() 'bin\mysql.exe'
$ini=(Resolve-Path -LiteralPath (Join-Path $State 'admin-client.ini')).ProviderPath
$clock=[Diagnostics.Stopwatch]::StartNew()
& $client "--defaults-file=$ini" "--execute=CREATE DATABASE $Schema CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci"
if($LASTEXITCODE -ne 0){throw 'Для восстановления нужно свободное имя схемы.'}
$process=New-Object Diagnostics.Process
$process.StartInfo.FileName=$client
$process.StartInfo.Arguments=('--defaults-file="{0}" --default-character-set=utf8mb4 --database={1}' -f $ini,$Schema)
$process.StartInfo.UseShellExecute=$false
$process.StartInfo.CreateNoWindow=$true
$process.StartInfo.RedirectStandardInput=$true
$process.StartInfo.RedirectStandardError=$true
try {
    [void]$process.Start()
    $stderr=$process.StandardError.ReadToEndAsync()
    $file=[IO.File]::OpenRead($Dump)
    try {$file.CopyTo($process.StandardInput.BaseStream)}
    finally {$file.Dispose(); $process.StandardInput.Close()}
    if(!$process.WaitForExit(45000)){
        $process.Kill()
        throw 'Импорт копии не завершился вовремя.'
    }
    if($process.ExitCode -ne 0){throw $stderr.GetAwaiter().GetResult()}
}
finally {$process.Dispose()}

$query = @"
SELECT (SELECT COUNT(*) FROM employers),(SELECT COUNT(*) FROM vacancies),
(SELECT COUNT(*) FROM candidates),(SELECT COUNT(*) FROM recruitments),
(SELECT COUNT(*) FROM interviews);
SELECT COUNT(*) FROM vacancies v LEFT JOIN employers e ON v.employer_id=e.id WHERE e.id IS NULL;
SELECT COUNT(*) FROM recruitments r LEFT JOIN vacancies v ON r.vacancy_id=v.id
LEFT JOIN candidates c ON r.candidate_id=c.id WHERE v.id IS NULL OR c.id IS NULL;
SELECT COUNT(*) FROM interviews i LEFT JOIN recruitments r ON i.recruitment_id=r.id WHERE r.id IS NULL;
"@
$rows=@(& $client "--defaults-file=$ini" "--database=$Schema" --batch --skip-column-names "--execute=$query")
if($LASTEXITCODE -ne 0 -or $rows.Count -ne 4){throw 'Запрос проверки восстановления завершился ошибкой.'}
[pscustomobject]@{
    schema=$Schema
    rows=($rows[0] -split '\s+' | ForEach-Object {[int]$_})
    brokenLinks=(([int]$rows[1])+([int]$rows[2])+([int]$rows[3]))
    elapsedMs=[math]::Round($clock.Elapsed.TotalMilliseconds,1)
}
