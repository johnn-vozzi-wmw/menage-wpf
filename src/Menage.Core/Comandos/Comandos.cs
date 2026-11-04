namespace Menage.Core.Comandos;

/// <summary>Uma ação do usuário. Pílulas, menus e a paleta Ctrl+K leem a mesma lista de comandos.</summary>
public interface IComando
{
    string Id { get; }

    string Texto { get; }

    string? Icone { get; }

    /// <summary>Ex.: "Ctrl+B"; nulo se não tem atalho.</summary>
    string? Atalho { get; }

    /// <summary>Grupo em menus e na paleta (ex.: "Git", "Beta").</summary>
    string Grupo { get; }

    /// <summary>Perfis de uso em que o comando aparece e executa.</summary>
    IReadOnlyCollection<PerfilDeUso> PerfisQueLiberam { get; }

    bool PodeExecutar();

    Task ExecutarAsync(CancellationToken cancelar = default);
}

public interface ICatalogoDeComandos
{
    IReadOnlyList<IComando> Todos { get; }

    /// <summary>Lança se o id já existe: dois módulos com o mesmo id é erro de programação.</summary>
    void Adicionar(IComando comando);

    IComando? Obter(string id);

    /// <summary>Comandos liberados para o perfil, em ordem de registro.</summary>
    IReadOnlyList<IComando> Disponiveis(PerfilDeUso perfil);

    /// <summary>Busca da paleta: texto ou grupo contém todas as palavras digitadas.</summary>
    IReadOnlyList<IComando> Buscar(string texto, PerfilDeUso perfil);
}

/// <summary>Cada módulo (feature folder) se registra uma vez; o app percorre todos os módulos na partida.</summary>
public interface IModulo
{
    string Nome { get; }

    void Registrar(ICatalogoDeComandos catalogo);
}

/// <summary>Comando definido por delegates; cobre quase todos os casos sem criar uma classe por comando.</summary>
public sealed record Comando(
    string Id,
    string Texto,
    Func<CancellationToken, Task> Executar,
    string Grupo = "Geral",
    string? Icone = null,
    string? Atalho = null,
    IReadOnlyCollection<PerfilDeUso>? Perfis = null,
    Func<bool>? Habilitado = null) : IComando
{
    public IReadOnlyCollection<PerfilDeUso> PerfisQueLiberam => Perfis ?? ConjuntosDePerfis.Todos;

    public bool PodeExecutar() => Habilitado?.Invoke() ?? true;

    public Task ExecutarAsync(CancellationToken cancelar = default) => Executar(cancelar);
}
