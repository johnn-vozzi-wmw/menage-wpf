namespace Menage.Core.Operacoes.Decoradores;

/// <summary>Diz se uma branch está protegida contra operações destrutivas.</summary>
public interface IRegrasDeProtecao
{
    bool Protegida(string branch);

    /// <summary>Branch que não pode receber push (respeita a chave "bloquear push" da configuração).</summary>
    bool ProtegidaParaPush(string branch) => Protegida(branch);

    /// <summary>Branch que não pode ser excluída (respeita a chave "bloquear exclusão").</summary>
    bool ProtegidaParaExclusao(string branch) => Protegida(branch);
}

/// <summary>Padrões de branch protegida: nome exato ou com <c>*</c> (ex.: <c>release*</c>).</summary>
public sealed class RegrasDeProtecao(IEnumerable<string> padroes) : IRegrasDeProtecao
{
    readonly List<System.Text.RegularExpressions.Regex> _regras =
        [.. padroes.Select(p => new System.Text.RegularExpressions.Regex(
            "^" + System.Text.RegularExpressions.Regex.Escape(p).Replace("\\*", ".*") + "$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase))];

    public static RegrasDeProtecao Padrao { get; } = new(Protecao.ConfigDeProtecao.BranchesPadrao);

    public bool Protegida(string branch) => _regras.Exists(r => r.IsMatch(branch));
}

/// <summary>Antes de uma operação destrutiva, recusa se algum projeto alvo está numa branch protegida.</summary>
public sealed class ExecutorComProtecao(IExecutorDeOperacoes interno, IGit git, IRegrasDeProtecao regras) : IExecutorDeOperacoes
{
    public async Task<Resultado<ResumoDoLote<TResultado>>> ExecutarAsync<TOpcoes, TResultado>(
        OperacaoEmLote<TOpcoes, TResultado> operacao, TOpcoes opcoes, SinaisDoLote sinais = default)
    {
        if (operacao.Destrutiva)
        {
            var protegidos = await ProjetosProtegidosAsync(operacao.ProjetosAlvo(opcoes), sinais.Abortar);
            if (protegidos.Count > 0)
                return Resultado.Falha<ResumoDoLote<TResultado>>(new Erro(
                    $"'{operacao.Nome}' bloqueada pela proteção de branches",
                    "Branch protegida em: " + string.Join(", ", protegidos),
                    "Mude de branch nesses projetos ou ajuste a proteção."));
        }

        return await interno.ExecutarAsync(operacao, opcoes, sinais);
    }

    async Task<List<string>> ProjetosProtegidosAsync(IReadOnlyList<Projeto> alvo, CancellationToken cancelar)
    {
        var protegidos = new List<string>();
        foreach (var projeto in alvo)
        {
            var args = Git.Em(projeto.Caminho).Comando("rev-parse").Opcao("--abbrev-ref").Argumento("HEAD");
            var saida = await git.ExecutarBrutoAsync(args, cancelar);
            var branch = saida.Texto.Trim();
            if (saida.Sucesso && regras.Protegida(branch)) protegidos.Add($"{projeto.Nome} ({branch})");
        }

        return protegidos;
    }
}
