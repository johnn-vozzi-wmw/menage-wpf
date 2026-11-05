using System.Windows.Controls;
using Menage.App.Controles;

namespace Menage.App.Galeria;

/// <summary>Segunda janela da galeria: prova a troca de tema em todas as abertas e o serviço de janelas.</summary>
public sealed class JanelaDeExemplo : JanelaBase, IJanelaComParametro, IJanelaComResultado
{
    public const string Id = "galeria.exemplo";

    readonly TextBlock _texto = new() { Margin = new System.Windows.Thickness(16) };

    public JanelaDeExemplo(ContextoDeJanela contexto) : base(contexto)
    {
        Title = "Janela de exemplo";
        Width = 480;
        Height = 340;
        ShowActivated = false;
        ShowInTaskbar = false;
        Content = _texto;
    }

    public object? Resultado { get; private set; }

    public void Receber(object? parametro) => _texto.Text = parametro?.ToString();
}
