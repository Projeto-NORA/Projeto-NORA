param(
    [ValidateSet('OBS-NORA', 'Painel-Municipal')]
    [string[]] $Applications = @('OBS-NORA', 'Painel-Municipal'),
    [ValidateRange(1, 60)]
    [int] $TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$failures = 0
foreach ($application in $Applications) {
    $executable = Join-Path $projectRoot ".build\$application\bin\Release\net10.0-windows\$application.exe"
    if (-not (Test-Path -LiteralPath $executable)) {
        throw "Compile $application em Release antes de executar este teste."
    }
    $process = Start-Process -FilePath $executable -PassThru
    $expectedTitle = if ($application -eq 'OBS-NORA') { 'NORA OBS' } else { 'NORA Municipal' }
    try {
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        do {
            Start-Sleep -Milliseconds 250
            $process.Refresh()
        } while (-not $process.HasExited -and ($process.MainWindowHandle -eq 0 -or $process.MainWindowTitle -ne $expectedTitle) -and (Get-Date) -lt $deadline)

        if ($process.HasExited) {
            throw "O executável encerrou antes de abrir a janela: ExitCode=$($process.ExitCode)."
        }
        if ($process.MainWindowHandle -eq 0 -or $process.MainWindowTitle -ne $expectedTitle -or -not $process.Responding) {
            throw 'A janela principal não ficou disponível no prazo do teste.'
        }
        Write-Output "PASS $application abriu janela real: $($process.MainWindowTitle)"
    }
    catch {
        $failures++
        Write-Output "FAIL ${application}: $($_.Exception.Message)"
    }
    finally {
        # Encerra somente o processo criado por este teste, sem fechar apps já abertos.
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}
if ($failures -gt 0) { exit 1 }
exit 0
