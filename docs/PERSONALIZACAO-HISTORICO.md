# Personalização e Histórico de releases

Branch `feature/personalizacao-historico` (cortada da `main` v1.0.0). Não depende de `feature/operacoes-lote`: o Histórico só lê os logs do Gerador de Beta (já na `main`) e as tags pelo `IGit`.

## Personalização

| Peça | Onde | Papel |
|---|---|---|
| `PreferenciasDeAparencia` | `Core/Personalizacao` | `aparencia.json` por `Armazenamento<T>`: modo (Sistema/Claro/Escuro), cor de destaque, densidade, tamanho do texto, barra A/B, reduzir movimento, atalhos trocados |
| `MedidasDeAparencia`, `Gesto`, `MapaDeAtalhos` | `Core/Personalizacao` | Regras puras e testadas: escala, cor `#RRGGBB`, lado do par claro/escuro, atalho canônico, conflito |
| `ServicoDeAparencia` | `App/Modulos/Personalizacao` | Aplica ao vivo: tema (via `IServicoDeTema`), destaque (`AjusteDeDestaque`), medidas (recursos), `ServicoDeTransicao.ReduzirMovimento`; escuta o Windows no modo Sistema |
| `ServicoDeAtalhos` | idem | Lê o padrão de cada comando do catálogo + as trocas; `JanelaBase.OnKeyDown` executa o comando dono do gesto |
| `JanelaDePersonalizacao` | idem | Duas abas (Aparência, Atalhos); cada opção vale na hora, sem botão Salvar |

Decisões:
- **Modo** nulo (arquivo novo) mantém o lado do tema de `tema.json`: quem já usava o Claro continua no Claro. Ctrl+T ou "Tema: ..." na paleta atualiza o modo para o lado pedido.
- **Cor de destaque** vale para qualquer tema (refaz só destaque, hover, seleção da lateral/grade); "Padrão do tema" volta à cor do tema.
- **Tamanho do texto = zoom do conteúdo das janelas** (`Interface.Escala`, `LayoutTransform` no conteúdo da `JanelaBase`; 90/100/115%). Escolhido porque os controles do WPF UI trazem tamanho próprio e não acompanhavam os tokens `Fonte.*`. Barra de título, avisos e paleta Ctrl+K não escalam.
- **Densidade** compacta: `Altura.Controle` 32→26, `Altura.Linha` 32→26 (grades), `Margem.Cartao` 14→8 (tokens agora por `DynamicResource`).
- **Barra lateral "modelo B"**: o repositório não define o que é; implementei como **painel contínuo** (sem moldura nem fundo de cartão, divisória fina entre blocos), por `DataTrigger` em `BarraLateralViewModel.ModeloB`. **Confirmar com o usuário se o "B" que ele tem em mente é esse.**
- **Atalhos**: os fixos (Ctrl+K, Ctrl+T, Ctrl+B, Ctrl+F, F5, Ctrl+A) saíram do código das janelas e passaram a vir do catálogo (`paleta.abrir` é comando novo). Ctrl+, abre a Personalização. Letra/número sozinhos são recusados; gesto em uso é recusado com o nome do comando dono.
- **Reduzir movimento**: liga `ServicoDeTransicao.ReduzirMovimento` (só opacidade). A barra lateral e os `Esmaece` ainda usam as animações próprias; não foram ligados a esta opção.

Comandos novos na paleta: Personalização, Tema (sistema/claro/escuro), Alternar reduzir movimento, Abrir a paleta.

## Histórico de releases (`mais.historico`, menu Mais)

`Core/LinhaDoTempo` (a pasta não se chama `Releases` porque o `.gitignore` ignora `Releases/`): `LeitorDeBetas` lê `%LOCALAPPDATA%\Menage\gerador-beta\logs\<data>_<modo>\NN-projeto.log` (cabeçalho Pasta/Branch, rodapé `RESULTADO:`; `-surefire.log` substitui o primeiro), `LeitorDeTags` lê as tags dos projetos marcados (mesma tag na mesma hora em vários projetos = um evento), `FiltroDeReleases` filtra por ambiente, texto e tipo. Tela: `App/Modulos/HistoricoReleases`.

Diferenças em relação ao Git Manager antigo: o antigo gravava `*.job.json` com o **commit** de cada projeto; o Gerador do Menage não grava commit, então a linha mostra branch e resultado, sem hash (as tags mostram o hash). Ambiente = pasta base cadastrada no Gerador que contém o projeto; sem base, a pasta acima do projeto (ou acima de `web/framework/app/pda`). Substitui o "Em breve" que `ComandosDaJanelaModulo` registrava para `mais.historico`.

## Testar

```bash
dotnet build src/Menage.App -warnaserror
dotnet test tests/Menage.Tests --filter "FullyQualifiedName~PersonalizacaoHistorico"
dotnet test tests/Menage.App.Tests --filter "FullyQualifiedName~ServicoDeAparencia"
dotnet run --project src/Menage.App -- --galeria-personalizacao docs/galeria/personalizacao-historico
```

A galeria usa `%TEMP%\menage-galeria-personalizacao`, dados de exemplo e logs de exemplo; não toca `%LOCALAPPDATA%\Menage`, `C:\wmw\dev` nem repositórios. Não usa mouse nem teclado reais.

## Roteiro de teste manual

1. Abra o app, **Configurações > Personalização** (ou Ctrl+,).
2. **Tema**: Escuro → Claro → Sistema; com Sistema, mude o tema do Windows e veja o app seguir sem reiniciar. Ctrl+T alterna e o botão de rádio acompanha.
3. **Cor de destaque**: clique numa amostra, digite `#00A878` + Enter, digite `abc` (deve recusar), "Padrão do tema". Troque a família de cores: o destaque do usuário permanece.
4. **Densidade** e **Tamanho do texto**: troque e veja a janela principal atrás mudar na hora; abra outras janelas (Checkout, Gerador).
5. **Barra lateral**: Modelo B; Ctrl+B ainda recolhe/expande.
6. **Reduzir movimento**: abra e feche uma janela (só fade).
7. **Atalhos**: selecione "Alternar tema", clique no campo e digite Ctrl+Shift+T (vale na hora; Ctrl+T deixa de alternar). Tente Ctrl+K em outro comando (recusa citando o dono). "Sem atalho", "Padrão", "Restaurar todos". Feche e reabra o app: tudo permanece.
8. **Mais > Histórico de releases**: com o Gerador já usado, confira betas e tags; filtro por ambiente e por texto; Betas/Tags desligam cada tipo.

## Não verificado

Clicar de verdade, digitar atalhos reais e modo Sistema com troca real do Windows (só com detector falso nos testes e com o código de registro compilado); menus e paleta ainda mostram o atalho **padrão** do catálogo, não o trocado; Histórico com logs reais do Gerador (só logs de exemplo e testes); DPI diferente de 100%.
