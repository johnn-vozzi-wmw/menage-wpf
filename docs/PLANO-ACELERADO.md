# Menage: plano acelerado (substitui as fases M0 a M11 de PLANO.md)

Ideia: tudo que não depende de outra coisa roda **ao mesmo tempo**, em trilhas separadas (uma sessão do Claude por trilha, cada uma na sua branch e com seus próprios arquivos). O motor (Core) e a tela (App) andam em paralelo, e os módulos independentes entre si também. O que continua em série é só o que realmente bloqueia: a base de contratos, a janela principal e a integração.

O escopo é o mesmo do plano anterior (todas as funcionalidades). Muda a ordem e o paralelismo.

## 1. Regras que permitem trabalhar em paralelo sem conflito

1. **Contratos primeiro (Onda 0).** Interfaces do Core e pontos de extensão da tela ficam prontos antes de qualquer módulo: `IGit`/`IProcessoRunner`, `IArmazenamento` (config por módulo), `IProjetos`, `IProgresso`, `IServicoDeJanelas`.
2. **Registro de comandos.** Cada módulo se registra sozinho (`IModulo.Registrar(ICatalogoDeComandos)`): pílula, item de menu, entrada da paleta Ctrl+K, perfil que o libera. Ninguém edita a janela principal para "pendurar" o seu botão. Assim a janela principal não vira ponto de conflito.
3. **Pasta e projeto por módulo.** `Menage.Core/<Modulo>` e `Menage.App/Modulos/<Modulo>`, testes em `tests/<Modulo>`. Uma trilha só mexe na própria pasta; mudança em compartilhado (Core base, design system) passa por pedido à trilha dona.
4. **Dono do compartilhado.** Trilha Base é dona do design system e do Core base; as outras só consomem.
5. **Uma branch por trilha**, integração diária na `main` por merge pequeno e testes verdes (`dotnet test`). Nenhuma trilha publica atualização.
6. **Dados de exemplo e repositórios descartáveis** gerados por um projeto de testes comum (`Menage.TestKit`), usado por todas as trilhas, para ninguém reinventar o cenário de teste (regra: nunca tocar nos repositórios reais).

## 2. Ondas

### Onda 0: base e contratos (1 semana, em série, feita por mim)
- Contratos do Core e do registro de comandos (§1).
- `Menage.TestKit` (repositórios descartáveis, Maven falso, servidor Nexus falso).
- Esqueleto do design system (tokens de cor, tipografia, espaçamento, duração/easing) e da janela base.
- **Provas de conceito do que é risco:** diff em AvalonEdit com arquivo de 7 mil linhas (pintura, rolagem sincronizada, arraste da barra), janela sem barra de título com redimensionar, grade virtualizada com 200 projetos, self-contained comprimido (abertura e tamanho).
- Saída: decisões fechadas (editor, publicação) e as trilhas da Onda 1 liberadas.

### Onda 1: quatro trilhas em paralelo (4 a 5 semanas)

| Trilha | Conteúdo | Depende de |
|---|---|---|
| **T1 Núcleo Git** | Executor do Git (variáveis `GIT_*`, SSH, `sshCommand`), configuração, grupos de projetos, operações básicas (fetch, pull, status, checkout, branch, stash, cherry, rebase), busca/índice de commits, proteção de branches, erros do Git. Porta das seções de teste correspondentes. | contratos |
| **T2 Design system e janela** | Temas (12 + privados, troca ao vivo), todos os controles (pílula, barra de pílulas responsiva, cartão, interruptor, chip, campos, DataGrid, menu, mensagem, aviso), janela base sem barra de título, animações por tempo, paleta Ctrl+K como componente, catálogo de comandos. Galeria automática por tema. | contratos |
| **T3 Diff e conflito** | Modelo de diff (porta direta), controle de diff lado a lado, ligações, régua, navegação, controlador único de rolagem (X e Y e arraste), editor de conflito com resultado editável, grava preservando codificação/BOM/fim de linha. Funciona isolado com arquivos de exemplo. | contratos, PoC |
| **T4 Motores independentes** | Portar só os motores (sem tela) que não dependem do Git em lote: Nexus, Tomcat Express, contexto do banco, Jira/diário, Beta automática (JQL e estados), Gerador de Beta (execução, target, entregas), pull requests, atualização. Cada um com testes e fachada pronta para a tela. | contratos, TestKit |

Saída da Onda 1: Core com todos os motores testados; design system completo; diff e conflito completos e testados; janela principal ainda não existe.

### Onda 2: janela principal (2 a 3 semanas, em série, com T2 + T1)
- Casca da janela: barra de ações (a partir do catálogo de comandos), grade com situação/etiquetas, busca, grupos, Saída recolhível, status, paleta, favoritos, fetch automático, perfis.
- Primeiras ações reais: Fetch, Pull, Sync, Checkout, Stash.
- Aqui nasce o serviço de janelas e o contexto "projetos marcados" que os módulos usam.
- Em paralelo (sem esperar): T3 e T4 continuam; T1 termina operações em lote que ainda faltam.

### Onda 3: telas dos módulos, seis trilhas em paralelo (4 a 6 semanas)

| Trilha | Telas | Motor já pronto em |
|---|---|---|
| **U1 Operações em lote** | Commit, Push, Nova branch, Rebase, Com develop, Renomear, Excluir, Limpeza, Desfazer commit, Buscar commit, Commit pom, Diagnóstico, Proteção, Ferramentas (tags, Git Doctor, remotos, não enviados), Configurações e perfis | T1 |
| **U2 Cherry-pick** | Janela Cherry-pick (filtros, painéis/abas, grade virtual), fila em lote (stepper, tiles, conflito, retomar) | T1 + T3 |
| **U3 Gerador de Beta** | Tela do gerador, favoritos, limpar target, console, Entregas, Beta automática | T4 |
| **U4 Melhorias e Diário** | Painel lateral, dashboard (abas, tags, filtros), editor, diário, contexto do banco | T4 |
| **U5 Nexus, Tomcat e PRs** | Tela Nexus, Tomcat Express (abas Ambiente/Banco/Preparar/Logs), pull requests | T4 |
| **U6 Instalador e atualização** | Instalador próprio (pasta e atalhos próprios, perfil de uso, reinstalar/desinstalar), atualização pelo GitHub, ícone e logos | independente |

Cada trilha usa o catálogo de comandos (§1.2): nada de editar a janela principal. Cada uma entrega com testes de ViewModel, captura por tema e roteiro de revisão.

### Onda 4: integração e acabamento (2 a 3 semanas)
- Revisão cruzada de paridade contra `docs/FUNCIONALIDADES.md` do Git Manager (somente leitura, como referência), item a item.
- Desempenho (abertura, memória, pintura), DPI 100/125/150, temas, teclado e acessibilidade.
- Personalização (GIF leve, frases), polimento visual e piloto com 2 ou 3 colegas.

## 3. Prazos

| | Plano anterior (série) | Acelerado |
|---|---|---|
| Base e provas | 1 a 2 semanas | 1 semana |
| Núcleo, tema, diff e motores | ~12 a 16 semanas em série | 4 a 5 semanas em paralelo (4 trilhas) |
| Janela principal | 4 a 6 semanas | 2 a 3 semanas |
| Telas dos módulos | ~20 a 28 semanas em série | 4 a 6 semanas em paralelo (6 trilhas) |
| Acabamento e piloto | 2 a 3 semanas | 2 a 3 semanas |
| **Total** | **8 a 9 meses** | **~3 a 4 meses** |

São estimativas minhas, não medidas. O prazo real depende de dois fatores: (1) **sua revisão**, que é o gargalo (telas prontas esperando aprovação seguram a trilha); (2) **o custo de rodar várias sessões ao mesmo tempo** (cada trilha é uma sessão com o contexto dela, usando mais do seu limite de uso do Claude). Se o limite apertar, reduzo para 2 ou 3 trilhas simultâneas e o total sobe para ~4 a 5 meses.

## 4. O que muda na prática para você
- Você aprova **por trilha**, com galeria de imagens por tema e roteiro curto de teste, em vez de acompanhar tela por tela.
- Marcos de entrega visíveis: fim da Onda 1 (diff e conflito funcionando sozinhos), fim da Onda 2 (janela principal usável para o dia a dia básico), fim da Onda 3 (paridade de funções).
- Dá para o piloto com colegas começar no fim da Onda 2 só com as ações básicas, enquanto a Onda 3 completa o resto.

## 5. Riscos específicos do paralelismo
| Risco | Tratamento |
|---|---|
| Contratos mal desenhados travam várias trilhas | Onda 0 é em série e revisada por você antes de liberar as trilhas; mudança de contrato só pela trilha dona. |
| Conflitos de merge em compartilhados | Pastas por módulo, registro de comandos, merge diário pequeno, testes verdes obrigatórios. |
| Estilo visual divergente entre trilhas | Só T2 cria controles; as demais usam o design system; revisão visual por galeria. |
| Esforço de revisão concentrado | Entrega por trilha com galeria e roteiro; revisar em lotes. |
| Limite de uso do Claude com várias sessões | Começar com 2 trilhas na Onda 1 (T1 + T2) e abrir T3 e T4 conforme o consumo. |

## 6. Decisões que preciso antes de começar a Onda 0
1. Autoriza rodar **várias sessões em paralelo** (uma por trilha) ou prefere no máximo 2 ao mesmo tempo?
2. Revisão: aceita aprovar **por trilha** com galeria de imagens, em vez de tela por tela?
3. Manter o escopo total desde já (todas as funções), ou entregar o piloto no fim da Onda 2 só com o básico?
