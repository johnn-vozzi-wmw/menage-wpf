using Menage.Core.ModoBeta;

namespace Menage.Core.Estado;

/// <summary>Estado do git mais a versão do pom.xml: tudo o que uma linha da grade mostra.</summary>
public sealed record SituacaoDoProjeto(EstadoDoProjeto Estado, string Versao)
{
    public Projeto Projeto => Estado.Projeto;
}

/// <summary>Leitura local e em paralelo (sem fetch). Pom ilegível ou ausente deixa a versão vazia.</summary>
public sealed class LeitorDeSituacao(LeitorDeEstado estados)
{
    public async Task<IReadOnlyList<SituacaoDoProjeto>> LerTodosAsync(IReadOnlyList<Projeto> projetos, CancellationToken cancelar = default)
    {
        var lidos = await estados.LerTodosAsync(projetos, cancelar);
        return [.. lidos.Select(e => new SituacaoDoProjeto(e, VersaoDoPom(e.Projeto.Caminho)))];
    }

    public static string VersaoDoPom(string repo)
    {
        try
        {
            var pom = Path.Combine(repo, "pom.xml");
            if (!File.Exists(pom)) return "";
            var info = LeitorDePom.Interpretar(LeitorDePom.Ler(pom));
            return info.Versao ?? info.PaiVersao ?? "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
