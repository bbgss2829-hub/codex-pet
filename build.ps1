$ErrorActionPreference='Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$localSdk=Join-Path $PSScriptRoot 'tools\dotnet\dotnet.exe'
$dotnet=if(Test-Path $localSdk){$localSdk}else{'dotnet'}
& $dotnet run --project (Join-Path $PSScriptRoot 'tests\CodexPet.Tests.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Tests failed; publish aborted.'}
& $dotnet publish (Join-Path $PSScriptRoot 'src\CodexPet\CodexPet.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $PSScriptRoot 'dist\win-x64') --nologo
if($LASTEXITCODE -ne 0){throw 'Publish failed.'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $PSScriptRoot 'dist\win-x64\README.md') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination (Join-Path $PSScriptRoot 'dist\win-x64') -Recurse -Force
