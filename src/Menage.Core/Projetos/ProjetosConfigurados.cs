namespace Menage.Core.Projetos;

/// <summary><see cref="IProjetos"/> de produção: lê a configuração; as marcações valem enquanto o app está aberto.</summary>
public sealed class ProjetosConfigurados : IProjetos
{
    readonly IConfiguracaoDeProjetos _configuracao;
    readonly HashSet<string> _marcados = new(StringComparer.OrdinalIgnoreCase);

    public ProjetosConfigurados(IConfiguracaoDeProjetos configuracao)
    {
        _configuracao = configuracao;
        _configuracao.Alterado += (_, _) => Alterado?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Alterado;

    public IReadOnlyList<Projeto> Todos => _configuracao.Projetos;

    public IReadOnlyList<string> Grupos => [.. _configuracao.Catalogo.Grupos.Select(g => g.Id)];

    public IReadOnlyList<Projeto> Marcados => [.. Todos.Where(p => _marcados.Contains(p.Caminho))];

    public IReadOnlyList<Projeto> DoGrupo(string grupo) =>
        [.. Todos.Where(p => grupo switch
        {
            GruposEspeciais.Todos => true,
            GruposEspeciais.SemGrupo => p.Grupos.Count == 0,
            _ => p.Grupos.Contains(grupo),
        })];

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
