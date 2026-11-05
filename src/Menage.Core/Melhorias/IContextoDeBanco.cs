namespace Menage.Core.Melhorias;

/// <summary>Perfil de banco como as Melhorias o enxergam (só id, nome e tipo: oracle, postgres ou sqlserver).</summary>
public sealed record PerfilDeBancoRef(string Id, string Nome, string Tipo);

/// <summary>
/// Ponte com o módulo de contexto do banco (outra branch). As Melhorias só guardam ids de perfis; quem lê/escreve o <c>context.xml</c>
/// implementa isto. Sem implementação real, <see cref="SemContextoDeBanco"/> mantém tudo como está.
/// </summary>
public interface IContextoDeBanco
{
    IReadOnlyList<PerfilDeBancoRef> Perfis { get; }

    PerfilDeBancoRef? Achar(string id);

    /// <summary>Banco ativo hoje no context.xml (com o id do perfil salvo equivalente, se houver); nulo se não há conexão reconhecida.</summary>
    PerfilDeBancoRef? AtivoNoContexto();

    Task<Resultado<Nada>> AplicarAsync(string perfilId, CancellationToken cancelar = default);
}

public sealed class SemContextoDeBanco : IContextoDeBanco
{
    public IReadOnlyList<PerfilDeBancoRef> Perfis => [];

    public PerfilDeBancoRef? Achar(string id) => null;

    public PerfilDeBancoRef? AtivoNoContexto() => null;

    public Task<Resultado<Nada>> AplicarAsync(string perfilId, CancellationToken cancelar = default) =>
        Task.FromResult(Resultado.Falha<Nada>("Contexto do banco indisponível", "O módulo de perfis de banco ainda não está ligado.", "Mantenha o banco como está."));
}
