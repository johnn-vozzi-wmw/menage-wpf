using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.App.Controles;
using Menage.Core;
using Menage.Core.BetaAutomatica;

namespace Menage.App.Modulos.BetaAutomatica;

public sealed partial class LinhaDeJql(JqlBeta jql, bool padrao, string tipo, Action aoMudar) : ObservableObject
{
    public JqlBeta Jql { get; } = jql;

    public bool PadraoDoPrograma { get; } = padrao;

    public bool Ativa
    {
        get => Jql.Ativa;
        set { Jql.Ativa = value; OnPropertyChanged(); aoMudar(); }
    }

    public string Nome => Jql.Nome;

    public void AtualizarNome() => OnPropertyChanged(nameof(Nome));

    [ObservableProperty]
    string _tipo = tipo;
}

/// <summary>Aba JQL: lista de consultas (padrão e suas), editor, teste (só leitura no Jira) e Salvar/Cancelar. As regras estão no <see cref="GerenciadorDeJql"/>.</summary>
public sealed partial class JqlViewModel : ObservableObject
{
    public static readonly IReadOnlyList<(string Texto, string Trecho)> TrechosProntos =
    [
        ("{projeto}", "{projeto}"), ("status", "status = \"{status}\""), ("Minhas", "assignee = currentUser()"), ("Já foi minha", "assignee WAS currentUser()"),
        ("Comentário", "comment ~ \"PROJETOS ALTERADOS\""), ("14 dias", "updated >= -14d"), ("AND", " AND "), ("OR", " OR "), ("Ordenar", " ORDER BY updated DESC"),
    ];

    readonly ServicoDeBetaAutomatica _servico;
    bool _carregando;
    string _base = "";

    [ObservableProperty]
    LinhaDeJql? _selecionada;

    [ObservableProperty]
    string _nome = "";

    [ObservableProperty]
    string _texto = "";

    [ObservableProperty]
    string _expandida = "";

    [ObservableProperty]
    string _aviso = "";

    [ObservableProperty]
    TomDeCor _tomDoAviso = TomDeCor.Neutro;

    [ObservableProperty]
    IReadOnlyList<TrechoDeJql> _trechos = [];

    [ObservableProperty]
    string _resultadoDoTeste = "";

    [ObservableProperty]
    bool _testando;

    [ObservableProperty]
    string _erro = "";

    [ObservableProperty]
    string _mensagem = "";

    [ObservableProperty]
    bool _alterado;

    [ObservableProperty]
    bool _podeTestar;

    [ObservableProperty]
    bool _podeExcluir;

    [ObservableProperty]
    bool _podeRestaurar;

    [ObservableProperty]
    bool _podeSubir;

    [ObservableProperty]
    bool _podeDescer;

    public JqlViewModel(ServicoDeBetaAutomatica servico)
    {
        _servico = servico;
        Recarregar();
    }

    public ObservableCollection<LinhaDeJql> Linhas { get; } = [];

    public bool NomeSomenteLeitura => Selecionada is null || Selecionada.PadraoDoPrograma;

    public bool TemSelecao => Selecionada is not null;

    public string TextoDoTeste => Testando ? "Testando..." : "Testar esta JQL";

    bool Tester => _servico.ModoTester;

    /// <summary>Volta ao que está gravado (Cancelar, abertura da janela).</summary>
    public void Recarregar()
    {
        Carregar(GerenciadorDeJql.Efetivas(_servico.Config, Tester), 0);
        Erro = "";
        Mensagem = "";
        _base = Assinatura();
        Alterado = false;
    }

    JqlBeta? PadraoDe(JqlBeta j) => GerenciadorDeJql.Padrao(Tester).FirstOrDefault(p => p.Id == j.Id);

    string TipoDe(JqlBeta j) => PadraoDe(j) is not { } p ? "sua" : j.Jql.Trim() == p.Jql.Trim() ? "padrão" : "padrão (editada)";

    void Carregar(IEnumerable<JqlBeta> itens, int selecionar)
    {
        _carregando = true;
        Linhas.Clear();
        foreach (var j in itens) Linhas.Add(new LinhaDeJql(j, PadraoDe(j) is not null, TipoDe(j), AoMudarLinha));
        _carregando = false;
        Selecionada = Linhas.Count > 0 ? Linhas[Math.Clamp(selecionar, 0, Linhas.Count - 1)] : null;
        Mostrar();
    }

    string Assinatura() => string.Join("\n", Linhas.Select(l => $"{l.Jql.Id}|{l.Jql.Nome}|{l.Jql.Ativa}|{l.Jql.Jql}"));

    void AoMudarLinha()
    {
        Atualizar();
        Revisar();
    }

    void Atualizar()
    {
        Alterado = Assinatura() != _base;
        Mensagem = "";
    }

    partial void OnSelecionadaChanged(LinhaDeJql? value)
    {
        if (_carregando) return;
        Mostrar();
    }

    void Mostrar()
    {
        var j = Selecionada?.Jql;
        _carregando = true;
        Nome = j?.Nome ?? "";
        Texto = j?.Jql ?? "";
        _carregando = false;
        OnPropertyChanged(nameof(NomeSomenteLeitura));
        OnPropertyChanged(nameof(TemSelecao));
        Revisar();
    }

    partial void OnNomeChanged(string value)
    {
        if (_carregando || Selecionada is not { PadraoDoPrograma: false } l) return;
        l.Jql.Nome = value;
        l.AtualizarNome();
        Atualizar();
    }

    partial void OnTextoChanged(string value)
    {
        if (_carregando || Selecionada is not { } l) return;
        l.Jql.Jql = value;
        l.Tipo = TipoDe(l.Jql);
        Atualizar();
        Revisar();
    }

    partial void OnTestandoChanged(bool value) => OnPropertyChanged(nameof(TextoDoTeste));

    void Revisar()
    {
        var j = Selecionada?.Jql;
        var i = Selecionada is null ? -1 : Linhas.IndexOf(Selecionada);
        PodeExcluir = j is not null && PadraoDe(j) is null;
        PodeSubir = i > 0;
        PodeDescer = i >= 0 && i < Linhas.Count - 1;
        Trechos = MarcadoresDeJql.Separar(j?.Jql);
        if (j is null) { Expandida = ""; Aviso = ""; PodeTestar = false; PodeRestaurar = false; return; }

        Expandida = GerenciadorDeJql.Expandir(j.Jql, _servico.Config, Tester);
        var a = GerenciadorDeJql.Avaliar(j.Jql);
        TomDoAviso = a.Erro ? TomDeCor.Vermelho : a.Mensagem is not null ? TomDeCor.Amarelo : TomDeCor.Neutro;
        Aviso = a.Mensagem ?? (j.Ativa ? "Consulta ativa." : "Consulta desligada: não é executada.");
        PodeTestar = !a.Erro && !Testando;
        PodeRestaurar = PadraoDe(j) is not null && TipoDe(j) != "padrão";
        Erro = "";
    }

    [RelayCommand]
    void Nova() => Adicionar(null);

    [RelayCommand]
    void Duplicar() => Adicionar(Selecionada?.Jql);

    void Adicionar(JqlBeta? origem)
    {
        var nova = new JqlBeta
        {
            Id = "c-" + Guid.NewGuid().ToString("N")[..8], Ativa = true,
            Nome = origem is null ? "Nova consulta" : origem.Nome + " (cópia)", Jql = origem?.Jql ?? GerenciadorDeJql.TrechoNova,
        };
        var itens = Linhas.Select(l => l.Jql).ToList();
        var posicao = origem is null || Selecionada is null ? itens.Count : Linhas.IndexOf(Selecionada) + 1;
        itens.Insert(posicao, nova);
        Refazer(itens, posicao);
    }

    [RelayCommand(CanExecute = nameof(PodeExcluir))]
    void Excluir()
    {
        if (Selecionada is not { PadraoDoPrograma: false } l) return;
        var i = Linhas.IndexOf(l);
        Refazer(Linhas.Where(x => x != l).Select(x => x.Jql).ToList(), i);
    }

    [RelayCommand(CanExecute = nameof(PodeRestaurar))]
    void Restaurar()
    {
        if (Selecionada?.Jql is not { } j || PadraoDe(j) is not { } p) return;
        var i = Linhas.IndexOf(Selecionada);
        j.Jql = p.Jql;
        j.Ativa = true;
        Refazer(Linhas.Select(x => x.Jql).ToList(), i);
    }

    [RelayCommand]
    void RestaurarTodas() => Refazer(GerenciadorDeJql.Padrao(Tester), 0);

    [RelayCommand(CanExecute = nameof(PodeSubir))]
    void Subir() => Mover(-1);

    [RelayCommand(CanExecute = nameof(PodeDescer))]
    void Descer() => Mover(1);

    void Mover(int passo)
    {
        if (Selecionada is null) return;
        var i = Linhas.IndexOf(Selecionada);
        var j = i + passo;
        if (j < 0 || j >= Linhas.Count) return;
        var itens = Linhas.Select(l => l.Jql).ToList();
        (itens[i], itens[j]) = (itens[j], itens[i]);
        Refazer(itens, j);
    }

    void Refazer(List<JqlBeta> itens, int selecionar)
    {
        Carregar(itens, selecionar);
        Atualizar();
    }

    partial void OnPodeExcluirChanged(bool value) => ExcluirCommand.NotifyCanExecuteChanged();

    partial void OnPodeRestaurarChanged(bool value) => RestaurarCommand.NotifyCanExecuteChanged();

    partial void OnPodeSubirChanged(bool value) => SubirCommand.NotifyCanExecuteChanged();

    partial void OnPodeDescerChanged(bool value) => DescerCommand.NotifyCanExecuteChanged();

    partial void OnPodeTestarChanged(bool value) => TestarCommand.NotifyCanExecuteChanged();

    /// <summary>Insere o trecho no ponto do cursor do editor (a tela informa a posição) e devolve a nova posição do cursor.</summary>
    public int Inserir(string trecho, int posicao, int selecionados)
    {
        if (Selecionada is null) return posicao;
        var p = Math.Clamp(posicao, 0, Texto.Length);
        var fim = Math.Clamp(p + selecionados, p, Texto.Length);
        Texto = Texto[..p] + trecho + Texto[fim..];
        return p + trecho.Length;
    }

    [RelayCommand(CanExecute = nameof(PodeTestar))]
    public async Task TestarAsync()
    {
        if (Selecionada is null) return;
        Testando = true;
        PodeTestar = false;
        ResultadoDoTeste = "Consultando o Jira...";
        try
        {
            var r = await _servico.TestarJqlAsync(GerenciadorDeJql.Expandir(Selecionada.Jql.Jql, _servico.Config, Tester));
            ResultadoDoTeste = Formatar(r);
        }
        finally
        {
            Testando = false;
            Revisar();
        }
    }

    static string Formatar(Resultado<IReadOnlyList<IssueJira>> r)
    {
        if (!r.EhOk) return "ERRO: " + r.Erro;
        var sb = new StringBuilder();
        sb.AppendLine(FormattableString.Invariant($"{r.Valor.Count} resultado(s){(r.Valor.Count >= 100 ? " (mostrando os 100 primeiros)" : "")}:"));
        foreach (var i in r.Valor.Take(100))
            sb.AppendLine(FormattableString.Invariant($"  {i.Chave}  [{i.Status}]  {i.Titulo}") + (i.Responsavel.Length > 0 ? FormattableString.Invariant($"  ({i.Responsavel})") : ""));
        sb.Append("A consulta só traz atividades; entra na lista só quem passar pelas regras de escopo.");
        return sb.ToString();
    }

    [RelayCommand]
    void Salvar()
    {
        foreach (var l in Linhas)
        {
            var a = GerenciadorDeJql.Avaliar(l.Jql.Jql);
            if (a.Erro) { Erro = $"\"{l.Nome}\": {a.Mensagem}"; return; }
            if (!l.PadraoDoPrograma && l.Nome.Trim().Length == 0) { Erro = "Dê um nome à consulta personalizada."; return; }
        }

        _servico.DefinirJqls(Linhas.Select(l => l.Jql));
        _servico.VerificarAgora();
        _base = Assinatura();
        Alterado = false;
        Mensagem = "Consultas salvas.";
    }

    [RelayCommand]
    void Cancelar() => Recarregar();
}
