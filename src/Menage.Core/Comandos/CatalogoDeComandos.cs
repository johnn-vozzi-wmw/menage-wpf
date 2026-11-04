namespace Menage.Core.Comandos;

public sealed class CatalogoDeComandos : ICatalogoDeComandos
{
    readonly List<IComando> _comandos = [];

    public IReadOnlyList<IComando> Todos => _comandos;

    /// <summary>Monta o catálogo pedindo a cada módulo que se registre.</summary>
    public static CatalogoDeComandos Montar(IEnumerable<IModulo> modulos)
    {
        var catalogo = new CatalogoDeComandos();
        foreach (var modulo in modulos) modulo.Registrar(catalogo);
        return catalogo;
    }

    public void Adicionar(IComando comando)
    {
        if (Obter(comando.Id) is not null)
            throw new InvalidOperationException($"Já existe um comando com o id '{comando.Id}'.");
        _comandos.Add(comando);
    }

    public IComando? Obter(string id) => _comandos.Find(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<IComando> Disponiveis(PerfilDeUso perfil) =>
        [.. _comandos.Where(c => c.PerfisQueLiberam.Contains(perfil))];

    public IReadOnlyList<IComando> Buscar(string texto, PerfilDeUso perfil)
    {
        var palavras = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return [.. Disponiveis(perfil).Where(c => palavras.All(p => Casa(c, p)))];
    }

    static bool Casa(IComando comando, string palavra) =>
        comando.Texto.Contains(palavra, StringComparison.CurrentCultureIgnoreCase) ||
        comando.Grupo.Contains(palavra, StringComparison.CurrentCultureIgnoreCase);
}
