$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet publish src/ReunionLocale.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o portable
    if ($LASTEXITCODE -ne 0) { throw 'La publication a échoué.' }
} finally { Pop-Location }
