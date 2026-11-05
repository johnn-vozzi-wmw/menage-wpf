using System.Windows;

namespace Menage.App.Controles;

/// <summary>Cores semânticas que um controle pode assumir; cada uma é um pincel do tema.</summary>
public enum TomDeCor { Destaque, Neutro, Azul, Verde, Amarelo, Laranja, Vermelho, GrupoWeb, GrupoFw, GrupoApp, GrupoTc }

public static class TonsDeCor
{
    public static string Chave(TomDeCor tom) => tom == TomDeCor.Neutro ? "Tema.TextoFraco" : $"Tema.{tom}";

    /// <summary>Liga a propriedade ao pincel do tom por referência dinâmica: acompanha a troca de tema.</summary>
    public static void Ligar(FrameworkElement elemento, DependencyProperty cor, TomDeCor tom) =>
        elemento.SetResourceReference(cor, Chave(tom));
}
