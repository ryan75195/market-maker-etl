#Requires -Version 7.0
<#
.SYNOPSIS
    Starts, stops, or reports the status of the local MarketMakerEtl stack
    (classifier sidecar, Api, Etl, and an optional scraper start script).

.DESCRIPTION
    Configuration is read from .local/run-config.json (gitignored). Copy
    scripts/run-config.example.json to .local/run-config.json and fill in
    machine-specific paths before running -Start.

.PARAMETER Start
    Builds the Api and Etl projects, then starts the classifier, Api and Etl
    processes (and the configured scraper start script, if any) hidden, with
    logs under .local/logs and PID records under .local/pids.

.PARAMETER Stop
    Stops every process recorded under .local/pids, killing each process
    tree and verifying nothing matching the recorded command line is left
    running.

.PARAMETER Status
    Reports each recorded process's PID, whether it is alive, and the
    classifier/Api health responses. This is the default action.

.PARAMETER ConfigPath
    Overrides the default .local/run-config.json location.

.EXAMPLE
    ./scripts/run-local.ps1 -Start

.EXAMPLE
    ./scripts/run-local.ps1 -Status

.EXAMPLE
    ./scripts/run-local.ps1 -Stop
#>
[CmdletBinding()]
param(
    [switch]$Start,
    [switch]$Stop,
    [switch]$Status,
    [string]$ConfigPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:LocalRoot = Join-Path $script:RepoRoot '.local'
$script:LogDirectory = Join-Path $script:LocalRoot 'logs'
$script:PidDirectory = Join-Path $script:LocalRoot 'pids'
$script:DefaultConfigPath = Join-Path $script:LocalRoot 'run-config.json'

$script:ProcessNames = @('classifier', 'api', 'etl', 'scraper')

function Resolve-ConfigPath
{
    if ($ConfigPath)
    {
        return (Resolve-Path -Path $ConfigPath).Path
    }

    return $script:DefaultConfigPath
}

function Get-RunConfig
{
    $path = Resolve-ConfigPath
    if (-not (Test-Path -Path $path))
    {
        throw "No run config found at '$path'. Copy scripts/run-config.example.json there and fill in machine-specific paths."
    }

    $raw = Get-Content -Path $path -Raw | ConvertFrom-Json

    $required = @('databasePath', 'modelsDir', 'pythonExe', 'apiPort', 'classifierPort', 'scraperBaseUrl')
    $missing = $required | Where-Object { -not (Get-Member -InputObject $raw -Name $_ -MemberType NoteProperty) }
    if ($missing)
    {
        throw "Run config '$path' is missing required field(s): $($missing -join ', ')"
    }

    $preload = @()
    if (Get-Member -InputObject $raw -Name 'classifierPreload' -MemberType NoteProperty)
    {
        $preload = @($raw.classifierPreload)
    }

    $extraEnv = @{}
    if (Get-Member -InputObject $raw -Name 'extraEnv' -MemberType NoteProperty)
    {
        $raw.extraEnv.PSObject.Properties | ForEach-Object { $extraEnv[$_.Name] = [string]$_.Value }
    }

    $scraperStartScript = $null
    if ((Get-Member -InputObject $raw -Name 'scraperStartScript' -MemberType NoteProperty) -and $raw.scraperStartScript)
    {
        $scraperStartScript = $raw.scraperStartScript
    }

    return [pscustomobject]@{
        DatabasePath        = $raw.databasePath
        ModelsDir           = $raw.modelsDir
        PythonExe           = $raw.pythonExe
        ApiPort             = [int]$raw.apiPort
        ClassifierPort      = [int]$raw.classifierPort
        ScraperBaseUrl      = $raw.scraperBaseUrl
        ScraperStartScript  = $scraperStartScript
        ClassifierPreload   = $preload
        ExtraEnv            = $extraEnv
    }
}

function Initialize-LocalDirectory
{
    foreach ($directory in @($script:LocalRoot, $script:LogDirectory, $script:PidDirectory))
    {
        if (-not (Test-Path -Path $directory))
        {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }
    }
}

function Resolve-RepoPath
{
    param([Parameter(Mandatory)][string]$RelativeOrAbsolutePath)

    if ([System.IO.Path]::IsPathRooted($RelativeOrAbsolutePath))
    {
        return $RelativeOrAbsolutePath
    }

    return (Join-Path $script:RepoRoot $RelativeOrAbsolutePath)
}

function Get-PidRecordPath
{
    param([Parameter(Mandatory)][string]$Name)

    return Join-Path $script:PidDirectory "$Name.json"
}

function Save-PidRecord
{
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][int]$ProcessId,
        [string]$CommandLine,
        [string]$LogPath,
        [string]$ErrorLogPath
    )

    $record = [ordered]@{
        name         = $Name
        processId    = $ProcessId
        commandLine  = $CommandLine
        startedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        logPath      = $LogPath
        errorLogPath = $ErrorLogPath
    }

    $record | ConvertTo-Json | Set-Content -Path (Get-PidRecordPath -Name $Name) -Encoding utf8
    return [pscustomobject]$record
}

function Get-PidRecord
{
    param([Parameter(Mandatory)][string]$Name)

    $path = Get-PidRecordPath -Name $Name
    if (-not (Test-Path -Path $path))
    {
        return $null
    }

    return Get-Content -Path $path -Raw | ConvertFrom-Json
}

function Remove-PidRecord
{
    [CmdletBinding(SupportsShouldProcess)]
    param([Parameter(Mandatory)][string]$Name)

    $path = Get-PidRecordPath -Name $Name
    if ((Test-Path -Path $path) -and $PSCmdlet.ShouldProcess($path, 'Remove PID record'))
    {
        Remove-Item -Path $path -Force
    }
}

function Get-LiveCommandLine
{
    param([Parameter(Mandatory)][int]$ProcessId)

    $process = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId=$ProcessId" -ErrorAction SilentlyContinue
    if (-not $process)
    {
        return $null
    }

    return $process.CommandLine
}

function Test-RecordedProcessAlive
{
    param([Parameter(Mandatory)]$Record)

    $liveCommandLine = Get-LiveCommandLine -ProcessId $Record.processId
    if (-not $liveCommandLine)
    {
        return $false
    }

    if ($Record.commandLine -and ($liveCommandLine -ne $Record.commandLine))
    {
        return $false
    }

    return $true
}

function Get-DescendantProcessId
{
    param([Parameter(Mandatory)][int]$ProcessId)

    $descendants = [System.Collections.Generic.List[int]]::new()
    $frontier = [System.Collections.Generic.Queue[int]]::new()
    $frontier.Enqueue($ProcessId)

    while ($frontier.Count -gt 0)
    {
        $current = $frontier.Dequeue()
        $children = Get-CimInstance -ClassName Win32_Process -Filter "ParentProcessId=$current" -ErrorAction SilentlyContinue
        foreach ($child in $children)
        {
            $descendants.Add([int]$child.ProcessId)
            $frontier.Enqueue([int]$child.ProcessId)
        }
    }

    return $descendants
}

function Stop-RecordedProcess
{
    [CmdletBinding(SupportsShouldProcess)]
    param([Parameter(Mandatory)][string]$Name)

    $record = Get-PidRecord -Name $Name
    if (-not $record)
    {
        Write-Host "  $Name : no PID record, nothing to stop"
        return
    }

    if (-not (Test-RecordedProcessAlive -Record $record))
    {
        Write-Host "  $Name : not running (stale PID record removed)"
        Remove-PidRecord -Name $Name
        return
    }

    $processId = [int]$record.processId
    $treeIds = @($processId) + (Get-DescendantProcessId -ProcessId $processId)

    if ($PSCmdlet.ShouldProcess("$Name (PID $processId and $($treeIds.Count - 1) descendant(s))", 'Stop process tree'))
    {
        foreach ($id in $treeIds)
        {
            Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
        }
    }

    Start-Sleep -Milliseconds 500

    $stillAlive = $treeIds | Where-Object { Get-CimInstance -ClassName Win32_Process -Filter "ProcessId=$_" -ErrorAction SilentlyContinue }
    if ($stillAlive)
    {
        Write-Host "  $Name : FAILED to stop PID(s) $($stillAlive -join ', ')" -ForegroundColor Red
    }
    else
    {
        Write-Host "  $Name : stopped (PID $processId, $($treeIds.Count) process(es) in tree)"
    }

    Remove-PidRecord -Name $Name
}

function Get-HttpProbe
{
    param([Parameter(Mandatory)][string]$Url)

    try
    {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
        return [pscustomobject]@{ Reachable = $true; Success = $true; StatusCode = [int]$response.StatusCode; Body = $response.Content }
    }
    catch
    {
        $httpResponse = $null
        if ($_.Exception.PSObject.Properties['Response'])
        {
            $httpResponse = $_.Exception.Response
        }

        if ($httpResponse)
        {
            return [pscustomobject]@{ Reachable = $true; Success = $false; StatusCode = [int]$httpResponse.StatusCode; Body = $null }
        }

        return [pscustomobject]@{ Reachable = $false; Success = $false; StatusCode = $null; Body = $null }
    }
}

function Wait-ClassifierHealthy
{
    param(
        [Parameter(Mandatory)][string]$BaseUrl,
        [int]$TimeoutSeconds = 60
    )

    $url = "$BaseUrl/health"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $probe = $null

    while ((Get-Date) -lt $deadline)
    {
        $probe = Get-HttpProbe -Url $url
        if ($probe.Success)
        {
            return [pscustomobject]@{ Healthy = $true; Endpoint = $url; StatusCode = $probe.StatusCode }
        }

        Start-Sleep -Milliseconds 500
    }

    return [pscustomobject]@{ Healthy = $false; Endpoint = $url; StatusCode = $probe.StatusCode }
}

function Wait-ApiHealthy
{
    param(
        [Parameter(Mandatory)][string]$BaseUrl,
        [int]$TimeoutSeconds = 60
    )

    $primary = "$BaseUrl/api/health"
    $fallback = "$BaseUrl/api/jobs"
    $endpoint = $primary
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $probe = $null

    while ((Get-Date) -lt $deadline)
    {
        $probe = Get-HttpProbe -Url $endpoint
        if ($probe.Success)
        {
            return [pscustomobject]@{ Healthy = $true; Endpoint = $endpoint; StatusCode = $probe.StatusCode }
        }

        if ($probe.Reachable -and $endpoint -eq $primary -and $probe.StatusCode -eq 404)
        {
            $endpoint = $fallback
            continue
        }

        Start-Sleep -Milliseconds 500
    }

    return [pscustomobject]@{ Healthy = $false; Endpoint = $endpoint; StatusCode = $probe.StatusCode }
}

function Invoke-ProjectBuild
{
    param([Parameter(Mandatory)][string]$ProjectPath)

    Write-Host "Building $ProjectPath ..."
    & dotnet build $ProjectPath -c Debug --verbosity quiet
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet build failed for $ProjectPath (exit code $LASTEXITCODE)"
    }
}

function Get-BuiltAssemblyPath
{
    param(
        [Parameter(Mandatory)][string]$ProjectDirectory,
        [Parameter(Mandatory)][string]$AssemblyName
    )

    $binDebug = Join-Path $ProjectDirectory 'bin/Debug'
    $match = Get-ChildItem -Path $binDebug -Filter "$AssemblyName.dll" -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if (-not $match)
    {
        throw "Could not find built assembly '$AssemblyName.dll' under $binDebug"
    }

    return $match.FullName
}

function Start-TrackedProcess
{
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [hashtable]$EnvironmentVariables = @{}
    )

    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $logPath = Join-Path $script:LogDirectory "$Name-$timestamp.log"
    $errorLogPath = Join-Path $script:LogDirectory "$Name-$timestamp.err.log"

    $previousValues = @{}
    foreach ($key in $EnvironmentVariables.Keys)
    {
        $previousValues[$key] = [System.Environment]::GetEnvironmentVariable($key)
        [System.Environment]::SetEnvironmentVariable($key, [string]$EnvironmentVariables[$key])
    }

    $process = $null
    try
    {
        if ($PSCmdlet.ShouldProcess($Name, "Start process: $FilePath $($ArgumentList -join ' ')"))
        {
            $startArgs = @{
                FilePath               = $FilePath
                WorkingDirectory       = $WorkingDirectory
                WindowStyle            = 'Hidden'
                PassThru               = $true
                RedirectStandardOutput = $logPath
                RedirectStandardError  = $errorLogPath
            }
            if ($ArgumentList.Count -gt 0)
            {
                $startArgs.ArgumentList = $ArgumentList
            }

            $process = Start-Process @startArgs
        }
    }
    finally
    {
        foreach ($key in $previousValues.Keys)
        {
            [System.Environment]::SetEnvironmentVariable($key, $previousValues[$key])
        }
    }

    if (-not $process)
    {
        throw "Failed to start process '$Name'."
    }

    Start-Sleep -Milliseconds 300
    $commandLine = Get-LiveCommandLine -ProcessId $process.Id

    Save-PidRecord -Name $Name -ProcessId $process.Id -CommandLine $commandLine -LogPath $logPath -ErrorLogPath $errorLogPath | Out-Null

    return [pscustomobject]@{ ProcessId = $process.Id; LogPath = $logPath; ErrorLogPath = $errorLogPath }
}

function Merge-EnvironmentVariable
{
    param(
        [Parameter(Mandatory)][hashtable]$Base,
        [Parameter(Mandatory)][hashtable]$Overrides
    )

    $merged = @{}
    foreach ($key in $Base.Keys)
    {
        $merged[$key] = $Base[$key]
    }
    foreach ($key in $Overrides.Keys)
    {
        $merged[$key] = $Overrides[$key]
    }
    return $merged
}

function Invoke-Start
{
    $config = Get-RunConfig
    Initialize-LocalDirectory

    $alreadyRunning = $script:ProcessNames | Where-Object {
        $record = Get-PidRecord -Name $_
        $record -and (Test-RecordedProcessAlive -Record $record)
    }
    if ($alreadyRunning)
    {
        throw "Already running: $($alreadyRunning -join ', '). Run -Stop first."
    }

    $databasePath = Resolve-RepoPath -RelativeOrAbsolutePath $config.DatabasePath
    $databaseDirectory = Split-Path -Parent $databasePath
    if ($databaseDirectory -and -not (Test-Path -Path $databaseDirectory))
    {
        New-Item -ItemType Directory -Path $databaseDirectory -Force | Out-Null
    }

    $classifierBaseUrl = "http://127.0.0.1:$($config.ClassifierPort)"
    $apiBaseUrl = "http://127.0.0.1:$($config.ApiPort)"

    if ($config.ScraperStartScript)
    {
        Write-Host 'Starting scraper via configured start script ...'
        $scraperPath = Resolve-RepoPath -RelativeOrAbsolutePath $config.ScraperStartScript
        Start-TrackedProcess -Name 'scraper' -FilePath 'pwsh' `
            -ArgumentList @('-NoProfile', '-File', $scraperPath) `
            -WorkingDirectory (Split-Path -Parent $scraperPath) `
            -EnvironmentVariables $config.ExtraEnv | Out-Null
    }

    Invoke-ProjectBuild -ProjectPath (Join-Path $script:RepoRoot 'src/MarketMakerEtl.Api/MarketMakerEtl.Api.csproj')
    Invoke-ProjectBuild -ProjectPath (Join-Path $script:RepoRoot 'src/MarketMakerEtl.Etl/MarketMakerEtl.Etl.csproj')

    Write-Host 'Starting classifier ...'
    $classifierEnv = Merge-EnvironmentVariable -Base @{
        MODELS_DIR         = $config.ModelsDir
        CLASSIFIER_PORT    = "$($config.ClassifierPort)"
        CLASSIFIER_HOST    = '127.0.0.1'
        CLASSIFIER_PRELOAD = ($config.ClassifierPreload -join ',')
    } -Overrides $config.ExtraEnv
    Start-TrackedProcess -Name 'classifier' -FilePath $config.PythonExe `
        -ArgumentList @('-m', 'mmclassifier') `
        -WorkingDirectory (Join-Path $script:RepoRoot 'classifier') `
        -EnvironmentVariables $classifierEnv | Out-Null

    $sharedEnv = @{
        'Database__ConnectionString' = "Data Source=$databasePath"
        'Classifier__BaseUrl'        = $classifierBaseUrl
        'Scraper__BaseUrl'           = $config.ScraperBaseUrl
    }

    Write-Host 'Starting Api ...'
    $apiDirectory = Join-Path $script:RepoRoot 'src/MarketMakerEtl.Api'
    $apiAssembly = Get-BuiltAssemblyPath -ProjectDirectory $apiDirectory -AssemblyName 'MarketMakerEtl.Api'
    $apiEnv = Merge-EnvironmentVariable -Base $sharedEnv -Overrides $config.ExtraEnv
    Start-TrackedProcess -Name 'api' -FilePath 'dotnet' `
        -ArgumentList @($apiAssembly, '--urls', $apiBaseUrl) `
        -WorkingDirectory $apiDirectory `
        -EnvironmentVariables $apiEnv | Out-Null

    Write-Host 'Starting Etl ...'
    $etlDirectory = Join-Path $script:RepoRoot 'src/MarketMakerEtl.Etl'
    $etlAssembly = Get-BuiltAssemblyPath -ProjectDirectory $etlDirectory -AssemblyName 'MarketMakerEtl.Etl'
    $etlEnv = Merge-EnvironmentVariable -Base $sharedEnv -Overrides $config.ExtraEnv
    Start-TrackedProcess -Name 'etl' -FilePath 'dotnet' `
        -ArgumentList @($etlAssembly) `
        -WorkingDirectory $etlDirectory `
        -EnvironmentVariables $etlEnv | Out-Null

    Write-Host 'Waiting for classifier health ...'
    $classifierHealth = Wait-ClassifierHealthy -BaseUrl $classifierBaseUrl -TimeoutSeconds 60

    Write-Host 'Waiting for Api health ...'
    $apiHealth = Wait-ApiHealthy -BaseUrl $apiBaseUrl -TimeoutSeconds 60

    Show-Status

    if (-not $classifierHealth.Healthy -or -not $apiHealth.Healthy)
    {
        Write-Host ''
        Write-Host 'One or more health checks did not succeed within the timeout; processes are left running for inspection.' -ForegroundColor Yellow
        exit 1
    }
}

function Show-Status
{
    $config = $null
    try
    {
        $config = Get-RunConfig
    }
    catch
    {
        Write-Host $_.Exception.Message -ForegroundColor Yellow
    }

    $rows = foreach ($name in $script:ProcessNames)
    {
        $record = Get-PidRecord -Name $name
        if (-not $record)
        {
            [pscustomobject]@{ Name = $name; ProcessId = ''; Alive = $false; Health = 'no record' }
            continue
        }

        $alive = Test-RecordedProcessAlive -Record $record
        $health = 'n/a'

        if ($alive -and $config)
        {
            if ($name -eq 'classifier')
            {
                $probe = Get-HttpProbe -Url "http://127.0.0.1:$($config.ClassifierPort)/health"
                $health = if ($probe.Success) { "healthy ($($probe.StatusCode))" } elseif ($probe.Reachable) { "unhealthy ($($probe.StatusCode))" } else { 'unreachable' }
            }
            elseif ($name -eq 'api')
            {
                $baseUrl = "http://127.0.0.1:$($config.ApiPort)"
                $probe = Get-HttpProbe -Url "$baseUrl/api/health"
                if (-not $probe.Reachable)
                {
                    $health = 'unreachable'
                }
                elseif ($probe.StatusCode -eq 404)
                {
                    $probe = Get-HttpProbe -Url "$baseUrl/api/jobs"
                    $health = if ($probe.Success) { "healthy via /api/jobs ($($probe.StatusCode))" } else { "unhealthy ($($probe.StatusCode))" }
                }
                else
                {
                    $health = if ($probe.Success) { "healthy ($($probe.StatusCode))" } else { "unhealthy ($($probe.StatusCode))" }
                }
            }
        }
        elseif (-not $alive)
        {
            $health = 'not running'
        }

        [pscustomobject]@{ Name = $name; ProcessId = $record.processId; Alive = $alive; Health = $health }
    }

    $rows | Format-Table -Property Name, ProcessId, Alive, Health -AutoSize | Out-String | Write-Host
}

function Invoke-Stop
{
    Write-Host 'Stopping stack ...'
    foreach ($name in $script:ProcessNames)
    {
        Stop-RecordedProcess -Name $name
    }
}

$actionsRequested = @($Start, $Stop, $Status | Where-Object { $_ })
if ($actionsRequested.Count -gt 1)
{
    throw 'Specify only one of -Start, -Stop, or -Status.'
}

if ($Start)
{
    Invoke-Start
}
elseif ($Stop)
{
    Invoke-Stop
}
else
{
    Show-Status
}
