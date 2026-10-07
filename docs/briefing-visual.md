# DIRETRIZES VISUAIS E DE UX — BASEADAS NO PROJETO ORIGINAL DOS MENINOS

IMPORTANTE:
Existe um projeto visual/protótipo anterior feito no Figma Make / React, já presente no repositório exportado.

Esse material NÃO deve ser reaproveitado como base técnica da aplicação final, mas DEVE ser analisado cuidadosamente como referência visual, estrutural e de UX.

Os arquivos observados incluem, entre outros:
- MobileApp
- UBSDashboard
- MunicipalDashboard
- componentes de UI
- estilos globais
- protótipos de interação

O objetivo é:
1. preservar a essência visual e estrutural do protótipo dos meninos;
2. reaproveitar suas boas ideias de layout e navegação;
3. melhorar consistência, acabamento e hierarquia visual;
4. adaptar a identidade para um visual mais alinhado à marca NORA;
5. puxar a estética para uma linha mais roxa;
6. garantir suporte completo a tema claro, escuro e sistema.

NÃO copiar cegamente o código do protótipo.
NÃO manter limitações do Figma Make.
USAR O PROTÓTIPO COMO REFERÊNCIA VISUAL E FUNCIONAL.

---

# REFERÊNCIA VISUAL OBRIGATÓRIA

A implementação visual deve se inspirar claramente no projeto anterior, especialmente em:

## 1. Estrutura do App Paciente
Manter o conceito de:
- interface amigável;
- foco em jornada do paciente;
- organização por telas simples;
- blocos/cartões informativos;
- destaque para consultas, fila e Vaga Relâmpago;
- visual leve e confiável.

## 2. Estrutura do sistema OBS
Manter o conceito de:
- sidebar lateral fixa;
- navegação administrativa clara;
- uso forte de cards;
- listagens e tabelas organizadas;
- barra de ações contextual ao selecionar um item;
- fluxo operacional eficiente.

## 3. Estrutura do sistema Municipal
Manter o conceito de:
- dashboard executivo;
- visão mais estratégica;
- cards de indicadores;
- gráficos;
- filtros;
- estrutura pensada para leitura gerencial.

---

# EVOLUÇÃO DO DESIGN

Apesar da referência visual anterior, a nova implementação deve ficar mais madura, mais consistente e mais ligada ao roxo.

A estética final deve transmitir:
- confiança;
- tecnologia;
- organização;
- clareza;
- saúde digital moderna.

Não transformar o sistema em algo excessivamente chamativo.
A prioridade é:
- legibilidade;
- consistência;
- facilidade de uso;
- acabamento profissional.

---

# PALETA VISUAL OBRIGATÓRIA

Adotar uma identidade visual centrada em roxo, com suporte a tema claro e escuro.

## Cor principal
Utilizar roxo como cor de marca e ação principal.

Sugestão de direção:
- Roxo principal
- Roxo escuro
- Roxo suave/lilás
- Branco
- Cinzas neutros

Pode definir tokens equivalentes, por exemplo:
- Primary / Brand
- PrimaryDark
- PrimaryLight
- Background
- Surface
- SurfaceAlt
- Border
- TextPrimary
- TextSecondary
- Success
- Warning
- Error
- Info

## Estados
Padronizar:
- verde = confirmado / concluído / sucesso
- amarelo = atenção / aguardando
- vermelho = cancelado / erro / bloqueio
- azul ou roxo claro = informação
- roxo = ação principal / destaque NORA

---

# TEMAS OBRIGATÓRIOS NOS 3 PROJETOS

OS 3 PROJETOS DEVEM TER CONFIGURAÇÕES DE APARÊNCIA PARA O USUÁRIO.

Projetos:
1. App Android do paciente
2. NORA OBS (WPF)
3. NORA Municipal (WPF)

Todos os 3 devem permitir escolher:

- Clara
- Escura
- Sistema

Essas três opções são obrigatórias.

A opção "Sistema" deve seguir o tema do sistema operacional/dispositivo quando tecnicamente possível.

A escolha do usuário deve:
- ser salva;
- ser persistida;
- ser carregada automaticamente ao abrir o app;
- atualizar a interface inteira corretamente.

---

# TELA DE CONFIGURAÇÕES OBRIGATÓRIA

Todos os 3 projetos devem possuir uma área de Configurações do usuário.

Essa tela deve se inspirar no padrão visual mostrado na referência enviada:
- título grande;
- subtítulo explicativo;
- cards de aparência;
- lista de preferências;
- layout limpo e administrativo.

## Parte obrigatória: Aparência
Exibir 3 opções em formato visual semelhante a cards de seleção:

- Clara
- Escura
- Sistema

Cada opção deve ter:
- nome;
- pequena descrição;
- indicação visual;
- estado selecionado claro.

Exemplo de descrições:
- Clara: tema claro com fundo claro
- Escura: tema escuro com fundo escuro
- Sistema: usar tema do sistema

---

# CONFIGURAÇÕES DO OBS E MUNICIPAL

Para os projetos desktop (OBS e Municipal), a tela de Configurações deve conter pelo menos:

## Aparência
- Clara
- Escura
- Sistema

## Preferências da interface
- mostrar dicas na barra lateral
- confirmar ao fechar o sistema
- salvar estado da navegação
- animações da interface
- lembrar estado da barra lateral
- modo tela cheia (quando fizer sentido)
- restaurar preferências padrão

## Atalhos
Criar uma área de atalhos configuráveis ou ao menos exibíveis, inspirada no modelo da referência.

Exemplos:
- ir para Home
- ir para Pacientes / Alunos
- ir para Consultas / Aulas
- pesquisar
- atualizar tela
- alternar tema
- alternar barra lateral
- entrar/sair de tela cheia
- fechar modal
- sair do sistema

Se a edição completa dos atalhos ficar complexa para a POC, no mínimo:
- exibir atalhos atuais;
- permitir futura expansão;
- manter estrutura preparada.

---

# CONFIGURAÇÕES DO APP ANDROID

O App Android também deve ter uma tela de Configurações.

Ela deve conter pelo menos:
- Aparência: Clara / Escura / Sistema
- Preferências de notificações
- Preferências gerais da interface
- opção de sair da conta
- informações do app / versão

No Android, não é obrigatório ter atalhos de teclado como no desktop, mas o padrão visual deve continuar coerente com a família NORA.

---

# SISTEMA DE TEMA COMPARTILHADO

Implementar tema de forma estruturada.

## No Android
Criar sistema de tema organizado em Jetpack Compose com suporte a:
- Light theme
- Dark theme
- Follow system

Usar paleta roxa como identidade central.

## No WPF
Criar sistema de tema reutilizável e limpo.
O tema deve conseguir trocar:
- cores de fundo
- cards
- bordas
- textos
- botões
- sidebar
- tabelas
- inputs
- modais
- gráficos
- indicadores

A troca de tema NÃO deve exigir reiniciar a aplicação, se possível.

Se reinicialização não for necessária, melhor.

---

# PADRÃO VISUAL DOS DASHBOARDS DESKTOP

OBS e Municipal devem compartilhar uma base visual consistente, porém com identidades levemente diferentes.

## OBS
Deve ter cara mais operacional.
Foco em:
- produtividade;
- cadastro;
- fluxo do dia a dia;
- tabelas;
- ações;
- fila;
- vagas;
- confirmação.

## Municipal
Deve ter cara mais analítica e executiva.
Foco em:
- indicadores;
- cards de resumo;
- gráficos;
- comparativos;
- filtros;
- visão ampla.

Ambos devem manter:
- sidebar lateral;
- hierarquia visual clara;
- cards arredondados;
- sombras leves;
- excelente legibilidade;
- consistência entre telas.

---

# BARRA LATERAL / SIDEBAR

Inspirar-se fortemente na organização lateral presente no projeto original.

A navegação lateral deve ser:
- limpa;
- clara;
- consistente;
- recolhível quando fizer sentido;
- compatível com tema claro e escuro.

A sidebar deve destacar:
- item ativo;
- hover;
- ícones;
- agrupamento coerente.

Usar roxo como principal elemento de destaque da marca e seleção.

---

# PADRÃO DE CARDS E PAINÉIS

Usar cards/painéis como principal unidade visual da interface.

Os cards devem seguir padrão consistente:
- cantos arredondados;
- boa separação visual;
- borda suave;
- sombra leve;
- padding adequado;
- hierarquia de título, subtítulo, conteúdo e ação.

Esse padrão deve ser usado nos 3 projetos.

---

# PADRÃO DE SELEÇÃO + AÇÕES

Preservar e evoluir a boa ideia visual do projeto antigo:
quando o usuário selecionar um item em listagens do OBS, exibir uma barra contextual de ações.

Esse padrão é importante e deve ser mantido.

Exemplo:
- selecionar uma consulta;
- aparecer barra de ações na parte inferior;
- exibir ações compatíveis com o item selecionado.

Essa barra deve ter aparência refinada e coerente com o novo tema roxo.

---

# ACABAMENTO VISUAL OBRIGATÓRIO

Melhorar o visual do protótipo antigo em:
- espaçamento;
- consistência de fontes;
- contraste;
- alinhamento;
- estados de hover/focus/selected;
- aparência de inputs;
- aparência de botões;
- componentes de filtro;
- tabelas;
- modais;
- empty states;
- loading states.

Não manter aparência "crua" de protótipo.

A entrega deve parecer um sistema real.

---

# ACESSIBILIDADE E USABILIDADE

Garantir:
- contraste suficiente em tema claro e escuro;
- texto legível;
- foco visível;
- componentes clicáveis com áreas adequadas;
- navegação coerente;
- consistência visual entre plataformas.

---

# O QUE FAZER COM O PROJETO DOS MENINOS

O projeto anterior deve ser usado como:
- referência de estrutura;
- referência de design;
- referência de navegação;
- referência de organização das áreas;
- referência de experiência visual.

O projeto anterior NÃO deve ser usado como:
- base técnica definitiva;
- código final a ser apenas adaptado;
- fonte de regras de negócio.

Extrair o melhor do design deles e reconstruir corretamente na stack final:
- Android Kotlin
- WPF OBS
- WPF Municipal
- ASP.NET Core API

---

# RESULTADO VISUAL ESPERADO

Ao final, os 3 projetos devem parecer parte da mesma plataforma NORA.

Características obrigatórias:
- mesma identidade visual;
- mesma linguagem de componentes;
- mesma lógica de tema;
- mesma noção de brand roxo;
- configuração de aparência em todos;
- coerência entre claro, escuro e sistema.

A experiência deve parecer:
profissional, moderna, consistente e pronta para demonstração.

Sim, manter:
- a base de design dos meninos;
- a estrutura de App / OBS / Municipal;
- a ideia de sidebar, cards, dashboards, ações contextuais.
Mas melhorar:
- identidade para roxo;
- consistência entre telas;
- tema claro / escuro / sistema;
- tela de configurações nos 3 projetos;
- refinamento visual geral.
