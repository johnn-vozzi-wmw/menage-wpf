namespace Menage.Core.Projetos;

public sealed record Projeto(string Nome, string Caminho, IReadOnlyList<string> Grupos)
{
    public Projeto(string nome, string caminho) : this(nome, caminho, []) { }

    /// <summary>Grupo que define cor e valores herdados; <see cref="GruposEspeciais.SemGrupo"/> se não há nenhum.</summary>
    public string GrupoPrincipal { get; init; } = GruposEspeciais.SemGrupo;

    /// <summary>Família técnica do modo beta (<see cref="Familias"/>); vazia se o projeto não pertence a nenhuma.</summary>
    public string Familia { get; init; } = "";

    public bool EhFramework => Nome.StartsWith("framework", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Projetos conhecidos, seus grupos e os que o usuário marcou na grade.</summary>
public interface IProjetos
{
    IReadOnlyList<Projeto> Todos { get; }

    IReadOnlyList<string> Grupos { get; }

    IReadOnlyList<Projeto> Marcados { get; }

    IReadOnlyList<Projeto> DoGrupo(string grupo);

    void Marcar(string caminho, bool marcado);

    void MarcarGrupo(string grupo, bool marcado);

    event EventHandler? Alterado;
}
