# Plano: Menage (WPF .NET 8), sucessor do Git Manager

Status: **só planejamento, nada implementado.** Os números de linhas vêm do código atual (42,7 mil linhas em 76 arquivos de `gitmanager\visual`, mais instalador, launcher e menu .bat). Os prazos são estimativas, não medidas.

## 1. Decisões de base

| Tema | Decisão proposta |
|---|---|
| Projeto | **Repositório novo e separado**, `C:\wmw\menage-wpf` (GitHub privado próprio). O repositório atual continua funcionando e recebendo correções até a paridade. |
| Plataforma | WPF, .NET 8 (LTS), C# 12, Windows 10/11 x64. |
| Estrutura | Solução com 4 projetos: `Menage.Core` (motor, sem tela), `Menage.App` (WPF), `Menage.Tests` (xUnit), `Menage.Installer`. |
| Padrão de telas | MVVM leve (CommunityToolkit.Mvvm). Motor nunca conhece a tela. |
| Compatibilidade | **Mesmos arquivos de configuração** (`config-*.cfg`, `gerador-beta.json`, `melhorias.json`, `perfil.json`, `fila-cherry.json`, `contexto-banco.json`, `preferencias.json`...), para o colega trocar de versão sem reconfigurar. |
| Editor de diff | AvalonEdit (WPF nativo) em vez de Scintilla por WindowsFormsHost (evita problemas de sobreposição e de DPI). Escolha final depende da **prova de conceito (M0)**, com arquivo de 7 mil linhas. |
| Instalação | Self-contained em arquivo único, ou framework-dependent com verificação do .NET 8 Desktop Runtime. Decidir no M0 (tamanho x pré-requisito). |
| Regras que continuam valendo | Testes só em repositórios descartáveis em `%TEMP%`; nunca testar deploy do Gerador de Beta (Maven falso); nunca escrever nos repositórios reais; não publicar atualização sem pedido; commits com o trailer da sessão. |

## 2. Estrutura alvo

```
menage-wpf\
  src\
    Menage.Core\        motor, modelos e configuração (net8.0, sem WPF)
    Menage.App\         WPF: janelas, controles, temas, ViewModels
    Menage.Installer\   instalador e atualização
  tests\
    Menage.Tests\       porta de Testes.cs (motor) + testes de ViewModel
  docs\                     mesmos documentos, atualizados
```

## 3. Inventário dos módulos e origem no código atual

Tamanho aproximado do que existe hoje (linhas de C#). "Motor" migra quase direto para o Core; "Tela" é reescrita em WPF.

| # | Módulo | Origem atual | Motor | Tela |
|---|---|---|---|---|
| A | Núcleo Git e configuração | `Nucleo.cs` (1713), `GitErros.cs`, `Programa.cs` (modos de teste), `GruposProjetos*.cs`, `SeletorPasta.cs`, `Automacao.cs` | ~2,6 mil | pequena |
| B | Base visual (temas, controles, janela sem barra) | `Tema.cs` (1780), `Campos.cs`, `Moldura.cs`, `Menus.cs`, `Mensagem.cs`, `AvisoCanto.cs`, `GifLeve.cs`, `Personalizacao.cs`, `TemasPrivados.cs` | — | ~5,5 mil |
| C | Janela principal | `Principal*.cs` (~3 mil), `PaletaComandos.cs`, `BarraFavoritos.cs`, `Favoritas.cs`, `Sugestoes.cs` | — | ~4,2 mil |
| D | Operações Git em lote | `Dialogos.cs` (1168), `CommitPush.cs`, `Exclusao.cs`, `Limpeza.cs`, `StashCommits.cs`, `Protecao.cs`, `Recursos.cs`, `DialogosRecursos.cs`, `PrincipalRecursos.cs`, `Ferramentas.cs`, `PrincipalFerramentas.cs` | ~1,7 mil | ~2,5 mil |
| E | Diff e conflito | `Editor*.cs` (EditorModelo, Visual, Diff, Conflito), `Diff.cs`, `ScriptAtividade.cs` | ~0,6 mil | ~2,5 mil |
| F | Cherry-pick (histórico e fila) | `Historico.cs`, `HistoricoForm.cs` (901), `FilaCherry*.cs` (~1,9 mil) | ~0,7 mil | ~2,2 mil |
| G | Gerador de Beta e entregas | `GeradorBeta.cs` (2036), `GeradorTarget.cs`, `Entregas.cs`, `Favoritos.cs`, `ConsoleBeta.cs` | ~1,2 mil | ~2 mil |
| H | Beta automática | `BetaAuto*.cs` (~2,4 mil) | ~1,8 mil | ~0,6 mil |
| I | Melhorias e Diário | `Melhorias*.cs` (~3,3 mil), `Diario*.cs` (~1,7 mil), `ArquivoEmUso.cs` | ~2,3 mil | ~2,7 mil |
| J | Contexto do banco | `ContextoBanco*.cs` (~1,1 mil) | ~0,5 mil | ~0,6 mil |
| K | Nexus e Tomcat Express | `Nexus.cs` (1196), `TomcatExpress*.cs` (~2,6 mil) | ~1,7 mil | ~2,1 mil |
| L | Pull requests | `PullRequests*.cs` (~0,8 mil) | ~0,5 mil | ~0,3 mil |
| M | Perfis, configurações, atualização | `Perfil*.cs`, `Atualizacao.cs`, parte de `Dialogos.cs` | ~0,6 mil | ~0,5 mil |
| N | Instalador, launcher e menu CMD | `installer\Instalador.cs`, `launcher\`, `Git Manager\*.bat` | — | ver §6 |
| O | Testes | `Testes.cs` (4518) | portar por módulo | — |

## 4. Fases e ordem

Cada fase termina com **critério de saída**: testes do motor passando, tela revisada por você e checklist de paridade do módulo (§5) sem item aberto.

### M0 — Fundação e provas de conceito (1 a 2 semanas)
- Criar o repositório, a solução, o CI local (`dotnet build`, `dotnet test`) e o `AGENTS`/`CLAUDE.md` do projeto novo.
- Prova de conceito 1: **diff lado a lado** com AvalonEdit em arquivo de 7 mil linhas e linhas longas; rolagem horizontal e vertical sincronizada, arraste da barra e animação de recolher/expandir. Medir pintura (meta: menos de 4 ms por painel) e memória.
- Prova de conceito 2: **janela sem barra de título** (WindowChrome) com redimensionar, encaixe e botões próprios; tema trocando ao vivo.
- Prova de conceito 3: **grade virtualizada** com 200 projetos e células ricas (ícone, etiquetas).
- Decidir: self-contained x runtime instalado; AvalonEdit x alternativa.

### M1 — Core: núcleo Git e configuração (2 a 3 semanas) — módulo A
- Portar `Git` (`ProcessStartInfo`, variáveis `GIT_*`, SSH/Pageant, `core.sshCommand`), `Configuracao`, `Operacoes` básicas (fetch, pull, status, checkout, branch, stash), `GitErros`, grupos de projetos.
- Portar a parte de `Testes.cs` correspondente (seções config, checkout, criar, stash, estados, fetchpull, indice).
- Encapsular I/O em interfaces (`IProcessoGit`, `ISistemaArquivos`) para testes sem disco quando possível.

### M2 — Base visual (3 a 4 semanas) — módulo B
- Sistema de temas por `ResourceDictionary` com pincéis dinâmicos: 12 temas (pares escuro/claro) + temas privados, troca ao vivo com fade, regra "paleta clara sem branco puro nem valores repetidos".
- Biblioteca de controles: botão de ação, pílula, barra de pílulas responsiva (cápsula, só ícone, quebra em linhas), cartão, interruptor, chip, ficha, barra de progresso, tooltip, menu moderno, caixa de mensagem (`Mensagem.Show`), aviso de canto, campo de texto com moldura animada, ComboBox, DataGrid estilizado com hover e estado vazio.
- Janela base (`DialogoBase`): fade, Esc, botões, barra de título própria, redimensionável.
- Animações padrão (ease-in-out por tempo) e regras de desempenho: sem layout por quadro, virtualização, `RenderOptions` e congelamento de recursos.
- Personalização (GIF leve, frases) por último, na fase de acabamento.

### M3 — Janela principal (4 a 6 semanas) — módulo C
- Barra de ações, grade com ícone de situação, etiquetas (Situação, Origin), busca (Ctrl+F), grupos laterais, Saída recolhível com a regra de altura, faixa de aviso, barra de status, paleta Ctrl+K, favoritos, atalhos, fetch automático em segundo plano.
- Execução de operações em `Task` com cancelamento ("Parar após o projeto atual") e progresso.
- Janela já funcional com Fetch, Pull, Sync, Checkout, Stash (módulos A e D).

### M4 — Operações Git em lote (4 a 5 semanas) — módulo D
Diálogos e motores de: Commit, Push (e múltiplos remotos), Nova branch, Rebase, Com develop, Renomear, Excluir, Limpeza, Desfazer commit, Buscar commit, Commit pom, Diagnóstico (divergência, arquivos alterados, releases), Auto-stash, Proteção de branches, convenção de commit, tag em massa, mudar branch de todos, commits não enviados, Git Doctor, copiar dos marcados.

### M5 — Diff e conflito (3 a 4 semanas) — módulo E
- Diff: lista de arquivos, recolher/expandir, ligações curvas, régua, navegação F7, destaque por palavra, trechos recolhidos, etiquetas de fim de linha/BOM, binário.
- Conflito: duas visões em cima, resultado editável embaixo, escolha por conflito (botões, atalhos, pílulas), desfazer/refazer, nomes dos lados por operação, gravação com codificação/BOM/fim de linha originais e `git add`.
- Reaproveitar o modelo (`ModeloDiff`, `Mapa`, `Blocos`, `Navegacao`, `LadosConflito`, `FormatoArquivo`) quase sem mudanças.
- Rolagem: um único controlador de sincronização (X e Y) com filtro por tempo, sem ficar "um quadro atrás".

### M6 — Cherry-pick (3 a 4 semanas) — módulo F
- Janela Cherry-pick: índice de commits em memória, filtros, painéis por projeto ou abas, ordenação, marcar vários, "ver branches que contêm".
- Fila em lote: stepper animado, tiles, barra, aviso de conflito, retomar após fechar, regra do script de banco fora da develop.

### M7 — Gerador de Beta, entregas e Beta automática (5 a 6 semanas) — módulos G e H
- Gerador: tabela de projetos, favoritos, modo install/deploy, limpar target, gatilho de entrega (zip, área de transferência), console. **Testes sempre com `mvn.cmd` falso.**
- Beta automática: motor (JQL, estados), tela.

### M8 — Melhorias, Diário e Contexto do banco (4 a 5 semanas) — módulos I e J
- Melhorias: cadastro, ativar/desvincular, painel e dashboard com tags e filtros inteligentes, version.conf, sqlite por atividade, chips fixos no Checkout.
- Diário: coleta por commit, worklogs, resumo por regras e IA, integração Jira.
- Contexto do banco: perfis, aplicar no `context.xml`, importar.

### M9 — Nexus, Tomcat Express e Pull requests (4 a 5 semanas) — módulos K e L

### M10 — Perfis, configurações, instalador e atualização (3 a 4 semanas) — módulos M e N
- Perfis de uso (bloqueio na interface), janela Configurações, temas, atualização pelo GitHub.
- Instalador novo (ver §6).

### M11 — Acabamento, migração e piloto (2 a 3 semanas)
- Revisão de desempenho (CPU, memória, abertura), DPI 100/125/150%, acessibilidade, foco e teclado.
- Piloto com 2 ou 3 colegas lado a lado com a versão antiga, correção do que aparecer, documentação e decisão de desligar o WinForms.

## 5. Checklist de paridade (por módulo)
Cada módulo só fecha quando:
1. As decisões do `docs\FUNCIONALIDADES.md` daquela seção estão implementadas ou conscientemente descartadas (registrar no próprio plano).
2. A seção correspondente de `Testes.cs` foi portada e passa.
3. O módulo respeita os perfis de uso (ids em `Perfil.Catalogo`).
4. Foi testado nos temas escuro e claro e com DPI 100% e 150%.
5. Você revisou a tela.

## 6. Instalador, atualização e menu CMD
- **Instalador:** novo, mantendo o que hoje faz: achar os projetos, grupos, chave SSH, perfil de uso, atalhos, reinstalar e desinstalar (sem tocar em repositórios nem em `C:\wmw\entregas`).
- **Atualização:** mesma ideia do `update\versao.txt` da `main`, com a versão nova do app.
- **Convivência:** a versão nova instala em pasta própria (ex.: `C:\wmw\GMW`), sem tocar nas cópias `GMV*` atuais, e lê as mesmas configurações. Só no fim se decide a troca.
- **Menu CMD (.bat):** fica como está, fora desta migração.

## 7. Estratégia de testes
- **Motor:** xUnit com repositórios descartáveis em `%TEMP%` (caminho curto), um config por teste; ordem das seções do `Testes.exe` preservada.
- **Telas:** testes de ViewModel (sem janela) + capturas automáticas por tema (equivalente do `--galeria`) + roteiro manual por módulo.
- **Desempenho:** metas medidas no M0 (pintura por painel, abertura da janela principal, memória) repetidas a cada fase.
- **Segurança de dados:** nenhuma fase escreve nos repositórios reais do usuário.

## 8. Riscos e como tratá-los
| Risco | Tratamento |
|---|---|
| Editor de diff no WPF (rolagem sincronizada, arquivos grandes, ligações curvas) | Prova de conceito no M0 antes de qualquer outra coisa; alternativa de renderização própria virtualizada. |
| Escopo (42 mil linhas, muitas regras de negócio documentadas) | Paridade por módulo com checklist; não migrar o que está obsoleto (perguntar antes). |
| Regressões de comportamento do Git (SSH/Pageant, `core.sshCommand`, CRLF/BOM) | Portar o motor com os testes antes da tela. |
| Dois sistemas em paralelo por meses | Correções críticas no antigo, evolução só no novo; congelar o antigo a partir do M7. |
| Runtime .NET 8 na máquina dos colegas | Decisão no M0: self-contained (maior) ou verificação/instalação do runtime. |
| Perda do ajuste fino de desempenho já feito (buffer duplo, animações por tempo) | Levar os princípios para as regras do novo projeto (`CLAUDE.md`). |

## 9. Cronograma estimado (uma pessoa em tempo integral com apoio do Claude)

| Marco | Entrega | Acumulado |
|---|---|---|
| M0 a M2 | Fundação, núcleo Git, temas e controles | ~2,5 a 3 meses (parcial, em paralelo) |
| M3 | Janela principal com operações básicas | ~3 a 4 meses |
| M4 a M6 | Operações em lote, diff, conflito e cherry-pick | ~5 a 6 meses |
| M7 a M9 | Gerador de Beta, melhorias, Diário, Nexus, Tomcat | ~7 a 8 meses |
| M10 a M11 | Instalador, atualização, acabamento e piloto | ~8 a 9 meses |

Estimativa conservadora. Como o ritmo real depende de quanto você revisa cada tela, vale reavaliar no fim do M3. Dá para entregar valor antes: janela principal + diff (M0 a M3 + M5) em ~3 a 4 meses.

## 10. Decisões que preciso de você antes do M0
1. Nome e local do novo repositório (sugestão: `C:\wmw\menage-wpf`, GitHub privado `wmw-menage-wpf`).
2. Distribuição: self-contained (instalador maior, sem pré-requisito) ou exigir o .NET 8 Desktop Runtime.
3. Funcionalidades a **não** migrar (se houver alguma obsoleta).
4. Se a versão nova convive com a antiga (pasta e atalhos próprios) até a paridade.
5. Permissão para instalar o .NET 8 SDK nesta máquina (via winget).
