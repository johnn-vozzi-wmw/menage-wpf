using System.Windows.Input;
using Menage.App.Controles;

namespace Menage.App.Modulos.Personalizacao;

/// <summary>Só liga a tela ao <see cref="JanelaDePersonalizacaoViewModel"/>; a captura da tecla é a única lógica de tela.</summary>
public partial class JanelaDePersonalizacao : JanelaBase
{
    public const string Id = "personalizacao.abrir";

    public JanelaDePersonalizacao(ContextoDeJanela contexto, JanelaDePersonalizacaoViewModel modelo, IconesViewModel icones) : base(contexto)
    {
        Modelo = modelo;
        Icones = icones;
        icones.EscolherArquivo = EscolherImagem;
        DataContext = modelo;
        FechaComEsc = true;
        InitializeComponent();
    }

    public JanelaDePersonalizacaoViewModel Modelo { get; }

    public IconesViewModel Icones { get; }

    string? EscolherImagem()
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog { Title = "Escolher ícone", Filter = "Imagens e GIF|*.png;*.jpg;*.jpeg;*.bmp;*.ico;*.gif" };
        return dialogo.ShowDialog(this) == true ? dialogo.FileName : null;
    }

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
