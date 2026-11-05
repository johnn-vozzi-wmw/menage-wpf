using Menage.Core.Armazenamento;
using Menage.Core.Beta;
using Menage.Core.Personalizacao;
using Menage.Core.Projetos;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.Core.LinhaDoTempo;

/// <summary>Junta as betas geradas pelo Gerador (logs) e as tags dos projetos marcados numa só linha do tempo. Só lê.</summary>
public sealed class HistoricoDeReleases(PreparadorDeGeracao gerador, RepositorioDoGerador repositorio, LeitorDeTags tags)
{
    public async Task<IReadOnlyList<EventoDeRelease>> CarregarAsync(IEnumerable<Projeto> marcados, CancellationToken cancelar = default)
    {
        var betas = await Task.Run(() => LeitorDeBetas.Ler(gerador.PastaDosLogs, repositorio.Configuracao.PastasBase), cancelar);
        var etiquetas = await tags.LerAsync(marcados, cancelar: cancelar);
        return [.. betas.Concat(etiquetas).OrderByDescending(e => e.Data)];
    }

    public static IReadOnlyList<string> Ambientes(IEnumerable<EventoDeRelease> eventos) =>
        [.. eventos.Where(e => e.Ambiente.Length > 0).Select(e => e.Ambiente).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
}

public static class PersonalizacaoEReleasesExtensions
{
    /// <summary>Preferências de aparência (<c>aparencia.json</c>) e Histórico de releases. O App registra as telas.</summary>
    public static IServiceCollection AddPersonalizacaoEReleases(this IServiceCollection servicos) =>
        servicos
            .AddSingleton<IArmazenamento<PreferenciasDeAparencia>>(sp =>
                new Armazenamento<PreferenciasDeAparencia>(sp.GetRequiredService<PastaDeDados>(), PreferenciasDeAparencia.Nome, padrao: () => new PreferenciasDeAparencia()))
            .AddSingleton<IPreferenciasDeAparencia, ServicoDePreferencias>()
            .AddSingleton<LeitorDeTags>()
            .AddSingleton<HistoricoDeReleases>();
}
