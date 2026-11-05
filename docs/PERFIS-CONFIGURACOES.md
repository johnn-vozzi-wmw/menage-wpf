# Perfis e Configurações (`feature/perfis-configuracoes`)

Migração dos **perfis de uso** e da janela **Configurações** do Git Manager antigo. O motor mora em `Menage.Core`, as telas em `Menage.App/Modulos`.

## O que existe

| Parte | Onde | Resumo |
|---|---|---|
| Perfis | `Core/Perfis` (`PerfilDefinido`, `ServicoDePerfis`, `Funcionalidades`) | Geral (fixo), Desenvolvedor e Tester (editáveis, com "Restaurar padrão") e perfis criados pelo usuário. Cada perfil tem uma **base** (Geral/Desenvolvedor/Tester) e uma lista `permitidas` (`*` libera tudo, `-id` tira um item), as mesmas regras do `perfil.json` antigo. Criar, editar, duplicar, excluir, ativar, importar e exportar (arquivo `menage-perfis`). Gravado em `perfis.json`. |
| Bloqueio | `ExecutorComPerfil`, `BarraDeAcoesViewModel` | O executor consulta a lista do perfil ativo (via `Funcionalidades.DaOperacao("git.push")` → `push`) além dos perfis que a operação declara. A pílula fica apagada com a dica "Bloqueado no perfil X. Disponível em: ...". Só na interface: não é segurança. |
| Configurações | `Core/Configuracoes/ConfiguracoesGerais` | Git (ferramenta de diff, minutos do fetch, tags), Jira (endereço, projetos, variável do token), caminhos (chave SSH, WinMerge, TortoiseGit), comportamento (verificar atualização, script da atividade). Gravado em `configuracoes.json`. A raiz dos projetos continua em `projetos.json` e a proteção de branches em `protecao.json` (módulos que já existiam). |
| Credenciais | `Core/Configuracoes/CofreDeCredenciais` | Usuário + segredo por serviço (Jira, Nexus, GitHub) em `credenciais.json`; o segredo é protegido por **DPAPI do usuário atual** (`ProtetorDpapi`, mesmo formato do Git Manager antigo). Nunca volta para a tela nem vai para log. |
| Migração | `Core/Configuracoes/MigradorDoGitManager` | **Somente leitura** da pasta antiga. `Analisar` lista o que há e o que cada item faria; `Aplicar` importa só os itens marcados. Itens: perfil.json, preferencias.json, protecao.json, config-geral.cfg (raiz), config-ssh.cfg, merge.json, config-update.cfg, diario.json (endereço e projetos do Jira, nunca o token), config-nexus.json (senha reprotegida), config-tema.cfg. Nunca escreve, move nem apaga nada na pasta antiga. |
| Telas | `App/Modulos/Perfis`, `App/Modulos/Configuracoes` | `JanelaDePerfis` (lista, editor com caixas por grupo, importar/exportar) e `JanelaDeConfiguracoes` (7 seções, um Salvar). Entrada na barra lateral (cartão Configurações), na paleta Ctrl+K (`config.abrir`, `config.perfis`, `config.importar`). A troca de seção usa opacidade/deslocamento por tempo (`Animacao.Media`/`Entrada`). |

## Decisões e limites

- "Perfil" aqui é o **perfil de uso** do Git Manager (Geral/Dev/Tester + personalizados). Grupos de projetos já existem em `Projetos` e não foram duplicados.
- Comandos do catálogo ainda filtram pela **base** do perfil (`PerfisQueLiberam`); a lista editável vale para operações em lote e para as pílulas da barra (por id). Comandos que não correspondem a uma funcionalidade do catálogo seguem só a base.
- Fetch automático (ligado/desligado) e "Saída recolhida" continuam em `janela-principal.json`, a fonte que a janela principal já lê; Configurações apenas escreve lá.
- O token do Jira não é migrado (o antigo não guardava; ficava em variável de ambiente). O Nexus: só usuário e senha vão para o cofre; URL/repositório ficam para o módulo do Nexus.
- A migração de tema só aplica ids que o Menage conhece; os demais são ignorados com aviso.
- `diario.json` antigo: nomes dos campos (`jiraUrl`, `projetosJira`, `varTokenJira`) lidos por analogia com o código antigo; **não foram conferidos com um arquivo real**.

## Testes

```
dotnet build tests/Menage.Tests -warnaserror
dotnet test tests/Menage.Tests --filter "FullyQualifiedName~PerfisConfiguracoes"
dotnet test tests/Menage.App.Tests --filter "FullyQualifiedName~PerfisViewModelTests"
```

Galeria (claro e escuro; usa pasta de dados e "Git Manager antigo" de mentira em `%TEMP%`, sem foco nem cliques):

```
src\Menage.App\bin\Debug\net8.0-windows\Menage.exe --galeria-configuracoes docs\galeria\perfis-configuracoes
```

## Roteiro de teste manual

Use uma cópia da pasta antiga se quiser ter certeza de que nada muda nela (o Menage só lê).

1. **Perfis.** Barra lateral > Configurações... > aba "Perfil de uso" > "Gerenciar perfis...". Selecione Tester: Push e Deploy desmarcados, Fetch marcado. Crie "Novo", desmarque Push, salve; duplique; exclua a cópia (pede confirmação). Tente excluir o Tester (recusa). Edite o Tester, "Restaurar padrão" volta a lista.
2. **Ativar.** Ative "Tester" na janela de perfis: a pílula Push da janela principal fica apagada com a dica "Bloqueado no perfil Tester. Disponível em: ...". Volte para Geral.
3. **Edição pendente.** Altere um nome e clique em outro perfil: aparece "Descartar alterações?". "Cancelar" mantém a edição.
4. **Importar/exportar.** Exporte um perfil seu; importe o arquivo: entra como "Nome (2)". Escolha um .json qualquer: mensagem de erro clara e nada muda.
5. **Configurações.** Em cada seção altere um valor e Salvar; feche e reabra: os valores voltam. Fetch fora de 1 a 240 ou endereço do Jira inválido: mensagem de erro e nada é gravado. "Descartar alterações" recarrega.
6. **Credenciais.** Seção Jira e credenciais: preencha usuário e token, "Guardar": estado "Guardada...". O campo do token esvazia. Abra `%LOCALAPPDATA%\Menage\credenciais.json`: o token não aparece. "Remover" pede confirmação.
7. **Proteção.** Troque a lista de branches, ligue a convenção de commit, salve e tente um Commit/Push numa branch protegida (a proteção continua a do módulo existente).
8. **Importar do Git Manager.** Seção "Importar do Git Manager": "Procurar em C:\wmw" ou "Escolher..." a pasta de uma instalação antiga (sem apontar para a pasta de dados de outro usuário). "Ver o que há na pasta": itens sem arquivo ficam apagados. Marque, "Importar": a prévia lista cada item; depois confira os valores nas outras seções. Confira no Explorer que a pasta antiga não mudou (datas e arquivos).
9. **Claro e escuro.** Ctrl+T com as janelas abertas: tudo troca de tema.

Não verificado: clique real em todas as telas com um perfil Tester ativo, a importação de uma pasta de instalação real do Git Manager e a leitura de um `diario.json` real.
