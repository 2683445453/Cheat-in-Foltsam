#requires -Version 7.2
param(
    [string]$GameDirectory = 'D:\SteamLibrary\steamapps\common\Flotsam'
)

$ErrorActionPreference = 'Stop'
$gameManaged = Join-Path (Resolve-Path -LiteralPath $GameDirectory).Path 'Flotsam_Data/Managed'
$build = Join-Path $PSScriptRoot 'build'
$managedOutput = Join-Path $build 'managed'
$dist = Join-Path $PSScriptRoot 'dist'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$installation) { throw 'Visual Studio C++ Build Tools (x64) are required.' }

New-Item -ItemType Directory -Force -Path $build,$dist | Out-Null
$project = Join-Path $PSScriptRoot 'src/Flotsam.Trainer.csproj'
& dotnet build $project -c Release -o $managedOutput "-p:GameManaged=$gameManaged" --nologo
if ($LASTEXITCODE) { throw 'Managed build failed.' }

Push-Location $build
try {
    $vcvars = Join-Path $installation 'VC/Auxiliary/Build/vcvars64.bat'
    $src = Join-Path $PSScriptRoot 'src'
    $command = 'call "{0}" >nul && cl /nologo /W4 /EHsc /std:c++17 /O2 /MT /utf-8 /LD "{1}\Bootstrap.cpp" /link /OUT:Flotsam.Bootstrap.dll && cl /nologo /W4 /EHsc /std:c++17 /O2 /MT /utf-8 /DUNICODE /D_UNICODE "{1}\Launcher.cpp" user32.lib /link /SUBSYSTEM:WINDOWS /OUT:FlotsamTrainer.exe' -f $vcvars,$src
    & cmd.exe /d /c $command
    if ($LASTEXITCODE) { throw 'Native x64 build failed.' }
}
finally {
    Pop-Location
}

$files = @(
    (Join-Path $build 'FlotsamTrainer.exe')
    (Join-Path $build 'Flotsam.Bootstrap.dll')
    (Join-Path $managedOutput 'Flotsam.Trainer.dll')
    (Join-Path $managedOutput '0Harmony.dll')
    (Join-Path $PSScriptRoot '使用说明.txt')
    (Join-Path $PSScriptRoot 'LICENSE')
    (Join-Path $PSScriptRoot 'LICENSE.Harmony.txt')
)
Compress-Archive -LiteralPath $files -DestinationPath (Join-Path $dist 'FlotsamTrainer.zip') -Force
