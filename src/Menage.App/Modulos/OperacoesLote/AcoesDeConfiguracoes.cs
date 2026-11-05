using Menage.App.Modulos.JanelaPrincipal;
using Menage.App.Modulos.OperacoesLote.Formulario;
using Menage.App.Tema;
using Menage.Core.Interacao;
using Menage.Core.Protecao;
using Menage.Core.Saida;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Modulos.OperacoesLote;

/// <summary>
/// Configurações básicas: proteção de branches, convenção de commit, fetch automático e tema. O perfil de uso ainda não tem
/// onde ser guardado (hoje é fixo) e fica de fora.
/// </summary>
public sealed class AcoesDeConfiguracoes(
    AcoesGit acoes, IServicoDeJanelas janelas, IServicoDeDialogos dialogos, ProtecaoDeBranches protecao,
    IServiceProvider servicos, IServicoDeTema tema, ICatalogoDeTemas temas, SaidaDeOperacoes saida)
{
    // Resolvida na hora: a janela principal depende do catálogo, que depende deste módulo (ciclo se fosse injetada).
    JanelaPrincipalViewModel Janela => servicos.GetRequiredService<JanelaPrincipalViewModel>();

    public Task AbrirAsync() => acoes.Proteger(async () =>
    {
        var atual = protecao.Atual;
        var nomesDosTemas = temas.Todos.Select(t => t.Nome).ToList();
        var pedido = new PedidoDeFormulario("Configurações", "Valem só dentro do Menage: nenhum hook é instalado nos repositórios.",
        [
            new CampoCaixa("bloquearPush", "Proteção de branches: bloquear push", atual.BloquearPush),
            new CampoCaixa("bloquearExclusao", "Proteção de branches: bloquear exclusão", atual.BloquearExclusao),
            new CampoTexto("branches", "Branches protegidas (uma por linha; aceita * como curinga, ex.: release*)", string.Join(Environment.NewLine, atual.Branches), VariasLinhas: true),
            new CampoCaixa("validarCommit", "Convenção de commit: exigir tipo (feat:, fix(escopo):...) ou chave de ticket", atual.ValidarCommit),
            new CampoTexto("tipos", "Tipos aceitos (separados por vírgula)", string.Join(", ", atual.Tipos)),
            new CampoCaixa("ticket", "Aceitar também chave de ticket (ex.: VENDAS-123: ...)", atual.AceitarTicket),
            new CampoCaixa("fetchAuto", "Fetch automático em segundo plano (a cada 5 minutos)", Janela.FetchAutomatico),
            new CampoOpcoes("tema", "Tema", nomesDosTemas, tema.Atual.Nome),
        ], "Salvar", Altura: 700);
        if (await janelas.AbrirDialogoAsync(JanelaDeFormulario.Id, pedido) is not RespostaDeFormulario r) return;

        var branches = r.Texto("branches").Split(['\r', '\n', ',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
        var tipos = r.Texto("tipos").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
        var config = atual with
        {
            BloquearPush = r.Caixa("bloquearPush"),
            BloquearExclusao = r.Caixa("bloquearExclusao"),
            Branches = branches.Length > 0 ? branches : ConfigDeProtecao.BranchesPadrao,
            ValidarCommit = r.Caixa("validarCommit"),
            Tipos = tipos.Length > 0 ? tipos : ConfigDeProtecao.TiposPadrao,
            AceitarTicket = r.Caixa("ticket"),
        };
        var salvou = protecao.Salvar(config);
        if (!salvou.EhOk)
        {
            await dialogos.MostrarErroAsync(salvou.Erro);
            return;
        }

        Janela.FetchAutomatico = r.Caixa("fetchAuto");
        if (temas.Todos.FirstOrDefault(t => t.Nome == r.Texto("tema")) is { } escolhido && escolhido.Id != tema.Atual.Id) tema.Trocar(escolhido.Id);
        saida.Escrever("Configurações salvas.", TipoDeLinha.Ok);
    });
}
