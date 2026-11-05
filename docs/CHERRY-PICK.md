# Cherry-pick

Motor em `src/Menage.Core/CherryPick/` (fila, script de banco, stash de commits); telas em `src/Menage.App/Modulos/CherryPick/`.

| Peça | Papel |
|---|---|
| `ServicoDaFila` | Dono da fila (`fila-cherry.json` via `Armazenamento<T>`): adicionar sem repetir, remover e reordenar pendentes, grava a cada mudança. Ao reabrir, "rodando" vira "parada" e "aplicando" volta a pendente |
| `FilaDeCherryPickEmLote` | Operação em lote: um projeto por vez, um commit por vez; conflito ou erro pausa a fila (`InterromperLote`) deixando o git em cherry-pick em andamento. Perfil Tester bloqueado |
| `PreparoDoProjeto` | Antes do 1º commit do projeto: pasta limpa, sem rebase/merge/cherry-pick, troca para o destino, avança pelo origin (ff-only, opcional), anota o HEAD e cria `backup/cherry-<destino>-<data>` |
| `AplicadorDaFila` | `cherry-pick -x --empty=drop`; já aplicado (mesmo patch, `git cherry`) ou vazio vira pulado; merge é pulado |
| `ControleDaFila` | Depois da pausa: "Já resolvi" (confere conflito e marcadores, `--continue`), pular, tentar de novo, abortar (opção de voltar cada projeto ao HEAD de antes) |
| `RetiradorDeScript` + `ConfigDoScript` | Em destino da lista (padrão `develop`, aceita `*`) tira `docs/modelo-er/scripts/script_lavendere_alteracoes_<BRANCH>*` do commit aplicado (amend, mantém mensagem e `-x`); commit só com o script não é criado; conflito só no script se resolve sozinho. Configuração em `merge.json` (ligada por padrão) |
| `StashDeCommitsEmLote` | Cada commit marcado vira um stash próprio, sem mexer na pasta nem na branch |
| `JanelaCherryPick` | Histórico dos projetos marcados (painéis lado a lado ou abas, Ctrl+L), índice de commits em memória, filtros, marcar (caixa, duplo clique, Espaço, Shift/Ctrl), ações |
| `JanelaFila` | Destino, backup, tiles, progresso, linha do tempo animada, reordenar (Alt+↑/↓), remover (Delete), aviso de pausa |

Comandos: pílula **Cherry-pick** (`git.cherry-pick`) e **Fila de cherry-pick** no menu Mais (`git.cherry-fila`). A janela principal avisa ao abrir se há fila pendente.

Nada é enviado ao origin. "Resolver aqui" abre o editor de conflito do módulo Diff (já faz `git add`).

## Testes
`dotnet test tests/Menage.Tests --filter FullyQualifiedName~CherryPick` (motor, em repositórios descartáveis). Galeria (repositórios em `%TEMP%`, em segundo plano, com conflito real): `dotnet run --project src/Menage.App -- --galeria-cherry docs/galeria/cherry-pick`.
