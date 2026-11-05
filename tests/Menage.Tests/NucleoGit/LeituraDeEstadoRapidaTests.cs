using Menage.Core.Estado;

namespace Menage.Tests.NucleoGit;

/// <summary>C2: a leitura do estado de cada projeto deve gastar poucos processos git (cada um custa dezenas de ms no Windows).</summary>
public sealed class LeituraDeEstadoRapidaTests : NucleoGitBase
{
    sealed class GitContador(IGit interno) : IGit
    {
        public int Chamadas { get; private set; }

        public Task<SaidaProcesso> ExecutarBrutoAsync(GitArgs args, CancellationToken cancelar = default)
        {
            Chamadas++;
            return interno.ExecutarBrutoAsync(args, cancelar);
        }

        public Task<Resultado<SaidaProcesso>> ExecutarAsync(GitArgs args, CancellationToken cancelar = default)
        {
            Chamadas++;
            return interno.ExecutarAsync(args, cancelar);
        }
    }

    [Fact]
    public async Task Ler_um_projeto_usa_um_unico_processo_git()
    {
        var contador = new GitContador(GitExec);
        var leitor = new LeitorDeEstado(contador);

        var estado = await leitor.LerAsync(Projeto);

        estado.Branch.Should().Be("main");
        estado.TemUpstream.Should().BeTrue();
        contador.Chamadas.Should().Be(1);
    }

    [Fact]
    public async Task Detecta_rebase_e_merge_em_andamento_sem_processo_extra()
    {
        Repo.Rodar("checkout", "-b", "feat");
        Repo.Commitar("README.md", "feat\n", "feat");
        Repo.Rodar("checkout", "main");
        Repo.Commitar("README.md", "main\n", "main");
        var merge = await GitExec.ExecutarBrutoAsync(Git.Em(Repo.Caminho).Comando("merge").Argumento("feat"));
        merge.Sucesso.Should().BeFalse();
        var contador = new GitContador(GitExec);

        var estado = await new LeitorDeEstado(contador).LerAsync(Projeto);

        estado.Operacao.Should().Be("merge em andamento");
        contador.Chamadas.Should().Be(1);
    }

    [Fact]
    public void Interpreta_o_status_porcelain_v2()
    {
        const string saida = "# branch.oid 1234567890abcdef\n# branch.head feature/x\n# branch.upstream origin/feature/x\n# branch.ab +2 -3\n"
            + "1 .M N... 100644 100644 100644 a b README.md\n2 R. N... 100644 100644 100644 a b R100 novo.txt\torig.txt\nu UU N... 1 2 3 4 a b c f.txt\n? x.txt\n? y/\n! ignorado.log\n";

        var s = StatusPorcelainV2.Interpretar(saida);

        (s.Branch, s.Detached, s.Head, s.TemUpstream, s.Ahead, s.Behind).Should().Be(("feature/x", false, "", true, 2, 3));
        (s.Alteracoes, s.NaoVersionados).Should().Be((3, 2));
    }

    [Fact]
    public void Detached_e_upstream_sumido_nao_tem_ab()
    {
        var s = StatusPorcelainV2.Interpretar("# branch.oid abcdef1234567890\n# branch.head (detached)\n");
        (s.Detached, s.Branch, s.Head, s.TemUpstream).Should().Be((true, "", "abcdef1", false));

        var sumido = StatusPorcelainV2.Interpretar("# branch.oid abc\n# branch.head main\n# branch.upstream origin/main\n");
        (sumido.TemUpstream, sumido.Ahead, sumido.Behind).Should().Be((false, 0, 0));
    }
}
