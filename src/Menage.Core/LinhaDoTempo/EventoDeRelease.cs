namespace Menage.Core.LinhaDoTempo;

public enum EstadoDoEvento { Ok, Erro, SemResultado }

public enum TipoDeEvento { Beta, Tag }

/// <summary>
/// Uma linha da linha do tempo: uma geração do Gerador de Beta (um por pasta de logs) ou uma tag
/// (a mesma tag no mesmo horário em vários projetos vira um evento só).
/// </summary>
/// <param name="Ambiente">Pasta base onde a beta foi gerada (ex.: <c>dev1</c>); vazio nas tags.</param>
/// <param name="Projetos">Nomes dos projetos, para o filtro de texto.</param>
public sealed record EventoDeRelease(
    DateTime Data, TipoDeEvento Tipo, string Titulo, string Detalhe, string Ambiente, EstadoDoEvento Estado, IReadOnlyList<string> Projetos)
{
    public bool EhTag => Tipo == TipoDeEvento.Tag;
}

/// <summary>O que a tela pede à linha do tempo.</summary>
public sealed record FiltroDeReleases(string Ambiente = "", string Texto = "", bool Betas = true, bool Tags = true)
{
    public const string TodosOsAmbientes = "";

    public IReadOnlyList<EventoDeRelease> Aplicar(IEnumerable<EventoDeRelease> eventos)
    {
        var termos = Texto.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return
        [
            .. eventos
                .Where(e => e.EhTag ? Tags : Betas)
                .Where(e => Ambiente.Length == 0 || e.EhTag || e.Ambiente.Equals(Ambiente, StringComparison.OrdinalIgnoreCase))
                .Where(e => termos.All(t => Casa(e, t)))
                .OrderByDescending(e => e.Data),
        ];
    }

    static bool Casa(EventoDeRelease e, string termo) =>
        e.Titulo.Contains(termo, StringComparison.CurrentCultureIgnoreCase)
        || e.Detalhe.Contains(termo, StringComparison.CurrentCultureIgnoreCase)
        || e.Ambiente.Contains(termo, StringComparison.CurrentCultureIgnoreCase)
        || e.Projetos.Any(p => p.Contains(termo, StringComparison.CurrentCultureIgnoreCase));
}
