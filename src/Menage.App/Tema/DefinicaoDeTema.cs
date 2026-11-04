using System.Windows.Media;

namespace Menage.App.Tema;

/// <summary>Um tema: identidade, par claro/escuro e as cores de cada <see cref="PapelDeCor"/>.</summary>
/// <param name="Emblema">Reservado para o logo próprio do tema (ex.: "bandeira" no Nacional).</param>
public sealed record DefinicaoDeTema(
    string Id, string Nome, string Par, bool Escuro, IReadOnlyList<Color> Cores, string? Emblema = null, bool Privado = false)
{
    public Color this[PapelDeCor papel] => Cores[(int)papel];
}
