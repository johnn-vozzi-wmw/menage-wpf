using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Menage.App.Controles;
using Menage.Core.BetaAutomatica;
using Menage.Core.Interacao;

namespace Menage.App.Modulos.BetaAutomatica;

public sealed partial class LinhaDeJql(JqlBeta jql, bool padrao, string tipo) : ObservableObject
{
    public JqlBeta Jql { get; } = jql;

    public bool PadraoDoPrograma { get; } = padrao;

    public bool Ativa
    {
        get => Jql.Ativa;
        set { Jql.Ativa = value; OnPropertyChanged(); }
    }

    public string Nome => Jql.Nome;

    public void AtualizarNome() => OnPropertyChanged(nameof(Nome));

    [ObservableProperty]
    string _tipo = tipo;
}

/// <summary>Gerenciador de JQL: edição, validação, prévia dos marcadores e teste (só leitura). As regras estão no <see cref="GerenciadorDeJql"/>.</summary>
public partial class JanelaDeJql : JanelaBase, IJanelaComParametro, IJanelaComResultado
{
    public const string Id = "beta-auto.jql";

    static readonly (string Texto, string Trecho)[] TrechosProntos =
    [
        ("{projeto}", "{projeto}"), ("status", "status = \"{status}\""), ("Minhas", "assignee = currentUser()"), ("Já foi minha", "assignee WAS currentUser()"),
        ("Comentário", "comment ~ \"PROJETOS ALTERADOS\""), ("14 dias", "updated >= -14d"), ("AND", " AND "), ("OR", " OR "), ("Ordenar", " ORDER BY updated DESC"),
    ];

    readonly ObservableCollection<LinhaDeJql> _linhas = [];
    ServicoDeBetaAutomatica? _servico;
    bool _carregando;

    public JanelaDeJql(ContextoDeJanela contexto) : base(contexto)
    {
        InitializeComponent();
        Lista.ItemsSource = _linhas;
        foreach (var (texto, trecho) in TrechosProntos)
        {
            var b = new Button { Content = texto, Margin = new Thickness(0, 0, 6, 4), Tag = trecho };
            b.Click += (_, _) => Inserir(trecho);
            Trechos.Children.Add(b);
        }
    }

    public object? Resultado { get; private set; }

    bool Tester => _servico?.ModoTester == true;

    LinhaDeJql? Atual => Lista.SelectedItem as LinhaDeJql;

    public void Receber(object? parametro)
    {
        if (parametro is not ServicoDeBetaAutomatica s) return;
        _servico = s;
        Recarregar(GerenciadorDeJql.Efetivas(s.Config, s.ModoTester), 0);
    }

    JqlBeta? PadraoDe(JqlBeta j) => GerenciadorDeJql.Padrao(Tester).FirstOrDefault(p => p.Id == j.Id);

    string TipoDe(JqlBeta j) => PadraoDe(j) is not { } p ? "Personalizada" : j.Jql.Trim() == p.Jql.Trim() ? "Padrão" : "Padrão (editada)";

    void Recarregar(IEnumerable<JqlBeta> itens, int selecionar)
    {
        _carregando = true;
        _linhas.Clear();
        foreach (var j in itens) _linhas.Add(new LinhaDeJql(j, PadraoDe(j) is not null, TipoDe(j)));
        _carregando = false;
        if (_linhas.Count > 0) Lista.SelectedIndex = Math.Clamp(selecionar, 0, _linhas.Count - 1);
        else Carregar();
    }

    void Carregar()
    {
        var j = Atual?.Jql;
        _carregando = true;
        Nome.Text = j?.Nome ?? "";
        Nome.IsReadOnly = j is null || PadraoDe(j) is not null;
        Jql.Text = j?.Jql ?? "";
        Jql.IsEnabled = j is not null;
        _carregando = false;
        Excluir.IsEnabled = j is not null && PadraoDe(j) is null;
        Revisar();
    }

    void Revisar()
    {
        var j = Atual?.Jql;
        if (j is null || _servico is null) { Expandida.Text = ""; AvisoJql.Text = ""; Testar.IsEnabled = false; return; }
        Expandida.Text = GerenciadorDeJql.Expandir(j.Jql, _servico.Config, Tester);
        var a = GerenciadorDeJql.Avaliar(j.Jql);
        AvisoJql.Foreground = (System.Windows.Media.Brush)FindResource(a.Erro ? "Tema.Vermelho" : a.Mensagem is not null ? "Tema.Amarelo" : "Tema.TextoFraco");
        AvisoJql.Text = a.Mensagem ?? (j.Ativa ? "Consulta ativa." : "Consulta desligada: não é executada.");
        Testar.IsEnabled = !a.Erro;
        Restaurar.IsEnabled = PadraoDe(j) is not null && TipoDe(j) != "Padrão";
        Erro.Text = "";
    }

    void AoSelecionar(object sender, SelectionChangedEventArgs e) { if (!_carregando) Carregar(); }

    void AoLigar(object sender, RoutedEventArgs e) => Revisar();

    void AoMudarNome(object sender, TextChangedEventArgs e)
    {
        if (_carregando || Atual is not { } l || l.PadraoDoPrograma) return;
        l.Jql.Nome = Nome.Text;
        l.AtualizarNome();
    }

    void AoMudarJql(object sender, TextChangedEventArgs e)
    {
        if (_carregando || Atual is not { } l) return;
        l.Jql.Jql = Jql.Text;
        l.Tipo = TipoDe(l.Jql);
        Revisar();
    }

    void Inserir(string trecho)
    {
        if (Atual is null) return;
        var p = Jql.SelectionStart;
        Jql.Text = Jql.Text[..p] + trecho + Jql.Text[(p + Jql.SelectionLength)..];
        Jql.SelectionStart = p + trecho.Length;
        Jql.Focus();
    }

    void AoNova(object sender, RoutedEventArgs e) => Adicionar(null);

    void AoDuplicar(object sender, RoutedEventArgs e) => Adicionar(Atual?.Jql);

    void Adicionar(JqlBeta? origem)
    {
        var nova = new JqlBeta
        {
            Id = "c-" + Guid.NewGuid().ToString("N")[..8], Ativa = true,
            Nome = origem is null ? "Nova consulta" : origem.Nome + " (cópia)", Jql = origem?.Jql ?? GerenciadorDeJql.TrechoNova,
        };
        var itens = _linhas.Select(l => l.Jql).Append(nova).ToList();
        Recarregar(itens, itens.Count - 1);
        Nome.Focus();
        Nome.SelectAll();
    }

    void AoExcluir(object sender, RoutedEventArgs e)
    {
        if (Atual is not { PadraoDoPrograma: false } l) return;
        var i = _linhas.IndexOf(l);
        Recarregar(_linhas.Where(x => x != l).Select(x => x.Jql).ToList(), i);
    }

    void AoRestaurar(object sender, RoutedEventArgs e)
    {
        if (Atual?.Jql is not { } j || PadraoDe(j) is not { } p) return;
        var i = _linhas.IndexOf(Atual);
        j.Jql = p.Jql;
        j.Ativa = true;
        Recarregar(_linhas.Select(x => x.Jql).ToList(), i);
    }

    void AoRestaurarTodas(object sender, RoutedEventArgs e) => Recarregar(GerenciadorDeJql.Padrao(Tester), 0);

    void AoSubir(object sender, RoutedEventArgs e) => Mover(-1);

    void AoDescer(object sender, RoutedEventArgs e) => Mover(1);

    void Mover(int passo)
    {
        var i = Lista.SelectedIndex;
        var j = i + passo;
        if (i < 0 || j < 0 || j >= _linhas.Count) return;
        var itens = _linhas.Select(l => l.Jql).ToList();
        (itens[i], itens[j]) = (itens[j], itens[i]);
        Recarregar(itens, j);
    }

    async void AoTestar(object sender, RoutedEventArgs e)
    {
        if (Atual is null || _servico is null) return;
        Testar.IsEnabled = false;
        Testar.Content = "Testando...";
        Resultado_.Text = "Consultando o Jira...";
        var r = await _servico.TestarJqlAsync(GerenciadorDeJql.Expandir(Atual.Jql.Jql, _servico.Config, Tester));
        var sb = new StringBuilder();
        if (!r.EhOk) sb.Append("ERRO: ").Append(r.Erro);
        else
        {
            sb.AppendLine(FormattableString.Invariant($"{r.Valor.Count} resultado(s){(r.Valor.Count >= 100 ? " (mostrando os 100 primeiros)" : "")}:"));
            foreach (var i in r.Valor.Take(100)) sb.AppendLine(FormattableString.Invariant($"  {i.Chave}  [{i.Status}]  {i.Titulo}") + (i.Responsavel.Length > 0 ? FormattableString.Invariant($"  ({i.Responsavel})") : ""));
            sb.Append("A consulta só traz atividades; entra na lista só quem passar pelas regras de escopo.");
        }

        Resultado_.Text = sb.ToString();
        Testar.Content = "Testar esta JQL";
        Revisar();
    }

    void AoSalvar(object sender, RoutedEventArgs e)
    {
        if (_servico is null) return;
        foreach (var l in _linhas)
        {
            var a = GerenciadorDeJql.Avaliar(l.Jql.Jql);
            if (a.Erro) { Erro.Text = $"\"{l.Nome}\": {a.Mensagem}"; return; }
            if (!l.PadraoDoPrograma && l.Nome.Trim().Length == 0) { Erro.Text = "Dê um nome à consulta personalizada."; return; }
        }

        _servico.DefinirJqls(_linhas.Select(l => l.Jql));
        _servico.VerificarAgora();
        Close();
    }
}
