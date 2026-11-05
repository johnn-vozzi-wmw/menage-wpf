namespace Menage.Core.Repos;

public enum TipoDeRef { BranchLocal, BranchEmOrigin, Tag }

/// <param name="Ref">O que passar ao git.</param>
public sealed record RefResolvida(string Ref, TipoDeRef Tipo)
{
    public string Descricao => Tipo switch
    {
        TipoDeRef.BranchLocal => "branch local",
        TipoDeRef.BranchEmOrigin => "branch em origin",
        _ => "tag",
    };
}
