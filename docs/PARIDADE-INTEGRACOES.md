# Paridade: integrações e Maven (Git Manager antigo × Menage)

Auditoria da rodada 4 (branch `feature/paridade-integracoes`). Fontes comparadas: `docs/FUNCIONALIDADES.md` do Git Manager, os `.cs` de `gitmanager/visual` citados abaixo e o código do Menage (`src/Menage.Core`, `src/Menage.App/Modulos`), mais as seções "Decisões" e "O que NÃO foi migrado" dos documentos do próprio Menage.

**Como foi feito (e o limite):** li as funcionalidades do app antigo, os documentos de cada módulo novo e o código dos pontos onde achei divergência. Não li linha a linha todos os arquivos antigos; onde a coluna diz "ok", é porque o recurso existe no Core/tela novos e (quase sempre) tem teste. Nada foi clicado com mouse real.

Legenda: **ok** = existe e equivale; **diferente (decisão)** = mudou de propósito, já registrado nos docs de módulo; **faltava → feito** = corrigido nesta branch; **fora** = continua faltando, com o motivo.

## Gerador de Beta, Limpar target, Favoritos, Entregas

| Recurso antigo | Onde está no Menage | Status |
|---|---|---|
| Grade com um clique marca, Espaço marca, projetos marcados na principal vêm marcados | `Modulos/GeradorBeta`, `RoteiroDoGerador` | ok |
| Modo install/deploy, Tester só install | `PreparadorDeGeracao` | ok |
| Uma janela cmd por projeto (`cmd /k`) | log único + status por projeto + "Abrir cmd do projeto" | diferente (decisão): `docs/GERADOR-BETA.md` |
| Falha não para a fila; parar fila / interromper agora | `ExecutorDoGerador`, `GeradorBetaViewModel.Geracao` | ok |
| Limpar target (só `target` ao lado de `pom.xml`, ignora `.git`/`node_modules`, `[AVISO]` se em uso, não salva habilitado sem projeto) | `LimpadorDeTarget`, `JanelaDeLimparTarget` | ok |
| Favoritos (aplicar, aplicar e gerar, atualizar, renomear, excluir) | `GeradorBetaViewModel.Favoritos`, `CartaoDeFavorito` | ok |
| Repetição do surefire no lavenderepda | `ExecutorDoGerador` | ok |
| Entrega Web/PDA: pasta, `.zip`, área de transferência, Explorer, painel "Entrega pronta"; nunca Slack | `Core/Entregas`, `JanelaDeEntrega(s)` | ok |
| Entrega com pasta por chave de atividade (`VENDAS-123`) para a beta automática | — | fora (o app antigo também não enviava a entrega da beta automática; o Menage não faz perguntas de entrega nela) |
| Importar o `gerador-beta.json` antigo na 1ª vez | — | diferente (decisão): o Menage não lê arquivos do app antigo (CLAUDE.md) |

## Beta automática

| Recurso antigo | Onde está | Status |
|---|---|---|
| Decisão por passagem, re-entrada, ignoradas, finalizadas, remover/limpar lista | `ServicoDeBetaAutomatica.*` | ok |
| Escopo dev e tester; comentário `BRANCH:`/`PROJETOS ALTERADOS:`, curingas, siglas | `EscopoBeta`, `LeitorDeComentario` | ok |
| Verificação sem checkout, complemento web pela tag, Diagnóstico só leitura | `VerificadorDeBranches`, `ServicoDeBetaAutomatica.Diagnostico` | ok |
| Clones isolados, ordem de dependência, ordem perguntada na hora | `PreparadorDeClone`, `OrdemDeProjetos` | ok |
| JQL editáveis com validação e "Testar esta JQL" | `GerenciadorDeJql`, `JanelaDeJql` | ok |
| Console de diagnóstico (20 mil linhas, ~6 MB, filtros) | `ConsoleDaBeta`, `JanelaConsoleBeta` | ok |
| Alerta no canto da tela, ícone na bandeja, piscar na barra de tarefas, "Iniciar com o Windows", instância única/`--bandeja` | — | fora: exige NotifyIcon/Win32 próprios; já listado em `docs/BETA-AUTOMATICA-MELHORIAS.md` |

## Melhorias

| Recurso antigo | Onde está | Status |
|---|---|---|
| Cadastro, status, notas, tags com cor, visões, busca com operadores | `RepositorioDeMelhorias`, `FiltroDeMelhorias` | ok |
| Ativar com prévia, auto-stash, conferência N de M, banco, version.conf, sqlite; Desvincular | `AtivadorDeMelhorias` | ok |
| Ativar/desvincular sem proteção contra clique duplo e sem aviso de andamento | `MelhoriasViewModel.TrabalharAsync` | **faltava → feito**: botões apagados enquanto roda, mensagem "Ativando...", erro inesperado vira mensagem em português. Cancelar no meio **não** foi feito de propósito: deixaria projetos pela metade (o Desvincular guardado só vale para ativação completa). |
| Quem está com o `.sqlite3` aberto (Restart Manager) e janela "Tentar de novo" com o nome do programa | `ArquivoEmUso`, `SqliteDeAtividade.QuemUsa` | **faltava → feito**: a pergunta e o aviso agora dizem "aberto em DBeaver (dbeaver.exe)"; sem Restart Manager cai na mensagem genérica |
| Issue da melhoria cadastrada para a branch (Diário) | `ServicoDoDiario.IssueDaMelhoria` | **faltava → feito** (o gancho `issueDaBranch` existia mas ninguém passava; ver Diário) |
| Seção "MELHORIA EM ANDAMENTO" na lateral, grade em laranja, chips da melhoria no Checkout, id sugerido em Nova branch/Commit, sincronização de version.conf/sqlite ao fazer Checkout/Nova branch/Nova tag fora de uma melhoria, modo beta de frameworks na ativação | — | fora: precisa de ganchos nas operações em lote e na janela principal (código compartilhado, outra frente). Continua listado em `docs/BETA-AUTOMATICA-MELHORIAS.md` |
| Dashboard: abas Visão geral com indicadores clicáveis | `JanelaMelhorias` (sem gráficos) | fora (visual) |

## Diário e contexto do banco

| Recurso antigo | Onde está | Status |
|---|---|---|
| Coleta pela data de autoria, `--all`, sem merges, cherry-pick conta uma vez | `ColetorDoDiario` | ok |
| Issue pela mensagem, depois pela branch, **depois pela melhoria cadastrada** | `ReconhecedorDeIssue` + `ServicoDoDiario.IssueDaMelhoria` | **faltava → feito** (teste `Commit_sem_id_na_mensagem_nem_na_branch_usa_a_melhoria_cadastrada_para_a_branch`) |
| Jornada 8h48, blocos de 4h, almoço, um worklog por commit | `DistribuidorDeJornada` | ok |
| Resumo por regras, máscara de segredos, IA só no clique, conferência de coerência | `ResumoDoDiario`, `AnaliseDeDiff`, `ClienteDeIa` | ok |
| Lançar com confirmação, para no primeiro erro, não duplica; `JIRA_TOKEN` nunca gravado | `ClienteJira`, `ServicoDoDiario` | ok |
| Perfis do context.xml, marcadores `GIT-MANAGER:BANCO`, importar ativos e comentados, `.gm.bak` | `ContextoXml.*`, `ServicoDeBancoContexto`, `JanelaDoBanco` | ok (marcadores mantidos iguais de propósito) |

## Nexus, Tomcat Express, Pull Requests

| Recurso antigo | Onde está | Status |
|---|---|---|
| Nexus: busca, favoritos, download com progresso/cancelar, subpasta `beta_Nexus_*`, credencial só no host configurado | `Core/Nexus`, `JanelaDoNexus` | ok |
| Tomcat: preparar (2 `.war`, rollback, portas, backup), subir/parar, log traduzido, reencontrar por PID | `PreparadorDoTomcat`, `ExecucaoDoTomcat`, `InterpretadorDeLog` | ok |
| Tomcat: aba **Banco de dados** (linhas `db_*` do `wmw_database_config.bat`, padrão + WEB/APP/INT, validação, aviso Oracle ≠ `wmw`, "Testar alcance", backup único) | `Core/Tomcat/BancoDoTomcat`, `JanelaDoBancoTomcat` | **faltava → feito** (o doc do Nexus/Tomcat dizia "fica para outra branch"; só existia a validação de linhas inválidas). Backup é `.menage.bak` (o antigo usava `.gm.bak`; separação total dos dois programas) |
| PRs: listar (API/refs), validar na cópia temporária, mesclar na main local, desfazer se falhar, push só com a caixa | `Core/PullRequests`, `JanelaDosPullRequests` | ok |
| PRs: parar uma validação/mescla/listagem em andamento | `PullRequestsViewModel.Cancelar` + botão "Cancelar" | **faltava → feito**: o Core já aceitava o cancelamento, a tela não passava token nem tinha botão. Cancelar não marca "✖" no PR interrompido e a mescla devolve a main ao estado anterior |
| PRs: "subir versão, recompilar instalador e gravar `update/versao.txt`" ao publicar | `publicar.ps1`/Velopack | diferente (decisão) |

## Histórico de releases

Não auditado a fundo nesta rodada (janela `HistoricoReleases`, documentada em `docs/PERSONALIZACAO-HISTORICO.md`). Fica como pendência de leitura.

## Acabamento verificado (estados, cancelamento, segredos)

- Cancelamento presente: Gerador (parar fila/interromper), Beta automática (cancelar atividade), Nexus (download), Tomcat (preparo), Diário (carregar dia), **PRs (novo)**. Melhorias: sem cancelamento por decisão (acima).
- Segredos: Nexus em DPAPI, tokens do Jira/IA/GitHub só por variável de ambiente ou Git Credential Manager, `contexto-banco.json` com senha já criptografada. O editor do banco do Tomcat não escreve valores de senha em log nem em mensagens de erro (as mensagens citam só o nome do campo).
- Mensagens em português com causa/explicação/ação (`Erro`) nas novas operações.

## Testes desta branch

```bash
dotnet build Menage.sln -warnaserror
dotnet test tests/Menage.Tests --filter "FullyQualifiedName~BancoDoTomcat|FullyQualifiedName~Menage.Tests.Melhorias|FullyQualifiedName~Menage.Tests.Diario|FullyQualifiedName~Menage.Tests.PullRequests"
dotnet run --project src/Menage.App -- --galeria-nexus-tomcat-prs docs/galeria/nexus-tomcat-prs
```

Novos testes: `BancoDoTomcatTests` (10: leitura, herança do padrão, gravação só das linhas `db_*`, cópia única, validações, aviso Oracle, alcance TCP em porta local), dois em `MelhoriasTests` (nome do programa no aviso do sqlite; `Quem` nunca lança) e um em `DiarioTests` (issue pela melhoria). A galeria ganhou `tomcat-banco-{escuro,claro}.png` com arquivo temporário.

**Não verificado:** cliques reais; o Restart Manager contra um DBeaver de verdade (só o contrato e a não-exceção); o cancelamento dos PRs na tela (o Core é testado, o botão só foi compilado); a janela do banco do Tomcat contra um `wmw_database_config.bat` real da WMW.
