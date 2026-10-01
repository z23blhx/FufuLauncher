param(
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$OutputEncoding = [System.Text.UTF8Encoding]::new()
$env:GIT_MERGE_AUTOEDIT = 'no'

$repoRoot = $PSScriptRoot
$projectPath = Join-Path $repoRoot 'FufuLauncher\FufuLauncher.csproj'
$buildRoot = Join-Path $repoRoot 'FufuLauncher\bin\x64\Debug'
$outputDirectory = $null
$executablePath = $null
$nativeCorePath = Join-Path $repoRoot '.local-dependencies\FufuLauncher.UnlockerIsland'
$nativeCoreRepository = 'https://github.com/FufuLauncher/FufuLauncher.UnlockerIsland.git'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'FufuLauncher.exe.lnk'

function Write-Step {
    param([string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Invoke-Git {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Git command failed: git $($Arguments -join ' ')"
    }
}

function Get-MSBuildPath {
    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vsWhere) {
        $found = & $vsWhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
            Select-Object -First 1
        if ($found) { return $found }
    }

    throw 'MSBuild with the Visual C++ desktop tools was not found.'
}

function Build-NativeLauncherCore {
    if (-not (Test-Path -LiteralPath (Join-Path $nativeCorePath '.git'))) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $nativeCorePath) | Out-Null
        Invoke-Git -Arguments @('clone', '--depth', '1', $nativeCoreRepository, $nativeCorePath)
    }
    else {
        Invoke-Git -Arguments @('-C', $nativeCorePath, 'pull', '--ff-only')
    }

    $msBuildPath = Get-MSBuildPath
    foreach ($nativeProject in @('Launcher\Launcher.vcxproj', 'Launcher_2\Launcher_2.vcxproj')) {
        & $msBuildPath (Join-Path $nativeCorePath $nativeProject) /m /nologo /verbosity:minimal `
            /p:Configuration=Release /p:Platform=x64 /p:PlatformToolset=v143
        if ($LASTEXITCODE -ne 0) { throw "Native launcher core build failed: $nativeProject" }
    }

    Copy-Item -LiteralPath (Join-Path $nativeCorePath 'Launcher\x64\Release\Launcher.dll') `
        -Destination (Join-Path $outputDirectory 'Launcher.dll') -Force
    Copy-Item -LiteralPath (Join-Path $nativeCorePath 'Launcher_2\x64\Release\Launcher_2.exe') `
        -Destination (Join-Path $outputDirectory 'Launcher_2.exe') -Force
}

function Resolve-BuildOutput {
    $builtExecutable = Get-ChildItem -LiteralPath $buildRoot -Recurse -File -Filter 'FufuLauncher.exe' |
        Where-Object { $_.DirectoryName -like '*\win-x64' } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if (-not $builtExecutable) {
        throw "No win-x64 FufuLauncher.exe was found below $buildRoot"
    }

    $script:executablePath = $builtExecutable.FullName
    $script:outputDirectory = $builtExecutable.DirectoryName
}

function Update-DesktopShortcut {
    if (-not (Test-Path -LiteralPath $executablePath)) {
        throw "The compiled executable was not found: $executablePath"
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $executablePath
    $shortcut.WorkingDirectory = $outputDirectory
    $shortcut.IconLocation = "$executablePath,0"
    $shortcut.Save()
}

Set-Location -LiteralPath $repoRoot

try {
    Write-Step 'Checking the additive-only customization branch'
    if ((git branch --show-current) -ne 'additive-custom') {
        throw 'Please switch to the additive-custom branch before updating.'
    }
    $pendingChanges = @(git status --porcelain | Where-Object { $_ -notmatch '^\?\? \.local-dependencies/' })
    if ($pendingChanges.Count -gt 0) {
        throw 'There are uncommitted local changes. Commit them before updating.'
    }

    Write-Step 'Stopping the locally built FufuLauncher'
    Get-Process -Name FufuLauncher -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-Process -Name FufuLauncher -ErrorAction SilentlyContinue | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue

    Write-Step 'Merging the latest official repository without modifying official files'
    Invoke-Git -Arguments @('fetch', 'upstream', 'master')
    & git merge --no-edit upstream/master
    if ($LASTEXITCODE -ne 0) {
        & git merge --abort
        throw 'The official update conflicts with an additive file. The merge was cancelled safely.'
    }

    Write-Step 'Building the x64 customized version'
    & dotnet build $projectPath -c Debug '-p:Platform=x64' '-p:RuntimeIdentifier=win-x64' `
        '-p:WarningLevel=0' --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    Resolve-BuildOutput

    Write-Step 'Building the native launcher core'
    Build-NativeLauncherCore

    Write-Step 'Updating the desktop shortcut'
    Update-DesktopShortcut

    Write-Step 'Uploading additive-custom to your fork'
    Invoke-Git -Arguments @('push', 'origin', 'additive-custom')

    if (-not $NoLaunch) {
        Write-Step 'Starting FufuLauncher'
        Start-Process -FilePath $executablePath -WorkingDirectory $outputDirectory
    }

    Write-Host "`nThe additive-only custom version is up to date." -ForegroundColor Green
}
catch {
    Write-Host "`nUpdate stopped: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
