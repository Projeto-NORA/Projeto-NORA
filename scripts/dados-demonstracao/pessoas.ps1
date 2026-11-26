# Geração das pessoas e do catálogo fictícios dos dados de demonstração (carregado por scripts/popular-dados-demonstracao.ps1 com ". ").
# Tudo é determinístico: a mesma semente dá sempre o mesmo resultado, então rodar de novo reconhece o que já foi criado.
# Nenhuma pessoa é real: nomes sorteados de listas comuns, e-mails em @email.example (domínio reservado, nunca entrega), CPFs só com dígitos
# verificadores válidos (e conferidos contra os que já existem) e telefones com um prefixo que não é usado por nenhuma operadora.

$script:DominioEmail = 'email.example'

function Get-DemoHash([string] $Texto) { [System.Security.Cryptography.MD5]::HashData([Text.Encoding]::UTF8.GetBytes($Texto)) }

# Mesmo cálculo de pg_temp.u (funcoes.sql): primeiros 32 bits do MD5 divididos por 2^32.
function Get-DemoU([string] $Semente) {
    $h = Get-DemoHash $Semente
    [double](([uint64]$h[0] -shl 24) -bor ([uint64]$h[1] -shl 16) -bor ([uint64]$h[2] -shl 8) -bor [uint64]$h[3]) / 4294967296.0
}

# Mesmo cálculo de pg_temp.did: UUID que começa com de0de0de- (é a marca que o -Remover reconhece).
function Get-DemoId([string] $Semente) {
    $hex = ([BitConverter]::ToString((Get-DemoHash $Semente)) -replace '-', '').ToLowerInvariant()
    'de0de0de-{0}-4{1}-8{2}-{3}' -f $hex.Substring(0, 4), $hex.Substring(4, 3), $hex.Substring(7, 3), $hex.Substring(10, 12)
}

function Get-Pick($Lista, [string] $Semente) { $Lista[[int][math]::Floor((Get-DemoU $Semente) * $Lista.Count)] }

function Remove-Acentos([string] $Texto) {
    $d = $Texto.Normalize([Text.NormalizationForm]::FormD)
    -join ($d.ToCharArray() | Where-Object { [Globalization.CharUnicodeInfo]::GetUnicodeCategory($_) -ne 'NonSpacingMark' })
}

function Get-ChaveNome([string] $Texto) { (Remove-Acentos (($Texto -replace '\s+', ' ').Trim())).ToLowerInvariant() }

# CPF com dígitos verificadores válidos (mesma regra de AccountService.ValidCpf), a partir de uma semente.
function New-CpfFicticio([string] $Semente) {
    $h = Get-DemoHash $Semente
    $n = [uint64]([BitConverter]::ToUInt64($h, 0) % 1000000000)
    $base = $n.ToString('D9')
    if (($base.ToCharArray() | Select-Object -Unique).Count -eq 1) { $base = '123456789' }
    $digitos = [System.Collections.Generic.List[int]]::new()
    foreach ($c in $base.ToCharArray()) { $digitos.Add([int][string]$c) }
    foreach ($contagem in 9, 10) {
        $soma = 0
        for ($i = 0; $i -lt $contagem; $i++) { $soma += $digitos[$i] * ($contagem + 1 - $i) }
        $dv = ($soma * 10) % 11
        if ($dv -eq 10) { $dv = 0 }
        $digitos.Add($dv)
    }
    -join $digitos
}

# Primeiro CPF da sequência da semente que já seja um dos fictícios ($Conhecidos) ou que ainda não exista em lugar nenhum ($Usados).
function Get-CpfFicticio([string] $Semente, [System.Collections.Generic.HashSet[string]] $Usados, [System.Collections.Generic.HashSet[string]] $Conhecidos) {
    for ($tentativa = 0; $tentativa -lt 500; $tentativa++) {
        $cpf = New-CpfFicticio "nora-demo-cpf:${Semente}:$tentativa"
        if ($Conhecidos.Contains($cpf)) { return $cpf }
        if ($Usados.Add($cpf)) { return $cpf }
    }
    throw 'Não consegui gerar um CPF fictício livre.'
}

$script:NomesM = 'Miguel', 'Arthur', 'Heitor', 'Davi', 'Gabriel', 'Bernardo', 'Samuel', 'Lucas', 'Pedro', 'Rafael', 'Enzo', 'Matheus', 'Guilherme', 'Gustavo', 'Leonardo', 'Felipe', 'João Pedro', 'Carlos', 'Eduardo', 'Ricardo', 'Marcos', 'André', 'Rodrigo', 'Fernando', 'Paulo', 'Sérgio', 'Thiago', 'Vinícius', 'Bruno', 'Diego', 'Renan', 'Caio'
$script:NomesMIdoso = 'José', 'Antônio', 'Francisco', 'João', 'Manoel', 'Sebastião', 'Raimundo', 'Benedito', 'Geraldo', 'Joaquim', 'Osvaldo', 'Luiz'
$script:NomesF = 'Helena', 'Alice', 'Laura', 'Maria Clara', 'Sofia', 'Valentina', 'Isabela', 'Manuela', 'Júlia', 'Heloísa', 'Luiza', 'Beatriz', 'Ana Carolina', 'Camila', 'Fernanda', 'Patrícia', 'Juliana', 'Aline', 'Larissa', 'Mariana', 'Gabriela', 'Renata', 'Vanessa', 'Tatiane', 'Priscila', 'Letícia', 'Débora', 'Carla', 'Simone', 'Cláudia', 'Bianca', 'Raquel'
$script:NomesFIdoso = 'Maria', 'Ana', 'Francisca', 'Antônia', 'Aparecida', 'Conceição', 'Terezinha', 'Lourdes', 'Joana', 'Rosa', 'Benedita', 'Josefa'
$script:Sobrenomes = 'Silva', 'Santos', 'Oliveira', 'Souza', 'Rodrigues', 'Ferreira', 'Alves', 'Pereira', 'Lima', 'Gomes', 'Costa', 'Ribeiro', 'Martins', 'Carvalho', 'Almeida', 'Lopes', 'Soares', 'Fernandes', 'Vieira', 'Barbosa', 'Rocha', 'Dias', 'Nascimento', 'Andrade', 'Moreira', 'Nunes', 'Marques', 'Machado', 'Mendes', 'Freitas', 'Cardoso', 'Ramos', 'Gonçalves', 'Santana', 'Teixeira', 'Araújo', 'Cavalcante', 'Monteiro', 'Moura', 'Correia', 'Pinto', 'Azevedo', 'Batista', 'Melo', 'Campos', 'Duarte', 'Medeiros', 'Borges', 'Tavares', 'Castro', 'Pires', 'Siqueira', 'Bueno', 'Franco', 'Cunha', 'Farias', 'Peixoto', 'Guimarães', 'Miranda', 'Fonseca'
$script:Particulas = @{ Silva = 'da'; Costa = 'da'; Rocha = 'da'; Souza = 'de'; Santos = 'dos'; Oliveira = 'de'; Lima = 'de'; Almeida = 'de'; Moura = 'de'; 'Araújo' = 'de'; Melo = 'de'; Castro = 'de' }
$script:Ruas ='Rua das Acácias', 'Rua dos Ipês', 'Rua das Palmeiras', 'Rua dos Jacarandás', 'Rua Quinze de Novembro', 'Avenida Brasil', 'Rua Sete de Setembro', 'Rua das Hortênsias', 'Rua Tiradentes', 'Rua São João', 'Avenida das Nações', 'Rua dos Girassóis', 'Rua Marechal Deodoro', 'Rua da Liberdade', 'Rua dos Lírios', 'Rua Santa Rita', 'Avenida Independência', 'Rua das Orquídeas', 'Rua Bela Vista', 'Rua do Comércio', 'Rua Primavera', 'Rua das Mangueiras', 'Rua Antônio de Castro', 'Rua José Bonifácio', 'Rua dos Cravos', 'Avenida Central', 'Rua das Violetas', 'Rua Dom Pedro II', 'Rua Rui Barbosa', 'Rua Boa Esperança'
$script:Bairros = 'Centro', 'Jardim Satélite', 'Vila Industrial', 'Jardim América', 'Parque Industrial', 'Vila Ema', 'Jardim Esplanada', 'Bosque dos Eucaliptos', 'Santana', 'Jardim Aquárius', 'Vila Tatetuba', 'Jardim Paulista'
$script:Complementos = '', '', '', '', 'Apto 12', 'Casa 2', 'Bloco B, apto 31', 'Fundos', 'Apto 204', 'Casa 1'

# Catálogo da simulação na escola (o mesmo de scripts/cadastrar-medicos-simulacao.ps1) com o que cada especialidade atende:
# Idades: c = criança, t = adolescente, a = adulto, i = idoso. Sexo '' = qualquer. Peso = demanda relativa (soma 100).
$script:CatalogoDemo = @(
    @{ Nome = 'Clínica Geral';              Medicos = 'Dra. Ana Ribeiro', 'Dr. Paulo Mendes';              Idades = 'ctai'; Sexo = '';  Peso = 28 }
    @{ Nome = 'Pediatria';                  Medicos = 'Dr. Bruno Carvalho', 'Dra. Helena Duarte';          Idades = 'ct';   Sexo = '';  Peso = 7 }
    @{ Nome = 'Odontologia';                Medicos = 'Dra. Carla Nogueira', 'Dr. Felipe Andrade';         Idades = 'ctai'; Sexo = '';  Peso = 10 }
    @{ Nome = 'Cardiologia';                Medicos = 'Dr. Ricardo Albuquerque', 'Dra. Mariana Teixeira';  Idades = 'ai';   Sexo = '';  Peso = 7 }
    @{ Nome = 'Ginecologia e Obstetrícia';  Medicos = 'Dra. Juliana Prado', 'Dra. Fernanda Lacerda';       Idades = 'tai';  Sexo = 'F'; Peso = 7 }
    @{ Nome = 'Ortopedia e Traumatologia';  Medicos = 'Dr. Marcelo Tavares', 'Dr. Gustavo Pimentel';       Idades = 'ctai'; Sexo = '';  Peso = 5 }
    @{ Nome = 'Dermatologia';               Medicos = 'Dra. Beatriz Campos', 'Dr. Rafael Monteiro';        Idades = 'ctai'; Sexo = '';  Peso = 5 }
    @{ Nome = 'Neurologia';                 Medicos = 'Dr. André Figueiredo', 'Dra. Camila Rezende';       Idades = 'tai';  Sexo = '';  Peso = 2.5 }
    @{ Nome = 'Oftalmologia';               Medicos = 'Dra. Patrícia Leal', 'Dr. Eduardo Siqueira';        Idades = 'ctai'; Sexo = '';  Peso = 5 }
    @{ Nome = 'Otorrinolaringologia';       Medicos = 'Dr. Leonardo Brandão', 'Dra. Renata Fontes';        Idades = 'ctai'; Sexo = '';  Peso = 3 }
    @{ Nome = 'Psiquiatria';                Medicos = 'Dra. Vanessa Moura', 'Dr. Thiago Barreto';          Idades = 'tai';  Sexo = '';  Peso = 4 }
    @{ Nome = 'Endocrinologia';             Medicos = 'Dra. Luciana Farias', 'Dr. Rodrigo Peixoto';        Idades = 'tai';  Sexo = '';  Peso = 4 }
    @{ Nome = 'Gastroenterologia';          Medicos = 'Dr. Fábio Rangel', 'Dra. Daniela Vieira';           Idades = 'tai';  Sexo = '';  Peso = 2 }
    @{ Nome = 'Urologia';                   Medicos = 'Dr. Sérgio Cardoso', 'Dr. Vinícius Lopes';          Idades = 'tai';  Sexo = 'M'; Peso = 2 }
    @{ Nome = 'Pneumologia';                Medicos = 'Dra. Cristina Amaral', 'Dr. Henrique Bastos';       Idades = 'ctai'; Sexo = '';  Peso = 2 }
    @{ Nome = 'Nefrologia';                 Medicos = 'Dr. Otávio Guimarães', 'Dra. Larissa Coutinho';     Idades = 'ai';   Sexo = '';  Peso = 0.8 }
    @{ Nome = 'Reumatologia';               Medicos = 'Dra. Simone Arruda', 'Dr. Caio Rocha';              Idades = 'ai';   Sexo = '';  Peso = 1.2 }
    @{ Nome = 'Infectologia';               Medicos = 'Dr. Murilo Sampaio', 'Dra. Tatiana Queiroz';        Idades = 'tai';  Sexo = '';  Peso = 1 }
    @{ Nome = 'Oncologia';                  Medicos = 'Dra. Gabriela Dantas', 'Dr. Augusto Freitas';       Idades = 'ai';   Sexo = '';  Peso = 0.5 }
    @{ Nome = 'Geriatria';                  Medicos = 'Dr. Antônio Valente', 'Dra. Rosana Batista';        Idades = 'i';    Sexo = '';  Peso = 3 }
)
# Médicos da simulação na escola: não recebem férias fictícias (docs/simulacao-escola.md cita a Dra. Ana, o Dr. Bruno e a Dra. Carla).
$script:MedicosProtegidos = 'Dra. Ana Ribeiro', 'Dr. Bruno Carvalho', 'Dra. Carla Nogueira'

# UBS fictícias extras (-UbsExtras): região, endereço, posição e o "jeito" de cada uma (multiplicador de faltas).
$script:ExtrasDemo = @(
    @{ Nome = 'UBS Jardim Aurora';     Regiao = 'Zona Sul';   Endereco = 'Avenida das Américas, 1250 - Jardim Aurora';  Lat = -23.2481; Lon = -45.9120; Faltas = 0.6 }
    @{ Nome = 'UBS Vila Esperança';    Regiao = 'Zona Norte'; Endereco = 'Rua Esperança, 480 - Vila Esperança';         Lat = -23.1810; Lon = -45.8892; Faltas = 2.4 }
    @{ Nome = 'UBS Parque Industrial'; Regiao = 'Zona Leste'; Endereco = 'Rua dos Metalúrgicos, 910 - Parque Industrial'; Lat = -23.2104; Lon = -45.8567; Faltas = 1.1 }
    @{ Nome = 'UBS Santana';           Regiao = 'Zona Norte'; Endereco = 'Rua Santana, 75 - Santana';                    Lat = -23.1695; Lon = -45.9003; Faltas = 1.4 }
    @{ Nome = 'UBS Bosque Oeste';      Regiao = 'Zona Oeste'; Endereco = 'Alameda dos Eucaliptos, 300 - Bosque Oeste';   Lat = -23.2290; Lon = -45.9455; Faltas = 0.9 }
)
# Especialidades das UBS extras (um conjunto menor).
$script:EspecialidadesExtras = 'Clínica Geral', 'Pediatria', 'Odontologia', 'Ginecologia e Obstetrícia', 'Cardiologia', 'Dermatologia'

# Gera os pacientes fictícios. $Usados = CPFs que já existem na rede (não podem repetir); $Conhecidos = CPFs fictícios já gravados numa execução anterior.
function New-PacientesDemo([int] $Quantidade, [string] $Salt, [int] $Primeiro, [System.Collections.Generic.HashSet[string]] $Usados, [System.Collections.Generic.HashSet[string]] $Conhecidos, [datetimeoffset] $Agora, [hashtable] $Local) {
    $grupos = 'a', 'a', 'i', 'a', 'c', 'a', 'i', 'a', 't', 'a', 'a', 'i'
    $lista = [System.Collections.Generic.List[hashtable]]::new()
    $emails = [System.Collections.Generic.HashSet[string]]::new()
    $nomes = [System.Collections.Generic.HashSet[string]]::new()
    for ($i = 0; $i -lt $Quantidade; $i++) {
        $k = $Primeiro + $i
        $sem = "$Salt#$k"
        $idade = $grupos[($k - 1) % 12]
        $sexo = if ((Get-DemoU "sx:$sem") -lt 0.55) { 'F' } else { 'M' }
        # Perfil de comparecimento: D = piorou muito nos últimos meses; C = irregular recente; A = muito assíduo; B = comum. Criança fica em A ou B.
        $perfil = if ($idade -in 'c', 't') { if ((Get-DemoU "pf:$sem") -lt 0.6) { 'A' } else { 'B' } }
                  elseif ($k % 16 -eq 11) { 'D' } elseif ($k % 8 -eq 4) { 'C' } elseif ((Get-DemoU "pf:$sem") -lt 0.55) { 'A' } else { 'B' }
        $perfilDados = switch ($perfil) {
            'A' { @{ F = 0.35; Dias = 0;  Mult = 1.0 } }
            'B' { @{ F = 1.0;  Dias = 0;  Mult = 1.0 } }
            'C' { @{ F = 1.8;  Dias = 60; Mult = 2.4 } }
            'D' { @{ F = 2.4;  Dias = 40; Mult = 2.6 } }
        }
        $pesoBase = @{ i = 1.6; a = 1.0; c = 1.0; t = 0.6 }[$idade]
        $peso = $pesoBase * (0.6 + 0.8 * (Get-DemoU "pw:$sem"))
        $inativo = ($k % 16 -eq 14)
        $criadoDias = if ($inativo) { 450 + [int][math]::Floor((Get-DemoU "cd:$sem") * 300) }
                      elseif ((Get-DemoU "cr:$sem") -lt 0.45) { 400 + [int][math]::Floor((Get-DemoU "cd:$sem") * 500) }
                      else { 12 + [int][math]::Floor((Get-DemoU "cd:$sem") * 318) }
        $desativadoDias = if ($inativo) { 40 + [int][math]::Floor((Get-DemoU "dd:$sem") * 150) } else { $null }

        $primeiros = if ($idade -eq 'i') { if ($sexo -eq 'F') { $script:NomesFIdoso } else { $script:NomesMIdoso } } else { if ($sexo -eq 'F') { $script:NomesF } else { $script:NomesM } }
        for ($t = 0; $t -lt 60; $t++) {
            $s1 = Get-Pick $script:Sobrenomes "s1:${sem}:$t"; $s2 = Get-Pick $script:Sobrenomes "s2:${sem}:$t"
            if ($s1 -eq $s2) { continue }
            # "da Silva", "de Souza", "dos Santos": a partícula só vai antes dos sobrenomes em que ela é de uso comum.
            $part = if ($script:Particulas.ContainsKey($s1) -and (Get-DemoU "pt:${sem}:$t") -lt 0.5) { " $($script:Particulas[$s1]) " } else { ' ' }
            $nome = "$(Get-Pick $primeiros "pn:${sem}:$t")$part$s1 $s2"
            if ($nomes.Add((Get-ChaveNome $nome))) { break }
        }
        $partes = (Remove-Acentos $nome).ToLowerInvariant() -split ' ' | Where-Object { $_ -and $_ -notin 'de', 'da', 'do', 'dos', 'das' }
        $email = "$($partes[0]).$($partes[-1])$('{0:D2}' -f ($k % 100))@$script:DominioEmail"
        while (-not $emails.Add($email)) { $email = "$($partes[0]).$($partes[-1])$('{0:D3}' -f ($k * 7 % 1000))@$script:DominioEmail" }

        $cpf = Get-CpfFicticio "p:$sem" $Usados $Conhecidos
        $bairro = Get-Pick $script:Bairros "bx:$sem"
        $cep = '122{0:D2}{1:D3}' -f (10 + [int][math]::Floor((Get-DemoU "c1:$sem") * 40)), [int][math]::Floor((Get-DemoU "c2:$sem") * 1000)
        $lista.Add(@{
            K = $k; Cpf = $cpf; Nome = $nome; Email = $email; Idade = $idade; Sexo = $sexo; Perfil = $perfil
            Phone = '1290{0:D7}' -f [int][math]::Floor((Get-DemoU "ph:$sem") * 10000000)
            Cep = $cep; Rua = (Get-Pick $script:Ruas "rua:$sem"); Numero = [string](10 + [int][math]::Floor((Get-DemoU "nu:$sem") * 1900))
            Complemento = (Get-Pick $script:Complementos "cp:$sem"); Bairro = $bairro; Cidade = $Local.Cidade; Uf = $Local.Uf; Ibge = $Local.Ibge
            F = $perfilDados.F; BoostDias = $perfilDados.Dias; BoostMult = $perfilDados.Mult; Peso = [math]::Round($peso, 3)
            CriadoDias = $criadoDias; DesativadoDias = $desativadoDias; Existente = $false; App = $false
        })
    }
    $lista
}

# Texto de "Serviços oferecidos" que o script grava quando a UBS ainda não informou nada (o -Remover só o desfaz se continuar idêntico).
function Get-ServicosUbs {
    'Consultas médicas e odontológicas, vacinação (calendário nacional e campanhas), curativos e inalação, coleta de exames de rotina, planejamento familiar, pré-natal, acompanhamento de hipertensão e diabetes, saúde mental e grupos de convivência para idosos. Agende pelo aplicativo NORA ou na recepção.'
}

# Nomes de médicos fictícios para as UBS extras (sem registro de conselho, como os da simulação).
function Get-MedicosExtra([string] $Semente, [int] $Quantidade) {
    $nomes = [System.Collections.Generic.List[string]]::new(); $chaves = [System.Collections.Generic.HashSet[string]]::new()
    for ($t = 0; $nomes.Count -lt $Quantidade -and $t -lt 400; $t++) {
        $fem = (Get-DemoU "mf:${Semente}:$t") -lt 0.5
        $lista = if ($fem) { $script:NomesF } else { $script:NomesM }
        $nome = "$(if ($fem) { 'Dra.' } else { 'Dr.' }) $(Get-Pick $lista "mn:${Semente}:$t") $(Get-Pick $script:Sobrenomes "ms:${Semente}:$t")"
        if ($nome -match 'Maria Clara|João Pedro|Ana Carolina') { continue }
        if ($chaves.Add((Get-ChaveNome $nome))) { $nomes.Add($nome) }
    }
    $nomes.ToArray()
}
