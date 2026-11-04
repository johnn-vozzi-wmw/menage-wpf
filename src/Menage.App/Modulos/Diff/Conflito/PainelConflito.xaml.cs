using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using Menage.App.Modulos.Diff.Rolagem;
using Menage.App.Modulos.Diff.Texto;
using Menage.Core.Diff;

namespace Menage.App.Modulos.Diff.Conflito;

/// <summary>Em cima o que entra e o lado atual (arquivo inteiro, conflitos em âmbar), embaixo o resultado editável.</summary>
public partial class PainelConflito : UserControl
{
    readonly CoresDiff _cores;
    readonly DispatcherTimer _espera = new() { Interval = TimeSpan.FromMilliseconds(350) };
    readonly Dictionary<int, EscolhaConflito> _escolhidos = [];
    readonly PilulasDeLinha _pilulasEntrando;
    readonly PilulasDeLinha _pilulasAtual;
    readonly PilulasDeLinha _pilulasResultado;
    TextSegmentCollection<TrechoEscolhido> _trechos = [];
    ArquivoEmConflito? _arquivo;
    AnaliseConflito? _analise;
    IReadOnlyList<LinhaVista> _baseEntrando = [];
    IReadOnlyList<LinhaVista> _baseAtual = [];
    int _versao;
    int _foco = -1;

    public PainelConflito()
    {
        _cores = new(this);
        InitializeComponent();
        Entrando = new PainelTexto(EditorEntrando, _cores);
        Atual = new PainelTexto(EditorAtual, _cores);
        Resultado = new PainelTexto(EditorResultado, _cores, editavel: true);
        Rolagem = new ControladorRolagem([Entrando, Atual, Resultado], [BarraVEntrando, BarraVAtual, BarraVResultado], [BarraHEntrando, BarraHAtual, BarraHResultado]);
        Rolagem.Moveu += AoMover;
        Rolagem.LigarRoda(ReguaArquivo);
        ReguaArquivo.Rolou += AoRegua;
        var estilo = (Style)FindResource("PilulaLinha");
        _pilulasEntrando = new PilulasDeLinha(Entrando.Vista, estilo);
        _pilulasAtual = new PilulasDeLinha(Atual.Vista, estilo);
        _pilulasResultado = new PilulasDeLinha(Resultado.Vista, estilo);
        _espera.Tick += async (_, _) => await RecalcularAsync();
        Resultado.Editor.Document.TextChanged += (_, _) => AoEditar();
        Resultado.Editor.Document.UndoStack.PropertyChanged += (_, _) => AtualizarDesfazer();
        LigarMouse(Entrando);
        LigarMouse(Atual);
        LigarMouse(Resultado);
    }

    public PainelTexto Entrando { get; }

    public PainelTexto Atual { get; }

    public PainelTexto Resultado { get; }

    public ControladorRolagem Rolagem { get; }

    public ArquivoEmConflito? Arquivo => _arquivo;

    public int Pendentes => _analise?.Pendentes.Count ?? 0;

    public string TextoDoEstado => TextoEstado.Text;

    /// <summary>Há algo a perder se fechar agora.</summary>
    public bool TemAlteracao => _arquivo is not null && Resultado.Editor.Document.UndoStack.CanUndo;

    public IReadOnlyList<string> LinhasDoResultado()
    {
        var doc = Resultado.Editor.Document;
        return doc.Lines.Select(l => doc.GetText(l)).ToList();
    }

    public async Task MostrarAsync(ArquivoEmConflito arquivo)
    {
        _arquivo = arquivo;
        _foco = -1;
        _escolhidos.Clear();
        Rotular(arquivo.Lados);
        var doc = Resultado.Editor.Document;
        _trechos = new TextSegmentCollection<TrechoEscolhido>(doc);
        _baseEntrando = Linhas(arquivo.Entrando, arquivo.Origem, TipoLinha.AlteradaEntrando);
        _baseAtual = Linhas(arquivo.Atual, arquivo.Origem, TipoLinha.AlteradaAtual);
        var colunas = new[] { arquivo.Atual, arquivo.Entrando, arquivo.Juncao.Resultado }.SelectMany(l => l).Concat(arquivo.Juncao.Conflitos.SelectMany(c => c.Atual.Concat(c.Entrando)))
            .Select(PainelTexto.Colunas).DefaultIfEmpty(0).Max();
        Entrando.Mostrar(_baseEntrando, colunas);
        Atual.Mostrar(_baseAtual, colunas);
        Resultado.DefinirColunas(colunas);
        doc.Text = string.Join('\n', arquivo.Juncao.Resultado);
        doc.UndoStack.ClearAll();
        Resultado.ConfigurarMargem(arquivo.Juncao.Resultado.Count + 100);
        _espera.Stop();
        await RecalcularAsync(posicaoZero: true);
        Ir(1);
    }

    /// <summary>Alt+1/2/3 e F7; a janela repassa as teclas.</summary>
    public bool Tecla(KeyEventArgs e)
    {
        var tecla = e.Key == Key.System ? e.SystemKey : e.Key;
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        if (alt && tecla == Key.D1) UsarNoAtual(EscolhaConflito.Entrando);
        else if (alt && tecla == Key.D2) UsarNoAtual(EscolhaConflito.Atual);
        else if (alt && tecla == Key.D3) UsarNoAtual(EscolhaConflito.AtualDepoisEntrando);
        else if (tecla == Key.F7) Ir(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
        else return false;
        return true;
    }

    /// <summary>Anda pelos conflitos pendentes; os três painéis acompanham pela rolagem.</summary>
    public void Ir(int passo)
    {
        if (_analise is not { Pendentes.Count: > 0 } analise || Rolagem.Mapa is not { } mapa) return;
        var h = Resultado.AlturaLinha;
        var linhas = analise.Pendentes.Select(p => p.LinhaResultado).ToList();
        var atual = analise.Pendentes.ToList().FindIndex(p => p.Numero == _foco);
        var i = Navegacao.Proxima(linhas, atual, (int)(Rolagem.Y(AnaliseConflito.PainelResultado) / h), (int)(Rolagem.Viewport / h), passo);
        var p = analise.Pendentes[i];
        _foco = p.Numero;
        Rolagem.IrPara(0, mapa.Unificado(AnaliseConflito.PainelResultado, p.LinhaResultado * h) - (Rolagem.Viewport / 3));
        Resultado.Editor.TextArea.Caret.Line = p.LinhaResultado + 1;
        Realcar(p);
        AtualizarEstado();
    }

    static List<LinhaVista> Linhas(IReadOnlyList<string> lado, IReadOnlyList<string> origem, TipoLinha alterada)
    {
        var mudou = new HashSet<int>();
        foreach (var b in BlocosDiff.Entre(origem, lado))
        {
            for (var l = b.BIni; l < b.BIni + b.BLen; l++) mudou.Add(l);
        }
        return lado.Select((t, i) => new LinhaVista(t, i + 1, mudou.Contains(i) ? alterada : TipoLinha.Igual)).ToList();
    }

    void Rotular(LadosConflito lados)
    {
        (TituloEntrando.Text, DetalheEntrando.Text) = (lados.TituloEntrando, lados.DetalheEntrando);
        (TituloAtual.Text, DetalheAtual.Text) = (lados.TituloAtual, lados.DetalheAtual);
        BotaoEntrando.Content = lados.UsarEntrando;
        BotaoAtual.Content = lados.UsarAtual;
        BotaoEntrando.ToolTip = $"Fica a versão de: {lados.CurtoEntrando} (Alt+1)";
        BotaoAtual.ToolTip = $"Fica a versão de: {lados.CurtoAtual} (Alt+2)";
        BotaoDois.ToolTip = $"Mantém os dois: primeiro {lados.CurtoAtual}, depois {lados.CurtoEntrando} (Alt+3)";
        Atalhos.Text = $"Alt+1 {lados.CurtoEntrando}   •   Alt+2 {lados.CurtoAtual}   •   Alt+3 os dois   •   F7 próximo conflito   •   duplo clique no bloco de cima usa aquele lado   •   o resultado é editável";
    }

    void AoEditar()
    {
        _espera.Stop();
        _espera.Start();
        AtualizarDesfazer();
    }

    void AtualizarDesfazer()
    {
        var pilha = Resultado.Editor.Document.UndoStack;
        BotaoDesfazer.IsEnabled = pilha.CanUndo;
        BotaoRefazer.IsEnabled = pilha.CanRedo;
    }

    /// <summary>Compara o resultado com os lados fora da thread de UI; só a última versão pedida é aplicada.</summary>
    async Task RecalcularAsync(bool posicaoZero = false)
    {
        _espera.Stop();
        if (_arquivo is not { } arquivo) return;
        var versao = ++_versao;
        var linhas = LinhasDoResultado();
        var analise = await Task.Run(() => AnaliseConflito.Analisar(arquivo.Atual, arquivo.Entrando, linhas));
        if (versao != _versao) return;
        _analise = analise;
        Decorar(arquivo, analise, linhas);
        var h = Resultado.AlturaLinha;
        var yResultado = Resultado.Vista.VerticalOffset;
        var largura = Resultado.Janela.ColunasTotais * Resultado.Janela.LarguraCaractere;
        MapaRolagem Criar(double tela) => new(analise.Ancoras, [arquivo.Entrando.Count, arquivo.Atual.Count, linhas.Count], h, tela);
        Rolagem.DefinirMapa(Criar, largura, m => posicaoZero ? 0 : m.UDeY(AnaliseConflito.PainelResultado, yResultado), Rolagem.X);
        ReguaArquivo.Configurar(_cores, Marcas(analise, Rolagem.Mapa!, h));
        if (analise.Pendentes.FirstOrDefault(p => p.Numero == _foco) is { Numero: > 0 } foco) Realcar(foco, pulsar: false);
        else _foco = -1;
        AtualizarEstado();
        AoMover();
    }

    void Decorar(ArquivoEmConflito arquivo, AnaliseConflito analise, IReadOnlyList<string> linhas)
    {
        var (entrando, atual) = (_baseEntrando.ToArray(), _baseAtual.ToArray());
        foreach (var p in analise.Pendentes)
        {
            Pintar(entrando, p.EntrandoIni, p.EntrandoLen);
            Pintar(atual, p.AtualIni, p.AtualLen);
        }
        var resultado = linhas.Select((t, i) => new LinhaVista(t, i + 1, TipoLinha.Igual)).ToArray();
        foreach (var l in analise.LinhasQueVieram) resultado[l] = resultado[l] with { Tipo = TipoLinha.Veio };
        foreach (var p in analise.Pendentes) resultado[p.LinhaResultado] = resultado[p.LinhaResultado] with { Tipo = TipoLinha.Conflito };
        DefinirPilulas(arquivo.Lados, analise);
        Entrando.Decorar(entrando);
        Atual.Decorar(atual);
        Resultado.Decorar(resultado);
    }

    static void Pintar(LinhaVista[] linhas, int inicio, int tamanho)
    {
        if (inicio < 0) return;
        for (var l = inicio; l < inicio + Math.Max(1, tamanho) && l < linhas.Length; l++) linhas[l] = linhas[l] with { Tipo = TipoLinha.Conflito };
    }

    List<MarcaRegua> Marcas(AnaliseConflito analise, MapaRolagem mapa, double h)
    {
        if (mapa.Altura <= 0) return [];
        double Fracao(int linha) => mapa.Unificado(AnaliseConflito.PainelResultado, linha * h) / mapa.Altura;
        var marcas = analise.LinhasQueVieram.Select(l => new MarcaRegua(Fracao(l), h / mapa.Altura, _cores.Sucesso, _cores.Sucesso)).ToList();
        marcas.AddRange(analise.Pendentes.Select(p => new MarcaRegua(Fracao(p.LinhaResultado), h / mapa.Altura, _cores.Ambar, _cores.Ambar)));
        return marcas;
    }

    void Realcar(PosicaoConflito p, bool pulsar = true)
    {
        Resultado.Realcar(p.LinhaResultado + 1, p.LinhaResultado + 2);
        if (p.AtualIni >= 0) Atual.Realcar(p.AtualIni + 1, p.AtualIni + 1 + Math.Max(1, p.AtualLen));
        if (p.EntrandoIni >= 0) Entrando.Realcar(p.EntrandoIni + 1, p.EntrandoIni + 1 + Math.Max(1, p.EntrandoLen));
        if (pulsar && Rolagem.Mapa is { Altura: > 0 } mapa)
            ReguaArquivo.DefinirFoco(mapa.Unificado(AnaliseConflito.PainelResultado, p.LinhaResultado * Resultado.AlturaLinha) / mapa.Altura, Resultado.AlturaLinha / mapa.Altura);
    }

    void AtualizarEstado()
    {
        var pendentes = Pendentes;
        var total = _arquivo?.Juncao.Conflitos.Count ?? 0;
        var resumo = pendentes == 1 ? "1 pendente" : $"{pendentes} pendentes";
        TextoEstado.Text = pendentes == 0 ? "✓  Tudo resolvido" : _foco > 0 ? $"Conflito {_foco} de {total}   •   {resumo}" : $"⚠  {resumo}";
        TextoEstado.Foreground = _cores.Pincel(pendentes == 0 ? _cores.Sucesso : _cores.Ambar);
    }

    void AoMover()
    {
        if (Rolagem.Mapa is not { Altura: > 0 } mapa) return;
        var tela = Math.Min(1, Rolagem.Viewport / mapa.Altura);
        ReguaArquivo.Posicionar(mapa.Maximo > 0 ? Rolagem.U / mapa.Maximo * (1 - tela) : 0, tela);
    }

    void AoRegua(double fracao, bool arrastando)
    {
        if (Rolagem.Mapa is { } mapa) Rolagem.IrPara(Rolagem.X, (fracao * mapa.Altura) - (Rolagem.Viewport / 2), arrastando);
    }

    void AoAnterior(object sender, RoutedEventArgs e) => Agir(() => Ir(-1));

    void AoProximo(object sender, RoutedEventArgs e) => Agir(() => Ir(1));

    void AoUsarEntrando(object sender, RoutedEventArgs e) => Agir(() => UsarNoAtual(EscolhaConflito.Entrando));

    void AoUsarAtual(object sender, RoutedEventArgs e) => Agir(() => UsarNoAtual(EscolhaConflito.Atual));

    void AoUsarDois(object sender, RoutedEventArgs e) => Agir(() => UsarNoAtual(EscolhaConflito.AtualDepoisEntrando));

    void AoDesfazer(object sender, RoutedEventArgs e) => Agir(() => Resultado.Editor.Undo());

    void AoRefazer(object sender, RoutedEventArgs e) => Agir(() => Resultado.Editor.Redo());

    /// <summary>Depois de um botão, o foco volta ao resultado: atalhos e digitação continuam funcionando.</summary>
    void Agir(Action acao)
    {
        acao();
        Resultado.Editor.TextArea.Focus();
    }
}
