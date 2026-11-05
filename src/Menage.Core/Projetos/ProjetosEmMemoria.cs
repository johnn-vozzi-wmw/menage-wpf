namespace Menage.Core.Projetos;

/// <summary>Implementação simples (sem disco). A de produção, que lê a configuração, nasce em <c>nucleo-git</c>.</summary>
public sealed class ProjetosEmMemoria(IEnumerable<Projeto>? projetos = null) : IProjetos
{
    readonly List<Projeto> _todos = [.. projetos ?? []];
    readonly HashSet<string> _marcados = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? Alterado;

    public IReadOnlyList<Projeto> Todos => _todos;

    public IReadOnlyList<string> Grupos => [.. _todos.SelectMany(p => p.Grupos).Distinct(StringComparer.OrdinalIgnoreCase).Order()];

    public IReadOnlyList<Projeto> Marcados => [.. _todos.Where(p => _marcados.Contains(p.Caminho))];

    public IReadOnlyList<Projeto> DoGrupo(string grupo) =>
        [.. _todos.Where(p => grupo == GruposEspeciais.Todos || p.Grupos.Contains(grupo, StringComparer.OrdinalIgnoreCase))];

    public void Adicionar(Projeto projeto)
    {
        _todos.Add(projeto);
        Alterado?.Invoke(this, EventArgs.Empty);
    }

    public void Marcar(string caminho, bool marcado)
    {
        if (marcado ? _marcados.Add(caminho) : _marcados.Remove(caminho))
            Alterado?.Invoke(this, EventArgs.Empty);
    }

    public void MarcarGrupo(string grupo, bool marcado)
    {
        foreach (var projeto in DoGrupo(grupo))
            if (marcado) _marcados.Add(projeto.Caminho);
            else _marcados.Remove(projeto.Caminho);
        Alterado?.Invoke(this, EventArgs.Empty);
    }
}
