using System.Text.RegularExpressions;

namespace Menage.Core.Operacoes.Excluir;

/// <param name="Nomes">Branches ou tags; se não for branch, procura tag.</param>
/// <param name="ConfirmouExtra">Confirmação reforçada, exigida para main/master/develop/release* e para exclusão sem backup.</param>
public sealed record OpcoesDeExclusao(
    IReadOnlyList<string> Nomes, bool Local = true, bool Remoto = false, bool Backup = true, string PrefixoDoBackup = "backup/", bool ConfirmouExtra = false);

public sealed record ResultadoDeExclusao(int Excluidos, IReadOnlyList<string> Backups);

public sealed record PreviaDeExclusao(Projeto Projeto, AlvoDeExclusao Alvo);

/// <summary>Exclui branches e tags (local e/ou origin), com backup e prévia. A branch atual nunca é excluída localmente.</summary>
public sealed partial class ExcluirEmLote(
    IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, ExclusorDeRefs exclusor, ConsultasGit consultas)
    : OperacaoEmLote<OpcoesDeExclusao, ResultadoDeExclusao>(projetos, dialogos, progresso)
{
    public override string Id => "git.excluir-branches";

    public override string Nome => "Excluir branches / tags";

    /// <summary>main, master, develop ou release*: pedem confirmação reforçada.</summary>
    public static bool EhSensivel(string nome) => NomesSensiveis().IsMatch(nome.Trim());

    public static bool ExigeConfirmacaoExtra(OpcoesDeExclusao opcoes) => !opcoes.Backup || opcoes.Nomes.Any(EhSensivel);

    /// <summary>Separa nomes digitados: um por linha, ou separados por vírgula, ponto e vírgula ou espaço.</summary>
    public static IReadOnlyList<string> SepararNomes(string texto) =>
        [.. (texto ?? "").Split(['\r', '\n', ',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal)];

    public async Task<IReadOnlyList<PreviaDeExclusao>> ListarAsync(IEnumerable<Projeto> alvo, OpcoesDeExclusao opcoes, CancellationToken cancelar = default)
    {
        var lista = new List<PreviaDeExclusao>();
        foreach (var projeto in alvo)
        {
            if (!ConsultasGit.EhRepositorio(projeto.Caminho))
            {
                lista.AddRange(opcoes.Nomes.Select(n => new PreviaDeExclusao(projeto, new AlvoDeExclusao(n) { Erro = "pasta do projeto não encontrada" })));
                continue;
            }

            var origin = opcoes.Remoto ? await exclusor.LerOriginAsync(projeto.Caminho, cancelar) : null;
            var atual = await consultas.BranchAtualAsync(projeto.Caminho, cancelar);
            foreach (var nome in opcoes.Nomes)
                lista.Add(new(projeto, await exclusor.AvaliarAsync(projeto.Caminho, nome, opcoes.Local, opcoes.Remoto, origin, atual, cancelar)));
        }

        return lista;
    }

    protected override Erro? Validar(OpcoesDeExclusao opcoes, IReadOnlyList<Projeto> alvo) =>
        opcoes.Nomes.Count == 0 ? new Erro("Informe ao menos um nome de branch ou tag")
        : !opcoes.Local && !opcoes.Remoto ? new Erro("Escolha onde excluir", null, "Marque Local e/ou Remoto.")
        : ExigeConfirmacaoExtra(opcoes) && !opcoes.ConfirmouExtra ? new Erro("Confirmação reforçada necessária", "Branch sensível (main, master, develop, release*) ou exclusão sem backup.", "Confirme no diálogo.")
        : base.Validar(opcoes, alvo);

    protected override async Task<Previa?> MontarPreviaAsync(OpcoesDeExclusao opcoes, IReadOnlyList<Projeto> alvo, CancellationToken cancelar)
    {
        var itens = await ListarAsync(alvo, opcoes, cancelar);
        var detalhes = itens.Select(i => $"{i.Projeto.Nome}: {Descrever(i.Alvo)}").ToList();
        var texto = opcoes.Backup ? $"Cada item é copiado para {opcoes.PrefixoDoBackup}<nome>-<data> antes de ser apagado." : "SEM backup: o conteúdo apagado só volta pelo reflog.";
        return new Previa("Excluir branches / tags", texto, detalhes);
    }

    protected override async Task<ResultadoDoProjeto<ResultadoDeExclusao>> ExecutarEmProjeto(Projeto projeto, OpcoesDeExclusao opcoes, CancellationToken cancelar)
    {
        var alvos = (await ListarAsync([projeto], opcoes, cancelar)).Select(i => i.Alvo).ToList();
        var backup = new OpcoesDeBackup(opcoes.Backup, opcoes.PrefixoDoBackup, DateTime.Now);
        var partes = new List<string>();
        var backups = new List<string>();
        var (excluidos, falhas, avisos) = (0, 0, 0);
        foreach (var alvo in alvos)
        {
            if (alvo.Erro.Length > 0) { falhas++; partes.Add($"{alvo.Nome} - {alvo.Erro}"); continue; }
            if (alvo.Tipo == TipoDeRefAlvo.Nenhum || alvo.Nada) { avisos++; partes.Add($"{alvo.Nome} - {alvo.Aviso}"); continue; }

            var feito = await exclusor.ExcluirAsync(projeto.Caminho, alvo, backup, cancelar);
            backups.AddRange(feito.Backups);
            if (feito.Falhou) falhas++; else excluidos++;
            partes.Add(Texto(alvo, feito));
        }

        var dados = new ResultadoDeExclusao(excluidos, backups);
        var mensagem = string.Join("; ", partes);
        return falhas > 0 ? ResultadoDoProjeto<ResultadoDeExclusao>.Falha(mensagem, null, "Veja a Saída.") with { Dados = dados }
            : excluidos == 0 || avisos > 0 ? ResultadoDoProjeto<ResultadoDeExclusao>.Aviso(mensagem, dados)
            : ResultadoDoProjeto<ResultadoDeExclusao>.Ok(dados, mensagem);
    }

    static string Texto(AlvoDeExclusao alvo, ResultadoDaExclusao feito)
    {
        var onde = feito.Feito.Count == 0 ? "nada foi excluído" : "excluída (" + string.Join(" + ", feito.Feito) + ")";
        var backup = feito.Backups.Count > 0 ? $" | backup: {string.Join(", ", feito.Backups)} (restaurar: git switch -c {alvo.Nome} {feito.Backups[0]})" : "";
        return $"{alvo.Rotulo} - {onde}{backup}" + (feito.Detalhe.Length > 0 ? $" | {feito.Detalhe}" : "");
    }

    static string Descrever(AlvoDeExclusao a) =>
        a.Erro.Length > 0 ? $"{a.Nome} - {a.Erro}"
        : a.Tipo == TipoDeRefAlvo.Nenhum || a.Nada ? $"{a.Nome} - {a.Aviso}"
        : $"{a.Rotulo} - excluir {string.Join(" + ", new[] { a.ExcluirLocal ? "local" : "", a.ExcluirRemoto ? "remoto" : "" }.Where(x => x.Length > 0))}";

    [GeneratedRegex("^(main|master|develop|release.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NomesSensiveis();
}
