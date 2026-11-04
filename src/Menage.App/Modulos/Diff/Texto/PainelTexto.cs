using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using Menage.Core.Diff;

namespace Menage.App.Modulos.Diff.Texto;

/// <summary>Um editor preparado para o diff: margem própria, linhas longas, realces, dicas e pulso. Não rola sozinho: quem manda é o <see cref="Rolagem.ControladorRolagem"/>.</summary>
public sealed class PainelTexto
{
    public const int LimiteLinhaLonga = 1200;
    const int LarguraTabulacao = 4;

    readonly CoresDiff _cores;
    readonly MargemNumeros _margem;
    readonly FundoLinhas _fundo;
    readonly EtiquetasDeLinha _etiquetas;
    readonly bool _editavel;
    readonly ToolTip _dica = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };
    readonly SolidColorBrush _pulso = new() { Opacity = 0 };
    string? _notaDaDica;

    public PainelTexto(TextEditor editor, CoresDiff cores, bool editavel = false)
    {
        (Editor, _cores, _editavel) = (editor, cores, editavel);
        Linhas = new() { NumerarPelaPosicao = editavel };
        Janela = new();
        _margem = new MargemNumeros(Linhas, cores);
        _fundo = new FundoLinhas(Linhas, cores) { PincelRealce = _pulso };
        _etiquetas = new EtiquetasDeLinha(Linhas, cores);
        Preparar();
        Instalar();
        Colorir();
    }

    /// <summary>Clique numa linha de trecho recolhido.</summary>
    public event Action<LinhaVista>? DobraClicada;

    public TextEditor Editor { get; }

    public TextView Vista => Editor.TextArea.TextView;

    public LinhasDoPainel Linhas { get; }

    public JanelaColunas Janela { get; }

    public IReadOnlyList<string> Fonte { get; set; } = [];

    public double AlturaLinha => Vista.DefaultLineHeight;

    public bool Editavel => _editavel;

    /// <summary>Coluna mais larga de um conjunto de linhas, com as tabulações expandidas.</summary>
    public static int Colunas(IEnumerable<LinhaVista> linhas) => linhas.Select(l => Colunas(l.Texto)).DefaultIfEmpty(0).Max();

    public static int Colunas(string linha)
    {
        var tabs = 0;
        foreach (var c in linha)
        {
            if (c == '\t') tabs++;
        }
        return linha.Length + (tabs * (LarguraTabulacao - 1));
    }

    /// <summary>Troca o texto. O '\n' final dá delimitador à última linha, onde o espaçador se apoia; o mapa de rolagem não a conta.</summary>
    public void Mostrar(IReadOnlyList<LinhaVista> linhas, int colunas)
    {
        Linhas.Itens = linhas;
        DefinirColunas(colunas);
        Editor.Document.Text = string.Concat(linhas.Select(l => l.Texto + "\n"));
        Editor.Document.UndoStack.ClearAll();
        _margem.Configurar(Editor.FontFamily, Editor.FontSize, linhas.Select(l => l.Numero).DefaultIfEmpty(0).Max());
    }

    /// <summary>Só troca a decoração (o texto já está no documento).</summary>
    public void Decorar(IReadOnlyList<LinhaVista> linhas)
    {
        Linhas.Itens = linhas;
        Vista.Redraw(DispatcherPriority.Render);
    }

    public void DefinirColunas(int colunas)
    {
        Janela.LarguraCaractere = Vista.WideSpaceWidth;
        Janela.ColunasTotais = colunas + 2;
    }

    public void ConfigurarMargem(int maiorNumero) => _margem.Configurar(Editor.FontFamily, Editor.FontSize, maiorNumero);

    /// <summary>Refaz só as linhas longas que estão na tela, e só quando a rolagem chega perto da borda da janela de colunas.</summary>
    public void AjustarJanela(double x)
    {
        var largura = Janela.LarguraCaractere;
        var tela = Math.Max(40, (int)(Math.Max(Vista.ActualWidth, 400) / largura));
        var inicio = (int)(x / largura);
        var seguroEsquerda = Janela.Inicio == 0 || inicio - Janela.Inicio >= tela;
        var seguroDireita = Janela.Fim > 0 && Janela.Fim - (inicio + tela) >= tela;
        if (seguroEsquerda && seguroDireita) return;
        Janela.Inicio = Math.Max(0, inicio - (2 * tela));
        Janela.Fim = inicio + (3 * tela);
        if (!Vista.VisualLinesValid) return;
        foreach (var linha in Vista.VisualLines)
        {
            if (linha.FirstDocumentLine.Length > LimiteLinhaLonga) Vista.Redraw(linha, DispatcherPriority.Render);
        }
    }

    /// <summary>Realça linhas [inicio, fim) (base 1) com um pulso curto que termina numa marca fraca.</summary>
    public void Realcar(int inicio, int fim)
    {
        _fundo.Realce = (inicio, fim);
        _pulso.BeginAnimation(Brush.OpacityProperty, null);
        _pulso.Color = _cores.Destaque;
        var animacao = new DoubleAnimationUsingKeyFrames();
        animacao.KeyFrames.Add(new EasingDoubleKeyFrame(0.45, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
        animacao.KeyFrames.Add(new EasingDoubleKeyFrame(0.18, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), new CubicEase { EasingMode = EasingMode.EaseInOut }));
        _pulso.BeginAnimation(Brush.OpacityProperty, animacao);
        Vista.InvalidateLayer(KnownLayer.Background, DispatcherPriority.Render);
    }

    public void LimparRealce()
    {
        _fundo.Realce = null;
        Vista.InvalidateLayer(KnownLayer.Background, DispatcherPriority.Render);
    }

    /// <summary>Linha do documento (base 1) sob um ponto da vista.</summary>
    public int LinhaEm(Point ponto) => (int)((ponto.Y + Vista.VerticalOffset) / AlturaLinha) + 1;

    void Preparar()
    {
        var opcoes = Editor.Options;
        opcoes.EnableHyperlinks = false;
        opcoes.EnableEmailHyperlinks = false;
        opcoes.EnableTextDragDrop = false;
        opcoes.AllowScrollBelowDocument = false;
        opcoes.CutCopyWholeLine = false;
        opcoes.IndentationSize = LarguraTabulacao;
        // Display: largura de caractere e altura de linha inteiras; o deslocamento dos painéis não treme entre pixels.
        TextOptions.SetTextFormattingMode(Editor, TextFormattingMode.Display);
        Editor.FontFamily = new FontFamily("Consolas");
        Editor.FontSize = 13;
        Editor.WordWrap = false;
        Editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        Editor.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        Editor.IsReadOnly = !_editavel;
        Editor.TextArea.SelectionBorder = null;
        Editor.TextArea.SelectionCornerRadius = 0;
        Editor.TextArea.SelectionForeground = null;
        if (_editavel) return;
        Editor.Document.UndoStack.SizeLimit = 0;
        Editor.TextArea.Caret.CaretBrush = Brushes.Transparent;
    }

    void Instalar()
    {
        Editor.TextArea.LeftMargins.Insert(0, _margem);
        Vista.ElementGenerators.Add(new FatiaLinhasLongas(Janela, LimiteLinhaLonga));
        Vista.ElementGenerators.Insert(0, new EspacadorLargura(Janela));
        Vista.BackgroundRenderers.Add(_fundo);
        Vista.BackgroundRenderers.Add(_etiquetas);
        Vista.LineTransformers.Add(new PalavrasAlteradas(Linhas, _cores, ladoNovo: !_editavel));
        Editor.TextArea.PreviewMouseLeftButtonDown += AoApertarMouse;
        Vista.QueryCursor += AoPedirCursor;
        Vista.MouseMove += (_, e) => MostrarDica(_etiquetas.NotaEm(e.GetPosition(Vista)));
        Vista.MouseLeave += (_, _) => MostrarDica(null);
        _margem.MouseMove += (_, e) => MostrarDica(Linhas.Em(LinhaEm(e.GetPosition(Vista)))?.Nota);
        _margem.MouseLeave += (_, _) => MostrarDica(null);
        CommandManager.AddPreviewExecutedHandler(Editor.TextArea, AoExecutarComando);
        _cores.Mudou += Colorir;
    }

    public void Colorir()
    {
        Editor.Background = _cores.Fundo;
        Editor.Foreground = _cores.Texto;
        Editor.TextArea.SelectionBrush = _cores.Selecao;
        if (_editavel) Editor.TextArea.Caret.CaretBrush = _cores.Texto;
        _etiquetas.Limpar();
        _margem.InvalidateVisual();
        Vista.Redraw();
    }

    void AoApertarMouse(object sender, MouseButtonEventArgs e)
    {
        var ponto = e.GetPosition(Vista);
        if (ponto.X < 0 || Linhas.Em(LinhaEm(ponto)) is not { Tipo: TipoLinha.Dobra } dobra) return;
        e.Handled = true;
        DobraClicada?.Invoke(dobra);
    }

    void AoPedirCursor(object sender, QueryCursorEventArgs e)
    {
        var ponto = e.GetPosition(Vista);
        if (ponto.X < 0 || Linhas.Em(LinhaEm(ponto)) is not { Tipo: TipoLinha.Dobra }) return;
        e.Cursor = Cursors.Hand;
        e.Handled = true;
    }

    void MostrarDica(string? nota)
    {
        if (nota == _notaDaDica) return;
        _notaDaDica = nota;
        _dica.IsOpen = false;
        if (nota is null) return;
        _dica.Content = new TextBlock
        {
            Inlines = { new System.Windows.Documents.Bold(new System.Windows.Documents.Run(NotasDiff.Titulo(nota))), new System.Windows.Documents.LineBreak(), new System.Windows.Documents.Run(NotasDiff.Explicacao(nota)) },
            MaxWidth = 520,
            TextWrapping = TextWrapping.Wrap,
        };
        _dica.PlacementTarget = Vista;
        _dica.IsOpen = true;
    }

    /// <summary>Copiar um trecho recolhido entrega as linhas reais, não o aviso "⋯ N linhas iguais".</summary>
    void AoExecutarComando(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Copy || Fonte.Count == 0 || Editor.TextArea.Selection.IsEmpty) return;
        var selecao = Editor.TextArea.Selection;
        var primeira = selecao.StartPosition.Line;
        var trechos = selecao.GetText().Split('\n');
        var trocou = false;
        for (var i = 0; i < trechos.Length; i++)
        {
            if (Linhas.Em(primeira + i) is not { Tipo: TipoLinha.Dobra } dobra) continue;
            trechos[i] = string.Join('\n', Fonte.Skip(dobra.PrimeiraEscondida).Take(dobra.Escondidas));
            trocou = true;
        }
        if (!trocou) return;
        Clipboard.SetText(string.Join('\n', trechos));
        e.Handled = true;
    }
}
