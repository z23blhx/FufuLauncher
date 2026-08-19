param(
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$OutputEncoding = [System.Text.UTF8Encoding]::new()
$env:GIT_MERGE_AUTOEDIT = 'no'

$repoRoot = $PSScriptRoot
$projectPath = Join-Path $repoRoot 'FufuLauncher\FufuLauncher.csproj'
$executablePath = Join-Path $repoRoot 'FufuLauncher\bin\x64\Debug\net8.0-windows10.0.26100.0\FufuLauncher.exe'
$buildOutputPath = Split-Path -Parent $executablePath
$nativeCorePath = Join-Path $repoRoot '.local-dependencies\FufuLauncher.UnlockerIsland'
$nativeCoreRepository = 'https://github.com/FufuLauncher/FufuLauncher.UnlockerIsland.git'

function Invoke-Git {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Git command failed: git $($Arguments -join ' ')"
    }
}

function Write-Step {
    param([string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Get-MSBuildPath {
    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vsWhere) {
        $found = & $vsWhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
            Select-Object -First 1
        if ($found) {
            return $found
        }
    }

    throw 'MSBuild with the Visual C++ desktop tools was not found. Install the Desktop development with C++ workload in Visual Studio 2022.'
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
    $nativeProjects = @(
        'Launcher\Launcher.vcxproj',
        'Launcher_2\Launcher_2.vcxproj'
    )

    foreach ($nativeProject in $nativeProjects) {
        & $msBuildPath (Join-Path $nativeCorePath $nativeProject) /m /nologo /verbosity:minimal `
            /p:Configuration=Release /p:Platform=x64 /p:PlatformToolset=v143
        if ($LASTEXITCODE -ne 0) {
            throw "Native launcher core build failed: $nativeProject"
        }
    }

    $launcherDll = Join-Path $nativeCorePath 'Launcher\x64\Release\Launcher.dll'
    $launcherExe = Join-Path $nativeCorePath 'Launcher_2\x64\Release\Launcher_2.exe'
    if (-not (Test-Path -LiteralPath $launcherDll) -or -not (Test-Path -LiteralPath $launcherExe)) {
        throw 'The native launcher core build completed without producing Launcher.dll and Launcher_2.exe.'
    }

    Copy-Item -LiteralPath $launcherDll -Destination (Join-Path $buildOutputPath 'Launcher.dll') -Force
    Copy-Item -LiteralPath $launcherExe -Destination (Join-Path $buildOutputPath 'Launcher_2.exe') -Force
}

Set-Location -LiteralPath $repoRoot

try {
    Write-Step 'Checking repository'

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'Git was not found.'
    }

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '.NET SDK was not found.'
    }

    $pendingChanges = @(git status --porcelain)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to read the Git working tree.'
    }
    if ($pendingChanges.Count -gt 0) {
        throw 'There are uncommitted local changes. Commit them before updating.'
    }

    Write-Step 'Stopping the locally built FufuLauncher'
    $runningLaunchers = @(Get-Process -Name 'FufuLauncher' -ErrorAction SilentlyContinue)
    if ($runningLaunchers.Count -gt 0) {
        try {
            $runningLaunchers | Stop-Process -Force -ErrorAction Stop
            $runningLaunchers | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
        }
        catch {
            throw 'Unable to stop FufuLauncher. Run the updater as administrator or exit the app from its tray icon.'
        }
    }

    if (Get-Process -Name 'FufuLauncher' -ErrorAction SilentlyContinue) {
        throw 'FufuLauncher is still running and would lock the build output.'
    }

    Write-Step 'Downloading updates from the official repository'
    Invoke-Git -Arguments @('fetch', 'upstream')

    Write-Step 'Updating the clean master branch'
    Invoke-Git -Arguments @('switch', 'master')
    Invoke-Git -Arguments @('pull', '--ff-only', 'origin', 'master')
    Invoke-Git -Arguments @('merge', '--ff-only', 'upstream/master')
    Invoke-Git -Arguments @('push', 'origin', 'master')

    Write-Step 'Merging the official update into local-custom'
    Invoke-Git -Arguments @('switch', 'local-custom')
    Invoke-Git -Arguments @('pull', '--ff-only', 'origin', 'local-custom')

    & git merge --no-edit master
    if ($LASTEXITCODE -ne 0) {
        & git merge --abort
        throw 'The official update conflicts with your customization. The merge was cancelled safely; send the conflict output to Codex.'
    }

    Write-Step 'Building the customized x64 Debug version'
    & dotnet build $projectPath -c Debug '-p:Platform=x64' '-p:WarningLevel=0' --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw 'Build failed.'
    }

    Write-Step 'Building the native launcher core'
    Build-NativeLauncherCore

    Write-Step 'Uploading the updated customization to your Fork'
    Invoke-Git -Arguments @('push', 'origin', 'local-custom')

    if (-not $NoLaunch) {
        if (-not (Test-Path -LiteralPath $executablePath)) {
            throw "The built executable was not found: $executablePath"
        }

        Write-Step 'Starting FufuLauncher'
        Start-Process -FilePath $executablePath
    }

    Write-Host "`nFufuLauncher custom version is up to date." -ForegroundColor Green
}
catch {
    Write-Host "`nUpdate stopped: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
