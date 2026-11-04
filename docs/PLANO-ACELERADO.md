# Menage: plano de implementação (feito pelo Claude)

Este plano substitui as fases M0 a M11 do `PLANO.md` (que continua valendo para inventário de módulos, checklist de paridade e decisões de base). Quem implementa é o Claude, então o plano não considera equipe, revezamento nem prazos de pessoas: considera **sessões de trabalho**, **ordem de dependência** e **o que pode rodar em paralelo**. O gargalo passa a ser a **revisão do usuário**, não a escrita do código.

## 1. Uma branch por funcionalidade

Regra do usuário: cada funcionalidade tem a **sua própria branch**, com o nome dizendo a finalidade e **com tudo o que já foi feito até ali**.

- **Nome:** `feature/<modulo>-<finalidade>`, por exemplo `feature/nucleo-git`, `feature/diff-conflito`, `feature/cherry-pick`.
- **Ponto de partida:** sempre a `main` atual. Como cada funcionalidade concluída é integrada na `main` antes da próxima começar (ou antes do rebase das paralelas), a nova branch já nasce com todo o conteúdo anterior.
- **Integração:** ao terminar (build limpo, testes verdes, galeria de telas gerada, checklist de paridade da seção), `merge --no-ff` na `main`, com mensagem dizendo o que a funcionalidade entrega. A branch **não é apagada** (fica como histórico da funcionalidade).
- **Branches em paralelo:** nascem do mesmo ponto da `main`. A que terminar depois faz **rebase na `main` atual** (resolve conflitos e roda os testes de novo) antes do merge. Para evitar conflito, cada branch só mexe na própria pasta (`Menage.Core/<Modulo>`, `Menage.App/Modulos/<Modulo>`, `tests/<Modulo>`).
- **Commits:** mensagem por arquivo (`git commit -F`), com o trailer da sessão. Nada de force-push na `main`.
- **Publicar atualização para colegas:** nunca sem pedido explícito do usuário.

## 1.1 Bibliotecas: nada de escrever componente do zero

Regra do usuário: componentes modernos, transições e animações agradáveis, usando **bibliotecas conhecidas do WPF**. Só se escreve um controle próprio quando nenhuma biblioteca cobre (e mesmo assim sobre os estilos da biblioteca). A escolha final da biblioteca visual sai da Etapa 0, com a mesma tela de teste montada nas duas candidatas.

| Necessidade | Biblioteca (NuGet) | Observação |
|---|---|---|
| **Visual base (candidata principal)** | **WPF UI** (`WPF-UI`) | Estilo Fluent/Windows 11: janela com Mica e barra de título própria, `NavigationView`, `Card`, `InfoBar`, `Snackbar`, `Flyout`, `ContentDialog`, `NumberBox`, `ToggleSwitch`, `DataGrid` estilizado, ícones Fluent, tema claro/escuro e cor de destaque trocados ao vivo. Leve. |
| **Visual base (alternativa)** | **MaterialDesignInXamlToolkit** | Mais animações prontas (`Transitioner`, `TransitioningContent`, ripple, `DialogHost`, `DrawerHost`, `Snackbar`, `Card`) e `PaletteHelper` para trocar cores em tempo de execução. Não misturar com a anterior: escolher uma. |
| Animações declarativas | **XamlFlair** | Entrada/saída de elementos (fade, slide, escala, listas em cascata) só com propriedades no XAML. |
| Comportamentos e gatilhos | **Microsoft.Xaml.Behaviors.Wpf** | Animações por evento, drag, foco, sem código. |
| MVVM, mensagens, comandos | **CommunityToolkit.Mvvm** | Geradores de código para `ObservableProperty` e `RelayCommand`. |
| Injeção de dependência e host | **Microsoft.Extensions.Hosting / DependencyInjection** | Serviços (janelas, Git, armazenamento). |
| Editor de diff e conflito | **AvalonEdit** | Edição, realce de sintaxe, margens, marcadores e renderizadores de fundo; sobre ele, o controle de rolagem sincronizada. |
| Algoritmo de diff | **DiffPlex** | Diff por linha e por palavra, como alternativa ao `ModeloDiff` portado; usar o que passar nos testes de arquivos reais (fim de linha, BOM, espaços). |
| Gráficos do dashboard | **LiveCharts2** (`LiveChartsCore.SkiaSharpView.WPF`) | Barras e indicadores animados (no lugar do `BarrasMini`). |
| GIF animado leve | **XamlAnimatedGif** | Substitui o `GifLeve` próprio. |
| Ícone de bandeja | **H.NotifyIcon.Wpf** | Se o app ganhar bandeja. |
| Instalador e atualização | **Velopack** | Instalador, atualização automática (com delta), desinstalador e atalhos prontos; publica em GitHub Releases. Substitui `Instalador.cs` e `Atualizacao.cs`. Repositório privado exige token (a confirmar na Etapa 0). |
| Log | **Serilog** (arquivo + console) | Diagnóstico das operações. |
| Testes | **xUnit**, **FluentAssertions**, **Verify** | `Verify` para comparar capturas e saídas (galeria por tema). |
| Git | **git.exe** (como hoje) | Mantém SSH/Pageant e `core.sshCommand`; **LibGit2Sharp** só para leituras onde o ganho de velocidade for medido. |

Consequências no plano: a branch do design system vira **configuração e extensão** da biblioteca escolhida (12 temas como paletas, estilos do que faltar), o instalador vira **configuração do Velopack**, e o diff usa AvalonEdit e DiffPlex como base.

## 1.2 Arquitetura e padrões de projeto (código enxuto e fácil de manter)

Objetivo: o Git Manager antigo tem 42 mil linhas porque cada módulo repete o mesmo esqueleto (percorrer projetos marcados, mostrar prévia, rodar em thread, resumir erros, ler/gravar JSON, bloquear por perfil). No Menage cada um desses esqueletos existe **uma vez** e os módulos só dizem o que é específico.

| Padrão | Onde entra | O que elimina |
|---|---|---|
| **Feature folders (fatias verticais)** | `Menage.Core/<Modulo>` + `Menage.App/Modulos/<Modulo>` + `tests/<Modulo>`; cada módulo é um `IModulo` que se registra no catálogo | Arquivos "Principal*.cs" gigantes e acoplamento com a janela principal |
| **Template Method / pipeline de operação em lote** | `OperacaoEmLote<TOpcoes, TResultado>`: define o fluxo único (validar → prévia → confirmar → executar por projeto com progresso e cancelamento → resumo). Cada operação (Commit, Push, Excluir, Renomear...) implementa só `ExecutarEmProjeto(...)` | Cerca de 80% do código repetido entre as ~20 operações |
| **Command + catálogo (Registry)** | Cada ação é um `IComando` (id, texto, ícone, atalho, perfil, `PodeExecutar`); pílulas, menus e a paleta Ctrl+K leem do mesmo catálogo | Três listas paralelas de comandos (barra, menu, paleta) |
| **Strategy** | Tipo de banco (Oracle/Postgres/SQL Server), ferramenta de diff, modo de SSH, critério de limpeza, formato de entrega | `switch` espalhado pelo código |
| **Decorator / behaviors (preocupações transversais)** | Perfil de uso, proteção de branches, log, medição de tempo, tratamento de erro: viram decoradores do executor de operações, ligados por DI | `if (Perfil.Bloqueia...)` e `try/catch` em cada tela |
| **Result em vez de exceção** | `Resultado<T>` (ok/erro com causa, explicação e ação sugerida) vindo dos motores; a tela só exibe | Cadeias de try/catch e mensagens de erro montadas à mão |
| **Repository genérico de configuração** | `Armazenamento<T>` tipado (JSON em `%LOCALAPPDATA%\Menage`, escrita atômica, versão de esquema) | Um leitor/gravador de JSON por módulo |
| **Builder** | `GitArgs` fluente (`Git.Em(repo).Comando("push").Opcao("-u")...`) | Strings de argumentos montadas à mão |
| **Specification / parser de consulta** | Filtros inteligentes (Melhorias), busca de projetos e de commits | Código de filtro duplicado por tela |
| **Observer / mensageria** | `WeakReferenceMessenger` (CommunityToolkit) para "projeto mudou", "tema mudou", "melhoria ativa mudou" | Eventos ligados à mão entre janelas |
| **MVVM com geradores de código** | `[ObservableProperty]`, `[RelayCommand]`; XAML com `DataTemplate`/estilos da biblioteca; sem code-behind além de ligação de controle | Boilerplate de propriedades e comandos |
| **Injeção de dependência** | Tudo por interface (`IGit`, `IArmazenamento`, `IServicoDeJanelas`); testes trocam por falsos | Estáticos globais (`Operacoes`, `Tema`, `Perfil`) do código antigo |

**Regras de código (para o Claude seguir em todas as branches)**
- C# 12: `record` para dados, construtores primários, coleções com `[...]`, `file-scoped namespace`, `global using`, nullable ligado e **avisos como erro**.
- Um arquivo, uma responsabilidade; arquivo passando de ~300 linhas é dividido. Método com mais de ~40 linhas é revisto.
- Sem classe estática de estado; sem lógica de negócio em code-behind nem em ViewModel (vai para o Core).
- Nada de `MessageBox` direto: sempre `IServicoDeDialogos`. Nada de `Thread`/`Process` solto: `Task` + `IProcessoRunner`.
- Nomes em português para o domínio (como já é no projeto) e inglês só para termos técnicos; sem abreviações obscuras.
- Comentário só quando explica um **porquê** não óbvio; documentação curta por módulo em `docs/ARQUITETURA.md`.
- `.editorconfig` e analisadores Roslyn no repositório; `dotnet format` e `dotnet build -warnaserror` no passo de verificação.
- Cada módulo novo segue o modelo de uma "branch exemplo" (`feature/operacoes-lote` com a primeira operação) para as demais copiarem a forma.

## 2. Ordem de dependência (caminho crítico)

```
contratos + TestKit
   ├─ nucleo-git ───────────────┐
   ├─ design-system ────────────┼─ janela-principal ─┬─ operacoes-lote
   ├─ diff-conflito             │                    ├─ cherry-pick (usa diff-conflito)
   └─ motores independentes     │                    ├─ gerador-beta + beta-automatica
      (nexus, tomcat, banco,     │                    ├─ melhorias + diario + contexto-banco
       diario, beta, PRs)        │                    └─ nexus-tomcat-ui + pull-requests-ui
                                 └─ instalador (independente)
```

O caminho crítico é: contratos, núcleo Git e design system, janela principal, módulos de tela. Diff e motores independentes andam ao lado sem bloquear.

## 3. Branches, na ordem em que nascem

### Etapa 0: fundação (série, uma sessão)
| Branch | Entrega | Pronto quando |
|---|---|---|
| `feature/fundacao-contratos` | Interfaces do Core (`IGit`, `IProcessoRunner`, `IArmazenamento`, `IProjetos`, `IProgresso`, `IServicoDeDialogos`, `IServicoDeJanelas`), `Resultado<T>`, `GitArgs`, `Armazenamento<T>`, **`OperacaoEmLote` (fluxo único das operações em lote)**, decoradores de perfil/proteção/log, catálogo de comandos (`IModulo`, `IComando`, `ICatalogoDeComandos`), `Menage.TestKit` (repositórios descartáveis, Maven falso, Nexus falso), `.editorconfig` e analisadores, regras de desempenho. | Compila com avisos como erro, testes de contrato passam. |
| `feature/provas-de-conceito` | Diff em AvalonEdit com arquivo de 7 mil linhas (pintura menor que 4 ms por painel, rolagem e arraste sincronizados), janela sem barra de título, grade virtualizada com 200 linhas, self-contained comprimido (tamanho e abertura). | Números medidos registrados; decisões de editor e publicação fechadas. |

### Etapa 1: quatro branches independentes (podem rodar em paralelo)
| Branch | Entrega |
|---|---|
| `feature/nucleo-git` | Executor do Git (variáveis `GIT_*`, SSH/Pageant, `sshCommand`), configuração, grupos, fetch/pull/status/checkout/branch/stash/cherry/rebase, índice de commits, proteção de branches, erros. Portar as seções de teste correspondentes. |
| `feature/design-system` | **Sobre a biblioteca escolhida (§1.1):** 12 temas + privados como paletas com troca ao vivo, estilos só do que a biblioteca não traz (pílula, barra responsiva de ações, chip, ficha, etiquetas da grade), janela base, animações com XamlFlair/Storyboards, paleta Ctrl+K como componente, galeria automática por tema. |
| `feature/diff-conflito` | Modelo de diff, controle lado a lado, ligações, régua, navegação, **controlador único de rolagem** (X, Y e arraste da barra), editor de conflito, gravação com codificação/BOM/fim de linha. |
| `feature/motores-independentes` | Motores sem tela e testados: Nexus, Tomcat Express, contexto do banco, Jira/Diário, Beta automática, execução do Gerador de Beta, pull requests, atualização. (Se ficar grande, cada motor vira a sua branch: `feature/motor-nexus`, `feature/motor-tomcat`, etc.) |

### Etapa 2: janela principal (série)
| Branch | Entrega |
|---|---|
| `feature/janela-principal` | Barra de ações a partir do catálogo, grade com situação e etiquetas, busca, grupos, Saída recolhível, status, paleta, favoritos, fetch automático, perfis; ações Fetch, Pull, Sync, Checkout, Stash funcionando de ponta a ponta. |

### Etapa 3: módulos de tela (branches independentes, parte em paralelo)
| Branch | Telas |
|---|---|
| `feature/operacoes-lote` | Commit, Push, Nova branch, Rebase, Com develop, Renomear, Excluir, Limpeza, Desfazer commit, Buscar commit, Commit pom, Diagnóstico, Proteção, Ferramentas (tags, Git Doctor, remotos, não enviados), Configurações. |
| `feature/cherry-pick` | Janela Cherry-pick e fila em lote (stepper, conflito, retomar). |
| `feature/gerador-beta` | Gerador de Beta, favoritos, limpar target, console, Entregas. |
| `feature/beta-automatica` | Tela e fluxo da Beta automática. |
| `feature/melhorias` | Painel lateral, dashboard, editor, tags e filtros, version.conf, sqlite. |
| `feature/diario` | Diário de trabalho e worklogs. |
| `feature/contexto-banco` | Perfis de banco e `context.xml`. |
| `feature/nexus-tomcat` | Telas do Nexus e do Tomcat Express. |
| `feature/pull-requests` | Tela de pull requests. |
| `feature/perfis-configuracoes` | Perfis de uso (bloqueio na interface), janela Configurações, temas e personalização. |
| `feature/instalador-atualizacao` | **Velopack:** instalador, atualização pelo GitHub (Releases), desinstalador, atalhos e ícones, tudo com pasta e atalhos próprios do Menage. (Independente: pode andar em paralelo a qualquer etapa depois da fundação.) |

### Etapa 4: integração e acabamento
| Branch | Entrega |
|---|---|
| `feature/paridade-acabamento` | Conferência item a item contra o `FUNCIONALIDADES.md` do Git Manager antigo (somente leitura), desempenho, DPI 100/125/150, temas, teclado, acessibilidade, GIF leve e frases, ajustes do piloto. |

## 4. Como cada branch é implementada (rotina do Claude)
1. Cria a branch a partir da `main` atual e relê as regras do `CLAUDE.md`.
2. Escreve primeiro o **motor com testes** (repositórios descartáveis em `%TEMP%`), depois a **tela**, depois a **galeria de capturas** por tema.
3. Mede desempenho nas telas pesadas (diff, grades, listas) e registra os números.
4. Roda `dotnet build` e `dotnet test`; só diz "passou" se rodou, e informa o que foi só renderizado e não clicado.
5. Entrega ao usuário a galeria e um roteiro curto de teste; **aguarda a revisão** antes do merge.
6. Faz o merge `--no-ff` na `main` e avisa o que ficou pendente.

## 5. Estimativa (do Claude implementando)
Não há "semanas de uma pessoa". Medindo em **sessões de trabalho focado** (cada uma entrega alguns milhares de linhas testadas) e supondo revisão rápida do usuário:

| Etapa | Sessões estimadas |
|---|---|
| 0: fundação, provas e escolha da biblioteca visual | 2 a 3 |
| 1: núcleo, design system (sobre biblioteca), diff (AvalonEdit/DiffPlex), motores | 6 a 10 (parte em paralelo) |
| 2: janela principal | 3 a 4 |
| 3: módulos de tela (controles prontos da biblioteca) | 10 a 15 (parte em paralelo) |
| 4: paridade e acabamento | 3 a 4 |
| **Total** | **~24 a 36 sessões** |

O uso de bibliotecas tira cerca de 4 a 6 sessões do total anterior (controles, animações, gráficos, instalador e GIF já vêm prontos).

Em tempo de calendário, isso fica em **cerca de 4 a 7 semanas** se houver sessões quase todo dia e revisão em até 1 dia. Os números são estimativas minhas, não medidas, e mudam com: tempo de revisão, quantas branches rodam em paralelo (cada uma é uma sessão, e várias ao mesmo tempo consomem mais do limite de uso) e surpresas nas provas de conceito.

## 6. Riscos
| Risco | Tratamento |
|---|---|
| Conflitos entre branches paralelas | Pastas por módulo, catálogo de comandos, rebase na `main` antes do merge, testes verdes. |
| Revisão acumulando | Entrega por branch com galeria e roteiro curto; revisar em lotes. |
| Editor de diff no WPF | Prova de conceito na Etapa 0, antes de investir no resto. |
| Divergência visual entre telas | Só a branch do design system cria estilos; as outras consomem a biblioteca escolhida. |
| Biblioteca visual mal escolhida (visual ou desempenho) | Etapa 0 monta a mesma tela nas duas candidatas (WPF UI e MaterialDesign) e mede abertura, memória e fluidez antes de decidir. |
| Biblioteca descontinuada ou com bug | Só pacotes populares e mantidos; versão fixada no `Directory.Packages.props`. |
| Limite de uso do Claude | Começar com 2 branches em paralelo e subir conforme o consumo. |

## 7. Decisões que preciso antes de começar a Etapa 0
1. Quantas branches posso rodar **ao mesmo tempo** (1, 2 ou 3)?
2. Aceita revisar **por branch** (galeria de imagens + roteiro) antes de cada merge na `main`?
3. As branches antigas mantidas (não apagar) tudo bem, ou prefere apagar depois do merge?
