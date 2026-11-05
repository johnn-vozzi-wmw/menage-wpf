using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Menage.Core.Diff;
using Wpf.Ui.Controls;

namespace Menage.App.Modulos.Diff;

/// <summary>Item da lista: etiqueta com a letra do tipo (na cor do tipo), nome e pasta.</summary>
public sealed record ItemArquivo(ArquivoAlterado Arquivo, Brush CorLetra, Brush FundoLetra)
{
    public string Nome => Arquivo.Nome;

    public string Pasta => Arquivo.Pasta.Length > 0 ? Arquivo.Pasta : "(raiz do projeto)";

    public string Letra => Arquivo.Letra.ToString();

    public string Dica => $"{Arquivo.Caminho}   ({Arquivo.Descricao})";
}

public partial class ListaArquivos : UserControl
{
    readonly CoresDiff _cores;

    public ListaArquivos()
    {
        _cores = new(this);
        InitializeComponent();
    }

    public event Action<ArquivoAlterado>? Escolheu;

    public event Action? PediuAtualizar;

    public event Action? PediuRecolher;

    public ArquivoAlterado? Selecionado => (Lista.SelectedItem as ItemArquivo)?.Arquivo;

    public int Total => Lista.Items.Count;

    /// <summary>Mostra os arquivos e mantém o escolhido, se ele ainda estiver na lista.</summary>
    public void Definir(IReadOnlyList<ArquivoAlterado> arquivos, string? manter = null)
    {
        var antes = manter ?? Selecionado?.Caminho;
        var itens = arquivos.Select(Item).ToList();
        Titulo.Text = $"Arquivos  ({arquivos.Count})";
        Lista.SelectionChanged -= AoSelecionar;
        Lista.ItemsSource = itens;
        Lista.SelectedItem = itens.Find(i => string.Equals(i.Arquivo.Caminho, antes, StringComparison.OrdinalIgnoreCase)) ?? itens.FirstOrDefault();
        Lista.SelectionChanged += AoSelecionar;
        if (Selecionado is { } a) Escolheu?.Invoke(a);
    }

    /// <summary>Escolhe pelo caminho, como um clique na lista.</summary>
    public void Selecionar(string caminho) =>
        Lista.SelectedItem = Lista.Items.OfType<ItemArquivo>().FirstOrDefault(i => i.Arquivo.Caminho == caminho);

    /// <summary>Volta a seleção sem avisar ninguém (o usuário desistiu de trocar de arquivo).</summary>
    public void VoltarPara(string? caminho)
    {
        Lista.SelectionChanged -= AoSelecionar;
        Lista.SelectedItem = Lista.Items.OfType<ItemArquivo>().FirstOrDefault(i => i.Arquivo.Caminho == caminho);
        Lista.SelectionChanged += AoSelecionar;
    }

    public void AoMudarEstado(bool expandida)
    {
        Titulo.Visibility = BotaoAtualizar.Visibility = expandida ? Visibility.Visible : Visibility.Collapsed;
        BotaoRecolher.Icon = new SymbolIcon { Symbol = expandida ? SymbolRegular.PanelLeftContract24 : SymbolRegular.PanelLeftExpand24 };
    }

    ItemArquivo Item(ArquivoAlterado a)
    {
        var cor = a.Tipo switch
        {
            TipoAlteracao.Novo or TipoAlteracao.Adicionado => _cores.Sucesso,
            TipoAlteracao.Excluido => _cores.Perigo,
            TipoAlteracao.Renomeado => _cores.Destaque,
            _ => _cores.Ambar,
        };
        return new(a, _cores.Pincel(cor), _cores.Pincel(Color.FromArgb(0x38, cor.R, cor.G, cor.B)));
    }

    void AoSelecionar(object sender, SelectionChangedEventArgs e)
    {
        if (Selecionado is { } a) Escolheu?.Invoke(a);
    }

    void AoAtualizar(object sender, RoutedEventArgs e) => PediuAtualizar?.Invoke();

    void AoRecolher(object sender, RoutedEventArgs e) => PediuRecolher?.Invoke();
}
