#Requires -Version 7.0
param(
    [string] $SourceHtml = 'docs/galeria/2026-10-10/index.html',
    [string] $CaptureDirectory = '.build/gallery-updated-2026-10-13/desktop',
    [string] $OutputDirectory = 'docs/galeria/2026-10-13-atualizada'
)
$ErrorActionPreference = 'Stop'
$noraRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($SourceHtml)) { $SourceHtml = Join-Path $noraRoot $SourceHtml }
$noraHistorical = Join-Path $noraRoot 'docs/galeria/2026-10-10'
$noraCaptureRoot = [IO.Path]::GetFullPath((Join-Path $noraRoot $CaptureDirectory))
$noraOutput = [IO.Path]::GetFullPath((Join-Path $noraRoot $OutputDirectory))
$noraComparison = [StringComparison]::OrdinalIgnoreCase
if (-not $noraCaptureRoot.StartsWith($noraRoot + [IO.Path]::DirectorySeparatorChar, $noraComparison)) {
    throw 'As capturas devem estar dentro do projeto.'
}
if (-not $noraOutput.StartsWith($noraRoot + [IO.Path]::DirectorySeparatorChar, $noraComparison) -or
    $noraOutput -eq $noraHistorical -or $noraOutput.StartsWith($noraHistorical + [IO.Path]::DirectorySeparatorChar, $noraComparison) -or
    $noraOutput -eq $noraCaptureRoot -or $noraOutput.StartsWith($noraCaptureRoot + [IO.Path]::DirectorySeparatorChar, $noraComparison) -or
    $noraOutput -eq [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($SourceHtml))) {
    throw 'Use destino dentro do projeto, diferente da galeria histórica.'
}
$noraHtml = [IO.File]::ReadAllText($SourceHtml)
$noraPattern = '(?<start><script id="data" type="application/json">)(?<json>[\s\S]*?)(?<end></script>)'
$noraMatch = [regex]::Match($noraHtml, $noraPattern)
if (-not $noraMatch.Success) { throw 'Manifesto embutido da galeria não encontrado.' }
$noraManifest = $noraMatch.Groups['json'].Value | ConvertFrom-Json
$noraCurrent = Get-Content -LiteralPath (Join-Path $noraCaptureRoot 'visual-captures.json') -Raw | ConvertFrom-Json
if ($noraManifest.captures.Count -ne 83 -or $noraCurrent.captures.Count -ne 68) { throw 'Quantidade inesperada de capturas; nenhuma imagem copiada.' }
if (@($noraManifest.captures.file | Select-Object -Unique).Count -ne 83) { throw 'Arquivo de captura duplicado.' }
$noraCopies = foreach ($noraCapture in $noraManifest.captures) {
    if ($noraCapture.file -notmatch '^(desktop|android)/[A-Za-z0-9_.-]+\.png$') { throw 'Caminho de captura inválido.' }
    $noraOldPath = Join-Path $noraHistorical $noraCapture.file
    if ((Get-FileHash -LiteralPath $noraOldPath -Algorithm SHA256).Hash -ne $noraCapture.sha256) { throw "Original divergente: $($noraCapture.file)" }
    $noraSource = $noraOldPath
    if ($noraCapture.file.StartsWith('desktop/')) {
        $noraName = [IO.Path]::GetFileName($noraCapture.file)
        $noraMatches = @($noraCurrent.captures | Where-Object File -EQ $noraName)
        if ($noraMatches.Count -ne 1) { throw "Captura atual ausente ou ambígua: $noraName" }
        $noraSource = Join-Path $noraCaptureRoot $noraName
        $noraCapture.width = $noraMatches[0].Width
        $noraCapture.height = $noraMatches[0].Height
        $noraCapture.dataSource = $noraMatches[0].DataSource
        $noraCapture.method = $noraCurrent.captureMethod
    }
    $noraBytes = [IO.File]::ReadAllBytes($noraSource)
    if ($noraBytes.Length -lt 24 -or [BitConverter]::ToString($noraBytes,0,8) -ne '89-50-4E-47-0D-0A-1A-0A') { throw 'Arquivo não é PNG.' }
    $noraWidth = $noraBytes[16]*16777216 + $noraBytes[17]*65536 + $noraBytes[18]*256 + $noraBytes[19]
    $noraHeight = $noraBytes[20]*16777216 + $noraBytes[21]*65536 + $noraBytes[22]*256 + $noraBytes[23]
    if ($noraWidth -ne $noraCapture.width -or $noraHeight -ne $noraCapture.height) { throw "Dimensões divergentes: $($noraCapture.file)" }
    $noraCapture.sha256 = (Get-FileHash -LiteralPath $noraSource -Algorithm SHA256).Hash
    [pscustomobject]@{ Source=$noraSource; File=$noraCapture.file; Hash=$noraCapture.sha256 }
}
# Validate every source before copying; HTML/CSS/JS stay those of the supplied site.
New-Item -ItemType Directory -Path $noraOutput -Force | Out-Null
foreach ($noraCopy in $noraCopies) {
    $noraDestination = Join-Path $noraOutput $noraCopy.File
    New-Item -ItemType Directory -Path (Split-Path -Parent $noraDestination) -Force | Out-Null
    Copy-Item -LiteralPath $noraCopy.Source -Destination $noraDestination -Force
    if ((Get-FileHash -LiteralPath $noraDestination -Algorithm SHA256).Hash -ne $noraCopy.Hash) { throw 'Cópia divergente.' }
}
$noraManifest.date = '2026-10-13'
$noraJson = ConvertTo-Json -InputObject $noraManifest -Depth 8
$noraSafeJson = $noraJson.Replace('<','\u003c').Replace('>','\u003e').Replace('&','\u0026')
$noraUpdated = [regex]::Replace($noraHtml, $noraPattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $match.Groups['start'].Value + $noraSafeJson + $match.Groups['end'].Value })
$noraUpdated = $noraUpdated.Replace('REVISÃO VISUAL · 10 OUT 2026','REVISÃO VISUAL · ATUALIZADA EM 13 OUT 2026')
$noraUpdated = $noraUpdated.Replace('Capturas da interface em 10/10/2026','Capturas Windows atualizadas em 13/10/2026 e Android preservadas de 10/10/2026')
$noraUpdated = $noraUpdated.Replace('Este conjunto preserva o estado de 10/10/2026; ajustes posteriores estão documentados no projeto.','As fotos Windows refletem as alterações até 13/10/2026; as fotos Android permanecem as de 10/10/2026.')
[IO.File]::WriteAllText((Join-Path $noraOutput 'index.html'),$noraUpdated)
[IO.File]::WriteAllText((Join-Path $noraOutput 'manifest.json'),$noraJson)
Write-Output "Galeria atualizada: 68 fotos Windows; 15 Android preservadas; 83 itens na ordem original. $noraOutput"
