using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Menage.App.Controles;
using Menage.Core.Melhorias;
using Menage.Core.Projetos;
using Microsoft.Win32;

namespace Menage.App.Modulos.Melhorias;

public sealed partial class LinhaDeProjeto(string nome) : ObservableObject
{
    public string Nome { get; } = nome;

    [ObservableProperty]
    bool _marcado;

    [ObservableProperty]
    string _branch = "";

    [ObservableProperty]
    string _base = "";
}

public sealed partial class LinhaDePerfil(PerfilDeBancoRef perfil) : ObservableObject
{
    public string Id { get; } = perfil.Id;

    public string Nome { get; } = perfil.Nome;

    public string Tipo { get; } = perfil.Tipo;

    [ObservableProperty]
    bool _marcado;

    [ObservableProperty]
    bool _aplicarAoAtivar;
}

/// <summary>Pede a edição: <c>Existente</c> nulo = melhoria nova.</summary>
public sealed record PedidoDeEdicaoDeMelhoria(Melhoria? Existente);

/// <summary>O que o editor entrega: a melhoria já gravada e o que fazer a seguir (só para melhoria nova).</summary>
public sealed record ResultadoDoEditor(string Id, bool Nova, bool CriarBranches, bool Publicar, string? MensagemDoSqlite);

/// <summary>Cadastro e edição de uma melhoria. A validação e a gravação são do <see cref="RepositorioDeMelhorias"/>.</summary>
public partial class JanelaEditorDeMelhoria : JanelaBase, IJanelaComParametro, IJanelaComResultado
{
    public const string Id = "melhorias.editor";

    readonly RepositorioDeMelhorias _repositorio;
    readonly SqliteDeAtividade _sqlite;
    readonly List<LinhaDeProjeto> _projetos;
    readonly List<LinhaDePerfil> _perfis;
    Melhoria? _existente;
    bool _ajustandoId;

    public JanelaEditorDeMelhoria(ContextoDeJanela contexto, RepositorioDeMelhorias repositorio, IProjetos projetos, IContextoDeBanco banco, SqliteDeAtividade sqlite) : base(contexto)
    {
        (_repositorio, _sqlite) = (repositorio, sqlite);
        InitializeComponent();
        _projetos = [.. projetos.Todos.Select(p => new LinhaDeProjeto(p.Nome))];
        _perfis = [.. banco.Perfis.Select(p => new LinhaDePerfil(p))];
        Projetos.ItemsSource = _projetos;
        Perfis.ItemsSource = _perfis;
        SemPerfis.Visibility = _perfis.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TagsExistentes.ItemsSource = repositorio.Config.TagsComContagem().Select(t => t.Key).ToList();
    }

    public object? Resultado { get; private set; }

    public void Receber(object? parametro)
    {
        _existente = (parametro as PedidoDeEdicaoDeMelhoria)?.Existente;
        var m = _existente;
        Title = m is null ? "Nova melhoria" : $"Editar {m.Id}";
        Id_.Text = m?.Id ?? "";
        Id_.IsReadOnly = m is not null;
        Titulo.Text = m?.Titulo ?? "";
        Notas.Text = m?.Notas ?? "";
        Tags.Text = string.Join(", ", m?.Tags ?? []);
        Status.SelectedIndex = m?.Status switch { Melhoria.Pausada => 1, Melhoria.Concluida => 2, _ => 0 };
        PainelNova.Visibility = m is null ? Visibility.Visible : Visibility.Collapsed;
        foreach (var p in _projetos)
        {
            var b = m?.Do(p.Nome);
            (p.Marcado, p.Branch, p.Base) = b is null ? (false, "", "develop") : (true, b.Branch, b.Base);
        }

        foreach (var p in _perfis)
        {
            p.Marcado = m?.Perfis.Contains(p.Id) == true;
            p.AplicarAoAtivar = m?.PerfilAtivoId == p.Id;
        }
    }

    // Marcar um projeto sugere a branch = id da melhoria (só enquanto o campo está vazio ou ainda igual ao id anterior).
    void AoMudarId(object sender, TextChangedEventArgs e)
    {
        if (_ajustandoId) return;
        _ajustandoId = true;
        foreach (var p in _projetos.Where(p => p.Branch.Length == 0)) p.Branch = Id_.Text.Trim();
        _ajustandoId = false;
    }

    void AoClicarTag(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        var atuais = Melhoria.InterpretarTags(Tags.Text);
        if (!atuais.Remove(tag)) atuais.Add(tag);
        Tags.Text = string.Join(", ", atuais);
    }

    void AoEscolherSqlite(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Title = "Banco sqlite desta melhoria", Filter = "sqlite|*.sqlite3;*.sqlite;*.db|Todos|*.*" };
        if (d.ShowDialog(this) == true) Sqlite.Text = d.FileName;
    }

    async void AoSalvar(object sender, RoutedEventArgs e)
    {
        var marcados = _projetos.Where(p => p.Marcado).ToList();
        var perfis = _perfis.Where(p => p.Marcado).ToList();
        var m = new Melhoria
        {
            Id = Id_.Text, Titulo = Titulo.Text.Trim(), Notas = Notas.Text, Status = (Status.SelectedItem as ComboBoxItem)?.Tag as string ?? Melhoria.Andamento,
            Tags = Melhoria.InterpretarTags(Tags.Text),
            Branches = [.. marcados.Select(p => new BranchDaMelhoria { Projeto = p.Nome, Branch = p.Branch.Trim(), Base = p.Base.Trim() })],
            Perfis = [.. perfis.Select(p => p.Id)],
            PerfilAtivoId = perfis.FirstOrDefault(p => p.AplicarAoAtivar)?.Id ?? perfis.FirstOrDefault()?.Id ?? "",
            CriadaEm = _existente?.CriadaEm ?? "", UltimoUso = _existente?.UltimoUso ?? "",
        };
        var r = _repositorio.Gravar(m, nova: _existente is null);
        if (!r.EhOk) { Aviso.Text = r.Erro.ToString(); return; }

        string? mensagem = null;
        if (Sqlite.Text.Trim().Length > 0) mensagem = (await _sqlite.AdotarAsync(_repositorio.Config.Sqlite, Sqlite.Text, m.Id)).Mensagem;
        Resultado = new ResultadoDoEditor(m.Id, _existente is null, _existente is null && CriarBranches.IsChecked == true, Publicar.IsChecked == true, mensagem);
        Close();
    }
}
