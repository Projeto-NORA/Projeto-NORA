# App do paciente

## Objetivo

O APPNORA oferece ao paciente um ponto simples para consultar a relação com a unidade de saúde. A navegação principal reúne Início, Jornada, Avisos, Família e Perfil. Configurações fica disponível pelo botão no topo e pelo Perfil. Da tela inicial, a pessoa chega diretamente a consultas, agendamento, unidades de saúde e Vaga Relâmpago.

O app do paciente usa branco e azul-marinho, com acentos em azul e turquesa, degradês, cartões elevados e formas iluminadas nos cabeçalhos das abas. Esses cabeçalhos mantêm uma base azul escura e texto claro nos dois temas. A tela de entrada usa a marca sem subtítulo ou esfera decorativa, atalho de ajuda, CPF/e-mail, senha com controle de olho, recuperação e criação de conta; a configuração técnica da unidade fica recolhida. A ajuda também abre Configurações antes do login, sem acrescentar uma engrenagem à entrada. A navegação inferior aparece após autenticação; telas secundárias oferecem retorno visível ao início. Os modos claro, escuro e sistema mantêm os mesmos papéis de cor. O app não preenche telas clínicas com consultas, distâncias ou registros fictícios. Quando a API ainda não fornece um dado, a tela identifica essa ausência.

## Navegação e conteúdo

### Início

Apresenta um atalho para consultar a agenda da UBS vinculada. Cards de acesso rápido levam a consultas, UBS, Jornada e Vaga Relâmpago. Com a sessão conectada, também mostra o estado de fila e as ofertas que a API já fornece. Responder a uma oferta passa pelo serviço; o aceite cria uma consulta na UBS.

### Jornada

Organiza o espaço de consultas, exames, vacinas e histórico. O Índice de Jornada não é calculado pelo app até que a rede forneça dados e uma regra aprovada. A tela informa que o índice não altera fila, prioridade clínica ou direito ao atendimento.

### Avisos

Mostra ofertas pendentes já retornadas pela API. O estado vazio descreve apenas a ausência de ofertas pendentes, pois outros avisos e a entrega de notificações externas ainda não estão conectados.

### Família

Apresenta a área para dependentes e informa que o vínculo ainda não está disponível. Dados de terceiros não são exibidos sem uma associação autorizada pela rede.

### Perfil e Configurações

Perfil exibe a identidade que a sessão atual retorna e dá acesso às preferências, aparência e saída da conta. Sair da conta retorna à tela de entrada. A API atual não retorna CPF ou e-mail no endpoint de sessão.

### Consultas, UBS e Agendamento

Na tela de UBS, o paciente conectado pode usar “Vincular outra UBS” para selecionar até vinte unidades e conferir seu CPF nos cadastros locais. O resultado identifica as UBS vinculadas e aquelas em que não foi encontrado cadastro ativo. A seleção não substitui os vínculos existentes; uma UBS desativada não é reativada pelo aplicativo.

A tela Consultas permite consultar a agenda da UBS vinculada; o agendamento direto ainda não está disponível. A lista de unidades vinculadas ao paciente é consultada pela sessão autenticada; o seletor permite ver nome, região e endereço da unidade escolhida. O paciente pode cancelar uma consulta após confirmação. Confirmar presença, selecionar horário ou concluir um agendamento permanecem indisponíveis.

Na tela de UBS, o mapa incorporado do OpenStreetMap oferece deslocamento e zoom. O botão “Encontrar minha UBS” solicita localização aproximada e usa uma posição disponível no aparelho com até dez minutos para selecionar a UBS vinculada mais próxima. Se não houver UBS vinculada, mostra a região no mapa. A posição aproximada pode ser enviada ao OpenStreetMap para centralizar o mapa. Sem permissão ou posição recente, a pessoa pode navegar manualmente. O mapa requer internet e atribui os dados ao OpenStreetMap. A lista vem da rota autenticada do paciente e contém apenas unidades vinculadas ao cadastro. Os vínculos são criados no cadastro inicial ou solicitados posteriormente pelo aplicativo; uma unidade desativada pela equipe deixa de aparecer até ser reativada. A remoção voluntária de vínculos ainda não está implementada. A pessoa pode selecionar uma UBS pelo nome para ver região, endereço e um marcador com as coordenadas cadastradas pela Secretaria; a seleção fica em memória e é apagada ao sair. O mapa envia somente as coordenadas escolhidas ao OpenStreetMap. A distância é usada somente para escolher a UBS vinculada mais próxima; não é exibida. Serviços, horários e ordenação por distância permanecem pendentes.

O mapa incorporado ajusta a altura da página à altura disponível do WebView e acompanha mudanças de tamanho. Essa correção evita a área branca observada quando o conteúdo de altura percentual recebia altura zero dentro da tela rolável. O término do carregamento da página não confirma que o mapa foi desenhado. A tela orienta a recarregar se o mapa não aparecer e informa erros de rede, HTTP e conexão segura; erros de certificado não são ignorados.

Em novembro de 2026, o componente do mapa foi aberto no emulador com um ponto fictício em São José dos Campos: ruas, marcador e controles de zoom foram exibidos. A captura usa uma tela temporária de diagnóstico e não representa a tela completa do aplicativo. Os 26 testes HTTP da API passaram, mas os testes PostgreSQL não foram executados por falta de banco descartável; a consulta autenticada de unidades não foi validada de ponta a ponta nessa execução.

### Criar conta e entrar com Google

A tela de cadastro recebe nome, CPF e senha, permite selecionar várias UBS e oferece os e-mails previamente cadastrados nessas unidades de forma parcialmente mascarada. Após o código enviado ao endereço escolhido ser confirmado, a conta é criada e o aplicativo informa as unidades vinculadas e as que não encontraram o CPF. O e-mail confirmado passa a ser usado no acesso e na recuperação de senha. O login por CPF ou e-mail usa o endpoint atual de sessão. O aplicativo verifica localmente os dígitos do CPF no login, na recuperação e no campo de cadastro; isso não verifica a identidade nem a situação cadastral na Receita. A API também valida o CPF no cadastro feito pela UBS e na recuperação. “Esqueci a senha” pede o CPF, solicita um código de seis dígitos ao serviço, valida o código e permite definir uma nova senha. Código inválido abre uma escolha entre tentar outro CPF ou reenviar; o formulário informa erros de conexão sem confirmar se um CPF existe. A recuperação depende da migração 005 e da configuração SMTP/segredo no servidor. “Continuar com Google” informa que OAuth precisa ser habilitado pela rede antes de autenticar. A configuração do endereço técnico do serviço fica recolhida para manter o primeiro acesso mais simples.

Login e cadastro atualmente não solicitam endereço, portanto não consultam CEP. Uma consulta de CEP pode ser acrescentada quando a rede oferecer armazenamento e edição de endereço; os campos devem continuar editáveis caso o serviço externo não responda. A verificação de situação cadastral junto à Receita depende de integração autorizada; o cálculo local dos dígitos não a substitui.

## Referências de interface

O painel inicial usa como referência o padrão de apps de saúde que priorizam a próxima consulta e as ações mais usadas. One Medical apresenta agendamento, lembretes e busca de unidades no próprio app; o MyChart organiza consultas, mensagens e resultados por tarefas. A navegação compacta mantém cinco destinos principais, em linha com a recomendação do Material 3 para barras inferiores. Essas referências orientam a organização, sem copiar a marca ou os componentes proprietários.

- [Amazon One Medical](https://health.amazon.com/onemedical)
- [Recursos do MyChart Mobile](https://mychart.ynhhs.org/mychart-prd/en-US/MyChartApp.html)
- [Navigation bar — Android Developers](https://developer.android.com/develop/ui/compose/components/navigation-bar)
- [Botões preenchidos — Android Developers](https://developer.android.com/develop/ui/compose/quick-guides/content/create-button)
- [Diretrizes de botões — Apple Human Interface Guidelines](https://developer.apple.com/design/human-interface-guidelines/buttons?changes=latest_1)
- [Ajuda de acesso do MyChart](https://www.mychart.org/Help/login-how-to)
- [Configurações em aplicativos Android](https://developer.android.com/design/ui/mobile/guides/patterns/settings)
- [Navegação e organização de telas Android](https://developer.android.com/design/ui/mobile/guides/layout-and-content/layout-and-nav-patterns)
- [Material 3 e acessibilidade no Compose](https://developer.android.com/develop/ui/compose/designsystems/material3)

## Integrações existentes e limites

A API retorna a fila e as ofertas do paciente na UBS vinculada selecionada. A oferta contém especialidade, profissional, horário, prazo e estado; o aceite cria uma consulta na unidade. Distância e orientações adicionais ainda não são fornecidas.

Agendamento e consultas exigem entidades e rotas para unidades, serviços, profissionais, horários, confirmações e cancelamentos. Jornada exige registros de consultas, exames e vacinas, além de uma definição aprovada para o Índice de Jornada que o mantenha separado de prioridade clínica. Modo Família exige vínculos de dependência e autorização por pessoa. Notificações externas precisam de canal de entrega e preferências vinculadas ao servidor.

## Execução

O projeto Android fica em `APPNORA`. Use o JDK 21 selecionado pelo Android Studio e o emulador Android com o wrapper Gradle conforme `execucao.md` e `android-emulador.md`. O app é um cliente Compose; os fluxos clínicos dependem da API, não de dados locais de demonstração.

## Integração da lista de UBS — 2026-11-07

A rota e a tela da lista foram implementadas, mas o fluxo completo entre Android, API e PostgreSQL descartável ainda precisa de validação. A solução .NET compilou em Release e os 26 checks HTTP existentes passaram; o teste de cadastro da UBS seguido de consulta pelo paciente passou com PostgreSQL 18 descartável, incluindo a verificação de que outro paciente sem vínculo não recebe a unidade. O build Android chegou a `assembleDebug`. Após uma falha ao excluir o diretório de resultados anterior, os 9 testes locais passaram em uma pasta separada.
 O teste instrumentado isolado no emulador passou para carregamento e seleção da UBS vinculada mais próxima com coordenadas sintéticas e limpeza no logout, preservando o app já instalado. O acesso à localização real e o mapa ainda não foram validados ponta a ponta.

A API e o fluxo do controlador Android foram preparados para esse cadastro. Em 2026-11-09, o controlador Android passou em emulador com API e bancos PostgreSQL separados descartáveis; o envio do código foi simulado. Um teste Compose separado passou na seleção de múltiplas unidades após a atualização das dependências de teste. A interação completa da tela com a API e a entrega real por SMTP ainda precisam de validação.

## Consultas por UBS

A tela Consultas permite escolher uma UBS vinculada e carregar as consultas do próprio paciente, em uma janela de 180 dias antes e depois da data atual. O servidor confere o vínculo e filtra pelo CPF da sessão. O paciente pode cancelar consultas agendadas ou chamadas após confirmação; a API registra sua autoria. Confirmação de presença e agendamento direto continuam indisponíveis.

## Fila e Vaga Relâmpago por UBS

O paciente seleciona uma UBS vinculada para consultar suas entradas e ofertas. As oportunidades têm prazo de dez minutos; ao aceitar, a API cria a consulta no banco da UBS. Recusar libera o horário para a próxima pessoa elegível. A interface ainda depende de validação completa com dados sintéticos, e notificações externas não foram implementadas.
