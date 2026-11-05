using System.Text.Json.Serialization;

namespace Menage.Core.Personalizacao;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ModoDeTema { Sistema, Claro, Escuro }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Densidade { Confortavel, Compacta }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TamanhoDeFonte { Pequeno, Normal, Grande }

/// <summary>A: cartões separados (padrão). B: painel contínuo, sem molduras, com divisórias finas.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ModeloDaBarra { A, B }

/// <summary>
/// Tudo o que o usuário personaliza, em <c>aparencia.json</c>. <see cref="Modo"/> nulo = ainda não escolhido:
/// vale o lado (claro/escuro) do tema salvo em <c>tema.json</c>. <see cref="CorDeDestaque"/> nula = a do tema.
/// <see cref="Atalhos"/>: id do comando para o gesto escolhido; texto vazio = comando sem atalho.
/// </summary>
public sealed record PreferenciasDeAparencia
{
    public const string Nome = "aparencia";

    public ModoDeTema? Modo { get; init; }

    public string? CorDeDestaque { get; init; }

    public Densidade Densidade { get; init; } = Densidade.Confortavel;

    public TamanhoDeFonte Fonte { get; init; } = TamanhoDeFonte.Normal;

    public ModeloDaBarra Barra { get; init; } = ModeloDaBarra.A;

    public bool ReduzirMovimento { get; init; }

    public Dictionary<string, string> Atalhos { get; init; } = [];

    public IconesPersonalizados Icones { get; init; } = new();
}
