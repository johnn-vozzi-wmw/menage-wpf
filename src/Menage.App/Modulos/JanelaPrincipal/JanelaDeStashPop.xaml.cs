using System.Windows;
using Menage.App.Controles;
using Menage.Core.Operacoes.Stash;

namespace Menage.App.Modulos.JanelaPrincipal;

public sealed class LinhaDeStashPop(EntradaDeStash origem)
{
    public EntradaDeStash Entrada { get; } = origem;

    public bool Marcado { get; set; }

    public string Texto => $"{Entrada.Projeto.Nome} · {Entrada.Mensagem} ({Entrada.Data})";
}

public partial class JanelaDeStashPop : JanelaBase, IJanelaComParametro, IJanelaComResultado
{
    List<LinhaDeStashPop> _linhas = [];

    public JanelaDeStashPop(ContextoDeJanela contexto) : base(contexto) => InitializeComponent();

    public object? Resultado { get; private set; }

    public void Receber(object? parametro)
    {
        if (parametro is not PedidoDeStashPop pedido) return;
        _linhas = [.. pedido.Entradas.Select(e => new LinhaDeStashPop(e))];
        Lista.ItemsSource = _linhas;
    }

    void AoConfirmar(object sender, RoutedEventArgs e)
    {
        var escolhidas = _linhas.Where(l => l.Marcado).Select(l => l.Entrada).ToList();
        if (escolhidas.Count == 0) return;
        Resultado = new OpcoesDeStashPop(escolhidas, Manter.IsChecked == true);
        Close();
    }
}
