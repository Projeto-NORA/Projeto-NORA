#Requires -Version 7.0
param([string] $OutputDirectory = '.build/galeria-visual-2026-10-10/android')

$ErrorActionPreference = 'Stop'
$noraRoot = Split-Path -Parent $PSScriptRoot
$noraSource = Join-Path $noraRoot 'APPNORA/app/src/screenshotTestDebug/reference/com/example/app_nora/NoraVisualPreviewsKt'
$noraBuild = [IO.Path]::GetFullPath((Join-Path $noraRoot '.build'))
$noraOutput = [IO.Path]::GetFullPath((Join-Path $noraRoot $OutputDirectory))
if (-not $noraOutput.StartsWith($noraBuild + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'O destino das capturas deve ficar dentro de .build/ deste repositório.'
}
if (-not (Test-Path -LiteralPath $noraSource -PathType Container)) {
    throw 'Referências Layoutlib ausentes. Execute :app:updateDebugScreenshotTest e :app:validateDebugScreenshotTest antes de exportar.'
}

# Each prefix includes the composable AND current Preview name. An obsolete preview
# with the same composable does not match; multiple hashes for this preview fail.
$noraMappings = @(
    @{ Preview = 'HomeDisconnectedLight_01 Home desconectada completa clara'; File = '01-inicio-desconectado-completo-claro.png'; Screen = 'Início desconectado e serviços'; Appearance = 'light'; Position = 'Conteúdo completo em canvas ampliado'; Fixture = $false },
    @{ Preview = 'LoginLight_02 Entrada clara'; File = '02-login-claro.png'; Screen = 'Entrada da conta'; Appearance = 'light'; Position = 'Topo'; Fixture = $false },
    @{ Preview = 'LoginDark_03 Entrada escura'; File = '03-login-escuro.png'; Screen = 'Entrada da conta'; Appearance = 'dark'; Position = 'Topo'; Fixture = $false },
    @{ Preview = 'LoginErrorLight_04 Erro de entrada'; File = '04-login-erro-claro.png'; Screen = 'Erro de acesso'; Appearance = 'light'; Position = 'Topo'; Fixture = $true },
    @{ Preview = 'PatientPendingLight_05 Conta fila e oferta pendente'; File = '05-paciente-oferta-pendente-claro.png'; Screen = 'Conta, fila e oferta pendente'; Appearance = 'light'; Position = 'Conteúdo completo em canvas ampliado'; Fixture = $true },
    @{ Preview = 'PatientAcceptedDark_06 Oferta confirmada escura'; File = '06-paciente-oferta-aceita-escuro.png'; Screen = 'Conta, fila concluída e oferta aceita'; Appearance = 'dark'; Position = 'Conteúdo completo em canvas ampliado'; Fixture = $true },
    @{ Preview = 'SettingsLight_07 Aparencia clara'; File = '07-aparencia-clara.png'; Screen = 'Configurações e aparência'; Appearance = 'light'; Position = 'Topo'; Fixture = $false },
    @{ Preview = 'SettingsDark_08 Aparencia escura'; File = '08-aparencia-escura.png'; Screen = 'Configurações e aparência'; Appearance = 'dark'; Position = 'Topo'; Fixture = $false },
    @{ Preview = 'SettingsSystemLight_09 Sistema claro'; File = '09-sistema-claro.png'; Screen = 'Aparência Sistema com dispositivo claro'; Appearance = 'system'; Position = 'Topo'; Fixture = $false },
    @{ Preview = 'SettingsSystemDark_10 Sistema escuro'; File = '10-sistema-escuro.png'; Screen = 'Aparência Sistema com dispositivo escuro'; Appearance = 'system'; Position = 'Topo'; Fixture = $false },
    @{ Preview = 'SettingsCompleteLight_11 Configuracoes completas claras'; File = '11-configuracoes-completas-claro.png'; Screen = 'Aparência, notificações, interface, versão e restauração'; Appearance = 'light'; Position = 'Conteúdo completo em canvas ampliado'; Fixture = $false },
    @{ Preview = 'SettingsCompleteDark_12 Configuracoes completas escuras conectadas'; File = '12-configuracoes-completas-escuro-conectado.png'; Screen = 'Configurações completas com conta conectada'; Appearance = 'dark'; Position = 'Conteúdo completo em canvas ampliado'; Fixture = $true },
    @{ Preview = 'LoginLargeFont_13 Fonte ampliada 130'; File = '13-login-fonte-130.png'; Screen = 'Entrada com fonte ampliada para 130%'; Appearance = 'light'; Position = 'Topo, fonte 1,3'; Fixture = $false },
    @{ Preview = 'SettingsTablet_14 Tablet configuracoes'; File = '14-configuracoes-tablet.png'; Screen = 'Configurações em tablet de 1000 dp'; Appearance = 'light'; Position = 'Topo, tela ampla'; Fixture = $false },
    @{ Preview = 'PatientWaitingLight_15 Fila aguardando sem oferta'; File = '15-fila-aguardando-sem-oferta-claro.png'; Screen = 'Fila aguardando, sem oferta'; Appearance = 'light'; Position = 'Conteúdo completo em canvas ampliado'; Fixture = $true }
)

# Validate every source first, before changing any export.
$noraResolved = foreach ($noraMapping in $noraMappings) {
    $noraPattern = '^' + [Regex]::Escape($noraMapping.Preview) + '_[a-f0-9]+_0\.png$'
    $noraMatches = @(Get-ChildItem -LiteralPath $noraSource -File | Where-Object Name -Match $noraPattern)
    if ($noraMatches.Count -ne 1) {
        throw "Preview ausente ou ambíguo: $($noraMapping.Preview) (encontrados: $($noraMatches.Count)). Nenhuma exportação iniciada."
    }
    $noraBytes = [IO.File]::ReadAllBytes($noraMatches[0].FullName)
    if ($noraBytes.Length -lt 24 -or [BitConverter]::ToString($noraBytes, 0, 8) -ne '89-50-4E-47-0D-0A-1A-0A') {
        throw "A referência não é um PNG válido: $($noraMapping.Preview)."
    }
    $noraWidth = [System.Buffers.Binary.BinaryPrimitives]::ReverseEndianness([BitConverter]::ToInt32($noraBytes, 16))
    $noraHeight = [System.Buffers.Binary.BinaryPrimitives]::ReverseEndianness([BitConverter]::ToInt32($noraBytes, 20))
    if ($noraWidth -le 0 -or $noraHeight -le 0) { throw "Dimensões PNG inválidas: $($noraMapping.Preview)." }
    @{ Mapping = $noraMapping; Source = $noraMatches[0]; Width = $noraWidth; Height = $noraHeight; Hash = (Get-FileHash -LiteralPath $noraMatches[0].FullName -Algorithm SHA256).Hash }
}

New-Item -ItemType Directory -Path $noraOutput -Force | Out-Null
$noraCaptures = foreach ($noraItem in $noraResolved) {
    $noraMapping = $noraItem.Mapping
    $noraDestination = Join-Path $noraOutput $noraMapping.File
    Copy-Item -LiteralPath $noraItem.Source.FullName -Destination $noraDestination -Force
    $noraCopyHash = (Get-FileHash -LiteralPath $noraDestination -Algorithm SHA256).Hash
    if ($noraCopyHash -ne $noraItem.Hash) { throw "SHA256 diferente após copiar $($noraMapping.File)." }
    [ordered]@{
        File = $noraMapping.File
        Application = 'Android'
        Screen = $noraMapping.Screen
        Appearance = $noraMapping.Appearance
        Position = $noraMapping.Position
        DataSource = $(if ($noraMapping.Fixture) { 'Layoutlib · UI Compose de produção; fixtures sintéticas exclusivas do preview, sem API ou conta real' } else { 'Layoutlib · UI Compose de produção, sem API nem dados de paciente' })
        Width = $noraItem.Width
        Height = $noraItem.Height
        Source = [IO.Path]::GetRelativePath($noraRoot, $noraItem.Source.FullName).Replace('\', '/')
        SourceSha256 = $noraItem.Hash
        Sha256 = $noraCopyHash
    }
}
$noraMetadata = [ordered]@{ Method = 'Compose Preview Screenshot Testing / Layoutlib'; Emulator = $false; ApiExecuted = $false; captures = @($noraCaptures) }
[IO.File]::WriteAllText((Join-Path $noraOutput 'visual-captures.json'), ($noraMetadata | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
Write-Host "15 capturas Android exportadas para $noraOutput. SHA256 das 15 cópias corresponde aos originais. Nenhuma imagem foi alterada."
