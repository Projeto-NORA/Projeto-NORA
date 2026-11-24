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
- [ ] Reproduzir e corrigir o segundo clique real nas telas afetadas (P-065).
- [ ] Verificar visualmente a seta nos estados aberto e fechado (P-065).
- [x] Acrescentar seta usando a cor de texto existente e vínculo ao estado do popup.
- [x] Compilar e rodar as verificações disponíveis de filtros.
- [x] Atualizar documentação e registrar pendência.

## Estado
O botão e o Popup são compartilhados entre Municipal e OBS em `Nora.Desktop/Views/ShellWindow.xaml`. A seta foi adicionada vinculada a `IsOpen`, apontando para baixo fechada e para cima aberta; usa a cor do botão. Build Release do Municipal: 0 erros e 0 avisos. Checks filtrados por Filtros: 8/8 passaram; avisos NU1900 por indisponibilidade do NuGet. Eles acionam o botão por automação, não reproduzem a captura de mouse do WPF e não comprovam o defeito relatado. A seta não foi inspecionada visualmente. Falta criar reprodução automatizada com mouse real, corrigir e verificar ambos os perfis; registrado na P-065.
