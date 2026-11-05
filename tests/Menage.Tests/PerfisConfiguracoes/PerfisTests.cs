using Menage.Core.Operacoes.Decoradores;
using Menage.Core.Perfis;

namespace Menage.Tests.PerfisConfiguracoes;

public sealed class PerfisTests : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "pf" + Guid.NewGuid().ToString("N")[..6]);

    ServicoDePerfis Criar() => new(new PastaDeDados(_pasta));

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    [Fact]
    public void Sem_arquivo_o_perfil_e_o_Geral_com_tudo_liberado_menos_o_que_e_de_dev()
    {
        var s = Criar();

        s.Ativo.Id.Should().Be("completo");
        s.Atual.Should().Be(PerfilDeUso.Geral);
        s.Permite("push").Should().BeTrue();
        s.Permite("melhorias").Should().BeFalse();
        s.Permite("diario").Should().BeFalse();
        s.Permite("beta-auto").Should().BeFalse();
    }

    [Fact]
    public void Tester_padrao_bloqueia_push_e_deploy_mas_tem_beta_automatica()
    {
        var s = Criar();
        s.Ativar("tester").EhOk.Should().BeTrue();

        s.Atual.Should().Be(PerfilDeUso.Tester);
        s.Permite("fetch").Should().BeTrue();
        s.Permite("push").Should().BeFalse();
        s.Permite("gerador-deploy").Should().BeFalse();
        s.Permite("beta-auto").Should().BeTrue();
        s.Permite("melhorias").Should().BeFalse();
        s.Motivo("push").Should().Contain("Bloqueado no perfil Tester").And.Contain("Geral").And.Contain("Desenvolvedor");
    }

    [Fact]
    public void Desenvolvedor_libera_tudo_e_menos_id_tira_um_item()
    {
        var s = Criar();
        s.Ativar("dev");
        s.Permite("melhorias").Should().BeTrue();

        var dev = s.Obter("dev")!;
        s.Salvar(dev with { Permitidas = ["*", "-gerador-deploy"] }).EhOk.Should().BeTrue();

        s.Permite("gerador-deploy").Should().BeFalse();
        s.Permite("push").Should().BeTrue();
    }

    [Fact]
    public void Perfil_ativo_e_edicoes_sobrevivem_a_reabrir_o_app()
    {
        var s = Criar();
        var criado = s.Salvar(new PerfilDefinido("", "Revisor", "Só olha", PerfilDeUso.Tester, ["fetch", "status"])).Valor;
        s.Ativar(criado.Id);

        var novo = Criar();

        novo.Ativo.Nome.Should().Be("Revisor");
        novo.Permite("status").Should().BeTrue();
        novo.Permite("push").Should().BeFalse();
        novo.Atual.Should().Be(PerfilDeUso.Tester);
    }

    [Fact]
    public void Criar_exige_nome_unico_e_funcionalidades_validas()
    {
        var s = Criar();

        s.Salvar(new PerfilDefinido("", "  ", "", PerfilDeUso.Geral, ["*"])).EhOk.Should().BeFalse();
        s.Salvar(new PerfilDefinido("", "tester", "", PerfilDeUso.Geral, ["*"])).Erro.Causa.Should().Contain("Já existe");
        s.Salvar(new PerfilDefinido("", "Novo", "", PerfilDeUso.Geral, ["voar"])).Erro.Causa.Should().Contain("desconhecida");
        s.Perfis.Should().HaveCount(3);
    }

    [Fact]
    public void Duplicar_copia_as_regras_com_nome_livre_e_o_original_fica()
    {
        var s = Criar();

        var copia = s.Duplicar("tester").Valor;
        var outra = s.Duplicar("tester").Valor;

        copia.Nome.Should().Be("Tester (cópia)");
        outra.Nome.Should().Be("Tester (cópia) (2)");
        copia.Id.Should().NotBe("tester");
        copia.Permitidas.Should().BeEquivalentTo(s.Obter("tester")!.Permitidas);
        copia.Embutido.Should().BeFalse();
    }

    [Fact]
    public void Excluir_so_perfil_criado_e_o_ativo_excluido_volta_para_Geral()
    {
        var s = Criar();
        var meu = s.Duplicar("dev").Valor;
        s.Ativar(meu.Id);

        s.Excluir("tester").EhOk.Should().BeFalse();
        s.Excluir("completo").EhOk.Should().BeFalse();
        s.Excluir(meu.Id).EhOk.Should().BeTrue();

        s.Ativo.Id.Should().Be("completo");
        s.Obter(meu.Id).Should().BeNull();
    }

    [Fact]
    public void Geral_nao_edita_e_padrao_do_Tester_pode_ser_restaurado()
    {
        var s = Criar();
        s.Salvar(PerfilDefinido.Geral with { Nome = "Outro" }).EhOk.Should().BeFalse();

        s.Salvar(s.Obter("tester")! with { Permitidas = ["*"] });
        s.Obter("tester")!.Permite("push").Should().BeTrue();
        s.RestaurarPadrao("tester");

        s.Obter("tester")!.Permite("push").Should().BeFalse();
    }

    [Fact]
    public void Exportar_e_importar_leva_os_perfis_como_novos_sem_repetir_nome()
    {
        var origem = Criar();
        var meu = origem.Salvar(new PerfilDefinido("", "Revisor", "Só olha", PerfilDeUso.Tester, ["fetch", "status"])).Valor;
        var arquivo = Path.Combine(_pasta, "perfis-exportados.json");
        origem.Exportar([meu.Id, "completo"], arquivo).EhOk.Should().BeTrue();

        var destino = new ServicoDePerfis(new PastaDeDados(Path.Combine(_pasta, "outro")));
        destino.Importar(arquivo).Valor.Should().Be(1);
        destino.Importar(arquivo).Valor.Should().Be(1);

        destino.Perfis.Select(p => p.Nome).Should().Contain(["Revisor", "Revisor (2)"]);
        destino.Perfis.First(p => p.Nome == "Revisor").Permite("status").Should().BeTrue();
        destino.Perfis.Select(p => p.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Importar_arquivo_invalido_nao_grava_nada_e_explica()
    {
        var s = Criar();
        Directory.CreateDirectory(_pasta);
        var lixo = Path.Combine(_pasta, "lixo.json");
        File.WriteAllText(lixo, "{ \"formato\": \"outra-coisa\" }");
        var ruim = Path.Combine(_pasta, "ruim.json");
        File.WriteAllText(ruim, "{ \"formato\": \"menage-perfis\", \"versao\": 1, \"perfis\": [ { \"nome\": \"X\", \"base\": 0, \"permitidas\": [\"voar\"] } ] }");

        s.Importar(lixo).EhOk.Should().BeFalse();
        s.Importar(ruim).Erro.Causa.Should().Contain("inválido");
        s.Importar(Path.Combine(_pasta, "nao-existe.json")).EhOk.Should().BeFalse();
        s.Perfis.Should().HaveCount(3);
    }

    [Fact]
    public void Avisa_quem_depende_quando_o_perfil_muda()
    {
        var s = Criar();
        var avisos = 0;
        s.Alterado += (_, _) => avisos++;

        s.Ativar("dev");
        s.Duplicar("dev");

        avisos.Should().Be(2);
    }

    [Theory]
    [InlineData("git.fetch", "fetch")]
    [InlineData("git.stash-pop", "stash-pop")]
    [InlineData("git.criar-branch", "nova-branch")]
    [InlineData("ferramentas.tag-em-massa", "tag-massa")]
    [InlineData("mais.divergencia", "diagnostico")]
    [InlineData("git.inexistente", null)]
    public void Operacao_vira_funcionalidade_do_catalogo(string operacao, string? esperada) =>
        Funcionalidades.DaOperacao(operacao).Should().Be(esperada);

    [Fact]
    public async Task Executor_recusa_operacao_que_o_perfil_editado_bloqueia()
    {
        var s = Criar();
        s.Ativar("tester");
        var projetos = new ProjetosEmMemoria([new Projeto("x", _pasta)]);
        projetos.Marcar(_pasta, true);
        var executor = new ExecutorComPerfil(new ExecutorDeOperacoes(), s, s);

        var recusada = await executor.ExecutarAsync(new OperacaoFalsa("git.push", projetos), 0);
        var liberada = await executor.ExecutarAsync(new OperacaoFalsa("git.fetch", projetos), 0);

        recusada.EhOk.Should().BeFalse();
        liberada.EhOk.Should().BeTrue();
    }

    sealed class OperacaoFalsa : OperacaoEmLote<int, int>
    {
        readonly string _id;

        public OperacaoFalsa(string id, IProjetos projetos) : base(projetos, new DialogosFalsos(), new ProgressoNulo()) => _id = id;

        public override string Id => _id;

        public override string Nome => _id;

        protected override Task<ResultadoDoProjeto<int>> ExecutarEmProjeto(Projeto projeto, int opcoes, CancellationToken cancelar) =>
            Task.FromResult(ResultadoDoProjeto<int>.Ok(opcoes));
    }
}
