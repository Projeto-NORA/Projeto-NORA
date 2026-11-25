# Legibilidade e ícones do NORA Municipal

## Skills e comandos invocados

- Plugin Skills Aura, com `diagnosing-bugs` e `modo-loop-verificacao`.
- Skill Graphify para consultar o grafo já existente do projeto.

## Pedido

- Corrigir a escrita na área principal do NORA Municipal, cuja leitura é menos nítida que a dos botões da barra lateral. A referência é a captura de Usuários e histórico, com a observação de que a legibilidade varia conforme o texto.
- Criar ícones mais representativos para Unidades, Especialidades e Relatórios e metas e aplicá-los aos respectivos botões da barra lateral do Municipal.

## Objetivo e restrições

Melhorar a leitura na área principal e facilitar o reconhecimento das três entradas no menu do NORA Municipal. A captura serve como referência visual. O pedido se limita à apresentação dessas áreas.

## Checklist

- [x] Ler a documentação e reproduzir a diferença de legibilidade.
- [x] Corrigir a apresentação do texto da área principal.
- [x] Redesenhar os ícones de Unidades, Especialidades e Relatórios e metas.
- [x] Conferir a leitura, o menu e as verificações do desktop.
- [x] Revisar a documentação e o diff da tarefa.

## Estado

Pedido registrado em 24/11/2026, antes da investigação do código. A regressão em Usuários e histórico falhou antes da correção: os textos dos cartões estavam dentro de um `DropShadowEffect` mesmo com as animações desligadas. O efeito do WPF desativa o ClearType no conteúdo. O contraste dos rótulos verificados já era de 14,08:1 no tema claro e 13,29:1 no escuro.

O estilo compartilhado agora desativa a sombra no perfil Municipal. A regressão passou nos dois temas. Os três novos vetores representam uma UBS, um estetoscópio e um relatório com gráfico de barras. O teste de contraste, nome acessível e destino dos três botões passou em claro e escuro. Capturas com dados sintéticos foram comparadas e o desenho da UBS foi ajustado para se distinguir de Visão geral.

A versão final foi validada em uma cópia da `main`, na branch `codex/legibilidade-icones-municipal`. O build Release da solução terminou com zero erros e avisos. A regressão com dados sintéticos passou em 206/206 verificações inicialmente e em 207/207 após integrar a atualização dos filtros que chegou à `main` durante a tarefa. Foram geradas 56 capturas de 13 telas nos dois temas, e a abertura dos dois executáveis WPF passou. O teste E2E com PostgreSQL e a conferência no monitor da imagem original ficaram fora dessa execução.

A execução filtrada por `Municipal:` falha na verificação da faixa de retorno também no executável da `main` sem a correção visual. Na mesma base, a verificação passa sozinha. A diferença entre os dois recortes ficou registrada como P-066 em [Pendências](../PENDENCIAS.md).

O ajuste está descrito em [Design da família NORA](../design.md#legibilidade-e-ícones-do-nora-municipal). O diff da branch foi conferido para conter somente os arquivos desta tarefa.
