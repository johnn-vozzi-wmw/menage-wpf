# Paridade da área Git: Git Manager antigo x Menage

Auditoria feita lendo `C:\wmw\git-manager-fontes\docs\FUNCIONALIDADES.md` e o código de `gitmanager\visual\` (`Principal*.cs`, `PrincipalVisual.cs`, `PrincipalFerramentas.cs`, `Diff.cs`) contra `src/Menage.App/Modulos` e `src/Menage.Core`. Comparação por leitura de código e por testes automáticos; **nenhum clique com mouse real** foi dado (regra do projeto: testes de tela em segundo plano).

Legenda: **ok** = já existia e bate; **feito** = faltava ou divergia e foi corrigido nesta branch; **decisão** = diferente de propósito; **fora** = não feito nesta rodada.

| Recurso do app antigo | Onde está no Menage | Status |
|---|---|---|
| Pílulas Fetch, Pull, Checkout, Commit, Push, Cherry-pick | `BarraDeAcoesViewModel` + `AcoesGit` / `AcoesDeCommitEPush` | ok |
| Pílula **Sync** (visível, entre Pull e Checkout) | Estava escondida no menu Stash; agora é pílula (`BarraDeAcoesViewModel.Definicoes`) | **feito** |
| Stash ▾ (Stash com nome por projeto, Stash pop) | `AcoesGit.StashAsync/StashPopAsync`, `JanelaDeStash`, `JanelaDeStashPop` | ok |
| Branch ▾ (Nova, Rebase, Com develop, Renomear, Excluir, Limpeza) | `AcoesDeBranch`, `AcoesDeManutencao`; Core `Operacoes/*` | ok |
| Mais ▾ > **Status** | Operação `StatusEmLote` existia sem comando; agora `git.status` no menu Mais | **feito** |
| Mais ▾ > Buscar commit, Commit pom, Desfazer commit, Diagnóstico, Ferramentas | `ComandosDeLoteModulo` | ok |
| Ferramentas: Push em vários remotos, Tag em massa, Mudar a branch de TODOS, Commits não enviados, Git Doctor, Copiar dos marcados | `AcoesDeFerramentas` | ok |
| Ferramentas > **Definir o origin da branch (sem upstream)** | Não existia. Core `Operacoes/DefinirOrigin/DefinirOriginEmLote` (vincula se já existe no origin; publica com `push -u` se marcado; respeita a proteção de branches; ignora detached e quem já tem origin) + comando `ferramentas.definir-origin` | **feito** |
| Checkout (tag/branch, fetch se não achar, auto-stash `GM-AUTO`), Nova branch com publicar | `CheckoutEmLote`, `AutoStash`, `CriarBranchEmLote` | ok |
| Commit (prévia, só versionados, push opcional, ticket, convenção, hook recusado vira erro) | `CommitEmLote`, `SugestaoDeTicket`, `ExecutorComConvencaoDeCommit` | ok |
| Push (prévia, publica nova, bloqueia se o origin avançou, `--force-with-lease` com confirmação) | `PushEmLote`, `EnvioParaOrigin` | ok |
| Excluir (backup, confirmação extra, branch atual nunca), Limpeza, Desfazer commit, Renomear | `ExcluirEmLote`, `LimpezaEmLote`, `DesfazerCommitEmLote`, `RenomearBranchEmLote` | ok |
| Fetch automático em segundo plano | `JanelaPrincipalViewModel` + `ConfigDeGit.FetchMinutos` | ok |
| **Conflito de Com develop / Rebase**: diálogo de decisão (resolver, já resolvi, pular, abortar) | O Core já perguntava (`IResolvedorDeConflitos`), mas o app só tinha `ResolvedorQueAborta`: todo conflito era abortado sem perguntar. Agora `ResolvedorDeConflitosWpf` + `JanelaDeDecisaoDeConflito` (lista os arquivos, botão "Resolver aqui..." abre o editor de conflito do módulo Diff; fechar/Esc aborta) | **feito** |
| Editor de conflito ("Resolver aqui") na fila de cherry-pick | `JanelaFila` + `JanelaConflito.Fabrica` | ok |
| **Ver diff das alterações** (botão direito) com a ferramenta escolhida em Configurações (interno, TortoiseGit, WinMerge) | A janela `JanelaDiff` existia mas nada a abria. `AcoesDoProjeto.VerDiffAsync` + Core `FerramentasExternasDeDiff` (acha TortoiseGit/WinMerge como o antigo; WinMerge via `git difftool --dir-diff HEAD`; avisa quando falta o programa ou não há alterações) | **feito** |
| **Menu de contexto da grade** | Não existia. `JanelaPrincipal.MenuDaGrade.cs` + `AcoesDoProjeto`: Fetch/Pull/Sync/Commit/Push/Cherry-pick só neste projeto (marca só ele, roda e devolve as marcações), Marcar só este, Ver diff, Abrir pasta, Copiar caminho, Abrir terminal (wt ou cmd), Definir o origin, Abrir log no TortoiseGit (só se instalado), Copiar branch / hash / link do PR / URL | **feito** |
| Busca de projetos: Esc limpa, Seta/Enter vão para a grade | `AoTeclaNaBusca` | **feito** |
| Espaço marca/desmarca a linha selecionada | `AoTeclaNaGrade` | **feito** |
| Cabeçalho "(N fora da busca)" para marcados escondidos pela busca | `GradeDeProjetosViewModel.TextoForaDaBusca` | **feito** |
| Estado vazio ("Nenhum projeto combina com ...") | `GradeDeProjetosViewModel.TituloVazio` | ok |
| Atalhos F5, Ctrl+F, Ctrl+A, Ctrl+K, Ctrl+T, Ctrl+M, Ctrl+J | catálogo + `ServicoDeAtalhos`, `JanelaBase` | ok |
| Ctrl+B = Beta automática (antigo) | Ctrl+B recolhe a barra lateral; painel da beta automática é Ctrl+Shift+B | decisão (já documentada em `JanelaPrincipal.xaml.cs`) |
| Grade: ícone de situação, Versão, Situação/Origin em etiquetas, Arquivos, Pasta só em tela larga, cor da branch | `JanelaPrincipal.xaml`, `ItemDeProjeto`, `ApresentacaoDoEstado` | ok |
| Saída recolhível, abre em erro, divisória, "Parar após o projeto atual", barra de status (git, SSH, origem) | `SaidaViewModel`, `JanelaPrincipalViewModel` | ok |
| Faixa de aviso da fila de cherry-pick pendente | Aviso no canto ao abrir (`AvisarFilaPendente`), não faixa fixa | decisão (módulo cherry-pick) |
| Personalizar a barra de pílulas (tirar, mostrar, mover, restaurar) | Botão direito na pílula: `JanelaPrincipal.PersonalizarBarra.cs` + `OrganizacaoDaBarra` (lógica testada); fica em `OrdemDaBarra`/`PilulasOcultas` nas preferências da janela. "Mais" nunca sai (é o caminho para o resto) e "Gerar beta" é fixa. Não há "ação principal" destacável (o destaque é do Gerar beta) | **feito** (parcial: sem "tornar principal") |
| Perfis de uso bloqueando botão com dica do motivo | `BarraDeAcoesViewModel.Motivo`, `ExecutorComPerfil`; o menu de contexto usa o mesmo motivo | ok |

## Acabamento

- Nomes acessíveis (`AutomationProperties.Name`) na busca, na grade e nos campos do diálogo de conflito; Enter confirma o botão padrão ("Já resolvi"), Esc fecha abortando.
- Mensagens de falha do origin continuam em português claro (`EnvioParaOrigin`); erros ao abrir pasta/terminal/ferramenta viram aviso no canto e linha na Saída, nunca exceção solta.
- Item apagado do menu de contexto mostra o motivo na dica (perfil, "Aguarde a operação terminar", "Em breve").

## Testes

`dotnet test tests/Menage.Tests --filter FullyQualifiedName~Paridade` (repositórios descartáveis em `%TEMP%`): Definir origin (já tem, existe no origin, publica, só avisa, protegida bloqueada, detached) e localizador de TortoiseGit/WinMerge. `tests/Menage.App.Tests/JanelaPrincipal/ParidadeGitTests.cs`: ordem da barra, Status no menu Mais, resolvedor de conflito, "fora da busca".

Galeria (claro e escuro, janelas fora da tela, nada clicado): `docs/galeria/paridade-git/` (gerada por `dotnet run --project src/Menage.App -- --galeria-principal <pasta>`). A imagem do menu de contexto sai cortada à direita/embaixo: é limite da captura do popup fora da tela, não do menu.

## Não verificado

## Verificado em segundo plano (sem mouse nem teclado)

`VerificacaoDeConflitoEDiff` (roda dentro de `--galeria-principal`, repositórios descartáveis em `%TEMP%`, janelas fora da tela, botões acionados por automação): merge real de **Com develop** com conflito decidido pelo diálogo verdadeiro. "Pular" aborta o merge e deixa a pasta limpa; resolver no editor de conflito + "Já resolvi" conclui o merge sem sobrar operação em andamento. A janela de diff abre sobre alterações reais e lista os arquivos. `DiffExternoComGitTests`: o comando do WinMerge roda no `git difftool --dir-diff` de verdade, com um programa falso que anota as pastas recebidas.

## Não verificado

- Clique real com mouse nos menus (contexto e da pílula) e nos botões: só compilado, renderizado e acionado por automação.
- O menu de personalizar a barra: lógica testada, menu não renderizado nem acionado.
- TortoiseGit e WinMerge **reais** (estão instalados nesta máquina) não foram abertos, para não tomar o foco do usuário; só o WinMerge falso via git.
- Rebase com conflito pela tela (o Core já tinha testes com resolvedor falso; a tela é a mesma do merge).
- Operações antigas marcadas "ok" foram comparadas por leitura e pelos testes dos módulos anteriores, não reexecutadas.
