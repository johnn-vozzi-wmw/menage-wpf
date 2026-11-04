namespace Menage.Core.Banco;

/// <summary>O módulo de banco vem depois; a janela principal só conhece este contrato.</summary>
public interface IServicoDeBanco
{
    IReadOnlyList<string> Perfis { get; }

    string PerfilAtual { get; }

    string Estado { get; }

    void Escolher(string perfil);

    Task<Resultado<Nada>> AplicarAsync(CancellationToken cancelar = default);
}

/// <summary>Perfis de mentira para a janela funcionar antes do módulo de banco.</summary>
public sealed class ServicoDeBancoFalso : IServicoDeBanco
{
    public IReadOnlyList<string> Perfis { get; } = ["Local (desenvolvimento)", "Homologação", "Sem banco"];

    public string PerfilAtual { get; private set; } = "Local (desenvolvimento)";

    public string Estado { get; private set; } = "Perfil não aplicado";

    public void Escolher(string perfil)
    {
        PerfilAtual = perfil;
        Estado = "Perfil não aplicado";
    }

    public Task<Resultado<Nada>> AplicarAsync(CancellationToken cancelar = default)
    {
        Estado = $"Aplicado: {PerfilAtual} (simulado)";
        return Task.FromResult(Resultado.Ok());
    }
}
