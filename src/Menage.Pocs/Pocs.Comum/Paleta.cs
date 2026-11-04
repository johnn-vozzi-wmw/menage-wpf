using System.Windows.Media;

namespace Menage.Pocs.Comum;

/// <summary>Par de cores de destaque: uma para o tema claro, outra para o escuro.</summary>
public record Paleta(string Nome, Color Claro, Color Escuro)
{
    public static Paleta Hex(string nome, string claro, string escuro) => new(nome,
        (Color)ColorConverter.ConvertFromString(claro), (Color)ColorConverter.ConvertFromString(escuro));
}

public sealed class CatalogoPaletas
{
    public IReadOnlyList<Paleta> Todas { get; } =
    [
        Paleta.Hex("Azul", "#1F6FD1", "#5AA2FF"), Paleta.Hex("Verde", "#1E8E4E", "#4CD38A"),
        Paleta.Hex("Laranja", "#D9680F", "#FF9A4D"), Paleta.Hex("Roxo", "#7B3FC4", "#B58BFF"),
        Paleta.Hex("Rosa", "#C2307A", "#FF7DB8"), Paleta.Hex("Turquesa", "#0E8C8C", "#4FD6D6"),
        Paleta.Hex("Vermelho", "#C62F2F", "#FF7070"), Paleta.Hex("Amarelo", "#B58A00", "#FFD23F"),
        Paleta.Hex("Indigo", "#3B4CCA", "#8C9BFF"), Paleta.Hex("Lima", "#6A9A12", "#B4E24D"),
        Paleta.Hex("Marrom", "#8B5A2B", "#D2A06B"), Paleta.Hex("Grafite", "#4F5B66", "#A9B7C4")
    ];
}
