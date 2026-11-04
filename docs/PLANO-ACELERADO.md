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
| `feature/fundacao-contratos` | Interfaces do Core (`IGit`, `IProcessoRunner`, `IArmazenamento`, `IProjetos`, `IProgresso`), catálogo de comandos (`IModulo`, `ICatalogoDeComandos`), `Menage.TestKit` (repositórios descartáveis, Maven falso, Nexus falso), tokens do design system, regras de desempenho. | Compila, testes de contrato passam. |
| `feature/provas-de-conceito` | Diff em AvalonEdit com arquivo de 7 mil linhas (pintura menor que 4 ms por painel, rolagem e arraste sincronizados), janela sem barra de título, grade virtualizada com 200 linhas, self-contained comprimido (tamanho e abertura). | Números medidos registrados; decisões de editor e publicação fechadas. |

### Etapa 1: quatro branches independentes (podem rodar em paralelo)
| Branch | Entrega |
|---|---|
| `feature/nucleo-git` | Executor do Git (variáveis `GIT_*`, SSH/Pageant, `sshCommand`), configuração, grupos, fetch/pull/status/checkout/branch/stash/cherry/rebase, índice de commits, proteção de branches, erros. Portar as seções de teste correspondentes. |
| `feature/design-system` | 12 temas + privados com troca ao vivo, todos os controles (pílula, barra responsiva, cartão, interruptor, chip, campos, grade, menu, mensagem, aviso), janela base, animações por tempo, paleta Ctrl+K como componente, galeria automática por tema. |
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
| `feature/instalador-atualizacao` | Instalador próprio (pasta e atalhos próprios), atualização pelo GitHub, ícones. (Independente: pode andar em paralelo a qualquer etapa depois da fundação.) |

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
| 0: fundação e provas | 2 a 3 |
| 1: núcleo, design system, diff, motores | 8 a 12 (parte em paralelo) |
| 2: janela principal | 3 a 4 |
| 3: módulos de tela | 12 a 18 (parte em paralelo) |
| 4: paridade e acabamento | 3 a 5 |
| **Total** | **~28 a 42 sessões** |

Em tempo de calendário, isso fica em **cerca de 4 a 8 semanas** se houver sessões quase todo dia e revisão em até 1 dia. Os números são estimativas minhas, não medidas, e mudam com: tempo de revisão, quantas branches rodam em paralelo (cada uma é uma sessão, e várias ao mesmo tempo consomem mais do limite de uso) e surpresas nas provas de conceito.

## 6. Riscos
| Risco | Tratamento |
|---|---|
| Conflitos entre branches paralelas | Pastas por módulo, catálogo de comandos, rebase na `main` antes do merge, testes verdes. |
| Revisão acumulando | Entrega por branch com galeria e roteiro curto; revisar em lotes. |
| Editor de diff no WPF | Prova de conceito na Etapa 0, antes de investir no resto. |
| Divergência visual entre telas | Só a branch do design system cria controles; as outras consomem. |
| Limite de uso do Claude | Começar com 2 branches em paralelo e subir conforme o consumo. |

## 7. Decisões que preciso antes de começar a Etapa 0
1. Quantas branches posso rodar **ao mesmo tempo** (1, 2 ou 3)?
2. Aceita revisar **por branch** (galeria de imagens + roteiro) antes de cada merge na `main`?
3. As branches antigas mantidas (não apagar) tudo bem, ou prefere apagar depois do merge?
