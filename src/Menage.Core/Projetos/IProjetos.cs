namespace Menage.Core.Projetos;

public sealed record Projeto(string Nome, string Caminho, IReadOnlyList<string> Grupos)
{
    public Projeto(string nome, string caminho) : this(nome, caminho, []) { }
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
