# Acesso, cabeçalhos, filtros e preferências do NORA

## Pedido e objetivo

Implementar o plano aprovado de acesso, cabeçalhos e filtros e os acréscimos baseados no WPF do AEV. Melhorar os ícones e deixar a escrita mais visível e legível ao longo do OBS, do Painel e do APPNORA.

Skills e comandos invocados: Aura Retomar Execução, com `retomar-execucao` e verificação em loop. Aplicam-se os padrões de desenvolvimento WPF, a auditoria de filtros e a referência visual do plano aprovado.

## Requisitos e restrições

- Entrada compacta no OBS e no Painel com e-mail, senha, API pré-configurada, sessão comum de 24 horas e opção de manter a sessão por 30 dias. Ativação do computador do OBS no login com permissão para a unidade.
- Dados do perfil pelo menu inferior esquerdo. A proposta aprovada mostra e-mail e permite editar nome e senha.
- Título único com descrição útil, topo mais compacto, remoção do selo genérico e hover nos botões. No Android, manter o retorno ao toque, com hover quando houver ponteiro.
- Filtros no topo das telas de listagem dos três clientes. Mover os existentes e acrescentar controles quando fizer sentido. Preservar ordenação operacional, permissões e ações.
- Ctrl+F pesquisa a tela aberta nos dois softwares e apresenta os resultados no topo. Configurações em duas colunas com cartões de aparência, interruptores, atalhos editáveis, ícones e restauração de preferências.
- Cartão de exclusão programada inspirado no AEV, desligado por padrão. Restringir a cadastros administrativos elegíveis sem vínculos ou histórico e preservar a integridade dos dados. Validar apenas em base descartável.
- Manter a paleta e identidade do NORA. Preservar alterações de outras sessões no provisionamento municipal.

## Checklist

- [x] Conferir a documentação, o plano, a referência AEV e o estado inicial.
- [x] Implementar entrada, ativação, sessão e perfil. Verificar caminhos de sucesso, rejeição, restauração e expiração.
- [x] Implementar cabeçalhos, filtros e legibilidade nos dois softwares e no Android.
- [x] Implementar pesquisa, atalhos, preferências e a política de exclusão. Verificar busca, conflito de atalhos, restauração e estado inicial do cartão.
- [x] Validar a execução da exclusão e as migrações no PostgreSQL 18 descartável (P-057).
- [x] Rodar builds, suíte WPF, testes focados de autenticação da API, unitários e teste instrumentado de filtros no Android.
- [ ] Confirmar a suíte instrumentada completa do Android (P-058) e a conferência física já registrada em P-007.
- [x] Revisar o diff, atualizar a documentação e registrar pendências e resultados reais.

## Estado

Tarefa aberta em 23/11/2026. O plano e a referência do AEV foram conferidos antes da edição. A árvore contém também as alterações do provisionamento municipal de outra sessão. A API e os clientes WPF e Android receberam as alterações.

- Os 17 testes focados de autenticação da API passaram em PostgreSQL 17 descartável. Cobrimos login, ativação automática, escolha de unidade, concorrência, revogação, períodos de sessão e edição do perfil.
- No Android, passaram 300 testes unitários, o teste instrumentado de filtros em Consultas, lint sem erros e compilação Debug e Release. A versão Release foi gerada sem assinatura de distribuição por ausência da chave neste checkout.
- A compilação .NET Release terminou com zero erros e avisos. A suíte do WPF passou 190/190, com capturas dos temas e testes em 980 × 640 e 1280 × 740. A busca por texto sem acento, os filtros da Fila e do catálogo e a edição do perfil passaram em cenários nativos. A nova entrada do Painel restaurou uma sessão protegida de 30 dias, apagou o arquivo local ao sair e confirmou que o prazo de exclusão vem desligado e recolhido.
- A primeira tentativa de iniciar PostgreSQL 18 foi bloqueada por limite de uso. Na retomada, a suíte PostgreSQL 18.6 descartável terminou com 283/283 verificações nas suítes com resumo, incluindo 3/3 de exclusão. A P-057 foi resolvida. Nenhum banco de uso foi acessado.
- A suíte instrumentada integral do Android ficou sem resultado recuperável depois da interrupção do acesso ao emulador. O teste focado de Consultas está confirmado. A P-058 registra a verificação integral.

Em Release, o endereço de distribuição é o Render informado pelo usuário. O Windows em Debug usa a API local por padrão, com a opção de sobrepor por `NORA_API_URL`. O prazo de 24 horas vale com o cliente aberto. As sessões marcadas para reabrir usam o DPAPI do Windows.

O cartão de Configurações permite agendar e cancelar para os cadastros elegíveis. A reativação continua nas listas administrativas. Os cadastros OBS anteriores à migração ficam protegidos porque o histórico de sessões antigas pode ter expirado. O destino do servidor e os clientes novos precisam da publicação conjunta com as migrações após as verificações. O trabalho está no checkout local, junto a alterações preexistentes de provisionamento.
