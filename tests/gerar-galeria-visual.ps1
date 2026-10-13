param(
    [string]$CaptureRoot = '.build/galeria-visual-2026-10-10',
    [string]$OutputRoot = 'docs/galeria/2026-10-10'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$capturePath = [IO.Path]::GetFullPath((Join-Path $projectRoot $CaptureRoot))
$outputPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputRoot))
if (-not $outputPath.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'A galeria deve ficar dentro do projeto.'
}
$items = @()
foreach ($platform in @('desktop', 'android')) {
    $source = Join-Path $capturePath $platform
    $metadata = Get-Content -LiteralPath (Join-Path $source 'visual-captures.json') -Raw | ConvertFrom-Json
    $method = $metadata.captureMethod
    if (-not $method) { $method = $metadata.Method }
    if (-not $method) { throw "Método de captura ausente: $platform" }
    $destination = Join-Path $outputPath $platform
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($capture in $metadata.captures) {
        if ([IO.Path]::GetFileName($capture.File) -ne $capture.File -or [IO.Path]::GetExtension($capture.File) -ne '.png') {
            throw "Nome de captura inválido: $($capture.File)"
        }
        $original = Join-Path $source $capture.File
        $target = Join-Path $destination $capture.File
        Copy-Item -LiteralPath $original -Destination $target -Force
        $hash = (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $hash) { throw 'Cópia de captura divergente.' }
        $screenNames = @{
            'login' = 'Acesso à conta'; 'home-desconectada' = 'Visão geral sem conta'
            'home-conectada' = 'Visão geral com conta'; 'pacientes-selecao' = 'Pacientes e seleção'
            'cadastro-paciente' = 'Cadastro de paciente'; 'paciente-cadastrado' = 'Paciente cadastrado'
            'fila-selecionada-acao' = 'Fila de espera e ação selecionada'; 'fila-cancelada' = 'Fila após cancelamento'
            'vaga-formulario' = 'Abertura de Vaga Relâmpago'; 'vagas-abertas' = 'Vagas abertas'
            'totais-agregados' = 'Totais agregados'; 'indicadores' = 'Indicadores'
            'configuracoes' = 'Configurações'; 'configuracoes-conta-conectada' = 'Configurações com conta'
            'configuracoes-sidebar-recolhida' = 'Configurações com sidebar recolhida'
            'login-sidebar-recolhida' = 'Acesso com sidebar recolhida'; 'fila-sidebar-recolhida' = 'Fila com sidebar recolhida'
            'indicadores-sidebar-recolhida' = 'Indicadores com sidebar recolhida'
            'login-janela-minima' = 'Acesso na janela mínima'; 'configuracoes-janela-minima' = 'Configurações na janela mínima'
            'atendimentos-contexto-base' = 'Base visual de atendimentos — ação indisponível'
        }
        $screen = $screenNames[$capture.Screen]
        if (-not $screen) { $screen = ($capture.Screen -replace '-', ' ') }
        $items += [ordered]@{
            file = "$platform/$($capture.File)"; application = $capture.Application
            screen = $screen; appearance = $capture.Appearance; position = $capture.Position
            dataSource = $capture.DataSource; width = $capture.Width; height = $capture.Height
            method = $method; sha256 = $hash
        }
    }
}
if ($items.Count -eq 0) { throw 'Nenhuma captura recebida.' }
$items = @($items | Sort-Object @{Expression={
    if ($_.file -like '*pacientes-selecao-light*') { 0 }
    elseif ($_.file -like '*fila-selecionada-acao-dark*') { 1 }
    elseif ($_.file -like '*vagas-abertas-light*') { 2 }
    elseif ($_.file -like '*totais-agregados-dark*') { 3 }
    elseif ($_.file -eq 'android/05-paciente-oferta-pendente-claro.png') { 4 }
    elseif ($_.file -eq 'android/08-aparencia-escura.png') { 5 }
    else { 6 }
}}, application, screen, appearance, position)
$manifest = [ordered]@{ date = '2026-10-10'; captures = @($items) }
$json = ConvertTo-Json -InputObject $manifest -Depth 8
Set-Content -LiteralPath (Join-Path $outputPath 'manifest.json') -Value $json -Encoding utf8
# JSON embutido permite abrir a galeria offline sem fetch nem servidor.
$safeJson = $json.Replace('<', '\u003c').Replace('>', '\u003e').Replace('&', '\u0026')
$template = @'
<!doctype html>
<html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>NORA — Galeria das interfaces</title>
<style>
:root{font:16px/1.55 system-ui,sans-serif;color:#241b35;background:#f7f5fb}*{box-sizing:border-box}body{margin:0}header,main{max-width:1500px;margin:auto;padding:30px}header{padding-bottom:12px}h1{font-size:clamp(28px,4vw,44px);margin:0}p{max-width:90ch}.note{padding:16px 20px;background:#ede6fa;border-radius:12px}nav{display:flex;gap:12px;flex-wrap:wrap;margin:24px 0}select,input,button{font:inherit;padding:10px 14px;border:1px solid #c5bdcf;border-radius:8px;background:white;color:inherit}input{flex:1;min-width:200px}button{cursor:pointer}button:focus-visible,select:focus-visible,input:focus-visible,a:focus-visible{outline:3px solid #6d28d9;outline-offset:3px}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,340px),1fr));gap:24px}article{border:1px solid #ddd5e7;border-radius:16px;overflow:hidden;background:white}article button{display:block;width:100%;padding:0;border:0;border-radius:0;background:#eee9f5}article img{width:100%;height:250px;object-fit:contain;display:block}article .caption{padding:16px}article h2{font-size:18px;margin:0 0 6px}.meta{color:#625970;font-size:14px;margin:0 0 6px}.source{font-size:13px;margin:8px 0}.count{color:#625970}dialog{border:0;border-radius:12px;padding:18px;width:96vw;max-width:1500px;height:94vh;background:#f7f5fb;color:#241b35}dialog::backdrop{background:#15111ecc}.viewer{display:flex;flex-direction:column;height:100%;gap:8px}.toolbar{display:flex;gap:12px;align-items:center;flex-wrap:wrap}.toolbar strong{flex:1}.image-area{overflow:auto;flex:1;text-align:center;background:#e8e2ef}.image-area img{max-width:100%;height:auto;vertical-align:top}.detail{margin:0;font-size:13px}footer{padding:20px 0}a{color:#6d28d9}@media(prefers-reduced-motion:no-preference){button:hover{filter:brightness(.97)}}
</style></head><body>
<header><p class="meta">REVISÃO VISUAL · 10 OUT 2026</p><h1>NORA, tela por tela</h1><p>Capturas da interface atual para conferir hierarquia, organização, aparência e fluxos disponíveis. Abra uma imagem para ver em tamanho maior; use as setas para navegar.</p><p class="note">Desktop: controles WPF renderizados nativamente. Android: componentes Compose renderizados pelo Layoutlib. Telas preenchidas usam dados sintéticos exclusivos dos testes. Esta galeria não comprova integração PostgreSQL, execução em dispositivo, nem funcionalidades ainda pendentes como chave OBS e painel financeiro.</p></header>
<main><nav aria-label="Filtros da galeria"><select id="app" aria-label="Aplicativo"><option value="">Todos os aplicativos</option></select><select id="theme" aria-label="Aparência"><option value="">Todas as aparências</option><option value="light">Clara</option><option value="dark">Escura</option><option value="system">Sistema</option></select><input id="search" type="search" placeholder="Buscar tela, fluxo ou configuração" aria-label="Buscar captura"></nav><p class="count" id="count" aria-live="polite"></p><section class="grid" id="grid" aria-label="Capturas das interfaces"></section><footer><a href="README.md">Índice e resultados da revisão</a> · <a href="manifest.json">Manifesto com origem e hashes</a></footer></main>
<dialog id="zoom" aria-label="Captura ampliada"><div class="viewer"><div class="toolbar"><button id="prev" aria-label="Captura anterior">←</button><button id="next" aria-label="Próxima captura">→</button><strong id="title"></strong><button id="close">Fechar</button></div><p id="detail" class="detail"></p><div class="image-area"><img id="large" alt=""></div><a id="original" target="_blank" rel="noopener">Abrir PNG original</a></div></dialog>
<script id="data" type="application/json">__MANIFEST__</script>
<script>
const captures=JSON.parse(document.getElementById('data').textContent).captures;
const $=id=>document.getElementById(id), themes={light:'Clara',dark:'Escura',system:'Sistema'};
let visible=[],selected=0;
for(const app of [...new Set(captures.map(c=>c.application))]){const o=document.createElement('option');o.value=app;o.textContent=app;$('app').append(o)}
function describe(c){return `${c.application} · ${c.screen} · ${themes[c.appearance]||c.appearance} · ${c.position}`}
function render(){const term=$('search').value.trim().toLocaleLowerCase('pt-BR');visible=captures.filter(c=>(!$('app').value||c.application===$('app').value)&&(!$('theme').value||c.appearance===$('theme').value)&&(!term||describe(c).toLocaleLowerCase('pt-BR').includes(term)));$('grid').replaceChildren();$('count').textContent=`${visible.length} de ${captures.length} capturas`;
visible.forEach((c,index)=>{const article=document.createElement('article'),button=document.createElement('button'),img=document.createElement('img'),caption=document.createElement('div'),h=document.createElement('h2'),meta=document.createElement('p'),source=document.createElement('p');img.src=c.file;img.alt=describe(c);img.loading='lazy';button.setAttribute('aria-label','Ampliar '+describe(c));button.append(img);button.onclick=()=>open(index);caption.className='caption';h.textContent=c.screen;meta.className='meta';meta.textContent=`${c.application} · ${themes[c.appearance]} · ${c.position} · ${c.width}×${c.height}`;source.className='source';source.textContent=c.dataSource;caption.append(h,meta,source);article.append(button,caption);$('grid').append(article)})}
function show(){const c=visible[selected];$('large').src=c.file;$('large').alt=describe(c);$('title').textContent=describe(c);$('detail').textContent=`${c.method} · ${c.dataSource}`;$('original').href=c.file;$('large').parentElement.scrollTop=0}
function open(index){selected=index;show();$('zoom').showModal()}
function step(delta){if(visible.length){selected=(selected+delta+visible.length)%visible.length;show()}}
for(const id of ['app','theme','search'])$(id).addEventListener('input',render);
$('close').onclick=()=>$('zoom').close();$('prev').onclick=()=>step(-1);$('next').onclick=()=>step(1);
$('zoom').addEventListener('keydown',e=>{if(e.key==='ArrowLeft'){step(-1);e.preventDefault()}if(e.key==='ArrowRight'){step(1);e.preventDefault()}});render();
</script></body></html>
'@
Set-Content -LiteralPath (Join-Path $outputPath 'index.html') -Value $template.Replace('__MANIFEST__', $safeJson) -Encoding utf8
Write-Output "Galeria gerada: $($items.Count) capturas; cópias SHA256 idênticas; $outputPath"
