# Validação do cadastro de médicos da simulação

## Skills e comandos invocados

- Plugin Aura Retomar Execucao, com a skill `retomar-execucao`.
- Retomada pelas seções 10 e 9 de `CONTEXTO-HANDOFF.md`.

## Pedido e objetivo

Corrigir `scripts/cadastrar-medicos-simulacao.ps1`, validar a seleção da UBS e o cadastro de médicos, consultórios e grade numa API descartável e encerrar todos os serviços de teste. Completar a checklist da seção 9 e coordenar as alterações com os outros chats do NORA.

## Restrições

Usar o worktree da simulação, preservar outras alterações e manter as operações de teste isoladas. A validação no Render depende da disponibilidade da API e da UBS da simulação. O ensaio no celular e a retirada dos instaladores seguem os gatilhos da seção 9. A abertura de PR depende de pedido do usuário.

## Checklist

- [x] Conferir handoff, branch, alterações locais e chat concorrente.
- [x] Reproduzir e corrigir a seleção e a lista de nomes de UBS.
- [x] Validar os passos 3 a 5, repetir o cadastro e conferir os registros.
- [x] Desligar a API e o PostgreSQL descartáveis e verificar a limpeza.
- [x] Atualizar os dois guias e registrar a decisão no documento original da tarefa.
- [x] Conferir os itens condicionais da seção 9 e o resultado antes da entrega.

## Estado

- O worktree da simulação estava limpo no commit `9a73d2e`. O chat “Planejar integração com Render e Neon” validou a conversão da URL do Neon separadamente e enviou a alteração na branch `codex/conexao-url-neon`. O [PR da conexão](https://github.com/Projeto-NORA/Projeto-NORA/pull/1) aguardava integração à `main` para seguir ao Render.
- A reprodução mostrou que `GET /api/obs/units` entrega uma lista na raiz. O acesso incorreto ao campo `units` deixava vazios os nomes exibidos e impedia o filtro. Com a correção, a resposta ambígua exibiu as três UBS, o filtro sem correspondência recusou o cadastro e `-Ubs 'UBS Cadastro E2E 1'` selecionou a unidade esperada.
- Na fixture com PostgreSQL 18, os passos 3 a 5 criaram 40 médicos, 20 consultórios e 200 faixas na UBS escolhida. O catálogo da rede continha 20 especialidades ativas. A leitura pela API confirmou cinco dias por médico, turnos e vínculos corretos. As outras duas UBS ficaram vazias. A segunda execução preservou os mesmos IDs e fez zero novas gravações. Em outro teste, o script restaurou a única faixa removida, preservou uma faixa personalizada e respeitou `-SemGrade`.
- O PowerShell 7.6.9 aceitou a sintaxe e executou as requisições com os códigos HTTP esperados. `dotnet build tests/Nora.Api.Checks -c Release --disable-build-servers -p:NuGetAudit=false` concluiu com zero avisos e erros. A suíte `--scheduling-only` passou em 21 de 21 verificações. Cada rodada encerrou a API e o PostgreSQL e verificou processos, portas e arquivos de controle.
- Atualizados `docs/simulacao-escola.md`, a parte 2.3 de `passo-a-passo-final-simulacao.md` em `Mateus-Projects` e o documento original da tarefa. Os guias explicam o vínculo da conta do paciente pelo CPF e o envio de código ao e-mail informado no OBS. O item de atualização do cronograma depende de troca de nomes, condição que não ocorreu.
- **P-049.** A consulta real ao `/health/ready` do Render expirou em 30 s sem receber resposta. O cadastro na UBS Escola e a conferência no app dependem da API pronta. O resultado foi registrado em `docs/PENDENCIAS.md`.
- **P-062.** O ensaio no celular Android real continua no roteiro de testes sugeridos após a implantação.
- **P-063.** A retirada dos dois instaladores de desktop continua prevista para depois da simulação.
- A abertura de PR segue condicionada a pedido do usuário.
