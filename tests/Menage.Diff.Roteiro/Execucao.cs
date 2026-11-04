using System.IO;
using System.Windows;
using System.Windows.Threading;
using Menage.App.Modulos.Diff;
using Menage.Core.Diff;
using Menage.TestKit;

namespace Menage.Diff.Roteiro;

/// <summary>Ordem do roteiro: abre as janelas reais sobre o repositório descartável e chama cada parte.</summary>
public sealed class Execucao(string saida, string? so, Dictionary<string, object?> resultado, string? filtro = null, bool escuro = false)
{
    bool Parte(string nome) => so is null || so == nome;

    public async Task RodarAsync()
    {
        DemoDiff.InstalarAparencia(escuro);
        if (Parte("motores")) resultado["motores"] = await new ComparacaoMotores().RodarAsync();
        if (Parte("demo")) resultado["demo"] = await MontarDemoAsync();
        if (Parte("conflito")) resultado["conflito"] = await new RoteiroConflito(saida, escuro).RodarAsync();
        var mouseReal = so == "mouse-real"; // nunca por padrão: toma o cursor e o foco do usuário
        if (!Parte("capturas") && !Parte("medir") && !Parte("arraste") && !mouseReal) return;
        // O TestKit bloqueia no git: fora da thread de UI, ou trava esperando o despachante.
        using var repo = await Task.Run(() =>
        {
            var novo = new RepoDeTeste();
            novo.MontarAlteracoes();
            return novo;
        });
        var janela = new JanelaDiff(new ServicoDiff(GitDeTeste.Criar()), repo.Caminho, "grande.java") { Width = 1600, Height = 960 };
        if (mouseReal) janela.Topmost = true;
        else Tela.EsconderDoUsuario(janela);
        janela.Show();
        await EsperarArquivo(janela, "grande.java");
        if (Parte("capturas")) await new GaleriaDiff(janela, saida, escuro).RodarAsync();
        if (Parte("medir")) resultado["medicao"] = await new MedicaoPintura(janela).RodarAsync();
        if (Parte("arraste")) resultado["arraste"] = await new ArrasteFundo(janela, saida, filtro).RodarAsync();
        if (mouseReal) resultado["mouse"] = await new MouseReal(janela, saida, filtro).RodarAsync();
        janela.Close();
    }

    /// <summary>Monta as janelas de <c>--diff</c> e <c>--conflito</c> pelo mesmo caminho do app, sem exibir: confere os argumentos e a injeção de dependência.</summary>
    static async Task<object> MontarDemoAsync()
    {
        using var repo = await Task.Run(() => new RepoDeTeste());
        var diff = DemoDiff.Criar(["--diff", repo.Caminho, "a.txt"]);
        var conflito = DemoDiff.Criar(["--conflito", repo.Caminho]);
        var ignorado = DemoDiff.Criar(["--outra-coisa"]);
        return new { diff = diff?.GetType().Name, conflito = conflito?.GetType().Name, semArgumento = ignorado is null };
    }

    public static async Task EsperarArquivo(JanelaDiff janela, string arquivo)
    {
        for (var i = 0; i < 200 && janela.ListaDeArquivos.Selecionado?.Caminho != arquivo; i++) await Esperar(30);
        await Esperar(900);
    }

    public static async Task Esperar(int ms)
    {
        await Task.Delay(ms);
        await Dispatcher.Yield(DispatcherPriority.Render);
    }
}
