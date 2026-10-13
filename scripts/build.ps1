param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$taskRepository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location -LiteralPath $taskRepository
try {
    & $Dotnet restore --locked-mode
    if ($LASTEXITCODE) { throw 'Dependency restore failed.' }
    & $Dotnet format --no-restore --verify-no-changes
    if ($LASTEXITCODE) { throw 'Formatting validation failed.' }
    & $Dotnet build -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Build failed.' }
    & $Dotnet test -c Release --no-build --no-restore --logger 'trx;LogFileName=unit-tests.trx'
    if ($LASTEXITCODE) { throw 'Tests failed.' }
}
finally { Pop-Location }
