#requires -Version 7
<#
.SYNOPSIS
  Cadastra na rede do NORA as especialidades de um hospital, dois médicos fictícios por especialidade, um consultório por
  especialidade e a grade semanal desses médicos. Feito para a simulação na escola (docs/simulacao-escola.md).

.DESCRIPTION
  Roda na raiz do repositório, numa janela do PowerShell 7:  pwsh -File scripts\cadastrar-medicos-simulacao.ps1

  Passos (cada um aparece na tela e é marcado quando termina):
    1. acorda a API e confere /health/ready;
    2. entra como Secretaria (Painel Municipal) e cria ou reativa as especialidades;
    3. entra como administrador do OBS e escolhe a UBS;
    4. cria os médicos (dois por especialidade) e um consultório por especialidade;
    5. cria a grade: o 1º médico de cada especialidade atende de manhã e o 2º à tarde, de segunda a sexta.

  Pode ser rodado de novo. O script completa os dias ausentes da grade padrão e preserva as grades personalizadas.
  As senhas são pedidas com a entrada oculta e não são gravadas em lugar nenhum.

  Os nomes dos médicos são inventados e não têm registro de conselho (CRM), para não coincidir com médicos reais.

.EXAMPLE
  pwsh -File scripts\cadastrar-medicos-simulacao.ps1
  pwsh -File scripts\cadastrar-medicos-simulacao.ps1 -Ubs "UBS Escola" -SemGrade
#>
[CmdletBinding()]
param(
    # Endereço da API. O padrão é a API do piloto no Render.
    [string] $Api = 'https://nora-api-kw8x.onrender.com',
    # E-mail da conta da Secretaria (Painel Municipal). Se ficar vazio, o script pergunta.
    [string] $EmailSecretaria,
    # E-mail da conta de administrador do OBS. Se ficar vazio, o script pergunta.
    [string] $EmailObs,
    # Parte do nome da UBS. Só é preciso quando a conta do OBS tem acesso a mais de uma.
    [string] $Ubs,
    # Duração de cada atendimento na grade, em minutos.
    [ValidateRange(5, 240)] [int] $Minutos = 20,
    # Cria só especialidades, médicos e consultórios, sem a grade.
    [switch] $SemGrade
)

$ErrorActionPreference = 'Stop'
$Api = $Api.TrimEnd('/')

# Especialidades de um hospital geral e os dois médicos fictícios de cada uma (manhã e tarde).
# Clínica Geral, Pediatria e Odontologia começam com os nomes usados no cronograma da simulação (Dra. Ana, Dr. Bruno, Dra. Carla).
$catalogo = [ordered]@{
    'Clínica Geral'               = @('Dra. Ana Ribeiro', 'Dr. Paulo Mendes')
    'Pediatria'                   = @('Dr. Bruno Carvalho', 'Dra. Helena Duarte')
    'Odontologia'                 = @('Dra. Carla Nogueira', 'Dr. Felipe Andrade')
    'Cardiologia'                 = @('Dr. Ricardo Albuquerque', 'Dra. Mariana Teixeira')
    'Ginecologia e Obstetrícia'   = @('Dra. Juliana Prado', 'Dra. Fernanda Lacerda')
    'Ortopedia e Traumatologia'   = @('Dr. Marcelo Tavares', 'Dr. Gustavo Pimentel')
    'Dermatologia'                = @('Dra. Beatriz Campos', 'Dr. Rafael Monteiro')
    'Neurologia'                  = @('Dr. André Figueiredo', 'Dra. Camila Rezende')
    'Oftalmologia'                = @('Dra. Patrícia Leal', 'Dr. Eduardo Siqueira')
    'Otorrinolaringologia'        = @('Dr. Leonardo Brandão', 'Dra. Renata Fontes')
    'Psiquiatria'                 = @('Dra. Vanessa Moura', 'Dr. Thiago Barreto')
    'Endocrinologia'              = @('Dra. Luciana Farias', 'Dr. Rodrigo Peixoto')
    'Gastroenterologia'           = @('Dr. Fábio Rangel', 'Dra. Daniela Vieira')
    'Urologia'                    = @('Dr. Sérgio Cardoso', 'Dr. Vinícius Lopes')
    'Pneumologia'                 = @('Dra. Cristina Amaral', 'Dr. Henrique Bastos')
    'Nefrologia'                  = @('Dr. Otávio Guimarães', 'Dra. Larissa Coutinho')
    'Reumatologia'                = @('Dra. Simone Arruda', 'Dr. Caio Rocha')
    'Infectologia'                = @('Dr. Murilo Sampaio', 'Dra. Tatiana Queiroz')
    'Oncologia'                   = @('Dra. Gabriela Dantas', 'Dr. Augusto Freitas')
    'Geriatria'                   = @('Dr. Antônio Valente', 'Dra. Rosana Batista')
}
$turnos = @(@{ Inicio = '08:00'; Fim = '12:00' }, @{ Inicio = '13:00'; Fim = '17:00' })

$total = 5; $script:passo = 0
function Write-Step([string] $texto) { $script:passo++; Write-Host ("[{0}/{1}] {2}" -f $script:passo, $total, $texto) -ForegroundColor Cyan }
function Write-Done([string] $texto) { Write-Host "      OK  $texto" -ForegroundColor Green }
function Write-Note([string] $texto) { Write-Host "      $texto" -ForegroundColor DarkGray }

# Compara nomes como a API: sem diferenciar maiúsculas, acentos e espaços repetidos.
function Get-Key([string] $nome) {
    $decomposto = ($nome -replace '\s+', ' ').Trim().ToLowerInvariant().Normalize([Text.NormalizationForm]::FormD)
    -join ($decomposto.ToCharArray() | Where-Object { [Globalization.CharUnicodeInfo]::GetUnicodeCategory($_) -ne 'NonSpacingMark' })
}

function Invoke-Nora([string] $Metodo, [string] $Rota, $Corpo = $null, [string] $Token = $null, [int] $Segundos = 60) {
    $parametros = @{ Method = $Metodo; Uri = "$Api$Rota"; SkipHttpErrorCheck = $true; StatusCodeVariable = 'status'; TimeoutSec = $Segundos }
    if ($Token) { $parametros.Headers = @{ Authorization = "Bearer $Token" } }
    if ($null -ne $Corpo) { $parametros.Body = ($Corpo | ConvertTo-Json -Depth 5 -Compress); $parametros.ContentType = 'application/json; charset=utf-8' }
    $resposta = Invoke-RestMethod @parametros
    [pscustomobject]@{ Status = [int]$status; Body = $resposta }
}

function Assert-Status($resultado, [int[]] $esperados, [string] $acao) {
    if ($resultado.Status -in $esperados) { return }
    $detalhe = if ($resultado.Body -and $resultado.Body.title) { "$($resultado.Body.title): $($resultado.Body.detail)" } else { "$($resultado.Body)" }
    throw "Falhou ao $acao (HTTP $($resultado.Status)). $detalhe"
}

function Read-Secret([string] $variavel, [string] $pergunta) {
    $valor = [Environment]::GetEnvironmentVariable($variavel)
    if ($valor) { return $valor }
    Read-Host -MaskInput $pergunta
}

function Connect-Nora([string] $escopo, [string] $email, [string] $senha) {
    $login = Invoke-Nora POST '/api/auth/login' @{ scope = $escopo; identifier = $email; password = $senha }
    if ($login.Status -eq 401) { throw "E-mail ou senha incorretos para a conta '$escopo' ($email)." }
    Assert-Status $login 200 "entrar com a conta '$escopo'"
    if (-not $login.Body.token) { throw "A API não devolveu a sessão da conta '$escopo'. Entre uma vez pelo programa e tente de novo." }
    if ($login.Body.user.mustChangePassword) { throw "A conta '$escopo' ($email) está com senha provisória. Entre uma vez pelo programa, troque a senha e rode o script de novo." }
    $login.Body.token
}

try {
    Write-Step "Acordando a API em $Api…"
    $pronto = $false
    foreach ($tentativa in 1..4) {
        try {
            $saude = Invoke-Nora GET '/health/ready' -Segundos 90
            if ($saude.Status -eq 200) { $pronto = $true; break }
            Write-Note "Tentativa $tentativa de 4: HTTP $($saude.Status)."
        } catch { Write-Note "Tentativa $tentativa de 4: a API ainda não respondeu." }
    }
    if (-not $pronto) { throw "A API não respondeu em $Api/health/ready. Confira o deploy no Render (docs/hospedagem.md) antes de cadastrar." }
    Write-Done 'API pronta.'

    Write-Step 'Especialidades (conta da Secretaria)…'
    if (-not $EmailSecretaria) { $EmailSecretaria = Read-Host 'E-mail da Secretaria (Painel Municipal)' }
    $tokenSecretaria = Connect-Nora 'municipal' $EmailSecretaria (Read-Secret 'NORA_SENHA_SECRETARIA' 'Senha da Secretaria (não aparece)')
    $lista = Invoke-Nora GET '/api/municipal/specialties' -Token $tokenSecretaria
    Assert-Status $lista 200 'ler as especialidades'
    $especialidades = @{}
    foreach ($item in $lista.Body.specialties) { $especialidades[(Get-Key $item.name)] = $item }
    $criadas = 0; $reativadas = 0
    foreach ($nome in $catalogo.Keys) {
        $existente = $especialidades[(Get-Key $nome)]
        if (-not $existente) {
            $nova = Invoke-Nora POST '/api/municipal/specialties' @{ name = $nome } $tokenSecretaria
            Assert-Status $nova 201 "criar a especialidade $nome"
            $especialidades[(Get-Key $nome)] = $nova.Body; $criadas++
        } elseif (-not $existente.isActive) {
            $ativa = Invoke-Nora PATCH "/api/municipal/specialties/$($existente.id)/active" @{ isActive = $true } $tokenSecretaria
            Assert-Status $ativa 200 "reativar a especialidade $nome"
            $existente.isActive = $true; $reativadas++
        }
    }
    Write-Done "$($catalogo.Count) especialidades prontas ($criadas novas, $reativadas reativadas, $($catalogo.Count - $criadas - $reativadas) já existiam)."

    Write-Step 'UBS (conta de administrador do OBS)…'
    if (-not $EmailObs) { $EmailObs = Read-Host 'E-mail do administrador do OBS' }
    $tokenObs = Connect-Nora 'obs' $EmailObs (Read-Secret 'NORA_SENHA_OBS' 'Senha do administrador do OBS (não aparece)')
    $unidades = Invoke-Nora GET '/api/obs/units' -Token $tokenObs
    Assert-Status $unidades 200 'ler as UBS da conta do OBS'
    $opcoes = @($unidades.Body ?? @())
    $nomeBuscado = Get-Key $Ubs
    if ($nomeBuscado) { $opcoes = @($opcoes | Where-Object { (Get-Key $_.name).Contains($nomeBuscado) }) }
    if ($opcoes.Count -eq 0) { throw 'A conta do OBS não tem acesso a nenhuma UBS com esse nome. No Painel Municipal, dê acesso à UBS da simulação para essa conta.' }
    if ($opcoes.Count -gt 1) { throw "A conta do OBS tem acesso a mais de uma UBS: $($opcoes.name -join '; '). Rode de novo com -Ubs ""<parte do nome>""." }
    $unidade = $opcoes[0]; $base = "/api/obs/units/$($unidade.id)"
    Write-Done "UBS escolhida: $($unidade.name)."

    Write-Step 'Médicos e consultórios…'
    $profissionais = Invoke-Nora GET "$base/professionals" -Token $tokenObs
    Assert-Status $profissionais 200 'ler os médicos da UBS'
    $porNome = @{}; foreach ($p in $profissionais.Body.professionals) { $porNome[(Get-Key $p.fullName)] = $p }
    $salas = Invoke-Nora GET "$base/rooms" -Token $tokenObs
    Assert-Status $salas 200 'ler os consultórios da UBS'
    $salaPorNome = @{}; foreach ($s in $salas.Body.rooms) { $salaPorNome[(Get-Key $s.name)] = $s }
    $medicosNovos = 0; $salasNovas = 0; $indice = 0
    foreach ($nome in $catalogo.Keys) {
        $indice++
        Write-Progress -Activity 'Médicos e consultórios' -Status $nome -PercentComplete ($indice * 100 / $catalogo.Count)
        $especialidade = $especialidades[(Get-Key $nome)]
        foreach ($medico in $catalogo[$nome]) {
            if ($porNome.ContainsKey((Get-Key $medico))) { continue }
            $novo = Invoke-Nora POST "$base/professionals" @{ fullName = $medico; specialtyIds = @($especialidade.id) } $tokenObs
            Assert-Status $novo 201 "cadastrar $medico"
            $porNome[(Get-Key $medico)] = $novo.Body; $medicosNovos++
        }
        $nomeSala = "Consultório $nome"
        if (-not $salaPorNome.ContainsKey((Get-Key $nomeSala))) {
            $sala = Invoke-Nora POST "$base/rooms" @{ name = $nomeSala } $tokenObs
            Assert-Status $sala 201 "criar o $nomeSala"
            $salaPorNome[(Get-Key $nomeSala)] = $sala.Body; $salasNovas++
        }
    }
    Write-Progress -Activity 'Médicos e consultórios' -Completed
    Write-Done "$($catalogo.Count * 2) médicos e $($catalogo.Count) consultórios prontos ($medicosNovos médicos e $salasNovas consultórios novos)."

    Write-Step 'Grade de atendimento…'
    if ($SemGrade) {
        Write-Note 'Pulada (-SemGrade). Atenção: com médicos cadastrados, o OBS só marca consulta em horário da grade.'
    } else {
        $atual = Invoke-Nora GET "$base/schedule" -Token $tokenObs
        Assert-Status $atual 200 'ler a grade da UBS'
        $gradePorMedico = ($atual.Body.schedule | Group-Object -Property professionalId -AsHashTable -AsString) ?? @{}
        $diasUteis = 1..5
        $gradesNovas = 0; $medicosComGrade = 0; $indice = 0
        foreach ($nome in $catalogo.Keys) {
            $indice++
            Write-Progress -Activity 'Grade de atendimento' -Status $nome -PercentComplete ($indice * 100 / $catalogo.Count)
            $especialidade = $especialidades[(Get-Key $nome)]
            $sala = $salaPorNome[(Get-Key "Consultório $nome")]
            for ($m = 0; $m -lt 2; $m++) {
                $medico = $porNome[(Get-Key $catalogo[$nome][$m])]
                $turno = $turnos[$m]
                $faixasExistentes = $gradePorMedico[$medico.id]
                $diasCadastrados = @($faixasExistentes | Where-Object {
                    $_.weekday -in $diasUteis -and $_.startTime -eq $turno.Inicio -and $_.endTime -eq $turno.Fim -and
                    $_.slotMinutes -eq $Minutos -and $_.roomId -eq $sala.id -and $_.specialtyId -eq $especialidade.id -and
                    -not $_.validFrom -and -not $_.validUntil
                } | ForEach-Object { $_.weekday })
                # Completa só a grade padrão. Uma faixa diferente indica uma grade que já foi personalizada.
                if ($faixasExistentes.Count -ne $diasCadastrados.Count -or $diasCadastrados.Count -eq $diasUteis.Count) {
                    $medicosComGrade++
                    continue
                }
                foreach ($dia in $diasUteis) {
                    if ($dia -in $diasCadastrados) { continue }
                    $grade = Invoke-Nora POST "$base/schedule" @{
                        professionalId = $medico.id; weekday = $dia; startTime = $turno.Inicio; endTime = $turno.Fim
                        slotMinutes = $Minutos; roomId = $sala.id; specialtyId = $especialidade.id
                    } $tokenObs
                    Assert-Status $grade 201 "criar a grade de $($medico.fullName)"
                    $gradesNovas++
                }
            }
        }
        Write-Progress -Activity 'Grade de atendimento' -Completed
        Write-Done "Grade pronta: 1º médico das 08:00 às 12:00 e 2º das 13:00 às 17:00, de segunda a sexta, atendimentos de $Minutos min ($gradesNovas faixas novas; $medicosComGrade médicos já tinham grade e foram mantidos)."
    }

    Write-Host ''
    Write-Host "PRONTO. Abra o OBS em 'Informações da UBS' para ver os médicos, os consultórios e a grade." -ForegroundColor Green
}
catch {
    Write-Host "FALHOU  $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
