namespace Menage.Core.Perfis;

public enum PerfilDeUso { Geral, Desenvolvedor, Tester }

public interface IPerfilAtual
{
    PerfilDeUso Atual { get; }
}

public sealed class PerfilFixo(PerfilDeUso perfil = PerfilDeUso.Geral) : IPerfilAtual
{
    public PerfilDeUso Atual { get; } = perfil;
}

public static class ConjuntosDePerfis
{
    public static IReadOnlyCollection<PerfilDeUso> Todos { get; } = [PerfilDeUso.Geral, PerfilDeUso.Desenvolvedor, PerfilDeUso.Tester];

    public static IReadOnlyCollection<PerfilDeUso> Somente(params PerfilDeUso[] perfis) => perfis;
}
