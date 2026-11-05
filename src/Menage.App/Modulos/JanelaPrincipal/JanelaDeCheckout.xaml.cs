using System.Windows;
using Menage.App.Controles;
using Menage.Core.Operacoes.Checkout;

namespace Menage.App.Modulos.JanelaPrincipal;

public partial class JanelaDeCheckout : JanelaBase, IJanelaComParametro, IJanelaComResultado
{
    public JanelaDeCheckout(ContextoDeJanela contexto) : base(contexto) => InitializeComponent();

    public object? Resultado { get; private set; }

    public void Receber(object? parametro)
    {
        if (parametro is not PedidoDeCheckout pedido) return;
        Branch.ItemsSource = pedido.Sugestoes;
        Resumo.Text = $"Projetos: {string.Join(", ", pedido.Projetos)}";
        Loaded += (_, _) => Branch.Focus();
    }

    void AoConfirmar(object sender, RoutedEventArgs e)
    {
        if (Branch.Text.Trim().Length == 0) return;
        Resultado = new OpcoesDeCheckout(Branch.Text.Trim(), AutoStash.IsChecked == true);
        Close();
    }
}
