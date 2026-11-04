# Provas de conceito da Etapa 0

Branch `feature/provas-de-conceito`. Código em `src/Menage.Pocs` (solução própria `Menage.Pocs.sln`, fora do app final). Todos os números abaixo foram medidos nesta máquina (Windows 10 19045, tela 1920x1080 a 100%, um monitor) com os próprios programas de teste; quando algo é só compilado ou não foi clicado, está dito.

Capturas de tela em `docs/galeria/pocs/` (geradas pelos programas, claro e escuro).

## Como rodar

| Projeto | O que é | Comando (a partir de `src/Menage.Pocs`) |
|---|---|---|
| `Pocs.WpfUi` | Tela de teste com WPF UI 4.3.0 | `dotnet run --project Pocs.WpfUi` |
| `Pocs.Material` | A mesma tela com MaterialDesignThemes 5.4.0 | `dotnet run --project Pocs.Material` |
| `Pocs.Diff` | Diff lado a lado (AvalonEdit 6.3 + DiffPlex 1.9) | `dotnet run --project Pocs.Diff -c Release` |
| `Pocs.Janela` | Janela sem barra de título (WindowChrome) | `dotnet run --project Pocs.Janela` |
| `Pocs.Comum` | Dados de amostra, 12 paletas, animador da barra lateral, medidores, barra de título | (biblioteca) |

Modo automático (usado nas medições): `--medir saida.json [--capturas pasta]` nos três primeiros; `--autoteste saida.json [--capturas pasta]` no `Pocs.Janela`. O `Pocs.Diff` gera os arquivos de teste em `%TEMP%\menage-poc-diff` e só **lê** o `Messages_en_US.java` de `C:\wmw\wmwvendas` (cópia em memória; nada é escrito lá).

## 1. Biblioteca visual: WPF UI x Material Design

Mesma tela nas duas: janela com barra de título própria, barra lateral que recolhe/expande, cartão com pílulas, `DataGrid` com 200 linhas virtualizadas (ícone de situação e etiquetas coloridas), tema claro/escuro, 12 paletas de destaque ao vivo (par claro/escuro por paleta, `CatalogoPaletas`), diálogo modal e entradas animadas com XamlFlair 1.2.13.

### Números (publish self-contained, `PublishSingleFile` + `EnableCompressionInSingleFile`, win-x64, Release; 5 execuções cada)

| Medida | WPF UI | Material Design |
|---|---|---|
| Tamanho do `.exe` comprimido | **62,3 MB** | 62,9 MB |
| + DLLs nativas que o publish deixa ao lado (WPF) | 7,8 MB | 7,8 MB |
| Abertura (início do processo até a janela pintada), mediana | **1,73 s** (1,46 a 1,81) | 1,85 s (1,81 a 2,10) |
| Memória ociosa, working set (mediana) | **231 MB** | 269 MB |
| Memória ociosa, privada | **127 MB** | 162 MB |
| Working set após usar (temas, animações, rolagem) | **256 MB** | 272 MB |
| Animação recolher/expandir: duração (30 ciclos) | 305 ms (alvo 220 ms + folga do cronômetro) | 285 ms |
| Passes de layout durante a animação, mediana (máximo) | 3 (14) | 3 (6) |
| Passes de layout com a tela parada (300 ms) | 0 | 0 |

Observações honestas sobre a abertura: os ~1,7 s incluem a descompressão do arquivo único. Em Debug, sem compressão, a abertura foi parecida (1,8 a 1,9 s), então o custo dominante é a inicialização do WPF e dos dicionários de estilos, não a compressão. A diferença entre as bibliotecas (cerca de 120 ms) está dentro da variação entre execuções.

### Fluidez da animação de recolher/expandir

- Técnica (`AnimadorBarraLateral`, em `Pocs.Comum`): a barra lateral tem sempre a largura cheia dentro de um `Canvas` (que não recorta o filho) e quem anima é só o `RenderTransform` do conteúdo, que tem fundo opaco e fica por cima, "descobrindo" ou "cobrindo" a barra. Ease-in-out cúbico por **tempo** (220 ms). A coluna do `Grid` muda de largura uma vez no começo e uma no fim.
- Resultado: **3 passes de layout por ciclo** (os dois da coluna + 1), em vez de um por quadro. A primeira versão animava o `Clip` do `Canvas` e gerou **um passe de layout por quadro (15 a 25 por ciclo)**, medido; foi descartada por isso.
- A técnica independe da biblioteca (é a mesma nas duas), então a diferença de fluidez entre as bibliotecas não existe; o que diferencia é só o custo dos controles em volta.
- O intervalo entre quadros (via `CompositionTarget.Rendering`) ficou com mediana ~12 a 13 ms e pior caso 30 a 40 ms em alguns ciclos (um ou dois quadros mais longos, na primeira vez que o ciclo roda). **Não verifiquei a sensação a olho nu**: só os números.
- Rolagem de 200 linhas virtualizadas na grade (40 saltos programáticos): ~0,95 s nas duas; não há diferença relevante.

### Pontos de qualidade (do que vi nas capturas e no código)

| Item | WPF UI | Material Design |
|---|---|---|
| Aparência | Fluent/Windows 11, combina com o sistema | Material, mais "de app web" |
| Troca claro/escuro + destaque ao vivo | `ApplicationThemeManager.Apply` + `ApplicationAccentColorManager.Apply(cor, tema)` | `PaletteHelper.SetTheme` com `SetPrimaryColor` |
| 12 paletas, par claro/escuro | Funcionou (capturas: azul, roxo, vermelho) | Funcionou (idem) |
| Janela própria | `FluentWindow` + `TitleBar` prontos (segundo a documentação, inclui o menu de encaixe do Windows 11 no botão de maximizar; não testei) | Não traz: usei a barra do `Pocs.Comum` com `WindowChrome` |
| Diálogo modal | `ContentDialog` | `DialogHost` (muito flexível) |
| Ícones | `SymbolIcon` (Fluent; ordem de ~1.000, da documentação, não conferi) | `PackIcon` (ordem de ~7.000, da documentação, não conferi) |
| Arestas | Pacote novo; alguns APIs mudaram na 4.x (`ContentDialogHost`); `DataGrid` simples | Mais maduro; mais pesado em memória |

### Recomendação: **WPF UI**

1. Menos memória (38 MB a menos de working set ocioso, 35 MB a menos de memória privada) e abertura um pouco mais rápida, com tamanho de publish praticamente igual (diferença de 0,6 MB).
2. Visual nativo do Windows 11 e janela própria pronta (`FluentWindow`/`TitleBar`), que, segundo a documentação, já resolve o menu de encaixe do Windows 11 no botão de maximizar (**não** consegui testar: esta máquina é Windows 10).
3. A troca de tema e de cor de destaque é uma chamada só, e as 12 paletas funcionaram.
4. Custo da escolha: biblioteca mais nova, menos ícones, API ainda se mexendo entre versões menores. Fixar a versão no `Directory.Packages.props` do app.

Se o usuário preferir o visual Material, a troca é viável: as medidas são próximas e a camada de tema/animação do `Pocs.Comum` é independente de biblioteca. A decisão é de gosto visual, e as capturas em `docs/galeria/pocs/` (`wpfui-*` e `materialdesign-*`) servem para ele escolher.

## 2. Diff lado a lado (AvalonEdit + DiffPlex)

### Arquivos de teste

`GeradorArquivosDiff` lê o `.java` real (7.585 linhas), anexa uma "cauda longa" (3.000 a 9.000 caracteres) a 1 de cada 25 linhas (304 linhas longas; a maior tem **9.223 caracteres**) e deriva a versão nova com linhas alteradas (1 a 2 palavras por linha, inclusive no fim das linhas longas), inseridas (3 linhas por bloco), removidas e um bloco reescrito. Resultado: **7.693 linhas alinhadas**, 247 trechos iguais recolhidos.

### O que foi construído

| Requisito | Como ficou | Estado |
|---|---|---|
| Diff lado a lado alinhado | `SideBySideDiffBuilder` do DiffPlex; colunas alinhadas com "linhas vazias" onde falta correspondente | Funciona (capturas) |
| Realce por palavra | Trechos de caracteres do DiffPlex; `DocumentColorizingTransformer` só nas linhas visíveis; fundo da linha inteira por `IBackgroundRenderer` | Funciona (capturas, claro e escuro) |
| Trechos iguais recolhidos | `FoldingManager` nos dois painéis, mesma faixa de linhas (3 de contexto); duplo clique alterna o trecho nos dois; botões "Recolher/Expandir tudo" | Recolher/expandir por código medido; **duplo clique não foi clicado** |
| Rolagem H e V sincronizada na MESMA mensagem | `ControladorRolagem` é o único dono: barras, roda, Shift+roda e touchpad só mudam o alvo; a cada quadro os dois `TextView` recebem o mesmo deslocamento no mesmo callback, seguido de `UpdateLayout` nos dois. Nada de aviso pós-pintura | Medido: 0 quadros com painéis diferentes |
| Arraste da barra fluido, por tempo | Posição atual desliza para o alvo em 150 ms (ease-out cúbico), re-ancorada a cada novo alvo; o polegar acompanha o mouse e o conteúdo desliza atrás | Medido por simulação (abaixo); **arraste real com mouse não foi feito** |
| Shift+roda e touchpad | Shift+roda e `WM_MOUSEHWHEEL` (touchpad horizontal) entram como alvo; passos finos de touchpad acumulam | **Só compilado, não testado com dispositivo real** |
| Fonte monoespaçada, linhas muito longas | `FatiaLinhasLongas`: fora de uma janela de colunas, o texto das linhas longas vira um bloco vazio de mesma largura; a janela é refeita só quando a rolagem chega perto da borda dela. `EqualizadorLargura`: espaçador no fim de 1 em cada 4 linhas para que os dois painéis tenham o mesmo comprimento horizontal | Funciona; ver custo abaixo |

### Pintura por painel (custo de cada passo na thread de UI: medida + layout + gravação do desenho; 80 passos por caso; Release; duas execuções)

Não é o tempo de GPU (WPF não expõe); é o tempo até o Dispatcher concluir o ciclo de renderização da thread de UI após o passo.

| Caso | Mediana | p95 |
|---|---|---|
| Vertical, 1 painel, trechos recolhidos | **3,9 a 4,9 ms** | 6,4 a 9,0 ms |
| Vertical, 1 painel, tudo expandido (7,7 mil linhas) | 5,1 a 6,3 ms | 8 a 11 ms |
| Horizontal, 1 painel | **0,9 a 1,9 ms** | 10 a 21 ms (quando a janela de colunas é refeita) |
| Vertical, os dois painéis juntos | 6,5 a 7,9 ms (recolhido), 9,2 a 9,7 ms (expandido) | 9 a 14 ms |
| Horizontal, os dois painéis juntos | 1,0 a 1,6 ms | 10 a 14 ms |

Antes da virtualização horizontal das linhas longas, a rolagem vertical custava mediana 9 a 11 ms por painel, com picos de 130 ms (cada linha longa nova exigia formatar milhares de caracteres). A `FatiaLinhasLongas` derrubou isso para ~4 a 6 ms. O `EqualizadorLargura` custou ~1 ms por painel a mais (sem ele ficava em 3 a 4 ms), mas é o que impede os dois painéis de pararem em X diferentes quando as linhas visíveis têm larguras diferentes (medido: sem ele, 2 quadros com painéis descasados, 587 px x 609 px).

**Meta de menos de 4 ms por painel: atingida só em parte.** Horizontal passa com folga (mediana ~1 a 2 ms). Vertical fica em ~4 a 6 ms (recolhido ~4 a 5; expandido ~5 a 6): no limite ou acima, e os picos no p95 (6 a 11 ms) e a refeita da janela de colunas na horizontal (10 a 21 ms) passam. Não consegui chegar abaixo de 4 ms na vertical sem abrir mão do alinhamento horizontal garantido; a próxima alavanca seria um espaçador só por tela, não por linha, ou um editor próprio (fora do escopo desta prova).

### Rolagem sincronizada e deslize (simulado por código, sem mouse)

| Cenário | Quadros | Maior passo por quadro | Quadros com painéis diferentes | Voltas para trás |
|---|---|---|---|---|
| Alvo distante (6.000 px em X, 30.000 px em Y) | 14 a 15 | 1.800 a 2.000 px em X, 8.600 a 9.400 px em Y (o ease-out começa rápido) | 0 | 0 |
| "Arraste rápido" (alvo muda a cada ~8 ms, 400 px em X e 600 px em Y por vez) | 113 a 139 | ~650 px em X, ~950 px em Y | 0 | 0 |

Custo de cada quadro aplicado (os dois painéis, até o fim do layout): mediana 4,7 a 4,9 ms, p95 11 a 20 ms, máximo 13 a 31 ms. A tela desta máquina entregou quadros a cada ~20 ms (cerca de 50 por segundo); um quadro de 20 a 30 ms a mais é um quadro perdido de vez em quando.

### Tempos e memória (Release, sem publicar)

| | Valor |
|---|---|
| Gerar os arquivos de teste | 53 a 60 ms |
| DiffPlex (7.585 x 7.693 linhas, ~300 linhas longas) | 131 a 136 ms |
| Carregar nos dois editores + recolhimentos | 111 a 117 ms |
| Abertura da janela (processo até pintada, inclui o diff) | 0,88 a 1,15 s |
| Memória após carregar (working set / privada / gerenciada) | 177 a 184 MB / 127 a 132 MB / 43 MB |
| Memória após o uso (rolagens, expandir tudo) | 196 a 205 MB / 133 a 143 MB / 27 MB |
| Altura do documento recolhido x expandido | ~29.900 px x ~116.300 px |


### Limitações e o que ficou de fora

- **Números de linha** não foram mostrados (a margem do AvalonEdit numera as linhas alinhadas, com vazias). Precisa de uma margem própria com os números reais.
- Editores só leitura e sem foco (sem seleção/cópia): é o preço de a rolagem ter um único dono.
- Fonte única monoespaçada assumida (Consolas); outra fonte exigiria recalcular a largura do caractere.
- Rolagem por teclado (PageUp/Down, Home/End) não foi ligada ao controlador.
- Largura máxima de linha formatada pelo WPF é limitada; linhas de 9 mil caracteres (66 mil px) funcionaram, mas não testei linhas maiores que isso.

## 3. Janela sem barra de título (WindowChrome)

`ConfiguradorJanela` + `BarraTitulo` (em `Pocs.Comum`), demonstradas no `Pocs.Janela` e usadas também na tela do Material. Medido por `--autoteste` (mensagem `WM_NCHITTEST` e posições reais):

| Verificação | Resultado |
|---|---|
| Estilo da janela tem `WS_THICKFRAME` e `WS_MAXIMIZEBOX` (requisitos do encaixe) | Sim |
| Bordas e quatro cantos devolvem HTLEFT/HTRIGHT/HTTOP/HTBOTTOM e os cantos | **11 de 11 pontos corretos** (barra de título = arrastar; botões e conteúdo = cliente) |
| Maximizada: conteúdo dentro da área de trabalho (sem vazar para fora da tela) | Sim (0,0 a 1920x1040 = área útil), depois de acertar o recuo |
| Encaixe real com Win+Seta Esquerda | Janela ficou com 960 px de 1.920 (metade) |

Dois achados que viraram correção: (1) com os botões encostados no topo, o canto superior direito devolvia HTCLIENT e não dava para redimensionar por ali; os botões ganharam margem superior de 6 px (= borda de redimensionar); (2) ao maximizar, o conteúdo vazava 8 px; o recuo agora é aplicado ao maximizar.

**Não testado:** menu de encaixe do Windows 11 ao passar o mouse no botão de maximizar (precisa do tratamento de `HTMAXBUTTON`; esta máquina é Windows 10) — o `FluentWindow` do WPF UI já traz isso; DPI diferente de 100%; mais de um monitor; arrastar a janela e a barra com o mouse (testei por mensagem, não por clique).

## 4. Velopack

Fora desta sessão (outra sessão cuida).

## Roteiro de teste manual (cerca de 10 minutos)

1. `dotnet run --project src/Menage.Pocs/Pocs.WpfUi` e depois `Pocs.Material`. Em cada uma:
   - Clique em "Recolher menu" várias vezes: a barra deve deslizar suave e voltar.
   - Clique em "Tema": claro/escuro. Clique nas 12 bolinhas: a cor de destaque (botão azul "Atualizar") muda na hora.
   - Clique em "Diálogo": aparece modal; o fundo não responde; "Cancelar" fecha.
   - Role a grade com a roda e arrastando a barra: deve ser fluido; os ícones e etiquetas devem aparecer sem atraso.
   - Arraste a janela pela barra de título, redimensione pelas bordas e cantos, maximize e restaure.
2. `dotnet run --project src/Menage.Pocs/Pocs.Diff -c Release`:
   - Role com a roda (vertical) e com Shift+roda (horizontal); se tiver touchpad, deslize com dois dedos nos dois sentidos.
   - Arraste o polegar da barra horizontal (embaixo) e da vertical (direita) rápido: o conteúdo deve deslizar atrás, sem pular, e os dois painéis devem andar juntos.
   - Duplo clique numa linha "··· N linhas iguais ···": expande/recolhe nos dois lados. Botões "Recolher/Expandir tudo".
   - Role até uma linha com fundo amarelo e palavra destacada longe da esquerda e confira se as palavras destacadas dos dois painéis ficam alinhadas.
   - "Tema claro/escuro".
3. `dotnet run --project src/Menage.Pocs/Pocs.Janela`: arraste para as bordas da tela (encaixa), Win+setas, duplo clique na barra (maximiza), redimensione pelo canto superior direito. Se estiver no Windows 11, passe o mouse no botão de maximizar (o menu de encaixe **não** deve aparecer; é a lacuna conhecida).
