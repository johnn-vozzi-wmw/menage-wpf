using System.Windows.Input;
using Menage.App.Controles;

namespace Menage.App.Modulos.Personalizacao;

/// <summary>Só liga a tela ao <see cref="JanelaDePersonalizacaoViewModel"/>; a captura da tecla é a única lógica de tela.</summary>
public partial class JanelaDePersonalizacao : JanelaBase
{
    public const string Id = "personalizacao.abrir";

    public JanelaDePersonalizacao(ContextoDeJanela contexto, JanelaDePersonalizacaoViewModel modelo) : base(contexto)
    {
        Modelo = modelo;
        DataContext = modelo;
        FechaComEsc = true;
        InitializeComponent();
    }

    public JanelaDePersonalizacaoViewModel Modelo { get; }

    void AoCapturar(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) return;
        e.Handled = true;
        if (ServicoDeAtalhos.De(e) is { } gesto)
        {
            Captura.Text = gesto.ToString();
            Modelo.DefinirAtalho(gesto);
        }
    }

    void AoDigitarCor(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Modelo.AplicarCampoDeCorCommand.Execute(null);
        e.Handled = true;
    }
}
