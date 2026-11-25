# Cadastro de município no provisionamento

## Skills e comandos invocados

- Plugin Skills Aura e skill de verificação.

## Pedido

- Ao cadastrar um município no PowerShell, solicitar o nome do município, o nome completo do funcionário, o e-mail e a senha do funcionário, nessa ordem.
- Usar o termo funcionário.

## Objetivo

Registrar a identificação do município junto ao acesso municipal inicial.

## Restrições

- Preservar o funcionamento dos demais cadastros e dos dados existentes.

## Checklist

- [x] Verificar documentação e implementação existentes.
- [x] Implementar e registrar o município e os dados do funcionário no fluxo municipal.
- [x] Validar o comportamento e atualizar a documentação aplicável.

## Estado

O fluxo municipal solicita município, nome completo, e-mail e senha do funcionário. A migração 023 acrescenta o nome do município à conta, com valor nulo para contas anteriores. A gravação ocorre na mesma inserção da conta. O nome não aparece no painel e não estabelece isolamento entre municípios. O fluxo OBS mantém as perguntas anteriores.

Compilação isolada da API e dos testes em Release concluída sem erros nem avisos. A saída normal estava em uso por outro processo. Testes em PostgreSQL 17 descartável: migração reaplicável, fluxos 28/28 e fechamento 17/17, incluindo persistência e rejeição de município vazio. PostgreSQL 18 não estava disponível neste ambiente. A documentação da API e hospedagem foi atualizada. A migração precisa ser executada no banco de uso antes de criar contas com esta versão.

Pendência P-056 registra a validação no PostgreSQL 18. As alterações permanecem locais, sem commit ou publicação.

## Validação complementar no PostgreSQL 18

O usuário perguntou se é melhor usar PostgreSQL 18. A validação será repetida nessa versão em uma instância descartável, conforme o ambiente de referência do projeto.

- [x] Localizar ou preparar os binários do PostgreSQL 18.
- [x] Repetir a validação da migração e do provisionamento municipal.
- [x] Registrar o resultado e atualizar a pendência P-056.

Os binários locais do PostgreSQL 18.6 foram encontrados no perfil Pichau. A API e os testes compilaram em Release com zero erros e avisos. A suíte descartável na porta 55441, com bancos separados por UBS, terminou com código zero. As 273 verificações das suítes passaram, além da verificação de bancos separados por UBS. O fechamento passou em 17/17, incluindo a persistência do município e a rejeição de nome vazio. O log do servidor confirmou a versão 18.6 e o encerramento do banco descartável. A P-056 foi movida para Resolvidas.
