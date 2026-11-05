# Paridade geral com o Git Manager antigo (`feature/paridade-geral`)

Auditoria do que **não é Git nem integração**: perfis, configurações, personalização, atalhos, paleta, barra lateral, Sobre/ajuda, linha de comando, instância única, exceções, log, abertura, DPI, acessibilidade, "reduzir movimento" e a importação dos dados do app antigo.
Fontes lidas (só leitura): `C:\wmw\git-manager-fontes\gitmanager\visual\*.cs` (Programa, Perfil, Personalizacao, Tema, PaletaComandos, BarraFavoritos, PrincipalBeta...) e `docs\FUNCIONALIDADES.md` / `ARQUITETURA.md`. Nenhuma pasta de dados ou instalação real foi lida ou alterada.

Legenda: **ok** = já existia e confere · **feito** = implementado nesta branch · **faltando** = lacuna que ficou · **decisão** = diferente de propósito.

## Tabela

| Recurso no Git Manager antigo | No Menage | Status |
|---|---|---|
| Perfis Geral/Desenvolvedor/Tester, `perfil.json` com `permitidas` (`*`, `-id`), bloqueio na interface com a dica "Bloqueado no perfil X" | `Core/Perfis` + `JanelaDePerfis`; mesmas regras; arquivo `perfis.json` | ok |
| Selo do perfil no cabeçalho (some no Geral) | Legenda da marca na barra lateral: "Projetos WMW · Tester" (some no Geral) | **feito** (`BarraLateralViewModel.LegendaDaMarca`) |
| Configurações (diff, fetch, tags, Jira, SSH, script da atividade, atualização) | `JanelaDeConfiguracoes`, `configuracoes.json`; segredos em `credenciais.json` com DPAPI | ok |
| Temas (12, pares escuro/claro, Ctrl+T, troca ao vivo) e temas privados | `CatalogoDeTemas`, `ServicoDeTema`, `CarregadorDeTemasPrivados` | ok |
| Contraste legível nos dois temas | Teste WCAG nos 12 temas (texto 4,5:1; destaque/estado 3:1) achou 2 falhas, corrigidas: `escuro` TextoFraco `#8C8F94→#95989D`, `ameixa-escuro` destaque `(124,78,236)→(136,94,242)` | **feito** (`Tema/Contraste.cs`, `ContrasteDosTemasTests`) |
| Personalização por tema (imagem, ícone e frases do letreiro; só com temas privados) | Não portada. Personalização do Menage é global (modo, destaque, densidade, texto, barra A/B, atalhos) | decisão (depende de repositório privado; sem pedido) |
| Janela sem barra de título do Windows, moldura própria (`Moldura.cs`, WM_NCCALCSIZE) | `FluentWindow` do WPF UI com barra própria | decisão (biblioteca no lugar de código próprio) |
| Mensagens no visual do app (`Mensagem.Show`) | `ServicoDeDialogos` + `ConteudoDeMensagem` | ok |
| Paleta Ctrl+K (sem acento, vários termos, bloqueado apagado com motivo, atalho exibido) | `PaletaDeComandos` sobre o catálogo único | ok |
| Atalhos: Ctrl+K, Ctrl+T, Ctrl+F, F5, Ctrl+A, Ctrl+J, Ctrl+M, Ctrl+Shift+T | Catálogo + `ServicoDeAtalhos` (e agora **editáveis**: Ctrl+,). Ctrl+B passou a recolher a barra lateral; o antigo Ctrl+B (Betas habilitadas) está só pela paleta/menu | ok / decisão (Ctrl+B mudou de dono; confirmar com o usuário) |
| Menus e paleta mostram o atalho trocado pelo usuário | Mostram o padrão do catálogo | faltando (já anotado em PERSONALIZACAO-HISTORICO) |
| Barra de ações responsiva (1 cápsula → 2 → só ícone), pílulas apagadas com o motivo | `BarraDePilulas` | ok |
| **Favoritos da barra** (`config-barra.cfg`: quais pílulas aparecem, ordem, ação principal; "Personalizar a barra...", mover, restaurar) | Barra fixa (`BarraDeAcoesViewModel.Definicoes`) | **faltando** (é tela + menu de contexto; motor simples. Não importado do arquivo antigo para não gravar algo que nada lê) |
| Barra lateral: grupos (clique/Shift), modo beta, banco, Configurações, recolher | `BarraLateralViewModel` | ok |
| Mesma barra lateral operável **pelo teclado** | Grupos, Diário e Configurações só respondiam ao mouse; agora entram no Tab e Enter/Espaço atuam (`AcessoPorTeclado`), com nome para leitor de tela | **feito** |
| Sobre / atualizações | `JanelaSobre`, Velopack, canal beta | ok |
| Ajuda da linha de comando | `--ajuda` / `--help` / `/?` mostra as opções | **feito** (`ArgumentosDoApp.Texto`) |
| `--pasta <dir>` (configuração em outra pasta) | `--dados <pasta>` (aceita também `--pasta`); sem o argumento, `%LOCALAPPDATA%\Menage` | **feito** |
| `--tema <id\|claro\|escuro>` | Idem; grava como escolha atual (diferença: o antigo só forçava a execução) | **feito** / decisão |
| `--bandeja` (abre sem mostrar a janela) | Abre **minimizado** (não há ícone de bandeja) | **feito** parcial |
| `--galeria <dir>` e `--teste-*` | `--galeria*`, `--catalogo`, `--diff <repo> [arq]`, `--conflito <repo>` (já existiam) | ok |
| `--instalar/--reinstalar/--desinstalar/--atualizar` | Substituídos por Velopack | decisão |
| Instância única por pasta de configuração (mutex + sinal "mostrar janela") | `InstanciaUnica`: mutex por pasta de dados; a segunda cópia pede à primeira que traga a janela e sai. Outra `--dados` convive | **feito** |
| Tratamento global de exceções | Não havia no Menage. `TratadorGlobalDeErros`: tela (mostra aviso e segue), tarefas não observadas e fatais vão para o log | **feito** |
| Logs | Antes `NullLoggerFactory` (nada era gravado). Agora Serilog → `<dados>\logs\menage-AAAAMMDD.log`, 7 dias, **sem segredos** (`RedatorDeSegredos`: `user:senha@`, `Authorization`, `token=`/`senha=`, `ghp_…`, `ATATT…`) | **feito** |
| Bandeja do Windows, "ficar na bandeja ao fechar", "iniciar com o Windows" (`HKCU\...\Run --bandeja`) | Não existe ícone de bandeja (WPF não tem; exigiria `NotifyIcon`/biblioteca). Beta automática roda só com a janela aberta | **faltando** (módulo Beta automática; fora desta rodada) |
| DPI (`SetProcessDPIAware`, escala 100–150%) | `app.manifest` com **PerMonitorV2** + `longPathAware` (antes: padrão do WPF) | **feito** (não verificado em monitor real de DPI diferente) |
| Posição/tamanho da janela | O antigo também não guardava; abre centralizada | ok |
| Acessibilidade: "foco visível, nomes acessíveis, Esc fecha, Enter/Espaço em botão" | Pílulas compactas e campo de busca ganharam nome; botões só com ícone da janela principal também; foco visível via teclado nos cartões | **feito** (só a janela principal e os controles base; as outras janelas **não** foram auditadas uma a uma) |
| Movimento reduzido | Opção existia, mas barra lateral, deslize das pílulas e barras de progresso ignoravam. Agora `Animacao.Movimento()` zera a duração quando "reduzir movimento" ou "animações do Windows desligadas" | **feito** (as animações de hover/pressão em `Pilulas.xaml` continuam; são opacidade/escala 0,97) |
| Migração de dados do app antigo | `MigradorDoGitManager` (somente leitura) já cobria 10 itens; **somado: `config-beta.cfg` (modo beta)**. `diario.json` conferido contra o código antigo (`JiraUrl`, `ProjetosJira`, `VarTokenJira`): os nomes batem | **feito** |
| Migração de: `config-barra.cfg`, `config-repos.cfg` (grupos legados), `personalizacao.json`, `config-personalizacao.cfg` | Não importados (a barra e a personalização por tema não existem; `config-geral.cfg` já cobre a raiz) | faltando / decisão |
| Migração de: `gerador-beta.json`, `melhorias.json`, `contexto-banco.json`, `beta-auto.json`, `diario.json` completo | Cada módulo dono lê o seu (ver docs de cada módulo) | ok (nos módulos) |

## Desempenho de inicialização e memória

Medido com `Menage.exe --dados <pasta em %TEMP%> --medir-inicio <arquivo>`: abre a janela principal **sem ativá-la** (não rouba o foco), espera ficar ociosa e grava os marcos (ms desde o início do processo) e a memória. Máquina de desenvolvimento, com outras sessões ativas: o ruído é de ±200 ms, por isso são várias rodadas e a primeira (fria) é descartada.

**Build normal (Release, dependente de framework):**

| | processo→OnStartup | serviços | janela criada | primeira renderização | memória privada / working set |
|---|---|---|---|---|---|
| antes (main) | ~260–330 | +25 | ~620–800 | **~1,1–1,3 s** | 70 MB / 141 MB |
| depois (log em segundo plano) | ~290–330 | +60–90 | ~740–790 | **~1,2–1,35 s** (dentro do ruído) | 70 MB / 142 MB |

A primeira versão do log em arquivo (Serilog criado na partida) somou ~100 ms ao passo "serviços"; passou a nascer em uma tarefa em segundo plano (`FabricaDeLogTardia`), e a diferença sumiu no ruído. Fica um custo residual de ~30–60 ms nos serviços.

**Formato de distribuição (`publicar.ps1`: autocontido, arquivo único), 4–5 rodadas cada:**

| variante | exe | primeira renderização | working set |
|---|---|---|---|
| A: arquivo único **comprimido** (como estava) | 76 MB | **~1,57–1,62 s** (processo→OnStartup ~600 ms: descompressão) | 249 MB |
| C: sem compressão | 175 MB | ~1,36–1,51 s | 144 MB |
| D: sem compressão + **ReadyToRun** | 205 MB | **~1,09–1,39 s** (mediana ~1,2 s) | 154 MB |

Mudança aplicada em `publicar.ps1`: variante D (abre ~25–30% mais rápido e usa ~100 MB a menos). Custo: o `.exe` instalado ocupa ~130 MB a mais em disco; o pacote do Velopack continua comprimido, mas o **tamanho do download e do delta não foi medido**. C e D já incluem o código novo de inicialização (A, não). Nada foi publicado.

Gargalos que **não** foram mexidos (sem reescrever): criação da `JanelaPrincipal` (~300 ms, XAML + DI) e a primeira renderização (~400–500 ms).

## O que mudou no código

- `Core/Inicializacao`: `ArgumentosDoApp`, `IdentidadeDaInstancia`, `RedatorDeSegredos` (puros, testados).
- `App/Inicializacao`: `InstanciaUnica`, `RegistroEmArquivo` + `FormatadorSemSegredos`, `FabricaDeLogTardia`, `TratadorGlobalDeErros`, `MedidorDeInicio`.
- `App.xaml.cs` / `ComposicaoDoApp`: usam os itens acima; `ModoDeInicio.Normal` limita instância única/log ao uso real (galerias e demos continuam isoladas).
- `app.manifest` (PerMonitorV2), `Tema/Contraste.cs`, `Controles/AcessoPorTeclado.cs`, `Animacao.Movimento`.
- Migrador: item `ModoBeta`.

## Testes

```
dotnet build Menage.sln -warnaserror
dotnet test tests/Menage.Tests --filter "FullyQualifiedName~ParidadeGeral|FullyQualifiedName~PerfisConfiguracoes"
dotnet test tests/Menage.App.Tests
```

Galeria (dados de exemplo em `%TEMP%`, em segundo plano, sem mouse nem teclado reais): `Menage.exe --galeria-principal docs\galeria\paridade-geral` (claro e escuro; `verificacoes.txt` inclui agora o nome acessível e o foco por Tab dos cartões).

## Roteiro manual (nada disto foi clicado de verdade)

1. Abra o app duas vezes (mesma pasta de dados): a segunda cópia não abre outra janela e traz a primeira para a frente (minimizada, restaura). Com `--dados C:\temp\outra`, abre uma segunda instância independente.
2. `Menage.exe --ajuda` mostra a lista de opções.
3. Provoque uma exceção (não há botão para isso): confira que existe `%LOCALAPPDATA%\Menage\logs\menage-AAAAMMDD.log` depois de abrir o app, e que nenhum token/senha aparece nele.
4. Tab na janela principal: chega nos grupos, em Diário e em Configurações; Enter/Espaço atuam. Ative o perfil Tester: a legenda da marca mostra "Projetos WMW · Tester".
5. Personalização > Reduzir movimento ligado: recolher a barra lateral é instantâneo.
6. Configurações > Importar do Git Manager: o item "Modo beta" aparece quando há `config-beta.cfg`.
7. Em outro monitor com escala diferente (125/150%), arraste a janela entre telas.

## Não verificado

Clique/teclado reais (só testes e galeria automática); duas instâncias reais e o pedido "mostrar janela" (só o sinal, em teste); a mensagem de exceção na tela (só com o `Action` de teste); PerMonitorV2 em monitores de DPI diferentes; tamanho do instalador/delta com a nova forma de publicar; o contraste de estados que não são os pares testados (hover, desabilitado, saída, miniaturas); leitor de tela de verdade; acessibilidade das demais janelas.
