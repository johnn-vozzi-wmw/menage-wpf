using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Menage.App.Controles;
using Menage.Core.Beta;
using Menage.Core.Entregas;
using Microsoft.Win32;

namespace Menage.App.Modulos.GeradorBeta;

/// <summary>Pede um texto curto (nome do favorito).</summary>
public partial class JanelaDeTexto : JanelaBase, IJanelaComParametro, IJanelaComResultado
{
    public const string Id = "beta.texto";

    public JanelaDeTexto(ContextoDeJanela contexto) : base(contexto) => InitializeComponent();

    public object? Resultado { get; private set; }

    public void Receber(object? parametro)
    {
        if (parametro is not PedidoDeTexto p) return;
        Title = p.Titulo;
        Rotulo.Text = p.Rotulo;
        Campo.Text = p.Valor;
        Loaded += (_, _) => { Campo.Focus(); Campo.SelectAll(); };
    }

    void AoConfirmar(object sender, RoutedEventArgs e)
    {
        Resultado = Campo.Text.Trim();
        Close();
    }
}

/// <summary>Ordem de execução dos projetos marcados (Subir e Descer).</summary>
public partial class JanelaDeOrdem : JanelaBase, IJanelaComParametro, IJanelaComResultado
{
    public const string Id = "beta.ordem";

    readonly ObservableCollection<string> _nomes = [];

    public JanelaDeOrdem(ContextoDeJanela contexto) : base(contexto)
    {
        InitializeComponent();
        Lista.ItemsSource = _nomes;
    }

    public object? Resultado { get; private set; }

    public void Receber(object? parametro)
    {
        if (parametro is not List<string> nomes) return;
        _nomes.Clear();
        foreach (var n in nomes) _nomes.Add(n);
        Lista.SelectedIndex = 0;
    }

    void AoSubir(object sender, RoutedEventArgs e) => Mover(-1);

    void AoDescer(object sender, RoutedEventArgs e) => Mover(1);

    void Mover(int passo)
    {
        var i = Lista.SelectedIndex;
        var j = i + passo;
        if (i < 0 || j < 0 || j >= _nomes.Count) return;
        _nomes.Move(i, j);
        Lista.SelectedIndex = j;
    }

    void AoConfirmar(object sender, RoutedEventArgs e)
    {
        Resultado = _nomes.ToList();
        Close();
    }
}

public sealed partial class ItemDeAlvo(string nome, string grupo, bool marcado) : ObservableObject
{
    [ObservableProperty]
    bool _marcado = marcado;

    public string Nome { get; } = nome;

    public string Grupo { get; } = grupo;
}

/// <summary>"Limpar target ao gerar": Desabilitado ou Habilitado e a lista de projetos por grupo.</summary>
public partial class JanelaDeLimparTarget : JanelaBase, IJanelaComParametro, IJanelaComResultado
{
    public const string Id = "beta.limpar-target";

    List<ItemDeAlvo> _itens = [];

    public JanelaDeLimparTarget(ContextoDeJanela contexto) : base(contexto) => InitializeComponent();

    public object? Resultado { get; private set; }

    public void Receber(object? parametro)
    {
        if (parametro is not PedidoDeLimparTarget p) return;
        _itens = [.. p.Projetos.Select(x => new ItemDeAlvo(x.Nome, x.Grupo.Length > 0 ? x.Grupo : "Sem grupo", p.Atual.Projetos.Contains(x.Nome, StringComparer.OrdinalIgnoreCase)))];
        var visao = new System.Windows.Data.ListCollectionView(_itens);
        visao.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(ItemDeAlvo.Grupo)));
        Lista.ItemsSource = visao;
        Habilitado.IsChecked = p.Atual.Ativo;
        AtualizarTexto();
    }

    void AoAlternar(object sender, RoutedEventArgs e) => AtualizarTexto();

    void AtualizarTexto()
    {
        var ligado = Habilitado.IsChecked == true;
        Habilitado.Content = ligado ? "Habilitado" : "Desabilitado";
        Lista.IsEnabled = ligado;
        Lista.Opacity = ligado ? 1 : 0.5;
        Aviso.Text = "";
    }

    void AoMarcarTodos(object sender, RoutedEventArgs e) => _itens.ForEach(i => i.Marcado = true);

    void AoMarcarNenhum(object sender, RoutedEventArgs e) => _itens.ForEach(i => i.Marcado = false);

    void AoConfirmar(object sender, RoutedEventArgs e)
    {
        var ativo = Habilitado.IsChecked == true;
        var projetos = _itens.Where(i => i.Marcado).Select(i => i.Nome).ToList();
        if (ativo && projetos.Count == 0)
        {
            Aviso.Text = "Marque ao menos um projeto ou escolha Desabilitado.";
            return;
        }

        Resultado = new LimparTargetConfig { Ativo = ativo, Projetos = projetos };
        Close();
    }
}

/// <summary>Pergunta se prepara a entrega, mostrando o que será copiado (✔ achou, ✖ não achou).</summary>
public partial class JanelaDeEntrega : JanelaBase, IJanelaComParametro, IJanelaComResultado
{
    public const string Id = "beta.entrega";

    public JanelaDeEntrega(ContextoDeJanela contexto) : base(contexto) => InitializeComponent();

    public object? Resultado { get; private set; }

    public void Receber(object? parametro)
    {
        if (parametro is not PedidoDeEntrega p) return;
        Title = $"Preparar a entrega do {p.Pacote}?";
        Subtitulo.Text = p.Subtitulo;
        Pasta.Text = $"Será criada uma pasta em {p.Pasta} com:";
        Itens.ItemsSource = p.Previa.Select(l => new { Texto = l, Falhou = l.StartsWith('✖') }).ToList();
        Rodape.Text = p.Zip
            ? "A pasta é compactada em .zip" + (p.CopiarZip ? " e o .zip vai para a área de transferência (no Slack: Ctrl+V)" : "") + ". Depois a pasta abre com o .zip selecionado. Nada é enviado ao Slack."
            : "Depois a pasta da entrega é aberta.";
    }

    void AoConfirmar(object sender, RoutedEventArgs e)
    {
        Resultado = true;
        Close();
    }
}

/// <summary>Configuração das entregas (pasta, zip, área de transferência) e "Preparar agora" por pacote.</summary>
public partial class JanelaDeEntregas : JanelaBase, IJanelaComParametro, IJanelaComResultado
{
    public const string Id = "beta.entregas";

    ConfiguracaoDeEntregas _config = new();

    public JanelaDeEntregas(ContextoDeJanela contexto) : base(contexto) => InitializeComponent();

    public object? Resultado { get; private set; }

    public void Receber(object? parametro)
    {
        if (parametro is not PedidoDeEntregas p) return;
        _config = p.Config;
        Pasta.Text = p.Config.Pasta;
        Zip.IsChecked = p.Config.Zip;
        Copiar.IsChecked = p.Config.CopiarZip;
        Pacotes.ItemsSource = p.Config.Pacotes.Select(x => new
        {
            x.Nome,
            Quando = x.Gatilho.Length > 0 ? $"pergunta quando {x.Gatilho} termina ok" : "pergunta no fim da geração",
            Itens = string.Join("\n", x.Itens.Select(i => $"{i.Projeto}: {i.Origem}")),
            Exige = "Exige ok: " + string.Join(", ", x.Obrigatorios) + (x.SeSelecionados.Count > 0 ? " (e " + string.Join(", ", x.SeSelecionados) + " se participar)" : ""),
        }).ToList();
    }

    void AoProcurar(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFolderDialog { Title = "Pasta das entregas" };
        if (dialogo.ShowDialog(this) == true) Pasta.Text = dialogo.FolderName;
    }

    ConfiguracaoDeEntregas Montar() => _config with { Pasta = Pasta.Text.Trim(), Zip = Zip.IsChecked == true, CopiarZip = Copiar.IsChecked == true };

    void AoPrepararAgora(object sender, RoutedEventArgs e)
    {
        Resultado = new RespostaDeEntregas(Montar(), (string)((FrameworkElement)sender).Tag);
        Close();
    }

    void AoConfirmar(object sender, RoutedEventArgs e)
    {
        if (Pasta.Text.Trim().Length == 0) return;
        Resultado = new RespostaDeEntregas(Montar(), null);
        Close();
    }
}
