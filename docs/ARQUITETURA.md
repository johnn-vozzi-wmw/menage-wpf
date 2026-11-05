# Menage: arquitetura (fundação)

Resumo do que existe depois de `feature/fundacao-contratos`. O plano geral está em `PLANO.md` e `PLANO-ACELERADO.md`.

## Estrutura

| Projeto | O que é |
|---|---|
| `src/Menage.Core` | Motor, **sem WPF**: contratos e implementações. Uma pasta por assunto (`Processos`, `Repos`, `Armazenamento`, `Projetos`, `Interacao`, `Operacoes`, `Comandos`, `Perfis`). Cada módulo novo ganha a sua pasta. |
| `src/Menage.TestKit` | Apoio a testes de qualquer módulo: repositórios Git descartáveis, `mvn.cmd` falso, Nexus falso, diálogos/progresso/janelas falsos. |
| `tests/Menage.Tests` | xUnit + FluentAssertions. Testes de Git rodam de verdade em repositórios em `%TEMP%`. |
| `tests/Menage.App.Tests` | Testes do app (net8.0-windows): temas, barra responsiva, paleta Ctrl+K. Design system em `DESIGN-SYSTEM.md`. |
| `src/Menage.App` | WPF sobre o design system (`Tema/`, `Controles/`). Só fala com o Core por interfaces. |

Raiz: `Directory.Build.props` (C# 12, nullable, avisos como erro, analisadores), `Directory.Packages.props` (versões fixas, gerenciamento central), `.editorconfig`.
Verificação: `dotnet build tests/Menage.Tests -warnaserror` e `dotnet test tests/Menage.Tests`.

## Contratos

| Contrato | Função |
|---|---|
| `Resultado<T>` / `Erro` | Ok ou erro com **causa**, **explicação** e **ação sugerida**. Motores devolvem isto; a tela só exibe. |
| `IProcessoRunner` | Único ponto que cria processos (`ProcessStartInfo`, `ArgumentList`, bytes ou texto, timeout, cancelamento com kill da árvore). Nunca lança por falha do processo. |
| `GitArgs` / `Git.Em(repo)` | Builder imutável: `Git.Em(r).Comando("push").Opcao("-u").Argumento("origin")`. Cada valor é um argumento separado (sem aspas à mão). |
| `IGit` / `GitExecutor` | Roda o git com `GIT_TERMINAL_PROMPT=0`, `GIT_EDITOR=true`, `GIT_SEQUENCE_EDITOR=true`, `GIT_PAGER=cat`. Código ≠ 0 vira `Erro` classificado (`GitErros`). |
| `IAmbienteSsh` / `AmbienteSsh` | Regras do Git Manager: `GIT_SSH_COMMAND` ou `core.sshCommand` do usuário **vencem** (nada é alterado); senão Pageant + TortoiseGitPlink (`GIT_SSH` + `GIT_SSH_VARIANT=tortoiseplink`); senão OpenSSH com a chave. Calculado uma vez. |
| `Armazenamento<T>` | JSON tipado `{versao, dados}` em `%LOCALAPPDATA%\Menage` (`PastaDeDados` injetável). Escrita atômica, migração por versão, nunca sobrescreve arquivo mais novo ou corrompido. |
| `IProjetos` | Projetos, grupos e marcados. `ProjetosEmMemoria` é provisório; a versão com disco nasce em `nucleo-git`. |
| `IProgresso`, `IServicoDeDialogos`, `IServicoDeJanelas` | O Core nunca abre janela nem `MessageBox`; o App implementa. Janelas são pedidas por id (string) para o Core não conhecer tipos WPF. |
| `IComando`, `ICatalogoDeComandos`, `IModulo` | Pílulas, menus e paleta Ctrl+K leem **o mesmo catálogo**. Cada comando declara os perfis que o liberam e `PodeExecutar()`. |
| `OperacaoEmLote<TOpcoes,TResultado>` | Fluxo único: validar → prévia → confirmar → por projeto (progresso, exceção isolada, "parar após o atual") → resumo ok/aviso/erro. |
| `IExecutorDeOperacoes` | Ponto único para rodar operações. Decoradores ligados por DI, de fora para dentro: **log → perfil → proteção de branches → execução**. |

`SinaisDoLote` tem dois tokens: `Abortar` (mata o projeto em curso) e `PararApos` (deixa o atual terminar e não começa o próximo).

## Como adicionar uma operação em lote (modelo: `Operacoes/Fetch`)

1. Crie `Core/<Modulo>/MinhaOperacao.cs` herdando `OperacaoEmLote<TOpcoes, TResultado>`. Implemente só `Id`, `Nome` e `ExecutarEmProjeto` (devolva `ResultadoDoProjeto.Ok/Aviso/Falha`).
2. Se muda algo: sobrescreva `MontarPreviaAsync` (a confirmação é automática). Se reescreve histórico ou publica: `Destrutiva => true` (a proteção de branches passa a valer). Se nem todo perfil pode: `PerfisQueLiberam`.
3. Não escreva `try/catch`, progresso, resumo nem `if` de perfil: já vêm do fluxo e dos decoradores.
4. Registre no DI (`AddSingleton<MinhaOperacao>()`) e crie o módulo (abaixo) com o comando que chama `executor.ExecutarAsync(operacao, opcoes, sinais)`.
5. Teste em repositório descartável (`RepositorioDescartavel.Criar()` e `GitDeTeste.Criar()`), no estilo de `FetchEmLoteTests`.

## Como adicionar um módulo

1. Pastas: `Menage.Core/<Modulo>`, `Menage.App/Modulos/<Modulo>` (quando houver tela) e testes em `tests/Menage.Tests/<Modulo>`.
2. Classe `<Modulo>Modulo : IModulo` com `Registrar(ICatalogoDeComandos)` adicionando seus `Comando(...)` (id, texto, grupo, ícone, atalho, `Perfis`, `Habilitado`).
3. `services.AddModulo<MeuModulo>()`. O catálogo é montado com todos os módulos; id repetido lança na partida.
4. Dados do módulo: `Armazenamento<T>` (nunca um leitor de JSON próprio). Diálogos: `IServicoDeDialogos`. Processos: `IProcessoRunner`/`IGit`.

## Ligação no app

```csharp
services.AddMenageCore()            // runner, git, ssh, catálogo, executor com decoradores
        .AddModulo<FetchModulo>();
// o App registra: IServicoDeDialogos, IServicoDeJanelas, IProgresso, IPerfilAtual, IProjetos (real)
// e ILoggerFactory (Serilog)
```

## TestKit

- `RepositorioDescartavel.Criar()`: `%TEMP%\mgXXXXXX`, remoto `origin` local, `main`, `develop` e `feature/exemplo` publicadas; `AvancarOrigin()` simula outra pessoa; `Dispose` apaga tudo.
- `GitDeTeste.Criar()`: `GitExecutor` isolado do `~/.gitconfig` e do SSH da máquina (a configuração global muda o comportamento do git).
- `MavenFalso.Criar(codigo, saida, erro, atraso)`: `mvn.cmd` que registra as chamadas. Nunca faz deploy.
- `NexusFalso.Iniciar()`: HttpListener em porta livre; `SimularFalha`, `SimularLentidao`, `SimularConexaoCortada`.
- `DialogosFalsos`, `ProgressoGravador`, `JanelasFalsas`, `RunnerFalso`.

## Regras de desempenho (valem para o App)

Sem layout por quadro de animação (animar `RenderTransform`/opacidade/clip); transições por tempo; listas e grades virtualizadas; rolagem sincronizada com um único controlador.
