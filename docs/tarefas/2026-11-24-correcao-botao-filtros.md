# Correção do botão de filtros

## Skills e comandos invocados
- Nenhum comando ou skill invocado explicitamente pelo usuário. Aplicadas `diagnosing-bugs` e `padronizar-filtros`.

## Pedido
- Corrigir o botão de filtros para que um segundo clique feche a caixa aberta.
- Verificar se o problema ocorre nas demais telas.
- Acrescentar à direita de “Filtros” uma seta azul escura que indique se a caixa está aberta ou fechada.
- Preservar exatamente o design atual, salvo a adição da seta.

## Objetivo
Alternar corretamente a caixa de filtros e tornar seu estado visível.

## Restrições
Preservar o design existente e usar como referência as imagens fornecidas.

## Checklist
- [x] Reproduzir e corrigir o segundo clique real no componente compartilhado (P-065).
- [x] Verificar visualmente a seta nos estados aberto e fechado (P-065).
- [x] Acrescentar seta usando a cor de texto existente e vínculo ao estado do popup.
- [x] Compilar e rodar as verificações disponíveis de filtros.
- [x] Atualizar documentação e registrar pendência.

## Estado
O botão e o Popup são compartilhados entre Municipal e OBS em `Nora.Desktop/Views/ShellWindow.xaml`. A seta foi adicionada vinculada a `IsOpen`, apontando para baixo fechada e para cima aberta; usa a cor do botão. Build Release do Municipal: 0 erros e 0 avisos. Checks filtrados por Filtros: 8/8 passaram; avisos NU1900 por indisponibilidade do NuGet. Eles acionam o botão por automação, não reproduzem a captura de mouse do WPF e não comprovam o defeito relatado. A seta não foi inspecionada visualmente. Falta criar reprodução automatizada com mouse real, corrigir e verificar ambos os perfis; registrado na P-065.

## Retomada
Usuário pediu continuação com aura-retomar-execucao. A retomada ocorre na cópia isolada da main publicada, sem tocar nos arquivos da outra tarefa. Próximo passo: reproduzir o clique real, corrigir e validar também a seta.
Build inicial da retomada na cópia isolada: 0 erros e 0 avisos. Foi aberta uma janela WPF com dados sintéticos para reproduzir os cliques, mas o controle de interface não conseguiu ler sua árvore de acessibilidade e a captura retornou outra janela. A tentativa não reproduziu o defeito; a instrumentação temporária foi removida. O segundo clique e a inspeção visual continuam em P-065.

## Continuação solicitada
O usuário autorizou concluir as pendências desta correção e depois fazer commit e push para a main. Preservar alterações das demais sessões.
Nova tentativa com janela sintética ativada e em primeiro plano: a janela não apareceu na lista do controle de interface. O Municipal aberto na máquina estava em uso em outra tarefa; não foi alterado. O programa temporário foi removido. Correção e verificação ainda não realizadas.

## Correção verificada
Janela isolada com dados fictícios: o segundo clique reproduziu o defeito antes e fechou o painel depois da correção; seta apontou para cima aberta e para baixo fechada, e Escape fechou o painel. O fechamento automático do Popup antes do Click reabria o painel; o código reconhece o pressionamento no botão e não reabre. Build Release sem avisos ou erros; 9/9 verificações direcionadas, incluindo os dois perfis. O teste com mouse foi visual no Municipal; o botão e a lógica são compartilhados com o OBS. O teste automatizado da seta não simula a captura real de mouse.
A suíte desktop completa sem banco passou em 198/198 verificações. A automação da seta cobre os perfis Municipal e OBS; a reprodução física do mouse foi na janela Municipal com dados fictícios.
Após integrar a main com a localização da UBS (8161675), a compilação e as 9/9 verificações direcionadas de filtros passaram novamente.
