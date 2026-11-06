# Beta automática e Melhorias

Motores em `src/Menage.Core/BetaAutomatica/` e `src/Menage.Core/Melhorias/` (sem WPF); telas em `src/Menage.App/Modulos/BetaAutomatica/` e `Modulos/Melhorias/`. Dados em `%LOCALAPPDATA%\Menage\beta-automatica.json` e `melhorias.json` (`Armazenamento<T>`); logs em `logs-beta-auto\` (`monitor.log`, `console-beta.log`, uma pasta por geração).

## Beta automática

- **Jira só leitura** (`IClienteJira`, HTTP em `ClienteJiraHttp`, token na variável `JIRA_TOKEN`, endereço e variável configuráveis). Os testes usam um Jira falso; o Core não depende de outra branch.
- **Decisão por passagem** (`ServicoDeBetaAutomatica.Decidir`): a mesma função serve ao ciclo real e ao Diagnóstico. Re-entrada reabre como nova; ignorada só volta com nova passagem; o que já estava no status antes do monitor entra sem alerta; saiu para o status final = Finalizada.
- **Escopo**: dev (subtask Coding minha, comentário meu com `BRANCH:`/`PROJETOS ALTERADOS:`, ou era meu quando saiu de In Development para Waiting for Code Review) e Tester (veio de In Code Review; "somente atribuídas" ou "qualquer"; comentário de qualquer pessoa).
- **Verificação sem checkout** (`VerificadorDeBranches`): ref local, `origin/`, depois `fetch origin` (1 por projeto a cada 20 s). O Diagnóstico nunca faz fetch (`ls-remote`). Projeto web sem branch entra pela **tag** da versão do `pom.xml` da branch (sem `-SNAPSHOT`).
- **Geração**: `PreparadorDeClone` (clone isolado, fetch, `checkout -B beta-auto`, `reset --hard`, `clean -fdx`) em `PastaTrabalho` e depois o `ExecutorDoGerador` do Gerador de Beta (uma instância própria, então a tela do Gerador não mistura status). Sempre `install`. Falha de preparo ou de Maven marca o projeto e a fila segue. Ordem de dependência em `OrdemDeProjetos`; projeto fora dela abre o diálogo de ordem do Gerador (`beta.ordem`) e guarda a escolha.
- **JQL** (`GerenciadorDeJql`): padrão + personalizadas, marcadores `{projeto}`, `{status}`, `{escopo}`, validação, teste só leitura. Grava só o que difere do padrão.
- **Console** (`ConsoleDaBeta`): 20 mil linhas na memória, arquivo aparado em ~6 MB, filtros por nível/origem/atividade/texto.
- **Janela única com abas** (opção 4 do protótipo, `docs/prototipos-beta/com-abas.html`): `JanelaBetaAutomatica` tem as abas Betas (lista + detalhe, gavetas Ignoradas/Finalizadas), Configurar, JQL e Diagnóstico (um UserControl e um ViewModel por aba, com transição) e o **console ancorado** embaixo (recolhível, altura própria, contador de erros). Não existem mais as janelas separadas de Configurar/JQL/Diagnóstico/Console. Capturas em `docs/galeria/beta-abas-opcao4/` (`--galeria-beta-melhorias`).
- Comandos: `beta-auto.painel` (**Ctrl+Shift+B**, aba Betas; o Ctrl+B da janela principal recolhe a barra), `beta-auto.configurar`, `beta-auto.jql`, `beta-auto.diagnostico` (cada um abre a janela na aba certa), `beta-auto.console` (Ctrl+Shift+T: abre a janela e expande o console), `beta-auto.verificar`. Perfis Desenvolvedor e Tester; o Geral não vê.
- Status de finalização automática aceita vários (separados por vírgula; chips na aba Configurar).
- O monitor sobe na abertura do app (`InicioDaBetaAutomatica`) só se estava ativo e o perfil é Dev/Tester; atividades presas em "Na fila/Gerando" voltam para Pronta.

## Melhorias

- **Cadastro** (`RepositorioDeMelhorias`): id, título, status, notas, tags, branch e base por projeto, perfis de banco (por id). Tags: renomear/remover em todas, cor por tag, visões prontas com contagem e visões salvas.
- **Busca** (`FiltroDeMelhorias`): `tag:`, `-tag:`, `status:`, `projeto:`, `banco:`, `ativa:`, `usada:`, `sem:`, frases, sem acento.
- **Ativar** (`AtivadorDeMelhorias`): prévia (só leitura), auto-stash (`AutoStash` do Checkout), troca/`fetch`/cria da base, conferência "N de M na branch", perfil de banco, `version.conf`, sqlite, estado anterior guardado. **Desvincular** devolve branches (stash volta), version.conf, sqlite e banco; com erro mantém tudo para tentar de novo.
- **version.conf** (`VersaoConfDaMelhoria`) e **sqlite por atividade** (`SqliteDeAtividade`): nunca sobrescrevem; arquivo aberto pergunta "Tentar de novo".
- **Banco**: o módulo de contexto do banco é de outra branch. `IContextoDeBanco` (Core/Melhorias) é a ponte; sem implementação vale `SemContextoDeBanco` (aba Banco vazia, nada é aplicado). Quem entregar o módulo de banco registra a implementação antes de `AddMelhoriasApp` (usa `TryAdd`).
- Comandos: `melhorias.painel` (**Ctrl+M**), `melhorias.nova`, `melhorias.desvincular`. Só perfil Desenvolvedor.

## O que NÃO foi migrado nesta branch

- Seção "MELHORIA EM ANDAMENTO" na barra lateral da janela principal e a marcação laranja na grade (a janela principal é de outra branch; o painel e o atalho já funcionam).
- Chips de branch/tag da melhoria no Checkout, sugestão do id em Nova branch/Buscar commit/Commit, e a sincronização automática de version.conf/sqlite ao fazer Checkout/Nova branch/Nova tag fora de uma melhoria (precisam de ganchos nas operações em lote).
- Modo beta dos frameworks na ativação (frameworks sem branch na melhoria).
- Quem tem o arquivo sqlite aberto: o aviso diz "outro programa (ex.: DBeaver)", sem o nome (o Restart Manager antigo não foi portado).
- "Importar atuais" virou desnecessário (toda atividade do escopo já entra, sem alerta se era anterior ao monitor); "Verificar agora" cobre.
- Ícone na bandeja e "iniciar com o Windows" da Beta automática; abas Visão geral do dashboard sem gráficos; "Commits" sem copiar hash por duplo clique.

## Testar

```bash
dotnet build tests/Menage.Tests -warnaserror
dotnet test tests/Menage.Tests --filter "FullyQualifiedName~Menage.Tests.BetaAutomatica|FullyQualifiedName~Menage.Tests.Melhorias"
dotnet run --project src/Menage.App -- --galeria-beta-melhorias docs/galeria/beta-automatica-melhorias
```

Os testes usam repositórios descartáveis e `mvn.cmd` falso em `%TEMP%` (caminho curto) e um Jira falso. Nada roda deploy nem toca em `C:\wmw\dev\dev1`. A galeria usa dados de exemplo e não chama Jira, git nem Maven.

## Roteiro manual

Precisa de `JIRA_TOKEN` na máquina. Use projetos de teste (`--projetos <pasta>`), nunca os reais.

1. Perfil Desenvolvedor: Ctrl+K "Betas habilitadas". **Configurar** (URL, status, pasta dos clones). **JQL...** > Testar esta JQL: lista só leitura. **Diagnóstico** > Simular: confere decisões sem alterar nada.
2. **Ativar monitor**: a 1ª consulta só grava a referência. Mova uma atividade sua para Waiting for Testing com `BRANCH: <branch existente>` no comentário: cartão "Pronta para gerar" e o painel abre.
3. **Gerar beta**: acompanhe no **Console** (Ctrl+Shift+T): clone, fetch, Maven install. Cancelar numa atividade; falha de um projeto não para os outros.
4. Ignorar/Reverter, Finalizar/Reabrir, Remover (volta na próxima consulta), Limpar lista.
5. Perfil Desenvolvedor: Ctrl+M. **Nova melhoria** (2 projetos de teste, criar branches). Ative: confira a prévia, pendências em stash e o resumo. **Desvincular** devolve tudo.
6. Busca: `tag:urgente -tag:lebon`, `usada:7`, visões, **Salvar** visão, renomear/remover tag (botão direito no chip), cores.
7. Ligue version.conf com um arquivo de teste e ative/desvincule; ligue sqlite com uma pasta de teste e troque de melhoria (abra o arquivo noutro programa para ver o "Tentar de novo").
