namespace Menage.Core.Operacoes.Fetch;

/// <summary>Módulo modelo: registra o comando que roda a operação pelo executor (com os decoradores).</summary>
public sealed class FetchModulo(FetchEmLote fetch, IExecutorDeOperacoes executor) : IModulo
{
    public string Nome => "Fetch";

    public void Registrar(ICatalogoDeComandos catalogo) =>
        catalogo.Adicionar(new Comando(
            fetch.Id, "Fetch dos projetos marcados",
            cancelar => executor.ExecutarAsync(fetch, new OpcoesDeFetch(), new SinaisDoLote(cancelar, default)),
            Grupo: "Git", Icone: "ArrowSync", Atalho: "Ctrl+F"));
}
