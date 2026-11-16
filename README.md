# Кадровое агентство «Карьера»

УП.04, вариант 24. Кейс 6 проверяет приложение C# Windows Forms и MySQL из предыдущих кейсов. В базе используются вымышленные записи.

## Запуск

1. В GitHub откройте **Actions → Сборка и автотесты** и выберите успешный запуск.
2. Скачайте артефакт **Career24-win-x64** и распакуйте его целиком на Windows x64.
3. Откройте **Запустить Карьеру.cmd**. Если подходящего экземпляра MySQL нет, сценарий скачает MySQL Community Server. Готовый комплект включает среду .NET.
4. Для демонстрационного входа используйте `manager` или `admin` с учебным паролем `CareerDemo!24`. Проверьте поиск «Бухгалтер» во вкладке «Вакансии».

Данные хранятся в профиле `%LOCALAPPDATA%\Career24-UP04-Case6`. Учебный сервер привязан к 127.0.0.1. Для остановки MySQL закройте форму, откройте PowerShell в папке комплекта и выполните:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Launch\Service.ps1 -Action Stop -State "$env:LOCALAPPDATA\Career24-UP04-Case6"
```

## Повторение проверок из исходников

Понадобятся Windows x64, SDK .NET 8 и MySQL 8.0. Сценарий скачает MySQL при необходимости. Если MySQL уже распакован отдельно, задайте `CAREER24_MYSQL_HOME` путём к каталогу с `bin/mysqld.exe`.

```powershell
dotnet build Career24.sln -c Release
dotnet build Verification/Verify.csproj -c Release
dotnet publish Program/Career24.csproj -c Release -r win-x64 --self-contained true -o artifacts/Kit/App
./Verification/Run-CI.ps1 -App artifacts/Kit/App/Career24.exe -Output artifacts/Proof
```

Параметр `-Measure` добавляет локальные замеры поиска, перезапуски MySQL и проверку окна в интерактивной сессии Windows.

GitHub Actions выполняет сборку и проверки при push, pull request и ручном запуске. Проверки используют собственный временный профиль MySQL с учебными данными. Успешный прогон сохраняет комплект Windows и JSON-протокол. Развёртывание выполняется запуском скачанного комплекта.
