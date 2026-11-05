using System.Windows;
using System.Windows.Controls;
using Menage.App.Controles;

namespace Menage.App.Modulos.OperacoesLote.Formulario;

/// <summary>
/// Diálogo único das operações em lote: monta os campos do <see cref="PedidoDeFormulario"/> e devolve uma
/// <see cref="RespostaDeFormulario"/> (nula se cancelado). Cada operação só descreve o que precisa perguntar.
/// </summary>
public partial class JanelaDeFormulario : JanelaBase, IJanelaComParametro, IJanelaComResultado
{
    public const string Id = "lote.formulario";

    readonly List<(CampoTexto Campo, TextBox Caixa)> _textos = [];
    readonly List<(CampoEscolha Campo, ComboBox Combo)> _escolhas = [];
    readonly List<(CampoOpcoes Campo, ComboBox Combo)> _opcoes = [];
    readonly List<(CampoCaixa Campo, CheckBox Caixa)> _caixas = [];
    readonly List<CampoLista> _listas = [];

    public JanelaDeFormulario(ContextoDeJanela contexto) : base(contexto) => InitializeComponent();

    public object? Resultado { get; private set; }

    public void Receber(object? parametro)
    {
        if (parametro is not PedidoDeFormulario pedido) return;
        (Title, Width, Height, Descricao.Text, Confirmar.Content) = (pedido.Titulo, pedido.Largura, pedido.Altura, pedido.Descricao, pedido.Confirmar);
        foreach (var campo in pedido.Campos) Adicionar(campo);
    }

    void Adicionar(CampoDeFormulario campo)
    {
        if (campo is not CampoCaixa) Corpo.Children.Add(Rotulo(campo.Rotulo));
        switch (campo)
        {
            case CampoTexto texto: AdicionarTexto(texto); break;
            case CampoCaixa caixa: AdicionarCaixa(caixa); break;
            case CampoEscolha escolha: AdicionarEscolha(escolha); break;
            case CampoOpcoes opcoes: AdicionarOpcoes(opcoes); break;
            case CampoInformacao info: AdicionarInformacao(info); break;
            case CampoLista lista: AdicionarLista(lista); break;
        }
    }

    TextBlock Rotulo(string texto) =>
        new() { Text = texto, Style = (Style)FindResource("Texto.Legenda"), Margin = new Thickness(0, 10, 0, 4), TextWrapping = TextWrapping.Wrap };

    void AdicionarTexto(CampoTexto campo)
    {
        var caixa = new TextBox { Text = campo.Valor, ToolTip = campo.Dica.Length > 0 ? campo.Dica : null };
        if (campo.VariasLinhas)
            (caixa.AcceptsReturn, caixa.TextWrapping, caixa.MinHeight, caixa.MaxHeight, caixa.VerticalScrollBarVisibility) = (true, TextWrapping.Wrap, 90, 180, ScrollBarVisibility.Auto);
        _textos.Add((campo, caixa));
        Corpo.Children.Add(caixa);
    }

    void AdicionarCaixa(CampoCaixa campo)
    {
        var caixa = new CheckBox { Content = campo.Rotulo, IsChecked = campo.Valor, Margin = new Thickness(0, 8, 0, 0) };
        _caixas.Add((campo, caixa));
        Corpo.Children.Add(caixa);
    }

    void AdicionarEscolha(CampoEscolha campo)
    {
        var combo = new ComboBox { IsEditable = true, ItemsSource = campo.Opcoes, Text = campo.Valor };
        _escolhas.Add((campo, combo));
        Corpo.Children.Add(combo);
    }

    void AdicionarOpcoes(CampoOpcoes campo)
    {
        var combo = new ComboBox { ItemsSource = campo.Opcoes, SelectedItem = campo.Opcoes.Contains(campo.Valor) ? campo.Valor : (campo.Opcoes.Count > 0 ? campo.Opcoes[0] : null) };
        _opcoes.Add((campo, combo));
        Corpo.Children.Add(combo);
    }

    void AdicionarInformacao(CampoInformacao campo) =>
        Corpo.Children.Add(new ListBox
        {
            ItemsSource = campo.Linhas,
            MaxHeight = 200,
            Style = (Style)FindResource("Lista.Virtualizada"),
            ItemTemplate = Modelo("<TextBlock xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Text='{Binding}' TextWrapping='Wrap' Margin='2'/>"),
        });

    void AdicionarLista(CampoLista campo)
    {
        _listas.Add(campo);
        Corpo.Children.Add(new ListBox
        {
            ItemsSource = campo.Itens,
            MaxHeight = 240,
            Style = (Style)FindResource("Lista.Virtualizada"),
            ItemTemplate = Modelo("<CheckBox xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' IsChecked='{Binding Marcado, Mode=TwoWay}' Content='{Binding Texto}' Margin='2'/>"),
        });
    }

    static DataTemplate Modelo(string conteudo) =>
        (DataTemplate)System.Windows.Markup.XamlReader.Parse(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" + conteudo + "</DataTemplate>");

    void AoConfirmar(object sender, RoutedEventArgs e)
    {
        if (_textos.Find(t => t.Campo.Obrigatorio && t.Caixa.Text.Trim().Length == 0) is { Caixa: { } vazia })
        {
            vazia.Focus();
            return;
        }

        var resposta = new RespostaDeFormulario();
        foreach (var (campo, caixa) in _textos) resposta.Textos[campo.Chave] = caixa.Text;
        foreach (var (campo, combo) in _escolhas) resposta.Textos[campo.Chave] = combo.Text;
        foreach (var (campo, combo) in _opcoes) resposta.Textos[campo.Chave] = combo.SelectedItem as string ?? "";
        foreach (var (campo, caixa) in _caixas) resposta.Caixas[campo.Chave] = caixa.IsChecked == true;
        foreach (var lista in _listas) resposta.Marcados[lista.Chave] = [.. lista.Itens.Where(i => i.Marcado).Select(i => i.Id)];
        Resultado = resposta;
        Close();
    }
}
