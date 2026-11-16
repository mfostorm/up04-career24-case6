param(
    [ValidateSet('Prepare','Start','Stop','ShowTables')]
    [string]$Action = 'Start',
    [string]$State = (Join-Path $env:LOCALAPPDATA 'Career24-UP04-Case6'),
    [int]$Port = 33435,
    [string]$App,
    [string]$LegacyDump,
    [string]$RecoveryDump,
    [string]$RecoveryKeyFile
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$utf8 = New-Object Text.UTF8Encoding($false)
$ProgressPreference = 'SilentlyContinue'
$recoveryKey = $null
if ($RecoveryDump -or $RecoveryKeyFile) {
    if (!$RecoveryDump -or !$RecoveryKeyFile -or $LegacyDump -or $Action -notin @('Prepare','Start')) {
        throw 'Укажите RecoveryDump и RecoveryKeyFile вместе и выберите новый профиль для запуска или подготовки.'
    }
    $RecoveryDump=(Resolve-Path -LiteralPath $RecoveryDump).ProviderPath
    $hash=(Get-FileHash -Algorithm SHA256 -LiteralPath $RecoveryDump).Hash
    $expected=((Get-Content -Raw -LiteralPath "$RecoveryDump.sha256").Trim() -split '\s+',2)[0]
    if ($expected -notmatch '^[a-fA-F0-9]{64}$' -or $expected -ne $hash) {
        throw 'Контрольная сумма SHA-256 файла восстановления не совпала.'
    }
    $recoveryKey=(Get-Content -Raw -LiteralPath $RecoveryKeyFile).Trim()
    if ($recoveryKey -notmatch '^[a-fA-F0-9]{32}$') { throw 'Повреждён или неверно выбран файл ключа восстановления.' }
}

function Locate-MySql {
    $download = Join-Path $State 'mysql-8.0.46-winx64'
    $locations = @($env:CAREER24_MYSQL_HOME, $download, "$env:ProgramFiles\MySQL\MySQL Server 8.0",
                  "$env:ProgramFiles\MySQL\MySQL Server 8.4")
    foreach ($location in $locations) {
        if ($location -and (Test-Path -LiteralPath (Join-Path $location 'bin\mysqld.exe')) -and
            (Test-Path -LiteralPath (Join-Path $location 'bin\mysql.exe'))) {
            return $location
        }
    }
    if ($Action -in @('Stop','ShowTables')) {
        throw 'Файлы MySQL не найдены. Сначала запустите «Запустить Карьеру.cmd».'
    }
    Write-Host 'Скачиваю MySQL Community Server с cdn.mysql.com. Это понадобится только при первом запуске.'
    $zip = Join-Path $State 'mysql-server.zip'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://cdn.mysql.com/Downloads/MySQL-8.0/mysql-8.0.46-winx64.zip' -OutFile $zip
    Expand-Archive -LiteralPath $zip -DestinationPath $State -Force
    if (!(Test-Path -LiteralPath (Join-Path $download 'bin\mysqld.exe'))) { throw 'Не удалось распаковать MySQL.' }
    Remove-Item -LiteralPath $zip
    return $download
}

function Run-Client([string]$optionFile, [string]$sql, [string]$dumpPath) {
    $process = New-Object Diagnostics.Process
    $process.StartInfo.FileName = $client
    $process.StartInfo.Arguments = ('--defaults-file="{0}" --batch --raw --skip-column-names --connect-timeout=3' -f $optionFile)
    if ($dumpPath) {
        $process.StartInfo.Arguments += ' --binary-mode --database=career24_up04'
    }
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardInput = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.StandardOutputEncoding = $utf8
    $process.StartInfo.StandardErrorEncoding = $utf8
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if ($dumpPath) {
            $stream=[IO.File]::OpenRead($dumpPath)
            try { $stream.CopyTo($process.StandardInput.BaseStream) } finally { $stream.Dispose() }
        }
        else {
            $bytes = $utf8.GetBytes($sql + [Environment]::NewLine)
            $process.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
        }
        $process.StandardInput.Close()
        if (!$process.WaitForExit(20000)) {
            $process.Kill()
            throw 'Клиент MySQL не ответил вовремя.'
        }
        if ($process.ExitCode -ne 0) { throw $stderr.GetAwaiter().GetResult().Trim() }
        return $stdout.GetAwaiter().GetResult().Trim()
    }
    finally { $process.Dispose() }
}

function Write-ClientOptions([string]$file, [string]$username, [string]$secret) {
    $contents = @"
[client]
host=127.0.0.1
port=$Port
protocol=tcp
user=$username
password=$secret
default-character-set=utf8mb4
ssl-mode=REQUIRED
"@
    [IO.File]::WriteAllText($file, $contents, $utf8)
}

function Query-OwnServer {
    try {
        $dir = Run-Client $rootSettings 'SELECT @@datadir;'
        if (!$dir) { return $false }
        $actual = [IO.Path]::GetFullPath($dir.TrimEnd('/','\'))
        if ($actual -ne $dataDir) { throw "Сервер на порту $Port использует другой каталог данных." }
        return $true
    } catch {
        if ($_.Exception.Message -like '*использует другой каталог данных*') { throw }
        return $false
    }
}

if ($Action -in @('Stop','ShowTables') -and !(Test-Path -LiteralPath $State)) {
    throw 'Сначала запустите «Запустить Карьеру.cmd».'
}
New-Item -ItemType Directory -Path $State -Force | Out-Null
$State = (Resolve-Path -LiteralPath $State).ProviderPath
$dataDir = Join-Path $State 'data'
$serverOptions = Join-Path $State 'my.ini'
$rootSettings = Join-Path $State 'admin-client.ini'
$managerSettings = Join-Path $State 'manager-client.ini'
$backupSettings = Join-Path $State 'backup-client.ini'
$credentials = Join-Path $State 'credentials.json'
$connection = Join-Path $State 'connection.json'
$stateHash = [BitConverter]::ToString(([Security.Cryptography.SHA256]::Create().ComputeHash(
    $utf8.GetBytes($State.ToLowerInvariant())))).Replace('-','')
$gate = New-Object Threading.Mutex($false, "Local\Career24UP04_Case5_$stateHash")
$locked = $false
try {
    try { $locked = $gate.WaitOne(150000) } catch [Threading.AbandonedMutexException] { $locked = $true }
    if (!$locked) { throw 'MySQL ещё запускается в другом окне.' }
    $mysqlBase = Locate-MySql
    $server = Join-Path $mysqlBase 'bin\mysqld.exe'
    $client = Join-Path $mysqlBase 'bin\mysql.exe'
    if (Test-Path -LiteralPath $credentials) {
        $auth = Get-Content -Raw -LiteralPath $credentials | ConvertFrom-Json
        if ($RecoveryDump) { throw 'Для полного восстановления выберите новый профиль.' }
        $Port = [int]$auth.port
    } else {
        if ($Action -in @('Stop','ShowTables')) { throw 'Сначала подготовьте учебную базу.' }
        while (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) { $Port += 1 }
        $auth = [ordered]@{
            port=$Port
            admin=[Guid]::NewGuid().ToString('N')
            identity=[Guid]::NewGuid().ToString('N')
            manager=[Guid]::NewGuid().ToString('N')
            administrator=[Guid]::NewGuid().ToString('N')
            backup=[Guid]::NewGuid().ToString('N')
            cipherKey=$(if($recoveryKey){$recoveryKey}else{[Guid]::NewGuid().ToString('N')})
        }
        [IO.File]::WriteAllText($credentials, ($auth | ConvertTo-Json), $utf8)
    }
    Write-ClientOptions $rootSettings 'root' $auth.admin
    Write-ClientOptions $managerSettings 'c5_manager' $auth.manager
    Write-ClientOptions $backupSettings 'c5_backup' $auth.backup
    if ($Action -eq 'Stop') {
        if (Query-OwnServer) {
            [void](Run-Client $rootSettings 'SHUTDOWN;')
            Write-Host 'Учебная база «Карьера» остановлена.'
        }
        return
    }
    if ($Action -eq 'ShowTables') {
        if (!(Query-OwnServer)) { throw 'Сначала запустите базу «Карьера».' }
        & $client "--defaults-file=$managerSettings" '--table' '--database=career24_up04' '--execute=SELECT VERSION() AS MySQL; SELECT CURRENT_ROLE(); SELECT id,phone,email FROM candidates WHERE id <= 3 ORDER BY id;'
        return
    }

    $basePath = $mysqlBase.Replace('\','/')
    $myData = $dataDir.Replace('\','/')
    $logPath = (Join-Path $State 'mysql.log').Replace('\','/')
    $options = @"
[mysqld]
basedir="$basePath"
datadir="$myData"
port=$Port
bind-address=127.0.0.1
mysqlx=OFF
character-set-server=utf8mb4
log-error="$logPath"
"@
    if (!(Query-OwnServer)) {
        if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) {
            throw "Порт $Port занят. Укажите свободный порт."
        }
        [IO.File]::WriteAllText($serverOptions, $options, $utf8)
        $serverArg = '--defaults-file="{0}"' -f $serverOptions
        $bootstrap = Join-Path $State 'bootstrap.sql'
        if (!(Test-Path -LiteralPath (Join-Path $dataDir 'mysql.ibd'))) {
            Write-Host 'Подготавливаю локальный каталог данных MySQL.'
            $initial = Start-Process -FilePath $server -ArgumentList @($serverArg,'--initialize-insecure') -WindowStyle Hidden -PassThru -Wait
            if ($initial.ExitCode -ne 0) { throw "Не удалось подготовить MySQL. Журнал: $logPath" }
            $alter = "ALTER USER 'root'@'localhost' IDENTIFIED BY '$($auth.admin)';"
            [IO.File]::WriteAllText($bootstrap, ($alter + [Environment]::NewLine), $utf8)
        }
        $serverArgs = @($serverArg)
        if (Test-Path -LiteralPath $bootstrap) { $serverArgs += '--init-file="{0}"' -f $bootstrap }
        $worker = Start-Process -FilePath $server -ArgumentList $serverArgs -WorkingDirectory $mysqlBase -WindowStyle Hidden -PassThru
        $ready = $false
        for ($attempt = 0; $attempt -lt 75; $attempt++) {
            if (Query-OwnServer) { $ready = $true; break }
            if ($worker.HasExited) { break }
            Start-Sleep -Milliseconds 500
        }
        if (!$ready) { throw "MySQL не запустилась. Журнал: $logPath" }
        if (Test-Path -LiteralPath $bootstrap) { Remove-Item -LiteralPath $bootstrap }
    }
    if (!$App) { $App=Join-Path $root 'App\Career24.exe' }
    if (!(Test-Path -LiteralPath $App)) { throw "Файл приложения не найден: $App" }
    $App=(Resolve-Path -LiteralPath $App).ProviderPath
    $marker=Join-Path $State 'case5-v1.ready'
    if ($LegacyDump -and (Test-Path -LiteralPath $marker)) {
        throw 'Для импорта копии из кейса 2 выберите новый профиль.'
    }
    if (!(Test-Path -LiteralPath $marker)) {
        Write-Host 'Подготавливаю таблицы, шифрование контактов и роли MySQL.'
        [void](Run-Client $rootSettings ([IO.File]::ReadAllText((Join-Path $root 'Database\01_schema.sql'), $utf8)))
        if ((Run-Client $rootSettings 'SELECT COUNT(*) FROM career24_up04.roles;') -eq '0') {
            if ($LegacyDump -or $RecoveryDump) {
                $legacy=if($RecoveryDump){$RecoveryDump}else{(Resolve-Path -LiteralPath $LegacyDump).ProviderPath}
                [void](Run-Client $rootSettings -dumpPath $legacy)
            }
            else {
                [void](Run-Client $rootSettings ([IO.File]::ReadAllText((Join-Path $root 'Database\02_demo.sql'), $utf8)))
            }
        }
        [void](Run-Client $rootSettings ([IO.File]::ReadAllText((Join-Path $root 'Database\03_contact_columns.sql'),$utf8)))
        $migration=Join-Path $State 'migration.json'
        $migrationLog=Join-Path $State 'contact-migration.log'
        $data=[ordered]@{
            server='127.0.0.1'; port=$Port; database='career24_up04'; user='root'
            password=$auth.admin; cipherKey=$auth.cipherKey
        }
        try {
            [IO.File]::WriteAllText($migration, ($data | ConvertTo-Json), $utf8)
            $m=Start-Process -FilePath $App -ArgumentList @('--migrate-contacts',
                ('"{0}"' -f $migration),('"{0}"' -f $migrationLog)) -WindowStyle Hidden -Wait -PassThru
            if ($m.ExitCode -ne 0) { throw "Контакты не перенесены. Журнал: $migrationLog" }
        }
        finally { if (Test-Path -LiteralPath $migration) { Remove-Item -LiteralPath $migration } }

        [void](Run-Client $rootSettings ([IO.File]::ReadAllText((Join-Path $root 'Database\04_access.sql'),$utf8)))
        $grants = @"
CREATE USER IF NOT EXISTS 'c5_identity'@'127.0.0.1' IDENTIFIED BY '$($auth.identity)' REQUIRE SSL;
CREATE USER IF NOT EXISTS 'c5_manager'@'127.0.0.1' IDENTIFIED BY '$($auth.manager)' REQUIRE SSL;
CREATE USER IF NOT EXISTS 'c5_admin'@'127.0.0.1' IDENTIFIED BY '$($auth.administrator)' REQUIRE SSL;
CREATE USER IF NOT EXISTS 'c5_backup'@'127.0.0.1' IDENTIFIED BY '$($auth.backup)' REQUIRE SSL;
GRANT 'c5_r_identity' TO 'c5_identity'@'127.0.0.1';
GRANT 'c5_r_manager' TO 'c5_manager'@'127.0.0.1';
GRANT 'c5_r_admin' TO 'c5_admin'@'127.0.0.1';
GRANT 'c5_r_backup' TO 'c5_backup'@'127.0.0.1';
SET DEFAULT ROLE 'c5_r_identity' TO 'c5_identity'@'127.0.0.1';
SET DEFAULT ROLE 'c5_r_manager' TO 'c5_manager'@'127.0.0.1';
SET DEFAULT ROLE 'c5_r_admin' TO 'c5_admin'@'127.0.0.1';
SET DEFAULT ROLE 'c5_r_backup' TO 'c5_backup'@'127.0.0.1';
"@
        [void](Run-Client $rootSettings $grants)
        [void](Run-Client $backupSettings 'SELECT COUNT(*) FROM career24_up04.users;')
        New-Item -ItemType File -Path $marker -Force | Out-Null
    }
    $connectionData = [ordered]@{
        server='127.0.0.1'; port=$Port; database='career24_up04'
        identity=@{user='c5_identity'; password=$auth.identity}
        manager=@{user='c5_manager'; password=$auth.manager}
        administrator=@{user='c5_admin'; password=$auth.administrator}
        cipherKey=$auth.cipherKey
    }
    [IO.File]::WriteAllText($connection, ($connectionData | ConvertTo-Json), $utf8)
    [IO.File]::WriteAllText((Join-Path $State 'mysql-home.txt'),$mysqlBase,$utf8)
    Write-Host "База «Карьера» готова на 127.0.0.1:$Port"
}
finally {
    if ($locked) { $gate.ReleaseMutex() }
    $gate.Dispose()
}

if ($Action -eq 'Start') {
    Start-Process -FilePath $App -ArgumentList ('"{0}"' -f $connection) -WorkingDirectory (Split-Path -Parent $App)
}
