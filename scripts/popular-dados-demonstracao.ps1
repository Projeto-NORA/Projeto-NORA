#requires -Version 7
<#
.SYNOPSIS
  Enche uma instalação do NORA com dados FICTÍCIOS e bonitos para tirar capturas de tela (OBS, Painel Municipal e app), sem tocar em nada que já existe.

.DESCRIPTION
  Roda na raiz do repositório, numa janela do PowerShell 7:  pwsh -File scripts\popular-dados-demonstracao.ps1

  Passos (cada um aparece na tela, marcado quando termina):
    1. pede a URI do Neon (conexão DIRETA, sem "-pooler"); a entrada é oculta e nada é gravado em arquivo;
    2. reconhece o banco central, escolhe a UBS e confere que os bancos estão na versão atual (se não estiverem, rode scripts\implantar-neon.ps1);
    3. conta as linhas de todas as tabelas (antes);
    4. prepara as pessoas e o catálogo fictícios (CPFs com dígitos válidos que ainda não existem na rede; e-mails @email.example, que nunca entregam);
    5. grava no banco central e no banco da UBS (cada banco numa transação só: se algo falhar, nada fica pela metade);
    6. conta de novo e recusa o resultado se alguma tabela tiver menos linhas.

  Regras que o script cumpre:
    - SÓ ACRESCENTA. Nenhum UPDATE ou DELETE em dado que já existe. A única exceção é preencher o horário de atendimento e os serviços da UBS
      se estiverem em branco (e o -Remover só desfaz isso se o texto ainda for o do script).
    - Pode ser rodado de novo no mesmo dia sem duplicar nada (tudo nasce de identificadores calculados a partir de uma semente).
    - Tudo o que ele cria é identificável (id começando com de0de0de-, e-mail @email.example, ator fictício) e o -Remover apaga SÓ isso.
    - Não cria conta que alguém consiga usar: os responsáveis da Família são contas inativas com uma senha impossível.
    - Deixa pelo menos 60% dos horários livres, por especialidade, em cada dia de hoje em diante (a simulação na escola usa a UBS real).

  Detalhes, avisos e o passo a passo para o Neon: docs/dados-demonstracao.md.

.PARAMETER Simular
  Faz tudo, mostra o que seria criado (linhas por tabela) e desfaz no fim. Não grava nada.
.PARAMETER Remover
  Apaga SÓ o que este script criou, depois de uma confirmação digitada. Com -Simular, só mostra o que removeria.
.PARAMETER Ubs
  Parte do nome da UBS que recebe os dados. Só é preciso quando a rede tem mais de uma UBS real.
.PARAMETER UbsExtras
  Cria de 1 a 5 UBS fictícias adicionais, com menos dados, para as comparações do Painel Municipal. Padrão 0.
.PARAMETER Pacientes
  Quantidade de pacientes fictícios na UBS (padrão 48).
.PARAMETER ConsultasPorDia
  Média de consultas por dia útil (padrão 12). Com 48 pacientes, valores muito maiores deixam cada paciente com consultas demais.
.PARAMETER BancoUbs
  Nome do banco da UBS, só se a sua API usa uma conexão explícita por UBS. Padrão: nora_ubs_ + id da UBS sem hífens (o que a API cria sozinha).
.PARAMETER FusoHorario
  Fuso dos horários da grade (o mesmo de Nora:TimeZone na API). Padrão America/Sao_Paulo.
.PARAMETER Completar
  Roda mesmo havendo dados de demonstração de outro dia (consultas fictícias que ficaram "agendadas" no passado). O normal é -Remover e rodar de novo.
.PARAMETER CpfApp
  CPFs de pacientes JÁ cadastrados na UBS (por exemplo, o seu paciente de teste do app) que também ganham consultas, exames e vacinas fictícios.
.PARAMETER ContaSecretaria
  E-mail da conta da Secretaria que "assina" os comunicados fictícios. Padrão: a conta municipal ativa mais antiga.
.PARAMETER SemAvisoPaciente
  Os comunicados fictícios ficam só para a equipe (o app dos pacientes não recebe nenhum aviso novo).
.PARAMETER SemVagaAgora
  Não cria as Vagas Relâmpago "em andamento agora".
.PARAMETER Local
  Usa um PostgreSQL local, sem senha (só 127.0.0.1/localhost), em vez da URI do Neon. Serve para testes. Combine com -Porta, -Banco e -Usuario.
.PARAMETER Confirmacao
  Texto de confirmação do -Remover (para uso automatizado). Sem ele, o script pede para você digitar.

.EXAMPLE
  pwsh -File scripts\popular-dados-demonstracao.ps1 -Simular
  pwsh -File scripts\popular-dados-demonstracao.ps1
  pwsh -File scripts\popular-dados-demonstracao.ps1 -UbsExtras 3
  pwsh -File scripts\popular-dados-demonstracao.ps1 -Remover
#>
[CmdletBinding()]
param(
    [switch] $Simular,
    [switch] $Remover,
    [string] $Ubs,
    [ValidateRange(0, 5)] [int] $UbsExtras = 0,
    [ValidateRange(12, 150)] [int] $Pacientes = 48,
    [ValidateRange(4, 150)] [int] $ConsultasPorDia = 12,
    [string[]] $CpfApp = @(),
    [string] $ContaSecretaria,
    [switch] $SemAvisoPaciente,
    [switch] $SemVagaAgora,
    [switch] $Local,
    [string] $Servidor = '127.0.0.1',
    [ValidateRange(1, 65535)] [int] $Porta = 5432,
    [string] $Banco = 'nora',
    [string] $Usuario = 'postgres',
    [ValidatePattern("^[A-Za-z0-9_]{1,63}$")] [string] $BancoUbs,
    [ValidatePattern("^[a-z][a-z0-9_]{0,30}$")] [string] $PrefixoBancoUbs = 'nora_ubs_',
    [string] $PostgreSqlBin,
    [string] $FusoHorario = 'America/Sao_Paulo',
    [datetimeoffset] $Agora = [datetimeoffset]::UtcNow,
    [string] $Cidade = 'São José dos Campos',
    [string] $Uf = 'SP',
    [string] $Ibge = '3549904',
    [string] $Confirmacao,
    [switch] $Completar
)

$ErrorActionPreference = 'Stop'
$script:Repo = Split-Path -Parent $PSScriptRoot
$script:PastaSql = Join-Path $PSScriptRoot 'dados-demonstracao'
. (Join-Path $script:PastaSql 'pessoas.ps1')

$script:Total = 0; $script:Passo = 0
$script:AtorDemo = 'de0de0de-0000-4000-8000-000000000001'
$script:Psql = $null; $script:Tmp = $null; $script:Raizes = $null
$script:Criado = [ordered]@{}      # tabela -> linhas criadas (soma de todas as execuções)
$script:Relatorio = [System.Collections.Generic.List[string]]::new()

function Write-Step([string] $Texto) { $script:Passo++; Write-Host ("[{0}/{1}] {2}" -f $script:Passo, $script:Total, $Texto) -ForegroundColor Yellow }
function Write-Done([string] $Texto) { Write-Host "      ✓ $Texto" -ForegroundColor Green }
function Write-Note([string] $Texto) { Write-Host "      $Texto" -ForegroundColor DarkGray }
function Write-Warn([string] $Texto) { Write-Host "      ! $Texto" -ForegroundColor DarkYellow }

# ---------------------------------------------------------------------------------------------------------------------------------
# Conexão (a mesma lógica do scripts/implantar-neon.ps1: URI oculta, nunca gravada, certificado verificado por completo)
# ---------------------------------------------------------------------------------------------------------------------------------
function ConvertTo-Conexao([string] $Uri) {
    # TryCreate em vez de [Uri]$texto: a conversão direta, quando falha, repete o texto inteiro (com a senha) na mensagem de erro.
    $parsed = $null
    if (-not [Uri]::TryCreate(([string]$Uri).Trim(), [UriKind]::Absolute, [ref]$parsed) -or $parsed.Scheme -notin 'postgres', 'postgresql') { throw 'Isso não parece uma URI do Neon (postgresql://...). Copie de novo a conexão direta.' }
    if ($parsed.Host -like '*-pooler*') { throw 'Use a conexão direta: no Neon, desligue "Connection pooling" e copie de novo.' }
    $user, $password = $parsed.UserInfo.Split(':', 2) | ForEach-Object { [Uri]::UnescapeDataString($_) }
    $database = $parsed.AbsolutePath.TrimStart('/')
    $port = if ($parsed.Port -gt 0) { $parsed.Port } else { 5432 }
    if (-not $user -or -not $password -or -not $database) { throw 'A URI precisa ter usuário, senha e banco.' }
    [pscustomobject]@{ Host = $parsed.Host; Port = $port; Database = $database; User = $user; Password = $password }
}

function Find-Psql {
    $candidatos = @(
        $(if ($PostgreSqlBin) { Join-Path $PostgreSqlBin 'psql.exe' }),
        (Get-Command psql -ErrorAction SilentlyContinue)?.Source,
        (Join-Path $env:LOCALAPPDATA 'Programs\PostgreSQL\18\bin\psql.exe'),
        'C:\Tools\postgresql-18\bin\psql.exe',
        'C:\Program Files\PostgreSQL\18\bin\psql.exe', 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
    )
    $candidatos | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}

# O psql portátil do Windows não enxerga a loja de certificados do sistema; exportamos as raízes que o Windows confia para um arquivo temporário
# e a verificação completa do certificado (verify-full) continua valendo.
function New-WindowsRootBundle {
    $certs = @(Get-ChildItem Cert:\LocalMachine\Root, Cert:\CurrentUser\Root -ErrorAction SilentlyContinue | Where-Object { $_.NotAfter -gt (Get-Date) } | Sort-Object Thumbprint -Unique)
    if ($certs.Count -eq 0) { throw 'Não encontrei certificados raiz confiáveis no Windows.' }
    $texto = [Text.StringBuilder]::new()
    foreach ($cert in $certs) {
        [void]$texto.Append("-----BEGIN CERTIFICATE-----`n").Append([Convert]::ToBase64String($cert.RawData, 'InsertLineBreaks').Replace("`r`n", "`n")).Append("`n-----END CERTIFICATE-----`n")
    }
    $caminho = Join-Path ([IO.Path]::GetTempPath()) ("nora-raizes-$([guid]::NewGuid().ToString('N')).pem")
    [IO.File]::WriteAllText($caminho, $texto.ToString(), [Text.Encoding]::ASCII)
    $caminho
}

# Roda o psql contra um banco. -Stream mostra na tela as linhas "E|texto" (etapas) do SQL; o resto volta como lista de linhas (campos separados por TAB).
function Invoke-Psql([string] $Database, [string[]] $Arguments, [switch] $Stream) {
    $erroArq = Join-Path $script:Tmp ('erro-' + [guid]::NewGuid().ToString('N') + '.txt')
    $todos = @('-X', '-q', '-A', '-t', '-F', "`t", '-v', 'ON_ERROR_STOP=1', '-d', $Database) + $Arguments
    $saida = [System.Collections.Generic.List[string]]::new()
    & $script:Psql @todos 2> $erroArq | ForEach-Object {
        if ($Stream -and $_ -like 'E|*') { Write-Host ('      · ' + $_.Substring(2)) -ForegroundColor DarkGray }
        elseif ($_ -ne '') { $saida.Add([string]$_) }
    }
    $codigo = $LASTEXITCODE
    $erro = if (Test-Path -LiteralPath $erroArq) { (Get-Content -LiteralPath $erroArq -Raw -Encoding utf8) } else { '' }
    Remove-Item -LiteralPath $erroArq -Force -ErrorAction SilentlyContinue
    if ($codigo -ne 0) { throw "O banco recusou o comando: $($erro.Trim())" }
    $saida.ToArray()
}
function Invoke-Sql([string] $Database, [string] $Sql) { Invoke-Psql $Database @('-c', $Sql) }
function Get-Scalar([string] $Database, [string] $Sql) { (Invoke-Sql $Database $Sql | Select-Object -First 1) }

function Q($Valor) {
    if ($null -eq $Valor) { return 'NULL' }
    if ($Valor -is [bool]) { return $(if ($Valor) { 'true' } else { 'false' }) }
    if ($Valor -is [string]) { return "'" + $Valor.Replace("'", "''") + "'" }
    [string]::Format([cultureinfo]::InvariantCulture, '{0}', $Valor)
}
function ConvertTo-CaminhoSql([string] $Caminho) { $Caminho.Replace('\', '/').Replace("'", "''") }
# Uma linha de VALUES (...) a partir de uma lista de valores.
function Get-LinhaValores([object[]] $Valores) { $partes = foreach ($v in $Valores) { Q $v }; '(' + ($partes -join ', ') + ')' }

# UBS fictícias extras com ligação real no banco central (pg_temp.ligacoes_reais, em funcoes.sql): id -> "tabela: n; ...".
function Get-LigacoesReais([string] $Central, [string[]] $Ids) {
    $mapa = @{}
    if (-not $Ids -or $Ids.Count -eq 0) { return $mapa }
    $arquivo = Join-Path $script:Tmp ('ligacoes-' + [guid]::NewGuid().ToString('N') + '.sql')
    $lista = 'ARRAY[' + (($Ids | ForEach-Object { "$(Q $_)::uuid" }) -join ',') + ']'
    [IO.File]::WriteAllText($arquivo, "\i '$(ConvertTo-CaminhoSql (Join-Path $script:PastaSql 'funcoes.sql'))'`nSELECT unit_id, tabela, n FROM pg_temp.ligacoes_reais($lista) ORDER BY 1, 2;`n", [Text.UTF8Encoding]::new($false))
    try {
        foreach ($linha in (Invoke-Psql $Central @('-f', $arquivo))) {
            $c = $linha.Split("`t")
            $mapa[$c[0]] = (@($mapa[$c[0]], "$($c[1]): $($c[2])") | Where-Object { $_ }) -join '; '
        }
    }
    finally { Remove-Item -LiteralPath $arquivo -Force -ErrorAction SilentlyContinue }
    $mapa
}

function Get-ContagemTabelas([string] $Database) {
    $sql = @"
SELECT table_schema||'.'||table_name, (xpath('/row/c/text()', query_to_xml(format('SELECT count(*) AS c FROM %I.%I', table_schema, table_name), false, true, '')))[1]::text
FROM information_schema.tables WHERE table_type='BASE TABLE' AND table_schema IN ('nora','nora_unit') ORDER BY 1
"@
    $mapa = @{}
    foreach ($linha in (Invoke-Sql $Database $sql)) { $campos = $linha.Split("`t"); $mapa[$campos[0]] = [long]$campos[1] }
    $mapa
}

# ---------------------------------------------------------------------------------------------------------------------------------
# Execução de um arquivo SQL numa transação REPEATABLE READ (o "driver" junta as entradas temporárias e o arquivo)
# ---------------------------------------------------------------------------------------------------------------------------------
function Invoke-Driver([string] $Database, [string] $Entradas, [string] $ArquivoSql, [bool] $Desfazer) {
    $driver = Join-Path $script:Tmp ('driver-' + [guid]::NewGuid().ToString('N') + '.sql')
    $conteudo = @"
\set ON_ERROR_STOP on
SET client_min_messages = warning;
BEGIN ISOLATION LEVEL REPEATABLE READ;
SET LOCAL lock_timeout = '60s';
\i '$(ConvertTo-CaminhoSql (Join-Path $script:PastaSql 'funcoes.sql'))'
$Entradas
\i '$(ConvertTo-CaminhoSql (Join-Path $script:PastaSql $ArquivoSql))'
$(if ($Desfazer) { 'ROLLBACK;' } else { 'COMMIT;' })
"@
    [IO.File]::WriteAllText($driver, $conteudo, [Text.UTF8Encoding]::new($false))
    try {
        # A transação enxerga um retrato fixo do banco (REPEATABLE READ). Se a API mexer, ao mesmo tempo, numa linha que este script vai tocar
        # (por exemplo, vencendo uma Vaga Relâmpago em andamento), o PostgreSQL recusa e desfaz tudo; como o script é repetível, basta tentar de novo.
        for ($tentativa = 1; ; $tentativa++) {
            try { return (Invoke-Psql $Database @('-f', $driver) -Stream) }
            catch {
                if ($tentativa -ge 4 -or $_.Exception.Message -notmatch 'serializ|40001|deadlock|impasse') { throw }
                Write-Warn "A API mexeu num dado ao mesmo tempo (tentativa $tentativa de 4); nada foi gravado, tentando de novo em 8 segundos."
                Start-Sleep -Seconds 8
            }
        }
    }
    finally { Remove-Item -LiteralPath $driver -Force -ErrorAction SilentlyContinue }
}

# Soma as linhas "T|tabela|antes|depois" de uma execução no total do relatório e devolve as demais ("R|", "K|").
# Com -Rotulo (nome da UBS), as linhas de resumo "R|" dizem de qual UBS são.
function Add-Resultado($Linhas, [string] $Rotulo) {
    foreach ($linha in $Linhas) {
        if ($linha -like 'T|*') {
            $c = $linha.Split('|')
            $delta = [long]$c[3] - [long]$c[2]
            if ($delta -ne 0) { $script:Criado[$c[1]] = [long]($script:Criado[$c[1]] ?? 0) + $delta }
        }
        elseif ($linha -like 'R|*' -and $Rotulo) { $script:Relatorio.Add("R|$Rotulo · " + $linha.Substring(2)) }
        elseif ($linha -match '^[RK]\|') { $script:Relatorio.Add($linha) }
    }
}

function Format-Tabela([hashtable] $Mapa) {
    foreach ($par in ($Mapa.GetEnumerator() | Sort-Object Key)) { Write-Host ("        {0,-42} {1,8:N0}" -f $par.Key, $par.Value) -ForegroundColor Gray }
}

# ---------------------------------------------------------------------------------------------------------------------------------
# Leitura da rede
# ---------------------------------------------------------------------------------------------------------------------------------
function Get-Unidades([string] $Central) {
    $sql = "SELECT id, name, region, address, latitude, longitude, (database_ready_at IS NOT NULL)::int, (left(id::text, 9) = 'de0de0de-')::int FROM nora.health_units ORDER BY name"
    foreach ($linha in (Invoke-Sql $Central $sql)) {
        $c = $linha.Split("`t")
        [pscustomobject]@{ Id = $c[0]; Nome = $c[1]; Regiao = $c[2]; Endereco = $c[3]; Lat = [double]::Parse($c[4], [cultureinfo]::InvariantCulture); Lon = [double]::Parse($c[5], [cultureinfo]::InvariantCulture); Pronta = ($c[6] -eq '1'); Ficticia = ($c[7] -eq '1') }
    }
}

function Get-NomeBancoUbs($Unidade, [bool] $Principal) {
    if ($Principal -and $BancoUbs) { return $BancoUbs }
    $PrefixoBancoUbs + $Unidade.Id.Replace('-', '')
}

function Get-VersaoEsperadaUbs {
    $numeros = Get-ChildItem -LiteralPath (Join-Path $script:Repo 'Nora.Api\Database\UnitMigrations') -Filter '*.sql' | ForEach-Object { [int]($_.Name.Split('_')[0]) }
    ($numeros | Measure-Object -Maximum).Maximum
}

function Test-BancoUbs([string] $Central, [string] $NomeBanco, $Unidade, [int] $VersaoEsperada) {
    $existe = Get-Scalar $Central "SELECT 1 FROM pg_database WHERE datname = $(Q $NomeBanco)"
    if ($existe -ne '1') { throw "Não encontrei o banco da UBS '$($Unidade.Nome)' ($NomeBanco). Se a sua API usa uma conexão explícita por UBS (Nora:UnitDatabases), informe o nome do banco em -BancoUbs." }
    $identidade = Get-Scalar $NomeBanco "SELECT unit_id FROM nora_unit.identity WHERE singleton"
    if ($identidade -ne $Unidade.Id) { throw "O banco $NomeBanco não é o da UBS '$($Unidade.Nome)' (identidade diferente)." }
    $versao = [int](Get-Scalar $NomeBanco "SELECT COALESCE((SELECT version FROM nora_unit.schema_version WHERE singleton), 0)")
    if ($versao -lt $VersaoEsperada) { throw "O banco da UBS '$($Unidade.Nome)' está na versão $versao e este repositório espera a $VersaoEsperada. Rode antes: pwsh -File scripts\implantar-neon.ps1 (ele aplica as migrações, sem perder nada)." }
}

# ---------------------------------------------------------------------------------------------------------------------------------
# Entradas temporárias em SQL
# ---------------------------------------------------------------------------------------------------------------------------------
function New-EntradasUbs([string] $UnitId, [array] $Pessoas, [array] $Catalogo, [hashtable] $Volume) {
    $sb = [Text.StringBuilder]::new()
    $protegidos = '{' + (($script:MedicosProtegidos | ForEach-Object { '"' + $_ + '"' }) -join ',') + '}'
    [void]$sb.AppendLine(@"
CREATE TEMP TABLE _p AS
SELECT x.unit_id, x.now_ts, x.zone, t.today,
       -- "Agora" da demonstração: o relógio real, mas nunca depois das 15:40 (à noite o dia continua com consultas agendadas à tarde) e nunca à frente do relógio real
       -- (uma consulta cancelada que ainda está no futuro viraria, na API, uma Vaga Relâmpago de verdade para quem tem conta).
       LEAST(x.now_ts, ((t.today + time '15:40') AT TIME ZONE x.zone)) AS ref_ts,
       $($Volume.Historico) AS hist_days, 14 AS fut_days, x.base_per_day, x.scale, x.miss_mult, x.lightning_rate, x.live, x.teto, x.protegidos, x.grade_enxuta
FROM (SELECT $(Q $UnitId)::uuid AS unit_id, $(Q $Agora.ToString('o'))::timestamptz AS now_ts, $(Q $FusoHorario)::text AS zone, $(Q $Volume.PorDia)::numeric AS base_per_day, $(Q $Volume.Escala)::numeric AS scale,
             $(Q $Volume.Faltas)::numeric AS miss_mult, 0.14::numeric AS lightning_rate, $(Q $Volume.Vaga) AS live, 0.35::numeric AS teto, $(Q $protegidos)::text[] AS protegidos, $(Q $Volume.Enxuta) AS grade_enxuta) x
CROSS JOIN LATERAL (SELECT (x.now_ts AT TIME ZONE x.zone)::date AS today) t;
CREATE TEMP TABLE _pat0 (k int, cpf text, full_name text, email text, phone text, cep text, street text, num text, complement text, neighborhood text, city text, state text, ibge text,
    sex text, age text, f numeric, boost_days int, boost_mult numeric, weight numeric, criado_dias int, desativado_dias int, existing boolean, app boolean, created_real timestamptz);
"@)
    $linhas = foreach ($p in $Pessoas) {
        Get-LinhaValores @($p.K, $p.Cpf, $p.Nome, $p.Email, $p.Phone, $p.Cep, $p.Rua, $p.Numero, $p.Complemento, $p.Bairro, $p.Cidade, $p.Uf, $p.Ibge,
            $p.Sexo, $p.Idade, $p.F, $p.BoostDias, $p.BoostMult, $p.Peso, $p.CriadoDias, $p.DesativadoDias, $p.Existente, $p.App, $p.CriadoReal)
    }
    if ($linhas) { [void]$sb.AppendLine('INSERT INTO _pat0 VALUES ' + ($linhas -join ",`n") + ';') }
    [void]$sb.AppendLine(@"
CREATE TEMP TABLE _pat AS
SELECT p0.*, COALESCE(p0.created_real, p.now_ts - p0.criado_dias * interval '1 day' - floor(pg_temp.u('ch:' || p0.cpf) * 10) * interval '1 hour') AS created_ts,
       CASE WHEN p0.desativado_dias IS NOT NULL THEN p.now_ts - p0.desativado_dias * interval '1 day' - floor(pg_temp.u('dh:' || p0.cpf) * 10) * interval '1 hour' END AS deact_ts
FROM _pat0 p0 CROSS JOIN _p p;
CREATE TEMP TABLE _cat (spec_key text, spec_id uuid, spec_name text, room_name text, doc1 text, doc2 text, weight numeric, ages text, sex text);
"@)
    $cat = foreach ($c in $Catalogo) { Get-LinhaValores @($c.Chave, $c.SpecId, $c.SpecNome, $c.Sala, $c.Medico1, $c.Medico2, $c.Peso, $c.Idades, $c.Sexo) }
    if ($cat) { [void]$sb.AppendLine('INSERT INTO _cat VALUES ' + ($cat -join ",`n") + ';') }
    $sb.ToString()
}

# Tabelas que este script escreve (para separar "o script perdeu linhas" de "a API apagou sessões vencidas enquanto isso").
$script:TabelasDoScript = @('nora.specialties', 'nora.unit_registry_index', 'nora.health_units', 'nora.accounts', 'nora.family_links', 'nora.announcements', 'nora.announcement_regions', 'nora.announcement_units',
    'nora.announcement_reads', 'nora.goals', 'nora.unit_opening_hours', 'nora.unit_database_events', 'nora_unit.rooms', 'nora_unit.professionals', 'nora_unit.professional_specialties', 'nora_unit.schedule_templates',
    'nora_unit.schedule_blocks', 'nora_unit.patient_registry', 'nora_unit.patient_registry_events', 'nora_unit.appointments', 'nora_unit.appointment_events', 'nora_unit.lightning_slots', 'nora_unit.lightning_offers',
    'nora_unit.health_exams', 'nora_unit.health_vaccines', 'nora_unit.health_record_events')

function Get-ContagemRede([string] $Central, $Unidades) {
    $todas = @{ 'central' = (Get-ContagemTabelas $Central) }
    foreach ($u in $Unidades | Where-Object { $_.Pronta }) {
        $nome = Get-NomeBancoUbs $u ($u.Id -eq $script:Principal.Id)
        if ((Get-Scalar $Central "SELECT 1 FROM pg_database WHERE datname = $(Q $nome)") -eq '1') { $todas[$u.Id] = Get-ContagemTabelas $nome }
    }
    $todas
}

function Compare-ContagemRede($Antes, $Depois, [string] $Contexto) {
    $erros = [System.Collections.Generic.List[string]]::new(); $avisos = [System.Collections.Generic.List[string]]::new()
    foreach ($banco in $Antes.Keys) {
        if (-not $Depois.ContainsKey($banco)) { $erros.Add("o banco $banco sumiu"); continue }
        foreach ($tabela in $Antes[$banco].Keys) {
            $a = $Antes[$banco][$tabela]; $d = $Depois[$banco][$tabela]
            if ($null -eq $d -or $d -lt $a) {
                $texto = "$banco / ${tabela}: $a -> $($d ?? 0)"
                if ($tabela -in $script:TabelasDoScript) { $erros.Add($texto) } else { $avisos.Add($texto) }
            }
        }
    }
    foreach ($a in $avisos) { Write-Warn "Mudou fora do que este script escreve (normalmente a própria API apagando sessões vencidas): $a" }
    if ($erros.Count) { Write-Host ("ATENÇÃO ({0}): tabelas com menos linhas: {1}" -f $Contexto, ($erros -join '; ')) -ForegroundColor Red; throw 'Resultado recusado: alguma tabela perdeu linhas.' }
}

# ---------------------------------------------------------------------------------------------------------------------------------
# Programa principal
# ---------------------------------------------------------------------------------------------------------------------------------
if ($MyInvocation.InvocationName -eq '.') { return }   # permite carregar só as funções para teste

$script:VariaveisPg = 'PGHOST', 'PGPORT', 'PGUSER', 'PGPASSWORD', 'PGSSLMODE', 'PGCHANNELBINDING', 'PGSSLROOTCERT', 'PGCLIENTENCODING'
$script:AmbienteAntigo = @{}
foreach ($nome in $script:VariaveisPg) { $script:AmbienteAntigo[$nome] = [Environment]::GetEnvironmentVariable($nome) }
$script:Tmp = Join-Path ([IO.Path]::GetTempPath()) ("nora-demo-$([guid]::NewGuid().ToString('N'))")
New-Item -ItemType Directory -Path $script:Tmp | Out-Null
$saidaAntiga = [Console]::OutputEncoding
$cronometro = [Diagnostics.Stopwatch]::StartNew()
try {
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    $env:PGCLIENTENCODING = 'UTF8'
    $modo = if ($Remover) { 'REMOVER' } elseif ($Simular) { 'SIMULAR' } else { 'APLICAR' }
    $script:Total = if ($Remover) { 8 } else { 8 + ($(if ($UbsExtras -gt 0) { 1 } else { 0 })) }
    Write-Host "NORA · dados de demonstração · modo $modo$(if ($Simular -and -not $Remover) { ' (nada será gravado)' })" -ForegroundColor Cyan

    # 1. Conexão ---------------------------------------------------------------------------------------------------------------
    Write-Step 'Conectando ao banco'
    $script:Psql = Find-Psql
    if (-not $script:Psql) { throw 'Não encontrei o psql. Instale o PostgreSQL 18 (ou informe a pasta bin com -PostgreSqlBin).' }
    if ($Local) {
        if ($Servidor -notin '127.0.0.1', 'localhost', '::1') { throw '-Local só aceita um banco no próprio computador (127.0.0.1). Para o Neon, rode sem -Local.' }
        $env:PGHOST = $Servidor; $env:PGPORT = "$Porta"; $env:PGUSER = $Usuario; $env:PGSSLMODE = 'disable'
        Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
        $central = $Banco
        Write-Done "Banco local $Servidor`:$Porta (sem senha)."
    }
    else {
        Write-Note 'Cole a URI do Neon (postgresql://...) e tecle Enter. Nada aparece na tela.'
        $conexao = ConvertTo-Conexao (Read-Host -MaskInput 'URI do Neon')
        $script:Raizes = New-WindowsRootBundle
        $env:PGHOST = $conexao.Host; $env:PGPORT = "$($conexao.Port)"; $env:PGUSER = $conexao.User; $env:PGPASSWORD = $conexao.Password
        $env:PGSSLMODE = 'verify-full'; $env:PGCHANNELBINDING = 'require'; $env:PGSSLROOTCERT = $script:Raizes
        $central = $conexao.Database
        Write-Done "Conexão direta reconhecida: $($conexao.Host) (banco $($conexao.Database))."
    }
    $null = Get-Scalar $central "SELECT now() AT TIME ZONE $(Q $FusoHorario)"

    # 2. Reconhecer a rede -----------------------------------------------------------------------------------------------------
    Write-Step 'Reconhecendo o banco central e a UBS'
    $tabelasCentral = [int](Get-Scalar $central "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'nora' AND table_name IN ('health_units','unit_registry_index','accounts','specialties','goals','announcements','family_links','unit_opening_hours','unit_database_events','account_photos')")
    if ($tabelasCentral -lt 10) { throw 'Este banco não parece um banco central do NORA já migrado. Rode antes: pwsh -File scripts\implantar-neon.ps1' }
    $versaoEsperada = Get-VersaoEsperadaUbs
    $unidades = @(Get-Unidades $central)
    $reais = @($unidades | Where-Object { -not $_.Ficticia -and $_.Pronta })
    if ($Ubs) { $busca = Get-ChaveNome $Ubs; $reais = @($reais | Where-Object { (Get-ChaveNome $_.Nome).Contains($busca) }) }
    if ($reais.Count -eq 0) { throw 'Não encontrei nenhuma UBS pronta com esse nome. Confira o nome no Painel Municipal (a UBS precisa ter o banco criado).' }
    if ($reais.Count -gt 1) { throw "A rede tem mais de uma UBS: $(($reais.Nome) -join '; '). Rode de novo com -Ubs ""<parte do nome>""." }
    $script:Principal = $reais[0]
    $nomeUbs = Get-NomeBancoUbs $script:Principal $true
    Test-BancoUbs $central $nomeUbs $script:Principal $versaoEsperada
    Write-Done "UBS escolhida: $($script:Principal.Nome) ($($script:Principal.Regiao)). Banco $nomeUbs na versão atual."
    $extrasExistentes = @($unidades | Where-Object { $_.Ficticia })

    if ($Remover) {
        # ------------------------------------------------------------------------------------------------------------------------
        # REMOVER
        # ------------------------------------------------------------------------------------------------------------------------
        Write-Step 'Procurando o que o script criou'
        # UBS fictícia que ganhou ligação real (acesso dado a alguém da equipe, paciente vinculado, comunicado real...) fica inteira, com o banco dela.
        $presas = Get-LigacoesReais $central @($extrasExistentes | ForEach-Object { $_.Id })
        $mantidas = @($extrasExistentes | Where-Object { $presas.ContainsKey($_.Id) })
        $extrasExistentes = @($extrasExistentes | Where-Object { -not $presas.ContainsKey($_.Id) })
        foreach ($m in $mantidas) { Write-Warn "A UBS fictícia '$($m.Nome)' fica (com o banco dela), porque ganhou ligação real: $($presas[$m.Id]). Desfaça essa ligação e rode o -Remover de novo." }
        $alvos = @(@{ Unidade = $script:Principal; Banco = $nomeUbs; Extra = $false }) + @($extrasExistentes | ForEach-Object { @{ Unidade = $_; Banco = (Get-NomeBancoUbs $_ $false); Extra = $true } })
        $resumo = [System.Collections.Generic.List[string]]::new(); $achou = $false
        foreach ($alvo in $alvos) {
            if ((Get-Scalar $central "SELECT 1 FROM pg_database WHERE datname = $(Q $alvo.Banco)") -ne '1') { continue }
            if ((Get-Scalar $alvo.Banco "SELECT to_regclass('nora_unit.patient_registry') IS NOT NULL") -ne 't') { $alvo.Pacientes = 0; $alvo.Consultas = 0; $resumo.Add("$($alvo.Unidade.Nome): banco sem as tabelas da UBS (será apagado)"); continue }
            $n = @(Invoke-Sql $alvo.Banco "SELECT (SELECT count(*) FROM nora_unit.patient_registry WHERE created_by = '$script:AtorDemo'), (SELECT count(*) FROM nora_unit.appointments WHERE id::text LIKE 'de0de0de-%')")
            $campos = $n[0].Split("`t")
            if ([long]$campos[0] + [long]$campos[1] -gt 0) { $achou = $true }
            $alvo.Pacientes = [long]$campos[0]; $alvo.Consultas = [long]$campos[1]
            $resumo.Add("$($alvo.Unidade.Nome): $($campos[0]) pacientes e $($campos[1]) consultas fictícios$(if ($alvo.Extra) { ' (UBS fictícia: o banco inteiro será apagado)' })")
        }
        $centralDemo = [long](Get-Scalar $central "SELECT (SELECT count(*) FROM nora.announcements WHERE id::text LIKE 'de0de0de-%') + (SELECT count(*) FROM nora.accounts WHERE id::text LIKE 'de0de0de-%') + (SELECT count(*) FROM nora.specialties WHERE id::text LIKE 'de0de0de-%') + (SELECT count(*) FROM nora.family_links WHERE id::text LIKE 'de0de0de-%') + (SELECT count(*) FROM nora.goals WHERE id::text LIKE 'de0de0de-%')")
        if ($centralDemo -gt 0) { $achou = $true }
        if (-not $achou -and $extrasExistentes.Count -eq 0) { Write-Done $(if ($mantidas.Count) { 'Nada mais a remover: só ficou o que ganhou ligação real (veja o aviso acima).' } else { 'Não há dados de demonstração neste banco. Nada a remover.' }); return }
        foreach ($linha in $resumo) { Write-Note $linha }
        Write-Note "No banco central: $centralDemo registros fictícios (comunicados, contas de responsáveis, Família, metas, especialidades)."
        Write-Done 'Encontrado.'

        Write-Step 'Confirmando'
        $frase = 'REMOVER DADOS DE DEMONSTRACAO'
        if (-not $Simular) {
            $resposta = if ($Confirmacao) { $Confirmacao } else { Read-Host "Isto apaga SÓ os dados fictícios listados acima (o que já existia fica como está). Digite $frase para confirmar" }
            if ($resposta -ne $frase) { Write-Host 'Nada foi apagado.' -ForegroundColor Red; exit 1 }
        }
        Write-Done $(if ($Simular) { 'Simulação: nada será apagado de verdade.' } else { 'Confirmado.' })

        Write-Step 'Contando as linhas de todas as tabelas (antes)'
        $script:Principal = $script:Principal
        $antes = Get-ContagemRede $central $unidades
        Write-Done "$(($antes.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum) tabelas lidas em $($antes.Count) bancos."

        Write-Step 'Removendo nos bancos das UBS'
        $cpfsRemovidos = [System.Collections.Generic.List[object]]::new()
        $extrasApagar = [System.Collections.Generic.List[object]]::new()
        foreach ($alvo in $alvos) {
            if ((Get-Scalar $central "SELECT 1 FROM pg_database WHERE datname = $(Q $alvo.Banco)") -ne '1') { if ($alvo.Extra) { $extrasApagar.Add($alvo) }; continue }
            $temTabelas = (Get-Scalar $alvo.Banco "SELECT to_regclass('nora_unit.patient_registry') IS NOT NULL") -eq 't'
            $cpfs = if ($temTabelas) { @(Invoke-Sql $alvo.Banco "SELECT cpf FROM nora_unit.patient_registry WHERE created_by = '$script:AtorDemo'") } else { @() }
            if ($alvo.Extra) {
                Write-Note "$($alvo.Unidade.Nome): UBS fictícia, o banco inteiro será apagado no fim."
                foreach ($c in $cpfs) { $cpfsRemovidos.Add(@{ Cpf = $c; Unit = $alvo.Unidade.Id }) }
                $extrasApagar.Add($alvo); continue
            }
            Write-Note "$($alvo.Unidade.Nome):"
            $linhas = Invoke-Driver $alvo.Banco '' 'remover-ubs.sql' ([bool]$Simular)
            Add-Resultado $linhas
            if (-not $Simular) {
                $restantes = [System.Collections.Generic.HashSet[string]]::new([string[]]@(Invoke-Sql $alvo.Banco "SELECT cpf FROM nora_unit.patient_registry"))
                foreach ($c in $cpfs) { if (-not $restantes.Contains($c)) { $cpfsRemovidos.Add(@{ Cpf = $c; Unit = $alvo.Unidade.Id }) } }
            }
            else { foreach ($c in $cpfs) { $cpfsRemovidos.Add(@{ Cpf = $c; Unit = $alvo.Unidade.Id }) } }
        }
        Write-Done 'Bancos das UBS limpos.'

        Write-Step 'Removendo no banco central'
        # Especialidades fictícias só saem se nenhuma UBS que continua existindo ainda usa.
        $espFicticias = @(Invoke-Sql $central "SELECT id FROM nora.specialties WHERE id::text LIKE 'de0de0de-%'")
        $emUso = [System.Collections.Generic.HashSet[string]]::new()
        if ($espFicticias.Count -gt 0) {
            $lista = "ARRAY[" + (($espFicticias | ForEach-Object { "'$_'::uuid" }) -join ',') + "]"
            foreach ($u in @($unidades | Where-Object { $_.Pronta -and -not $_.Ficticia }) + $mantidas) {
                $nome = Get-NomeBancoUbs $u ($u.Id -eq $script:Principal.Id)
                if ((Get-Scalar $central "SELECT 1 FROM pg_database WHERE datname = $(Q $nome)") -ne '1') { continue }
                if ((Get-Scalar $nome "SELECT to_regclass('nora_unit.professional_specialties') IS NOT NULL") -ne 't') { continue }
                foreach ($id in (Invoke-Sql $nome "SELECT specialty_id::text FROM nora_unit.professional_specialties WHERE specialty_id = ANY($lista) UNION SELECT specialty_id::text FROM nora_unit.schedule_templates WHERE specialty_id = ANY($lista) UNION SELECT specialty_id::text FROM nora_unit.appointments WHERE specialty_id = ANY($lista) UNION SELECT specialty_id::text FROM nora_unit.lightning_slots WHERE specialty_id = ANY($lista)")) { [void]$emUso.Add($id) }
            }
        }
        $entradas = [Text.StringBuilder]::new()
        [void]$entradas.AppendLine('CREATE TEMP TABLE _rm_idx (cpf text, unit_id uuid); CREATE TEMP TABLE _rm_spec (id uuid); CREATE TEMP TABLE _rm_units (id uuid); CREATE TEMP TABLE _c (unit_id uuid, servicos text);')
        if ($cpfsRemovidos.Count) { [void]$entradas.AppendLine('INSERT INTO _rm_idx VALUES ' + (($cpfsRemovidos | ForEach-Object { "($(Q $_.Cpf), $(Q $_.Unit))" }) -join ',') + ';') }
        foreach ($id in $espFicticias) { if (-not $emUso.Contains($id)) { [void]$entradas.AppendLine("INSERT INTO _rm_spec VALUES ($(Q $id));") } }
        foreach ($e in $extrasApagar) { [void]$entradas.AppendLine("INSERT INTO _rm_units VALUES ($(Q $e.Unidade.Id));") }
        [void]$entradas.AppendLine("INSERT INTO _c VALUES ($(Q $script:Principal.Id), $(Q (Get-ServicosUbs)));")
        Add-Resultado (Invoke-Driver $central $entradas.ToString() 'remover-central.sql' ([bool]$Simular))
        if (-not $Simular) {
            foreach ($e in $extrasApagar) {
                if ((Get-Scalar $central "SELECT 1 FROM pg_database WHERE datname = $(Q $e.Banco)") -eq '1') {
                    if ($e.Banco -ne ($PrefixoBancoUbs + $e.Unidade.Id.Replace('-', '')) -or -not $e.Unidade.Ficticia) { throw "Recusei apagar o banco $($e.Banco): não é o de uma UBS fictícia." }
                    $null = Invoke-Sql $central "DROP DATABASE `"$($e.Banco)`" WITH (FORCE)"
                    Write-Note "Banco da UBS fictícia $($e.Unidade.Nome) apagado."
                }
            }
        }
        Write-Done 'Banco central limpo.'

        Write-Step 'Conferindo as contagens (depois)'
        $unidadesDepois = @(Get-Unidades $central)
        $depois = Get-ContagemRede $central $unidadesDepois
        if ($Simular) { Write-Done 'Simulação: nada foi apagado; as contagens abaixo são as que o -Remover alcançaria.' }
        else {
            # Aqui as tabelas DEVEM perder linhas (é a remoção); o que não pode é perder fora do que o script escreve.
            foreach ($banco in $antes.Keys) {
                if (-not $depois.ContainsKey($banco)) { continue }
                foreach ($tabela in $antes[$banco].Keys) { if ($depois[$banco][$tabela] -lt $antes[$banco][$tabela] -and $tabela -notin $script:TabelasDoScript -and $tabela -ne 'nora.unit_access_events') { Write-Warn "Mudou fora do que este script escreve: $banco / ${tabela}: $($antes[$banco][$tabela]) -> $($depois[$banco][$tabela])" } }
            }
            Write-Done 'Remoção concluída.'
        }
        foreach ($k in ($script:Relatorio | Where-Object { $_ -like 'K|*' })) { $c = $k.Split('|'); Write-Warn "Mantido por ter ganho ligação com algo real: $($c[2]) $($c[1])" }
        Write-Host '      Linhas removidas por tabela:' -ForegroundColor Cyan
        $m = [ordered]@{}; foreach ($par in $script:Criado.GetEnumerator()) { $m[$par.Key] = -[long]$par.Value }
        Format-Tabela ([hashtable]$m)
        Write-Host ("`nPRONTO em {0:N0} s." -f $cronometro.Elapsed.TotalSeconds) -ForegroundColor Green
        return
    }

    # ---------------------------------------------------------------------------------------------------------------------------
    # APLICAR / SIMULAR
    # ---------------------------------------------------------------------------------------------------------------------------
    Write-Step 'Contando as linhas de todas as tabelas (antes)'
    $antes = Get-ContagemRede $central $unidades
    Write-Done "$(($antes.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum) tabelas lidas em $($antes.Count) bancos."
    # Dados de uma execução de outro dia ficam "vencidos" (consultas agendadas ou chamadas no passado). Rodar por cima deles deixaria a agenda estranha.
    $vencidas = [long](Get-Scalar $nomeUbs "SELECT count(*) FROM nora_unit.appointments WHERE id::text LIKE 'de0de0de-%' AND status IN ('scheduled','called') AND starts_at < (date_trunc('day', $(Q $Agora.ToString('o'))::timestamptz AT TIME ZONE $(Q $FusoHorario)) AT TIME ZONE $(Q $FusoHorario))")
    if ($vencidas -gt 0 -and -not $Completar) {
        Write-Host "      Já existem dados de demonstração de outro dia ($vencidas consultas fictícias ficaram 'agendadas' no passado)." -ForegroundColor Red
        Write-Host '      Para renovar: rode com -Remover e depois de novo. Para só completar o que falta, mesmo assim: use -Completar.' -ForegroundColor Red
        exit 1
    }

    Write-Step 'Preparando as pessoas e o catálogo fictícios'
    $gerente = $null
    $linhasGerente = @(Invoke-Sql $central "SELECT id, email FROM nora.accounts WHERE scope = 'municipal' AND is_active AND id::text NOT LIKE 'de0de0de-%' ORDER BY created_at, id")
    foreach ($l in $linhasGerente) { $c = $l.Split("`t"); if (-not $ContaSecretaria -or $c[1] -eq $ContaSecretaria.Trim().ToLowerInvariant()) { $gerente = $c[0]; break } }
    if (-not $gerente) { throw 'Não achei uma conta ativa da Secretaria (municipal) para assinar os comunicados. Crie uma com scripts\implantar-neon.ps1 ou informe -ContaSecretaria.' }
    $decisor = Get-Scalar $central "SELECT a.id FROM nora.accounts a JOIN nora.staff_units s ON s.account_id = a.id WHERE s.unit_id = $(Q $script:Principal.Id) AND a.scope = 'obs' AND a.is_active ORDER BY a.created_at, a.id LIMIT 1"
    if (-not $decisor) { $decisor = $gerente }

    $usados = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($c in (Invoke-Sql $central "SELECT cpf FROM nora.accounts WHERE cpf IS NOT NULL UNION SELECT cpf FROM nora.unit_registry_index UNION SELECT dependent_cpf FROM nora.family_links")) { [void]$usados.Add($c) }
    foreach ($c in (Invoke-Sql $nomeUbs "SELECT cpf FROM nora_unit.patient_registry")) { [void]$usados.Add($c) }
    $conhecidos = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($c in (Invoke-Sql $nomeUbs "SELECT cpf FROM nora_unit.patient_registry WHERE created_by = '$script:AtorDemo'")) { [void]$conhecidos.Add($c) }
    foreach ($c in (Invoke-Sql $central "SELECT cpf FROM nora.accounts WHERE id::text LIKE 'de0de0de-%'")) { [void]$conhecidos.Add($c) }
    $localInfo = @{ Cidade = $Cidade; Uf = $Uf; Ibge = $Ibge }
    $pessoas = @(New-PacientesDemo $Pacientes $script:Principal.Id 1 $usados $conhecidos $Agora $localInfo)

    foreach ($digitos in $CpfApp) {
        $cpf = ($digitos -replace '\D', '')
        $linha = Invoke-Sql $nomeUbs "SELECT full_name, to_char(created_at AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI:SS'), is_active FROM nora_unit.patient_registry WHERE cpf = $(Q $cpf)" | Select-Object -First 1
        if (-not $linha) { throw "O CPF $cpf (-CpfApp) não está cadastrado na UBS $($script:Principal.Nome)." }
        $c = $linha.Split("`t"); if ($c[2] -ne 't') { throw "O cadastro do CPF $cpf está desativado na UBS." }
        $pessoas += @{ K = 9000 + $pessoas.Count; Cpf = $cpf; Nome = $c[0]; Email = ''; Phone = $null; Cep = $null; Rua = $null; Numero = $null; Complemento = ''; Bairro = $null; Cidade = $null; Uf = $null; Ibge = $null
            Idade = 'a'; Sexo = 'X'; Perfil = 'A'; F = 0.35; BoostDias = 0; BoostMult = 1.0; Peso = 0.45; CriadoDias = 0; DesativadoDias = $null; Existente = $true; App = $true
            CriadoReal = ([datetimeoffset]::new([datetime]::SpecifyKind([datetime]::ParseExact($c[1], 'yyyy-MM-dd HH:mm:ss', [cultureinfo]::InvariantCulture), [DateTimeKind]::Utc))).ToString('o') }
    }

    # Especialidades da rede: reaproveita as que já existem (mesmo nome, sem diferenciar acento e maiúscula); cria só as que faltam.
    $especialidades = @{}
    foreach ($l in (Invoke-Sql $central "SELECT id, name, name_key, is_active::int FROM nora.specialties")) { $c = $l.Split("`t"); $especialidades[$c[2]] = @{ Id = $c[0]; Nome = $c[1]; Ativa = ($c[3] -eq '1') } }
    $specLinhas = [System.Collections.Generic.List[string]]::new(); $catalogo = [System.Collections.Generic.List[hashtable]]::new(); $criarSpec = 0
    foreach ($item in $script:CatalogoDemo) {
        $chave = Get-ChaveNome $item.Nome
        $existente = $especialidades[$chave]
        if ($existente -and -not $existente.Ativa) { Write-Warn "A especialidade '$($existente.Nome)' está desativada na rede: ficou de fora (o script não reativa nada)."; continue }
        $id = if ($existente) { $existente.Id } else { Get-DemoId "spec:$chave" }
        $nomeSpec = if ($existente) { $existente.Nome } else { $item.Nome }
        if (-not $existente) { $criarSpec++ }
        $specLinhas.Add("($(Q $chave), $(Q $nomeSpec), $(Q $id), $(Q (-not $existente)))")
        $catalogo.Add(@{ Chave = $chave; SpecId = $id; SpecNome = $nomeSpec; Sala = "Consultório $($item.Nome)"; Medico1 = $item.Medicos[0]; Medico2 = $item.Medicos[1]; Peso = $item.Peso; Idades = $item.Idades; Sexo = $item.Sexo })
    }
    Write-Done "$($pessoas.Count) pacientes, $($catalogo.Count) especialidades ($criarSpec novas; as outras já existiam), $($catalogo.Count * 2) profissionais, gerente da Secretaria e decisor da Família escolhidos."

    $nomeCentralPasso = if ($UbsExtras -gt 0) { 'Gravando no banco central (especialidades e UBS extras)' } else { 'Gravando no banco central (especialidades)' }
    Write-Step $nomeCentralPasso
    $extras = @()
    for ($i = 0; $i -lt $UbsExtras; $i++) { $extras += $script:ExtrasDemo[$i] }
    $entradas = [Text.StringBuilder]::new()
    [void]$entradas.AppendLine('CREATE TEMP TABLE _spec (spec_key text, nome text, spec_id uuid, criar boolean); CREATE TEMP TABLE _idx (cpf text, unit_id uuid); CREATE TEMP TABLE _units (id uuid, nome text, regiao text, endereco text, lat double precision, lon double precision);')
    if ($specLinhas.Count) { [void]$entradas.AppendLine('INSERT INTO _spec VALUES ' + ($specLinhas -join ",`n") + ';') }
    $pessoasNovas = @($pessoas | Where-Object { -not $_.Existente })
    $indice = [System.Collections.Generic.List[string]]::new()
    foreach ($p in $pessoasNovas) { $indice.Add("($(Q $p.Cpf), $(Q $script:Principal.Id))") }
    # CPFs e dados das UBS extras (gerados agora para o índice central já nascer completo).
    $planoExtras = @()
    for ($i = 0; $i -lt $extras.Count; $i++) {
        $x = $extras[$i]; $uid = Get-DemoId "ubs-extra:$($x.Nome)"
        $bancoX = $PrefixoBancoUbs + $uid.Replace('-', '')
        # O banco da extra pode existir sem as migrações (uma execução anterior interrompida): aí ainda não há cadastro para ler.
        if ((Get-Scalar $central "SELECT 1 FROM pg_database WHERE datname = $(Q $bancoX)") -eq '1' -and (Get-Scalar $bancoX "SELECT to_regclass('nora_unit.patient_registry') IS NOT NULL") -eq 't') {
            foreach ($c in (Invoke-Sql $bancoX "SELECT cpf FROM nora_unit.patient_registry WHERE created_by = '$script:AtorDemo'")) { [void]$conhecidos.Add($c) }
        }
        $pe = @(New-PacientesDemo 14 $uid 1 $usados $conhecidos $Agora $localInfo)
        foreach ($p in $pe) { $indice.Add("($(Q $p.Cpf), $(Q $uid))") }
        $planoExtras += @{ Extra = $x; Id = $uid; Pessoas = $pe }
        $sqlUnidade = "INSERT INTO _units VALUES ($(Q $uid), $(Q $x.Nome), $(Q $x.Regiao), $(Q $x.Endereco), $(Q $x.Lat), $(Q $x.Lon));"; [void]$entradas.AppendLine($sqlUnidade)
    }
    # O índice central de CPFs (nora.unit_registry_index) entra depois dos cadastros, a partir do que de fato ficou gravado nas UBS: assim uma execução
    # interrompida nunca deixa linha de índice sem cadastro (e a próxima execução reaproveita os mesmos CPFs). Só CPFs fictícios, que ninguém usa para marcar consulta.
    # Na simulação tudo é desfeito, então o índice planejado vai junto, na mesma transação das UBS extras (a chave estrangeira exige que elas existam).
    if ($Simular -and $indice.Count) { [void]$entradas.AppendLine('INSERT INTO _idx VALUES ' + ($indice -join ",`n") + ';') }
    Add-Resultado (Invoke-Driver $central $entradas.ToString() 'central-1.sql' ([bool]$Simular))
    Write-Done $(if ($Simular) { 'Simulado (desfeito).' } else { 'Gravado.' })

    # UBS extras: banco novo, migrações e a marca de "pronto" (o mesmo que a API faz ao cadastrar uma UBS).
    if ($UbsExtras -gt 0) {
        Write-Step "Criando $UbsExtras UBS fictícia(s) e os bancos delas"
        if ($Simular) { Write-Done 'Simulação: as UBS fictícias seriam criadas com banco próprio e migrações (nada foi feito).' }
        else {
            foreach ($pl in $planoExtras) {
                $banco = $PrefixoBancoUbs + $pl.Id.Replace('-', '')
                if ((Get-Scalar $central "SELECT 1 FROM pg_database WHERE datname = $(Q $banco)") -ne '1') {
                    $null = Invoke-Sql $central "CREATE DATABASE `"$banco`""
                    $null = Invoke-Sql $central "REVOKE CONNECT ON DATABASE `"$banco`" FROM PUBLIC"
                    $null = Invoke-Sql $central "GRANT CONNECT ON DATABASE `"$banco`" TO CURRENT_USER"
                }
                $pasta = Join-Path $script:Repo 'Nora.Api\Database\UnitMigrations'
                $arquivos = Get-ChildItem -LiteralPath $pasta -Filter '*.sql' | Sort-Object { $_.Name } -CaseSensitive
                $pronto = $false
                if ((Get-Scalar $banco "SELECT to_regclass('nora_unit.schema_version') IS NOT NULL") -eq 't') { $pronto = ((Get-Scalar $banco "SELECT COALESCE((SELECT version FROM nora_unit.schema_version WHERE singleton), 0)") -as [int]) -ge $versaoEsperada }
                if (-not $pronto) {
                    $mig = Join-Path $script:Tmp ('mig-' + [guid]::NewGuid().ToString('N') + '.sql')
                    $corpo = [Text.StringBuilder]::new()
                    [void]$corpo.AppendLine("BEGIN; SELECT pg_advisory_xact_lock(62419804);")
                    foreach ($a in $arquivos) { [void]$corpo.AppendLine((Get-Content -LiteralPath $a.FullName -Raw -Encoding utf8)) }
                    [void]$corpo.AppendLine("INSERT INTO nora_unit.identity(singleton, unit_id) VALUES (true, $(Q $pl.Id)) ON CONFLICT (singleton) DO NOTHING;")
                    [void]$corpo.AppendLine("INSERT INTO nora_unit.schema_version(singleton, version) VALUES (true, $versaoEsperada) ON CONFLICT (singleton) DO UPDATE SET version = GREATEST(nora_unit.schema_version.version, EXCLUDED.version); COMMIT;")
                    [IO.File]::WriteAllText($mig, $corpo.ToString(), [Text.UTF8Encoding]::new($false))
                    $null = Invoke-Psql $banco @('-f', $mig)
                    Remove-Item -LiteralPath $mig -Force
                }
                $null = Invoke-Sql $central "UPDATE nora.health_units SET database_ready_at = CURRENT_TIMESTAMP WHERE id = $(Q $pl.Id) AND database_ready_at IS NULL; INSERT INTO nora.unit_database_events(unit_id, actor_id) SELECT $(Q $pl.Id), NULL WHERE NOT EXISTS (SELECT 1 FROM nora.unit_database_events WHERE unit_id = $(Q $pl.Id))"
                Write-Note "$($pl.Extra.Nome): banco $banco pronto."
            }
            Write-Done "$UbsExtras UBS fictícia(s) criada(s)."
        }
    }

    Write-Step "Gravando a UBS $($script:Principal.Nome) (pode levar de 20 s a 2 min; o banco faz o trabalho pesado)"
    $volume = @{ Historico = 365; Escala = 1; Faltas = 1; Vaga = (-not $SemVagaAgora); PorDia = $ConsultasPorDia; Enxuta = $false }
    $entradasUbs = New-EntradasUbs $script:Principal.Id $pessoas $catalogo $volume
    $linhasUbs = Invoke-Driver $nomeUbs $entradasUbs 'ubs.sql' ([bool]$Simular)
    Add-Resultado $linhasUbs $script:Principal.Nome
    Write-Done $(if ($Simular) { 'Simulado (desfeito).' } else { 'UBS gravada.' })

    # Extras: um conjunto menor de dados em cada UBS fictícia.
    if ($UbsExtras -gt 0 -and -not $Simular) {
        foreach ($pl in $planoExtras) {
            $banco = $PrefixoBancoUbs + $pl.Id.Replace('-', '')
            $medicosX = Get-MedicosExtra $pl.Id (2 * $script:EspecialidadesExtras.Count)
            $cat = @(); $pos = 0
            foreach ($nomeEsp in $script:EspecialidadesExtras) {
                $item = $script:CatalogoDemo | Where-Object { $_.Nome -eq $nomeEsp } | Select-Object -First 1
                $chave = Get-ChaveNome $item.Nome; $esp = $especialidades[$chave]
                $idSpec = if ($esp) { $esp.Id } else { Get-DemoId "spec:$chave" }; $nomeSpec = if ($esp) { $esp.Nome } else { $item.Nome }
                $cat += @{ Chave = $chave; SpecId = $idSpec; SpecNome = $nomeSpec; Sala = "Consultório $($item.Nome)"; Medico1 = $medicosX[2 * $pos]; Medico2 = $medicosX[2 * $pos + 1]; Peso = $item.Peso; Idades = $item.Idades; Sexo = $item.Sexo }
                $pos++
            }
            Write-Note "$($pl.Extra.Nome):"
            $vol = @{ Historico = 180; Escala = 0.30; Faltas = $pl.Extra.Faltas; Vaga = $false; PorDia = $ConsultasPorDia; Enxuta = $true }
            Add-Resultado (Invoke-Driver $banco (New-EntradasUbs $pl.Id $pl.Pessoas $cat $vol) 'ubs.sql' $false) $pl.Extra.Nome
        }
    }

    Write-Step 'Gravando o índice de CPFs, Família, comunicados, metas e atendimento ao público (banco central)'
    # (Na simulação o índice já foi ensaiado junto com as UBS extras, no passo das especialidades.)
    if (-not $Simular) {
        $indiceFinal = [System.Collections.Generic.List[string]]::new()
        $bancosIndice = @(@{ Banco = $nomeUbs; Unit = $script:Principal.Id }) + @($planoExtras | ForEach-Object { @{ Banco = ($PrefixoBancoUbs + $_.Id.Replace('-', '')); Unit = $_.Id } })
        foreach ($b in $bancosIndice) { foreach ($c in (Invoke-Sql $b.Banco "SELECT cpf FROM nora_unit.patient_registry WHERE created_by = '$script:AtorDemo'")) { $indiceFinal.Add("($(Q $c), $(Q $b.Unit))") } }
        $entradasI = 'CREATE TEMP TABLE _spec (spec_key text, nome text, spec_id uuid, criar boolean); CREATE TEMP TABLE _units (id uuid, nome text, regiao text, endereco text, lat double precision, lon double precision); CREATE TEMP TABLE _idx (cpf text, unit_id uuid);'
        if ($indiceFinal.Count) { $entradasI += "`nINSERT INTO _idx VALUES " + ($indiceFinal -join ",`n") + ';' }
        Add-Resultado (Invoke-Driver $central $entradasI 'central-1.sql' $false)
    }
    # Responsáveis e pedidos de Família: dependentes são pacientes fictícios da UBS; quem decidiu é a conta real escolhida.
    $criancas = @($pessoas | Where-Object { -not $_.Existente -and $_.Idade -in 'c', 't' -and $null -eq $_.DesativadoDias })
    $idosos = @($pessoas | Where-Object { -not $_.Existente -and $_.Idade -eq 'i' -and $null -eq $_.DesativadoDias })
    $plano = @()
    if ($criancas.Count -ge 1) { $plano += @{ G = 1; Dep = $criancas[0]; Status = 'pending'; Dias = 1 } }
    if ($criancas.Count -ge 2) { $plano += @{ G = 2; Dep = $criancas[1]; Status = 'pending'; Dias = 2 } }
    if ($criancas.Count -ge 3) { $plano += @{ G = 3; Dep = $criancas[2]; Status = 'pending'; Dias = 5 } }
    if ($criancas.Count -ge 4) { $plano += @{ G = 4; Dep = $criancas[3]; Status = 'approved'; Dias = 34 } }
    if ($criancas.Count -ge 5) { $plano += @{ G = 4; Dep = $criancas[4]; Status = 'approved'; Dias = 34 } }
    if ($idosos.Count -ge 1) { $plano += @{ G = 5; Dep = $idosos[0]; Status = 'approved'; Dias = 61 } }
    if ($criancas.Count -ge 6) { $plano += @{ G = 6; Dep = $criancas[5]; Status = 'rejected'; Dias = 9 } }
    $guardioes = @{}
    foreach ($item in $plano) {
        if ($guardioes.ContainsKey("g$($item.G)")) { continue }
        $sobrenome = ($item.Dep.Nome -split ' ')[-1]
        $sexoG = if ((Get-DemoU "gs:$($item.G)") -lt 0.7) { 'F' } else { 'M' }
        $nomeG = "$(Get-Pick ($(if ($sexoG -eq 'F') { $script:NomesF } else { $script:NomesM })) "gn:$($item.G)") $(Get-Pick $script:Sobrenomes "g2:$($item.G)") $sobrenome"
        $emailG = "$((Remove-Acentos $nomeG).ToLowerInvariant().Split(' ')[0]).$((Remove-Acentos $sobrenome).ToLowerInvariant())$('{0:D2}' -f ($item.G * 13))@$script:DominioEmail"
        $guardioes["g$($item.G)"] = @{ Cpf = (Get-CpfFicticio "g:$($script:Principal.Id)#$($item.G)" $usados $conhecidos); Nome = $nomeG; Email = $emailG; Phone = '1290{0:D7}' -f [int][math]::Floor((Get-DemoU "gp:$($item.G)") * 10000000) }
    }
    $entradasC = [Text.StringBuilder]::new()
    $servicos = Get-ServicosUbs
    [void]$entradasC.AppendLine(@"
CREATE TEMP TABLE _c (now_ts timestamptz, unit_id uuid, gerente uuid, decisor uuid, regiao text, aviso_paciente boolean, meta_ubs boolean, servicos text);
INSERT INTO _c VALUES ($(Q $Agora.ToString('o')), $(Q $script:Principal.Id), $(Q $gerente), $(Q $decisor), $(Q $script:Principal.Regiao), $(Q (-not $SemAvisoPaciente)), true, $(Q $servicos));
CREATE TEMP TABLE _g (cpf text, nome text, email text, phone text);
CREATE TEMP TABLE _fl (resp_cpf text, dep_cpf text, status text, motivo text, dias int);
"@)
    if ($guardioes.Count) { [void]$entradasC.AppendLine('INSERT INTO _g VALUES ' + (($guardioes.Values | ForEach-Object { "($(Q $_.Cpf), $(Q $_.Nome), $(Q $_.Email), $(Q $_.Phone))" }) -join ",`n") + ';') }
    if ($plano.Count) {
        [void]$entradasC.AppendLine('INSERT INTO _fl VALUES ' + (($plano | ForEach-Object {
            $motivo = if ($_.Status -eq 'rejected') { 'Os documentos enviados não conferem com o cadastro. Traga a certidão de nascimento e peça de novo.' } else { $null }
            "($(Q $guardioes["g$($_.G)"].Cpf), $(Q $_.Dep.Cpf), $(Q $_.Status), $(Q $motivo), $($_.Dias))" }) -join ",`n") + ';')
    }
    Add-Resultado (Invoke-Driver $central $entradasC.ToString() 'central-2.sql' ([bool]$Simular))
    Write-Done $(if ($Simular) { 'Simulado (desfeito).' } else { 'Gravado.' })

    Write-Step 'Conferindo as contagens (depois)'
    if ($Simular) { Write-Done 'Simulação: nada foi gravado; as contagens abaixo são as que seriam criadas.' }
    else {
        $unidadesDepois = @(Get-Unidades $central)
        $depois = Get-ContagemRede $central $unidadesDepois
        Compare-ContagemRede $antes $depois 'depois da gravação'
        Write-Done "Nenhuma tabela perdeu linhas ($(($antes.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum) tabelas conferidas)."
    }
    Write-Host ($(if ($Simular) { '      Linhas que seriam criadas por tabela:' } else { '      Linhas criadas por tabela:' })) -ForegroundColor Cyan
    Format-Tabela ([hashtable]$script:Criado)
    $total = ($script:Criado.Values | Measure-Object -Sum).Sum
    Write-Host ("      Total: {0:N0} linhas." -f $total) -ForegroundColor Cyan
    $resumo = @($script:Relatorio | Where-Object { $_ -like 'R|*' })
    foreach ($r in $resumo) { $c = $r.Split('|'); Write-Note ("{0}: {1} = {2}" -f $c[1], $c[2], $c[3]) }
    Write-Host ("`nPRONTO em {0:N0} s.$(if ($Simular) { ' (simulação: nada foi gravado)' })" -f $cronometro.Elapsed.TotalSeconds) -ForegroundColor Green
    if (-not $Simular) { Write-Host '      Para desfazer tudo: pwsh -File scripts\popular-dados-demonstracao.ps1 -Remover' -ForegroundColor Cyan }
}
catch {
    Write-Host "FALHOU  $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    [Console]::OutputEncoding = $saidaAntiga
    # O ambiente volta a ser o de antes (a senha nunca fica para trás, mesmo quando o script é chamado de dentro de uma janela do PowerShell já aberta).
    foreach ($nome in $script:VariaveisPg) { [Environment]::SetEnvironmentVariable($nome, $script:AmbienteAntigo[$nome]) }
    if ($script:Raizes) { Remove-Item -LiteralPath $script:Raizes -Force -ErrorAction SilentlyContinue }
    if ($script:Tmp -and (Test-Path -LiteralPath $script:Tmp)) { Remove-Item -LiteralPath $script:Tmp -Recurse -Force -ErrorAction SilentlyContinue }
}
