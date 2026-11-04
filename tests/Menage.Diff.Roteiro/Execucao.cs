using System.IO;
using System.Windows;
using System.Windows.Threading;
using Menage.App.Modulos.Diff;
using Menage.Core.Diff;
using Menage.TestKit;

namespace Menage.Diff.Roteiro;

/// <summary>Ordem do roteiro: abre as janelas reais sobre o repositório descartável e chama cada parte.</summary>
public sealed class Execucao(string saida, string? so, Dictionary<string, object?> resultado)
{
    bool Parte(string nome) => so is null || so == nome;

    public async Task RodarAsync()
    {
        DemoDiff.InstalarAparencia(false);
        if (Parte("motores")) resultado["motores"] = await new ComparacaoMotores().RodarAsync();
        if (Parte("conflito")) resultado["conflito"] = await new RoteiroConflito(saida).RodarAsync();
        if (!Parte("capturas") && !Parte("medir") && !Parte("mouse")) return;
        // O TestKit bloqueia no git: fora da thread de UI, ou trava esperando o despachante.
        using var repo = await Task.Run(() =>
        {
            var novo = new RepoDeTeste();
            novo.MontarAlteracoes();
            return novo;
        });
        var janela = new JanelaDiff(new ServicoDiff(GitDeTeste.Criar()), repo.Caminho, "grande.java") { Topmost = true, Width = 1600, Height = 960 };
        janela.Show();
        await EsperarArquivo(janela, "grande.java");
        if (Parte("capturas")) await new GaleriaDiff(janela, saida).RodarAsync();
        if (Parte("medir")) resultado["medicao"] = await new MedicaoPintura(janela).RodarAsync();
        if (Parte("mouse")) resultado["mouse"] = await new MouseReal(janela, saida).RodarAsync();
        janela.Close();
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

    public static void Tema(bool escuro) => DemoDiff.InstalarAparencia(escuro);

    public static async Task Tema2(Window janela, bool escuro)
    {
        Tema(escuro);
        janela.UpdateLayout();
        await Esperar(300);
    }
}
