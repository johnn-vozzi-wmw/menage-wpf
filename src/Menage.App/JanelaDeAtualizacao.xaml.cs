using System.Windows;

namespace Menage.App;

public enum EscolhaDeAtualizacao { Depois, Instalar, Ignorar }

public partial class JanelaDeAtualizacao : Window
{
    public EscolhaDeAtualizacao Escolha { get; private set; } = EscolhaDeAtualizacao.Depois;

    public event Action? EscolhaFeita;

    public JanelaDeAtualizacao(string versaoNova)
    {
        InitializeComponent();
        Mensagem.Text = $"Há uma versão nova do Menage: {versaoNova}.";
    }

    /// <summary>Troca os botões pela barra de progresso enquanto baixa.</summary>
    public void MostrarProgresso(int percentual)
    {
        Botoes.Visibility = Visibility.Collapsed;
        Barra.Visibility = Visibility.Visible;
        Barra.Value = percentual;
        Mensagem.Text = $"Baixando a atualização... {percentual}%";
    }

    private void Instalar_Click(object sender, RoutedEventArgs e) => Escolher(EscolhaDeAtualizacao.Instalar);
    private void Depois_Click(object sender, RoutedEventArgs e) => Escolher(EscolhaDeAtualizacao.Depois);
    private void Ignorar_Click(object sender, RoutedEventArgs e) => Escolher(EscolhaDeAtualizacao.Ignorar);

    private void Escolher(EscolhaDeAtualizacao escolha)
    {
        Escolha = escolha;
        if (escolha == EscolhaDeAtualizacao.Instalar) { MostrarProgresso(0); EscolhaFeita?.Invoke(); }
        else Close();
    }
}
