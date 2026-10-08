# Estudo do acesso OBS por chave

Pesquisa em 2026-10-08 para a solicitação de ativação gerada no painel central, sem tela de login de administrador ou atendente no OBS. Este documento apresenta fatos verificados e uma proposta; não registra aprovação de alcance, prazo ou funcionamento offline. Essas decisões continuam em P-014. O painel financeiro pertence à etapa seguinte, P-015.

## Avaliação da ideia

A chave é uma boa forma de simplificar a instalação e o controle dos computadores autorizados. Recomendo usá-la como **convite de ativação de uso único**, trocado por uma credencial própria da instalação, com revogação no painel. Não recomendo uma chave permanente compartilhada por todos os computadores da clínica.

Essa é uma recomendação técnica para o NORA, baseada na distinção entre identidade e autorização e no risco de vazamento de credenciais. A OWASP diferencia autenticação de autorização, orienta privilégio mínimo, bloqueio por padrão e validação em cada requisição. Sua orientação REST também desaconselha proteger recursos sensíveis exclusivamente por API keys. Nenhuma dessas fontes afirma que uma chave comercial identifica a pessoa que está operando a clínica. [OWASP Authorization](https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html), [OWASP REST Security](https://cheatsheetseries.owasp.org/cheatsheets/REST_Security_Cheat_Sheet.html).

O pedido de ausência de login OBS pode ser atendido com abertura automática após ativação. A consequência precisa ser explícita: o sistema identificará o computador/perfil autorizado; se várias pessoas compartilham a mesma sessão Windows, não haverá comprovação individual de quem cadastrou um paciente ou cancelou uma entrada. Registrar um nome digitado livremente não resolve essa limitação.

## O que existe na referência Oliver Imports

A leitura foi somente local, sem executar o aplicativo ou alterar o projeto em `C:/Users/mateu/source/repos/OliverImportsBusinnes`. Foram consultados os READMEs, `Panel-Desktop/docs/ARQUITETURA_INTEGRACAO.md` e `Sistema-Gerenciamento-Estoque/docs/SECURITY.md`, `ONBOARDING.md` e `INICIALIZACAO.md` antes do código.

O estoque tem **ativação da instalação e login de funcionário**. O bootstrap carrega `SessaoUsuarioService` e abre `TelaLogin` quando não há sessão restaurada. A opção de manter usuário conectado evita repetir a senha, mas preserva uma identidade humana. Isso aparece em [Aplicativo.xaml.cs](C:/Users/mateu/source/repos/OliverImportsBusinnes/Sistema-Gerenciamento-Estoque/Sistema-Gerenciamento-Estoque/App/Aplicativo.xaml.cs:199) e na documentação de segurança.

A API separa o código inicial da credencial do dispositivo. `ActivationService` prepara o material necessário antes do consumo do código e trata ativação recuperável, código vencido, código usado em outro computador e falta de vagas. O armazenamento local `installation.bin` é protegido com DPAPI `CurrentUser`. Evidências: [ActivationService.cs](C:/Users/mateu/source/repos/OliverImportsBusinnes/Panel-Desktop/OliverLicensing.Api/Services/ActivationService.cs:20) e [InstalacaoLocalService.cs](C:/Users/mateu/source/repos/OliverImportsBusinnes/Sistema-Gerenciamento-Estoque/Sistema-Gerenciamento-Estoque/Services/InstalacaoLocalService.cs:257).

O padrão de ativação pode orientar NORA, mas não deve ser copiado integralmente: a referência provisiona conexão ao banco operacional da loja. No NORA, os clientes continuam acessando apenas a API; senha PostgreSQL não deve ser entregue ao OBS.

## Fluxo proposto, sujeito à confirmação

1. O dono autenticado no painel gera um convite para uma clínica e uma instalação. A chave aparece integralmente somente nessa criação; o servidor guarda hash, prazo, emissor e estado.
2. O OBS solicita a chave uma vez. A API valida convite, limites e vínculo e cria a autorização em transação. Consumo concorrente só pode autorizar uma instalação.
3. O servidor retorna credencial específica da instalação. O OBS a grava protegida pelo Windows; a chave inicial deixa de ser a credencial cotidiana.
4. Nas próximas aberturas, a credencial permite obter sessão automaticamente. O servidor verifica estado da instalação e sua clínica em todas as operações. O painel pode revogar o computador e invalidar suas sessões.
5. Se a resposta da ativação se perde ou a gravação local falha, uma recuperação limitada para a mesma instalação deve evitar consumir uma segunda chave. A política e o prazo dessa recuperação ainda precisam ser aprovados.

Os itens acima são a proposta deste estudo, não uma implementação existente. O vínculo de clínica deve nascer da autorização no servidor; não aceitar um `clinicId` enviado pelo cliente como prova de acesso. A OWASP descreve a autorização por objeto como necessária em cada operação que recebe identificadores, incluindo casos de acesso a registros de outras organizações. [OWASP API1:2023](https://api-security.owasp.org/editions/2023/en/0xa1-broken-object-level-authorization/).

## Sessões, revogação e protocolos

O RFC 8628 permite aprovação de dispositivo em outra superfície, com códigos temporários e consulta periódica. Ele foi pensado para dispositivos com entrada/navegador limitados e afirma que não substitui a autorização por navegador em aplicativos nativos capazes. Portanto, chave de ativação própria do OBS não deve ser anunciada como implementação do RFC 8628. Se futuramente houver autorização OAuth do proprietário pelo navegador, o padrão nativo indicado é navegador externo com PKCE. [RFC 8628](https://www.rfc-editor.org/info/rfc8628/), [RFC 8252](https://www.rfc-editor.org/info/rfc8252/).

A revogação deve atingir a autorização do dispositivo e as sessões associadas. O RFC 7009 descreve revogação de tokens de acesso/renovação e a possibilidade de invalidar concessões relacionadas. O RFC 9700 exige proteção contra repetição de refresh token em clientes públicos por vinculação criptográfica ou rotação. Recomendo sessões curtas e renovação revogável, com detecção de reutilização se for escolhido refresh token. Não há exigência de transformar a API atual em servidor OAuth para adotar essas propriedades. [RFC 7009](https://www.rfc-editor.org/info/rfc7009/), [RFC 9700, seção 4.14](https://www.rfc-editor.org/info/rfc9700/#section-4.14).

Uma autorização validada somente offline não recebe revogação enquanto estiver desconectada. Como NORA já depende de API/PostgreSQL, recomendo manter acesso operacional online nesta etapa, sem criar cache de pacientes ou licença offline por conveniência. Essa é uma recomendação de escopo e segurança, ainda sujeita à decisão do usuário.

## Proteção Windows e identidade humana

DPAPI `CurrentUser` restringe a descriptografia ao contexto da conta Windows. `LocalMachine` permite que outras contas da máquina descriptografem o material e não é a escolha indicada para um computador com usuários não confiáveis. DPAPI protege o segredo em repouso; não identifica o funcionário, não impede acesso por processo malicioso na mesma conta e não substitui revogação no servidor. [Microsoft DataProtectionScope](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.dataprotectionscope), [Microsoft CryptProtectData](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata).

Um UUID de instalação ou impressão digital enviada junto de um bearer token não prova criptograficamente a posse daquele computador. Se for necessária resistência maior à cópia de credenciais, considerar chave privada protegida por TPM e desafio assinado. Windows Hello oferece autenticação por chave com gesto PIN/biometria e não expõe a chave privada à aplicação; a disponibilidade e integração WPF precisam ser verificadas antes de prometê-lo. [Microsoft Windows Hello](https://learn.microsoft.com/en-us/windows/apps/develop/security/windows-hello).

Windows Hello ou identidade Windows individual são melhorias possíveis sem formulário de senha NORA, mas exigem configuração e aprovação. O pedido atual não autoriza impô-las como login obrigatório. Sem identidade humana comprovada, a auditoria deve declarar corretamente dispositivo/autorização, ação, resultado e horário, sem inventar responsável nominal. Tokens, chaves, senhas, CPF e dados de saúde não devem aparecer nos logs de auditoria técnica. [OWASP Logging](https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html).

## Decisões necessárias antes da implementação

- Confirmar se cada convite ativa um computador/perfil Windows, quantos computadores uma clínica pode ter e como será a troca de equipamento.
- Confirmar prazo da chave inicial, política de renovação/revogação e se a operação será sempre online.
- Confirmar se a ausência de identidade humana individual é aceita ou se haverá conta Windows individual/Windows Hello posteriormente.

O estudo recomenda ativação individual por instalação, controle central revogável e preservação da simplicidade de abertura do OBS. A proposta apresentada ao usuário utiliza chave inicial válida por 24 horas e operação online. A confirmação dessa política e a definição de uma ou várias clínicas por API ainda são necessárias para implementar o isolamento adequado.
