using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using Menage.Pocs.Comum;

namespace Menage.Pocs.Diff;

public partial class MainWindow : Window
{
    private CoresDiff _cores = CoresDiff.Claro();
    private bool _escuro;
    private RecolhimentoIguais _recolhimento = null!;

    public ModeloDiff Modelo { get; private set; } = null!;
    public ControladorRolagem Rolagem { get; }
    public RecolhimentoIguais Recolhimento => _recolhimento;
    public TextEditor[] Editores => [Esquerdo, Direito];
    public Dictionary<string, double> Tempos { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        Rolagem = new ControladorRolagem(Esquerdo, Direito, BarraV, BarraH);
        foreach (var ed in Editores)
        {
            ed.Options.EnableHyperlinks = false;
            ed.Options.EnableEmailHyperlinks = false;
            ed.Options.EnableTextDragDrop = false;
            ed.MouseDoubleClick += AoDuploClique;
        }
        Loaded += (_, _) => Rolagem.LigarTouchpad(this);
        AplicarCores();
    }

    public void Carregar(string antigo, string novo)
    {
        var sw = Stopwatch.StartNew();
        Modelo = new ConstrutorDiff().Construir(File.ReadAllText(antigo), File.ReadAllText(novo));
        Tempos["diffPlexMs"] = sw.ElapsedMilliseconds;
        sw.Restart();
        var maior = Math.Max(Modelo.Esquerda.Max(l => l.Texto.Length), Modelo.Direita.Max(l => l.Texto.Length));
        _larguraChar = LarguraCaractere();
        _janela.ColunasTotais = maior + 2;
        // '\n' final: toda linha (inclusive a última) tem delimitador, onde o EqualizadorLargura põe o espaçador.
        Esquerdo.Text = string.Join('\n', Modelo.Esquerda.Select(l => l.Texto)) + "\n";
        Direito.Text = string.Join('\n', Modelo.Direita.Select(l => l.Texto)) + "\n";
        InstalarRealce();
        _recolhimento = new RecolhimentoIguais(Esquerdo, Direito);
        _recolhimento.Criar(Modelo);
        Tempos["carregarEditoresMs"] = sw.ElapsedMilliseconds;
        Rolagem.DefinirLarguraExtent(_janela.ColunasTotais * _larguraChar);
        Resumo.Text = $"{Modelo.Esquerda.Count} linhas alinhadas, {_recolhimento.Trechos} trechos iguais recolhidos, linha mais longa {maior} caracteres";
    }

    private readonly JanelaColunas _janela = new();
    private double _larguraChar;

    /// <summary>Refaz a janela de colunas formatadas nas linhas longas quando a rolagem chega perto da borda dela.</summary>
    public void AjustarJanela(double x)
    {
        var vp = Math.Max(40, (int)(Math.Max(Esquerdo.ActualWidth, 400) / _larguraChar));
        var ini = (int)(x / _larguraChar);
        var seguroEsq = _janela.Inicio == 0 || ini - _janela.Inicio >= vp;
        var seguroDir = _janela.Fim > 0 && _janela.Fim - (ini + vp) >= vp;
        if (seguroEsq && seguroDir) return;
        _janela.Inicio = Math.Max(0, ini - 2 * vp);
        _janela.Fim = ini + 3 * vp;
        foreach (var ed in Editores) ed.TextArea.TextView.Redraw();
    }

    private void InstalarRealce()
    {
        Rolagem.AntesDeAplicar = AjustarJanela;
        foreach (var ed in Editores)
        {
            ed.TextArea.TextView.ElementGenerators.Add(new FatiaLinhasLongas(_janela, _larguraChar));
            ed.TextArea.TextView.ElementGenerators.Insert(0, new EqualizadorLargura(_janela, _larguraChar));
        }
        AjustarJanela(0);
        var (esq, dir) = (Esquerdo.TextArea.TextView, Direito.TextArea.TextView);
        esq.BackgroundRenderers.Add(new FundoLinhaDiff(Modelo.Esquerda, () => _cores));
        dir.BackgroundRenderers.Add(new FundoLinhaDiff(Modelo.Direita, () => _cores));
        esq.LineTransformers.Add(new PalavrasDiff(Modelo.Esquerda, () => _cores, false));
        dir.LineTransformers.Add(new PalavrasDiff(Modelo.Direita, () => _cores, true));
    }

    private double LarguraCaractere()
    {
        var ft = new FormattedText("0", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Esquerdo.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), Esquerdo.FontSize, Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return ft.WidthIncludingTrailingWhitespace;
    }

    public void AplicarTema(bool escuro)
    {
        _escuro = escuro;
        _cores = escuro ? CoresDiff.Escuro() : CoresDiff.Claro();
        var fundo = escuro ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Colors.White;
        var texto = escuro ? Color.FromRgb(0xD8, 0xD8, 0xD8) : Color.FromRgb(0x20, 0x20, 0x20);
        Raiz.Background = new SolidColorBrush(fundo);
        foreach (var ed in Editores)
        {
            ed.Background = new SolidColorBrush(fundo);
            ed.Foreground = new SolidColorBrush(texto);
            ed.TextArea.TextView.Redraw();
        }
        Resumo.Foreground = Rodape.Foreground = new SolidColorBrush(texto);
    }

    private void AplicarCores() => AplicarTema(false);
    private void AoTema(object s, RoutedEventArgs e) => AplicarTema(!_escuro);
    private void AoRecolher(object s, RoutedEventArgs e) => _recolhimento.DefinirTodos(true);
    private void AoExpandir(object s, RoutedEventArgs e) => _recolhimento.DefinirTodos(false);

    private void AoDuploClique(object s, MouseButtonEventArgs e)
    {
        var ed = (TextEditor)s;
        var pos = ed.GetPositionFromPoint(e.GetPosition(ed));
        if (pos is { } p) _recolhimento.Alternar(p.Line);
    }
}
