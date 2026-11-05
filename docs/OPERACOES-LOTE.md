# Operações em lote (Commit, Push, Branch, Mais, Ferramentas, Configurações)

Branch `feature/operacoes-lote`. Cada operação herda `OperacaoEmLote` (Core) e só implementa o específico; os diálogos usam um formulário genérico (`Modulos/OperacoesLote/Formulario`), então não há um XAML por operação. Os comandos entram no catálogo por `ComandosDeLoteModulo` (pílula, menu e paleta leem a mesma lista); o registro no app é uma linha em `ComposicaoDoApp` (`AddModuloOperacoesLote`) e no Core, uma em `MenageCoreExtensions` (`AddOperacoesDeLote`).

| Onde | Operação | Core |
|---|---|---|
| Pílula Commit | Commit (prévia, só versionados, push opcional, ticket da branch, convenção) | `Operacoes/Commit` |
| Pílula Push | Push (fetch, publica branch nova, bloqueia se o origin avançou, `--force-with-lease` com confirmação) | `Operacoes/Push` |
| Branch ▾ | Criar (já existia), Renomear, Excluir, Limpeza, Rebase (já existia), Com develop | `Renomear`, `Excluir`, `Limpeza`, `ComDevelop` |
| Mais ▾ | Desfazer commit, Commit pom, Buscar commit (já existia), Diagnóstico, Ferramentas, Configurações | `DesfazerCommit`, `CommitPom`, `Diagnostico`, `Ferramentas` |

Regras de segurança cobertas por teste: proteção de branches (push e exclusão), backup `backup/<nome>-<data>` antes de excluir (nada é apagado se o backup falha), confirmação reforçada para main/master/develop/release* e para exclusão sem backup, branch atual nunca excluída localmente, tag nunca movida, push forçado só depois da confirmação, commit já enviado só é desfeito com permissão, Git Doctor com stash de segurança.

Fora do escopo desta entrega: Histórico de releases (depende dos logs do gerador de beta, outra branch) e perfil de uso nas Configurações (hoje `PerfilFixo`, sem armazenamento).

## Roteiro de teste manual

1. Abra o app com uma pasta de repositórios descartáveis (`--projetos <pasta>`), marque 2 projetos e mude arquivos neles.
2. **Commit**: a prévia lista os projetos; escreva a mensagem (a sugestão de ticket vem da branch), marque "Fazer push em seguida" numa branch que não seja protegida.
3. **Push**: confira ↑/↓ por projeto; numa branch sem upstream deve publicar; em `main` deve avisar "BLOQUEADO".
4. **Branch ▾ > Excluir**: informe uma branch de teste com backup; confira a branch `backup/...`. Tente `develop` e veja a confirmação reforçada.
5. **Branch ▾ > Limpeza**: mescladas vêm marcadas; as só inativas não.
6. **Mais ▾ > Desfazer commit**, **Commit pom**, **Diagnóstico**, **Ferramentas** (tag em massa, Git Doctor com stash) e **Configurações** (salvar e reabrir).

Galeria dos diálogos (dados de exemplo, claro e escuro): `docs/galeria/operacoes-lote/`, gerada por `dotnet run --project src/Menage.App -- --galeria-principal <pasta>`.
