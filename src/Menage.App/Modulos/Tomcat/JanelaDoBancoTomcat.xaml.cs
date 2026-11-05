using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Menage.App.Controles;
using Menage.Core.Tomcat;

namespace Menage.App.Modulos.Tomcat;

/// <summary>Edita as linhas db_* do wmw_database_config.bat do ambiente (padrão + WEB/APP/INT). As regras estão em <see cref="BancoDoTomcat"/>.</summary>
public partial class JanelaDoBancoTomcat : JanelaBase, IJanelaComParametro
{
    public const string Id = "tomcat.banco";

    static readonly string[] Rotulos = ["Driver", "Host", "Porta", "Usuário", "Senha (criptografada)", "Banco / service name"];

    readonly Dictionary<string, Control> _celulas = [];
    Action? _cancelarTeste;
    static readonly List<string> OpcoesDeDriver = ["", .. BancoDoTomcat.Drivers];
    BancoDoTomcat? _banco;
    string _pasta = "";
    bool _carregando, _sujo, _descartar;

    public JanelaDoBancoTomcat(ContextoDeJanela contexto) : base(contexto)
    {
        InitializeComponent();
        MontarGrade();
        Closing += AoFechando;
        Closed += (_, _) => _cancelarTeste?.Invoke();
    }

    public void Receber(object? parametro)
    {
        if (parametro is not string pasta) return;
        _pasta = pasta;
        Carregar();
    }

    void MontarGrade()
    {
        for (var i = 0; i <= Rotulos.Length; i++) Grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var j = 0; j < BancoDoTomcat.Grupos.Count; j++)
        {
            var g = BancoDoTomcat.Grupos[j];
            var titulo = new TextBlock { Text = g.Length == 0 ? "Padrão" : g.ToUpperInvariant(), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 6) };
            titulo.SetResourceReference(TextBlock.ForegroundProperty, "Tema.TextoForte");
            Grade.Children.Add(Colocar(titulo, 0, j + 1));
        }

        for (var i = 0; i < Rotulos.Length; i++)
        {
            var campo = BancoDoTomcat.Campos[i];
            var rotulo = new TextBlock { Text = Rotulos[i], VerticalAlignment = VerticalAlignment.Center, Style = (Style)FindResource("Texto.Legenda") };
            Grade.Children.Add(Colocar(rotulo, i + 1, 0));
            for (var j = 0; j < BancoDoTomcat.Grupos.Count; j++)
            {
                var g = BancoDoTomcat.Grupos[j];
                Control celula = campo == "driver"
                    ? new ComboBox { ItemsSource = OpcoesDeDriver }
                    : new TextBox();
                celula.Margin = new Thickness(0, 0, 8, 8);
                celula.ToolTip = $"{BancoDoTomcat.Chave(campo, g)}{(g.Length > 0 ? " (vazio = usa o Padrão)" : "")}";
                if (celula is TextBox t) t.TextChanged += (_, _) => Alterou();
                else if (celula is ComboBox c) c.SelectionChanged += (_, _) => Alterou();
                _celulas[BancoDoTomcat.Chave(campo, g)] = celula;
                Grade.Children.Add(Colocar(celula, i + 1, j + 1));
            }
        }
    }

    static UIElement Colocar(UIElement e, int linha, int coluna)
    {
        Grid.SetRow(e, linha);
        Grid.SetColumn(e, coluna);
        return e;
    }

    void Alterou()
    {
        if (_carregando) return;
        _sujo = true;
        _descartar = false;
        Title = "Banco de dados do Tomcat ●";
    }

    void Carregar()
    {
        Arquivo.Text = "Arquivo: " + BancoDoTomcat.Arquivo(_pasta);
        var r = BancoDoTomcat.Ler(_pasta);
        _banco = r.EhOk ? r.Valor : null;
        _carregando = true;
        foreach (var g in BancoDoTomcat.Grupos)
            foreach (var c in BancoDoTomcat.Campos)
                Escrever(_celulas[BancoDoTomcat.Chave(c, g)], _banco?.Obter(c, g) ?? "");
        _carregando = false;
        (_sujo, _descartar) = (false, false);
        Title = "Banco de dados do Tomcat";
        foreach (var celula in _celulas.Values) celula.IsEnabled = _banco is not null;
        (BotaoSalvar.IsEnabled, BotaoTestar.IsEnabled) = (_banco is not null, _banco is not null);
        Dizer(r.EhOk ? "" : r.Erro.ToString(), erro: !r.EhOk);
    }

    static void Escrever(Control celula, string valor)
    {
        if (celula is TextBox t) t.Text = valor;
        else if (celula is ComboBox c) c.SelectedItem = valor;
    }

    static string Ler(Control celula) => celula switch { TextBox t => t.Text, ComboBox c => c.SelectedItem as string ?? "", _ => "" };

    void Coletar()
    {
        foreach (var g in BancoDoTomcat.Grupos)
            foreach (var c in BancoDoTomcat.Campos)
                _banco!.Definir(c, g, Ler(_celulas[BancoDoTomcat.Chave(c, g)]));
    }

    void AoSalvar(object sender, RoutedEventArgs e)
    {
        if (_banco is null) return;
        Coletar();
        var r = _banco.Gravar();
        if (!r.EhOk)
        {
            Dizer(r.Erro.ToString(), erro: true);
            return;
        }

        _sujo = false;
        Title = "Banco de dados do Tomcat";
        var avisos = _banco.Avisos();
        Dizer(avisos.Count > 0 ? "Salvo. " + string.Join(" ", avisos) : "Salvo no wmw_database_config.bat (cópia original em .menage.bak).", erro: false);
    }

    void AoRecarregar(object sender, RoutedEventArgs e) => Carregar();

    /// <summary>Com alterações pendentes, o primeiro Fechar só avisa; o segundo descarta.</summary>
    void AoFechar(object sender, RoutedEventArgs e) => Close();

    void AoFechando(object? sender, CancelEventArgs e)
    {
        if (!_sujo || _descartar) return;
        e.Cancel = true;
        _descartar = true;
        Dizer("Há alterações não salvas. Feche de novo para descartá-las, ou clique em Salvar.", erro: true);
    }

    async void AoTestar(object sender, RoutedEventArgs e)
    {
        if (_banco is null) return;
        Coletar();
        using var teste = new CancellationTokenSource();
        _cancelarTeste = teste.Cancel;
        BotaoTestar.IsEnabled = false;
        Dizer("Testando host e porta de cada conexão...", erro: false);
        try
        {
            var linhas = await _banco.TestarAlcanceAsync(TimeSpan.FromSeconds(4), teste.Token);
            Dizer(string.Join("   ", linhas), erro: linhas.Any(l => l.Contains("NÃO", StringComparison.Ordinal)));
        }
        catch (OperationCanceledException)
        {
            // janela fechada durante o teste
        }
        finally
        {
            BotaoTestar.IsEnabled = _banco is not null;
        }
    }

    void Dizer(string texto, bool erro)
    {
        Mensagem.Text = texto;
        Mensagem.SetResourceReference(TextBlock.ForegroundProperty, erro ? "Tema.Vermelho" : "Tema.TextoFraco");
    }
}
