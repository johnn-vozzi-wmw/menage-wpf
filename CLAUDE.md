# Menage (WPF .NET 8) - regras do projeto

Sucessor do Git Manager (WinForms), **totalmente separado dele**: repositório, pasta de dados, instalador e atalhos próprios. Nada aqui lê, escreve ou depende de arquivos do Git Manager antigo (`C:\wmw\git-manager-fontes`, cópias `C:\wmw\GMV*`). Importar configurações do antigo, se um dia houver, será uma ação explícita do usuário.
Responda ao usuário em **português**, direto, dizendo o que foi feito e o que NÃO foi verificado. Plano geral em `docs/PLANO.md`.

## Estrutura
- `src/Menage.Core`: motor (Git, Maven, Nexus, configuração), sem referência a WPF.
- `src/Menage.App`: WPF (janelas, controles, temas, ViewModels).
- `tests/Menage.Tests`: xUnit.
- Dados do usuário em `%LOCALAPPDATA%\Menage` (nunca ao lado do exe, nunca na pasta do sistema antigo).

## Compilar e testar
- `dotnet build Menage.sln` e `dotnet test`. SDK: .NET 8 (instalado via winget). Distribuição: **self-contained** (colegas não baixam nada).
- Se o `dotnet` não for achado numa sessão antiga, recarregue o PATH da máquina.

## Regras que NÃO podem ser quebradas
1. Testes de Git só em repositórios descartáveis em `%TEMP%` com caminho curto. **Nunca** escrever nos repositórios reais do usuário (`C:\wmw\dev\dev1`, `C:\wmw\wmwvendas`...). **Nunca** testar deploy do gerador de beta (usar `mvn.cmd` falso). Nunca fechar/mover janelas do usuário.
2. Git roda via `ProcessStartInfo` com `GIT_TERMINAL_PROMPT=0`, `GIT_EDITOR=true`, `GIT_SEQUENCE_EDITOR=true`, `GIT_PAGER=cat`.
3. Não publicar atualização para os colegas sem pedido explícito.
4. Nada de afirmar que build/teste passou sem ter rodado; dizer o que foi só compilado e não clicado.
5. Commits terminam com o trailer indicado pela sessão; mensagem por arquivo (`git commit -F`).

## Desempenho e animação (aprendido no Git Manager antigo)
- Nenhum layout por quadro de animação: animar `RenderTransform`/opacidade/clip, não largura de controles pesados.
- Transições por **tempo** (ease-in-out), nunca por quadro.
- Listas e grades sempre virtualizadas.
- Rolagem sincronizada (diff) com um único controlador que move os dois painéis na mesma mensagem.
## Padrões e estilo de código (resumo; detalhe em docs/PLANO-ACELERADO.md §1.2)
- Uma branch por funcionalidade: `feature/<modulo>-<finalidade>`, cortada da `main` atual; merge `--no-ff` só depois da revisão do usuário; branches não são apagadas.
- Usar bibliotecas conhecidas (WPF UI ou MaterialDesign, XamlFlair, CommunityToolkit.Mvvm, AvalonEdit, DiffPlex, LiveCharts2, Velopack...) em vez de escrever controles do zero.
- Fluxo único de operações em lote (`OperacaoEmLote`), catálogo de comandos (pílula, menu e paleta leem a mesma lista), `Resultado<T>` em vez de exceção, `Armazenamento<T>` para JSON, `GitArgs` fluente, decoradores para perfil/proteção/log, injeção de dependência (sem estáticos de estado).
- C# 12 enxuto: `record`, construtores primários, nullable ligado, avisos como erro. Arquivo até ~300 linhas, método até ~40. Lógica de negócio no Core, nunca em code-behind/ViewModel.