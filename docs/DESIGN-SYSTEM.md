# Design system do Menage

Base: **WPF UI 4.3.0** + **XamlFlair 1.2.13**. Só esta pasta cria estilos globais: `src/Menage.App/Tema/` (tokens, temas) e `src/Menage.App/Controles/` (componentes). Tela nova usa o que está aqui; se faltar algo, acrescente aqui, não na tela.

Ver tudo junto: `dotnet run --project src/Menage.App -- --catalogo` (tela para clicar). Regerar capturas: `dotnet run --project src/Menage.App -- --galeria <pasta>`, que grava também `verificacoes.txt`. Os dois modos usam `%TEMP%\menage-galeria`, então não mexem no tema salvo do usuário. Capturas versionadas em `docs/galeria/design-system/`.

## Tokens

| Tipo | Como usar | Valores |
|---|---|---|
| Cor (pincel) | `{DynamicResource Tema.<Papel>}` | Um por `PapelDeCor`: `Fundo`, `Painel`, `PainelClaro`, `Lateral`, `Borda`, `Texto`, `TextoFraco`, `TextoForte`, `Destaque`, `DestaqueHover`, `Verde`, `Amarelo`, `Laranja`, `Vermelho`, `Azul`, `GradeHover`, `GradeSel`, `GrupoWeb/Fw/App/Tc`, `Console`, `ConsoleTexto`... |
| Cor (valor) | `{DynamicResource Tema.<Papel>.Cor}` | A mesma cor como `Color` |
| Texto sobre cor | `Tema.TextoSobreCor` | Branco. Única cor fora da paleta |
| Espaço | `Espaco.P/M/G/GG` | 4, 8, 16, 24 |
| Raio | `Raio.Controle`, `Raio.Pilula`, `Raio.Etiqueta`, `Raio.Chip` | 8, 8, 6, 14 |
| Fonte | `Fonte.Legenda/Corpo/Subtitulo/Titulo`, `Fonte.Mono` | 12, 14, 16, 20 |
| Texto pronto | `Texto.Titulo`, `Texto.Subtitulo`, `Texto.Corpo`, `Texto.Legenda` | Estilos de `TextBlock` |
| Movimento | `{x:Static t:Animacao.Rapida/Media/Lenta}`, `Animacao.Suave`, `Animacao.Entrada` | 100, 220, 320 ms; ease-in-out, ease-out |
| Entrada (XamlFlair) | `Entra.Item`, `Entra.Fade`; `Animacao.CascataMs` | 220 ms; 35 ms entre itens |

Regras: cor sempre por `DynamicResource` (`StaticResource` não acompanha a troca de tema); nunca cor fixa em tela. Animar só `RenderTransform` e opacidade, por tempo. Dicionário novo que use token com `StaticResource` precisa mesclar `Tema/Tokens.xaml` (sem isso o token chega vazio e a tela quebra ao abrir).

## Temas

- `IServicoDeTema`: `Trocar(id)`, `AlternarPar()` (Ctrl+T), `Atual`. A escolha vai para `tema.json` por `Armazenamento<T>`.
- 12 temas em `CatalogoDeTemas`. Toda paleta passa por `DerivacaoDePaleta.Finalizar`: Saída preta, cores sem repetição, sem branco puro no claro. Os testes conferem isso em cada tema.
- Quem desenha com cor própria escuta `TemaMudou` pelo `IMessenger`.
- **Temas privados:** `%LOCALAPPDATA%\Menage\temas-privados.json`, lido na abertura. Tema salvo que sumiu do arquivo cai no Escuro.

```json
{ "versao": 1, "dados": { "temas": [
  { "id": "x-escuro", "nome": "X escuro", "par": "x-claro", "escuro": true,
    "matiz": "#101820", "destaque": "#3574F0", "destaqueHover": "#4B86F7", "azul": "#6EA8FE" } ] } }
```

## Componentes

| Componente | Uso |
|---|---|
| Pílula | `<c:Pilula Texto="Push" Icone="ArrowUp24" Tom="Azul"/>`. Ação principal: `Tom="Destaque" Preenchida="True"`. Com menu: `Menu="{...}"` (ganha seta) |
| Barra de pílulas | `<c:BarraDePilulas>` com pílulas dentro. Sozinha faz cápsula → só ícone (texto vira dica) → linhas, dividindo a sobra entre as pílulas e deslizando ao mudar de modo |
| Chip | `<c:Chip Texto="Alterados" IsChecked="True"/>`; `Removivel="True"` + evento `Removido` |
| Ficha | `<c:Ficha Texto="Mudança 3 de 10" Tom="Verde"/>`: contador ou estado |
| Etiqueta | `<c:Etiqueta Texto="WEB" Tom="GrupoWeb"/>`: leve, para células de grade |
| Selo | `<c:Selo Tipo="Erro"/>`: i, !, × ou ? |
| Cartão | `<Border Style="{StaticResource Cartao.Padrao}">` ou o `ui:Card` |
| Grade | `<c:GradePadrao TituloVazio="..." DescricaoVazia="...">` com `Columns`: virtualizada, com hover, seleção e estado vazio |
| Estado vazio | `<c:EstadoVazio Titulo="..." Descricao="..." Icone="..."/>` |
| Lista | `Style="{StaticResource Lista.Virtualizada}"` |
| Lista em cascata | `<xfc:AnimatedListBox Style="{StaticResource Lista.Virtualizada}" xf:Animations.Items="{xf:Animate BasedOn={StaticResource Entra.Item}}" xf:Animations.InterElementDelay="{x:Static t:Animacao.CascataMs}"/>` |
| Busca | `<c:CampoDeBusca PlaceholderText="Buscar projeto"/>` |
| Saída | `<TextBox Style="{StaticResource Saida.Area}"/>`: preta em qualquer tema |
| Menu com ícones | `FabricaDeComandos.Menu(catalogo.Disponiveis(perfil))`; pílula: `FabricaDeComandos.Pilula(comando)` |
| Barra lateral | `AnimadorBarraLateral` (técnica medida em `docs/PROVAS-DE-CONCEITO.md`) |

`Tom`: `Destaque`, `Neutro`, `Azul`, `Verde`, `Amarelo`, `Laranja`, `Vermelho`, `GrupoWeb`, `GrupoFw`, `GrupoApp`, `GrupoTc`. Cor livre: propriedade `Cor`.

Prontos do WPF UI, sem estilo nosso: `ui:Card`, `ui:InfoBar`, `ui:ToggleSwitch`, `ui:NumberBox`, `ui:Button`, `ComboBox`.

## Janelas e diálogos

```xml
<c:JanelaBase x:Class="..." xmlns:c="clr-namespace:Menage.App.Controles" Title="Minha janela"> ... </c:JanelaBase>
```
```csharp
public partial class MinhaJanela(ContextoDeJanela contexto) : JanelaBase(contexto) { ... }
// registro: services.AddJanela<MinhaJanela>("modulo.minha-janela");
```

- A janela já traz barra de título, entrada animada, Ctrl+K, Ctrl+T, dissolução da troca de tema e `Avisar(titulo, mensagem)` (Snackbar).
- Esc fecha, nesta ordem: paleta, diálogo, a própria janela (`FechaComEsc`, ligado sozinho em `AbrirDialogoAsync`).
- `IServicoDeJanelas`: abre pelo id, uma janela por id. Parâmetro: `IJanelaComParametro`. Resultado de diálogo: `IJanelaComResultado`.
- `IServicoDeDialogos`: mensagens na janela ativa, de qualquer thread. Botão padrão com foco; Esc cancela.

## Paleta Ctrl+K

Lê `ICatalogoDeComandos.Todos`; comando novo entra sozinho ao registrar o módulo. Busca sem acento e por vários termos (texto e grupo). Comando fora do perfil, ou com `PodeExecutar() == false`, aparece apagado com o motivo e no fim da lista. Ícone do comando: nome do `SymbolRegular` sem o tamanho (`"ArrowSync"`).

## Testes

`tests/Menage.App.Tests` (net8.0-windows): paletas, troca e gravação do tema, cálculo da barra, busca e ViewModel da paleta. O restante do comportamento na tela é conferido pelo `--galeria`.

## Transições (janelas, painéis e telas)
Serviço único `ServicoDeTransicao` (`src/Menage.App/Animacoes`) + algoritmo puro `CalculoDeTransicao` (Core, testado).
- Só `Opacity` e `RenderTransform`, por tempo, com `BitmapCache` durante a animação (removido ao fim). Durações cheias em `CalculoDeTransicao` (entrada de página 180, saída 120, entrada de janela 200, saída 140 ms).
- Uma transição que chega no meio de outra parte do estado visível e dura só a fração que falta (mínimo 60 ms): sem pulo.
- Nível de movimento: sem animações no Windows (`SystemParameters.ClientAreaAnimation`) = instantâneo; `ServicoDeTransicao.ReduzirMovimento` = só opacidade.
- Janelas: `JanelaBase` anima entrada (0.96→1 + fade) e saída (fecha ao terminar); `PodeFechar()` veta antes da saída. Painéis (paleta Ctrl+K) usam o mesmo animador.
- Telas: `<a:HospedeDeTransicao Indice="{Binding Posicao}" Destino="{Binding Tela}"/>` (ligar `Indice` antes de `Destino`); a direção vem da ordem dos índices.
- `JanelaConflito` ainda é `FluentWindow` puro e não participa.
