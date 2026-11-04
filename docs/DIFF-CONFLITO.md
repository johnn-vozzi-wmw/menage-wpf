# Diff e conflito

Branch `feature/diff-conflito`. Motor em `src/Menage.Core/Diff`, tela em `src/Menage.App/Modulos/Diff`, testes em `tests/Menage.Tests/Diff`. Capturas em `docs/galeria/diff/` (claro e escuro).

Abre sozinho, sem a janela principal:

```bash
dotnet run --project src/Menage.App -- --diff C:\caminho\repo [arquivo] [--escuro]
```

```bash
dotnet run --project src/Menage.App -- --conflito C:\caminho\repo [--escuro]
```

## Decisões

| Assunto | Decisão | Por quê |
|---|---|---|
| Linhas de preenchimento | Nenhuma. Cada painel mostra o texto real, com a numeração dele, e faixas curvas ligam as mudanças | Regra do app antigo; a prova de conceito preenchia |
| Trechos recolhidos | O documento só tem as linhas visíveis, com uma linha "⋯ N linhas iguais". Sem `FoldingManager` | Menos custo por linha e números reais de graça |
| Motor de diff | **DiffPlex**, em duas etapas: alinha pelo texto exato (sem o fim de linha) e, só dentro de cada bloco alterado, compara sem espaços | Ver comparação abaixo |
| Rolagem | `MapaRolagem`: coordenada unificada e sincronia fixa no meio da tela, `Y = limite(linha(U + meio) - meio)` | Em trecho igual a diferença entre painéis é um número inteiro de linhas: alinhado no pixel. Ninguém anda para trás e todos chegam às pontas juntos |
| Quadro de rolagem | `InvalidateLayer(.., Render)` depois de mudar a posição | `SetVerticalOffset` só agenda a medida; a tela ficava um quadro atrás nos dois painéis |
| Acompanhar o alvo | Decaimento exponencial por tempo (28 ms no arraste, 55 ms no resto) | Responde na hora, não passa do alvo, não depende da taxa de quadros |
| Texto | `TextFormattingMode=Display`, espaçador uma linha por tela, objetos de texto em vez de `Border` | Altura e largura inteiras; a prova criava um elemento WPF por linha a cada quadro |
| HEAD no diff | `git cat-file --filters` | Com `core.autocrlf`, o conteúdo cru faria todas as linhas parecerem mudança de fim de linha |
| Pendências do conflito | A linha de aviso no resultado é a fonte da verdade | Desfazer, refazer e editar continuam certos sem estado à parte |

Não há ViewModels neste módulo: a lógica está no Core e o code-behind só liga controles.

## DiffPlex x modelo antigo (`git diff -U100000000`)

150 pares reais de `.java` do lavenderepda (só leitura, últimos commits).

| Medida | Resultado |
|---|---|
| Mesmas linhas alteradas que o git | **149 de 150** (o outro tem diferenças de fim de linha, que aqui ficam alinhadas à parte) |
| Mesmo número de blocos | 123 de 150 (os demais separam linhas que só mudaram espaços) |
| Segmentos cobrindo os dois arquivos sem buraco | 150 de 150 |
| Tempo, mediana / p95 | **1 ms / 19 ms** (git 50 ms / 75 ms, por criar processo) |
| 8.000 linhas, todas CRLF → LF | 11 a 18 ms, nenhuma mudança real, 8.000 linhas explicadas |
| 8.000 linhas sem nada em comum | 420 a 690 ms (git 60 ms): é o pior caso do DiffPlex; roda fora da thread de UI |

A primeira versão comparava já ignorando espaços e divergia em 23 de 150 arquivos: o `}` de um bloco inserido casava com o `}` antigo de outra indentação. Por isso as duas etapas.

## Números medidos

Windows 10, 1920x1080 a 100%, Release, janela 1300x760 (a mesma da prova), arquivo de 7,6 mil linhas com linhas de até 9 mil caracteres. Janela fora da tela. Mesmo método da prova: thread de UI até o ciclo de renderização terminar, 80 passos.

| Caso | Prova | Agora: mediana | p95 |
|---|---|---|---|
| Vertical, 1 painel, recolhido | 3,9 a 4,9 ms | **2,2 a 3,0 ms** | 4 a 5 ms |
| Vertical, 1 painel, expandido | 5,1 a 6,3 ms | **2,2 a 2,4 ms** | 5 a 6 ms |
| Vertical, os dois painéis | 6,5 a 9,7 ms | 3,9 a 4,8 ms | 7 a 9 ms |
| Refeita da janela de colunas (trabalho na thread de UI) | pico de 10 a 21 ms | **1,0 ms**, máximo 2 a 8 ms | 1,8 ms |
| Refeita com TODAS as linhas longas (42 por painel, pior caso) | não medido | 9 a 10 ms, máximo 17 a 31 ms | 13 a 15 ms |

A meta de menos de 4 ms por painel foi atingida. A primeira medição desta branch deu 4,9 a 5,3 ms expandido; o que baixou foi o espaçador uma vez por tela e a janela de colunas de 3 telas. O valor oscila com a carga da máquina (numa rodada com outras tarefas, 3,3 a 4,7 ms). Com todas as linhas longas o arraste horizontal cai para cerca de 50 quadros por segundo.

### Arraste

32 cenários: barra da esquerda e da direita, rápido (260 ms de ponta a ponta), rápido na volta e devagar (2,6 s), régua, arquivo só à direita, arquivo inteiro recolhido e expandido, horizontal com todas as linhas longas. Cada quadro é conferido depois do layout contra o mapa, e em três momentos do arraste as duas faixas de texto são comparadas pixel a pixel em volta da linha de sincronia.

| Rodada | Quadros | Painéis desalinhados | Recuos | Pixels no arquivo de alinhamento |
|---|---|---|---|---|
| **Mouse real** (`SetCursorPos` + `mouse_event`, pixels do monitor) | cerca de 6.900 | 0 | 0 | idênticos, exceto quando a faixa cai num bloco alterado |
| Segundo plano, código final | 2.754 | 0 | 0 | idênticos em 21 de 21 capturas |

Com o mouse real, o intervalo entre quadros ficou em 8 a 10 ms de mediana e máximo de 37 ms, e o conteúdo assenta 200 a 500 ms depois de soltar, nos arrastes mais rápidos. Em segundo plano os intervalos de 60 a 120 ms são o custo da própria captura no meio do arraste.

O arraste real foi feito antes de três correções finais (arredondamento igual nos painéis, pílula antes do espaçador, estilo da lista). A rodada de segundo plano, que não toca no mouse, roda sobre o código final e dispara o mesmo evento da barra: do controlador em diante o caminho é o mesmo; só o polegar pegando o mouse não é exercitado.

**Sem explicação:** 2 quadros de 2.754 (e 1 a 2 nas rodadas com mouse real) foram marcados como "linha do topo diferente da posição", com as linhas válidas. Não apareceu em nenhuma comparação de pixels. Pode ser efeito do próprio registro, mas não foi confirmado.

## O que foi visto de verdade e o que não foi

| Situação | Itens |
|---|---|
| Testado com mouse e teclado reais | Arraste das barras e da régua, PageUp/Down, setas, Home/End, F7, Shift+F7, Alt+↓, Ctrl+B, F5, seleção com o mouse nos dois painéis, Ctrl+C, clique na dobra, Alt+1 |
| Testado em segundo plano, sem entrada real | Escolha, desfazer, refazer, desfazer escolha, recusa de salvar com pendente, confirmação ao fechar, gravação com BOM e CRLF, `git add`, binário, navegação entre conflitos, cópia trazendo as linhas de um trecho recolhido |
| Conferido nas capturas | Faixas, números, palavras, etiquetas, aviso, régua, pulso, lista recolhida, arquivo só à direita, binário, pílulas, âmbar, claro e escuro |
| Só compilado, **não clicado** | Dica ao passar o mouse nas etiquetas, clique nas chaves, nas pílulas e nos botões da barra do conflito, duplo clique no bloco, menu do botão direito, Alt+2, Alt+3, Ctrl+Z e Ctrl+Y no teclado, Shift+roda, touchpad, as caixas de diálogo reais (`DialogosDiff`) |
| Não verificado | Abrir `--diff`/`--conflito` pelo `Menage.exe` com a janela visível (a montagem pelos mesmos argumentos foi testada sem exibir), DPI diferente de 100%, mais de um monitor, sensação a olho nu |

## Pendências conhecidas

- **Troca de tema ao vivo:** nesta montagem isolada, com os dicionários adicionados por código e a cor de destaque aplicada, a primeira troca com a janela aberta lança exceção de `AllowsTransparency` no `FluentWindow`. A demonstração escolhe o tema na partida (`--escuro`), então isso não aparece nela. Sem a cor de destaque a troca funcionou, e a prova trocou tema e destaque ao vivo com os dicionários no `App.xaml`. **Conferir ao integrar com o design system.**
- **Cor de destaque:** a demonstração usa azul fixo (o destaque do Windows desta máquina é quase preto); a paleta real vem do design system.
- **Sem realce de sintaxe:** o app antigo colore Java. Não fazia parte do pedido e custaria tempo por linha; precisa ser medido se entrar.
- **Sem comando no catálogo:** o módulo ainda não tem `IModulo`; entra com a janela principal, assim como a implementação geral de `IServicoDeDialogos`.
- **`feature/nucleo-git`:** o módulo usa só a fundação (`IGit`); nada dependeu do núcleo.
- **Linha longa com tabulação:** fora da janela de colunas o texto conta 1 coluna por caractere; uma tabulação numa linha com mais de 1.200 caracteres pode deslocar o texto ao refazer a janela.

## Roteiro `tests/Menage.Diff.Roteiro`

Fora do app final e fora do `dotnet test`. Cria repositórios descartáveis em `%TEMP%` e só lê `C:\wmw\wmwvendas`.

```bash
dotnet run --project tests/Menage.Diff.Roteiro -c Release -- --saida pasta --so arraste
```

`--so` aceita `capturas`, `medir`, `arraste`, `conflito`, `motores` e `demo`; todos rodam **fora da tela, sem tocar no mouse, no teclado nem no foco**. `mouse-real` toma o cursor e o foco: só rode saindo do computador.

## Roteiro de teste manual (cerca de 10 minutos)

Num repositório descartável, com alguns arquivos alterados, um novo e um excluído:

1. `--diff <repo>`. A primeira mudança aparece no meio da tela, com pulso azul curto, e a ficha diz "Mudança 1 de N".
2. **Arraste a barra da esquerda e a da direita, rápido e devagar.** Os painéis não podem atrasar um do outro nem pular. Repita na régua e num arquivo novo (só à direita).
3. F7, Shift+F7, Alt+↑/↓ e as setas da barra. Clique num "⋯ N linhas iguais".
4. Selecione texto nos dois lados e copie, inclusive por cima de um trecho recolhido.
5. Desligue "Ignorar fim de linha e espaços" num arquivo com CRLF trocado, passe o mouse na etiqueta e na bolinha da margem.
6. Ctrl+B várias vezes; F5 mantendo o arquivo escolhido.
7. Crie um conflito e abra `--conflito <repo>`. Use botões, Alt+1/2/3, duplo clique, as pílulas, o botão direito (os dois, nas duas ordens) e "Desfazer escolha". Ctrl+Z e Ctrl+Y.
8. Tente salvar com pendente, feche sem salvar e confirme. Salve e confira `git status` e os fins de linha.
