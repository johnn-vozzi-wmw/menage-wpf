using System.IO;
using Menage.App.Modulos.Diff;

namespace Menage.Diff.Roteiro;

/// <summary>Capturas do diff em claro e escuro, tiradas da tela de verdade.</summary>
public sealed class GaleriaDiff(JanelaDiff janela, string saida)
{
    public async Task RodarAsync()
    {
        var painel = janela.PainelDoDiff;
        foreach (var escuro in new[] { false, true })
        {
            var tema = escuro ? "escuro" : "claro";
            Execucao.Tema(escuro);
            await Mostrar("grande.java");
            Salvar($"diff-{tema}-grande");
            painel.Navegar(1);
            painel.Navegar(1);
            await Execucao.Esperar(150);
            Salvar($"diff-{tema}-navegacao-pulso");
            await Mostrar("invisivel.txt");
            Salvar($"diff-{tema}-invisivel-ignorado");
            painel.DefinirChaves(recolher: true, ignorar: false);
            await Execucao.Esperar(700);
            Salvar($"diff-{tema}-invisivel-etiquetas");
            painel.DefinirChaves(recolher: true, ignorar: true);
            foreach (var arquivo in new[] { "so-direita.java", "excluido.txt", "renomeado.txt", "logo.bin", "acentos1252.txt" })
            {
                await Mostrar(arquivo);
                Salvar($"diff-{tema}-{Path.GetFileNameWithoutExtension(arquivo)}");
            }
            janela.Animador.Alternar();
            await Execucao.Esperar(500);
            await Mostrar("grande.java");
            Salvar($"diff-{tema}-lista-recolhida");
            janela.Animador.Alternar();
            await Execucao.Esperar(500);
        }
        Execucao.Tema(false);
        await Mostrar("grande.java");
    }

    async Task Mostrar(string arquivo)
    {
        await janela.RecarregarAsync(arquivo);
        await Execucao.Esperar(900);
    }

    void Salvar(string nome) => Tela.Salvar(Tela.Capturar(Tela.AreaDe(janela)), Path.Combine(saida, nome + ".png"));
}
