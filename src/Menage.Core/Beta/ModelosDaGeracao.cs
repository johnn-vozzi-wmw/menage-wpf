namespace Menage.Core.Beta;

public enum FaseDaGeracao { NaoSelecionado, NaFila, Executando, Repetindo, Ok, Erro, Interrompido, NaoExecutado }

/// <summary>Um projeto pronto para rodar: pasta e JDK já resolvidos, comando final e se a pasta target é apagada antes.</summary>
public sealed record PlanoDeProjeto(
    ProjetoDoGerador Projeto, string Pasta, string Branch, string JavaHome, string Comando, bool LimparTarget, bool ComandoPersonalizado)
{
    public string Nome => Projeto.Nome;
}

public sealed record PedidoDeGeracao(
    IReadOnlyList<PlanoDeProjeto> Planos, bool Deploy, string PastaDeLogs, string MavenBin, string Java8Home, bool PararNoPrimeiroErro = false);

/// <summary>Situação de um projeto durante a geração. <c>Inicio</c> permite à tela calcular o tempo corrido sem polling.</summary>
public sealed record StatusDoProjeto(
    string Projeto, FaseDaGeracao Estado, TimeSpan Tempo = default, string Resumo = "", string? Log = null, DateTime? Inicio = null)
{
    public bool Terminou => Estado is FaseDaGeracao.Ok or FaseDaGeracao.Erro or FaseDaGeracao.Interrompido or FaseDaGeracao.NaoExecutado;
}

public sealed record ResumoDaGeracao(IReadOnlyList<StatusDoProjeto> Projetos, string PastaDeLogs, bool Interrompida)
{
    public int Oks => Projetos.Count(p => p.Estado == FaseDaGeracao.Ok);

    public int Falhas => Projetos.Count(p => p.Estado is FaseDaGeracao.Erro or FaseDaGeracao.Interrompido);

    public int NaoExecutados => Projetos.Count(p => p.Estado == FaseDaGeracao.NaoExecutado);
}
