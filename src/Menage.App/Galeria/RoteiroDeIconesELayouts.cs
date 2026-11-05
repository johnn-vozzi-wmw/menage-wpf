using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Menage.App.Icones;
using Menage.App.Modulos.JanelaPrincipal;
using Menage.Core.Personalizacao;

namespace Menage.App.Galeria;

/// <summary>
/// Teste em segundo plano (sem mouse nem teclado): gera um PNG e um GIF animado em %TEMP%, grava como ícones personalizados
/// (app, comando, grupo, projeto), confere o que o app passou a mostrar e captura a janela em cada modelo de barra lateral.
/// </summary>
public sealed class RoteiroDeIconesELayouts(IPreferenciasDeAparencia preferencias, JanelaPrincipalViewModel modelo, ServicoDeIcones icones)
{
    readonly List<string> _verificacoes = [];
    Action<string> _salvar = _ => { };

    public async Task<IReadOnlyList<string>> ExecutarAsync(JanelaPrincipal janela, string pasta, Action<string> salvar, Func<int, Task> esperar)
    {
        _salvar = salvar;
        var arquivos = Path.Combine(Path.GetTempPath(), "menage-icones-teste");
        Directory.CreateDirectory(arquivos);
        var png = Gravar(Path.Combine(arquivos, "app.png"), new PngBitmapEncoder(), 1);
        var gif = Gravar(Path.Combine(arquivos, "grupo.gif"), new GifBitmapEncoder(), 4);
        var ruim = Path.Combine(arquivos, "ruim.png");
        await File.WriteAllTextAsync(ruim, "não sou imagem");

        var projeto = modelo.Grade.Visiveis.First();
        preferencias.Alterar(p => p with
        {
            Icones = p.Icones.ComIconeDoApp(IconesPersonalizados.TodosOsTemas, png)
                .ComIconeDe(AlvoDeIcone.Comando, "git.fetch", png)
                .ComIconeDe(AlvoDeIcone.Grupo, "WEB", gif)
                .ComIconeDe(AlvoDeIcone.Projeto, projeto.Caminho, png)
                with { AplicarNoMenuEPilulas = true, AplicarEmGruposEProjetos = true },
        });
        await esperar(600);

        Conferir("ícone do app vale na janela (Window.Icon)", janela.Icon is not null);
        Conferir("o logo do cabeçalho troca o 'M' pela imagem", Imagens(janela).Count(i => i.Source is not null || WpfAnimatedGif.ImageBehavior.GetAnimatedSource(i) is not null) >= 4);
        Conferir("GIF do grupo WEB tem animação carregada", Imagens(janela).Any(i => WpfAnimatedGif.ImageBehavior.GetAnimatedSource(i) is not null));
        Conferir("arquivo inválido não vira imagem (fica o ícone padrão)", icones.Imagem(ruim) is null);
        Conferir("cada arquivo é decodificado uma vez (mesma instância)", ReferenceEquals(icones.Imagem(png), icones.Imagem(png)));
        _salvar("icones-expandida");

        foreach (var m in new[] { ModeloDaBarra.A, ModeloDaBarra.B, ModeloDaBarra.C, ModeloDaBarra.D })
        {
            preferencias.Alterar(p => p with { Barra = m });
            await esperar(500);
            _salvar($"barra-modelo-{m}-expandida");
            janela.AlternarBarra();
            await esperar(600);
            _salvar($"barra-modelo-{m}-recolhida");
            janela.AlternarBarra();
            await esperar(600);
        }

        preferencias.Alterar(p => p with { Icones = p.Icones with { AplicarNoMenuEPilulas = false, AplicarEmGruposEProjetos = false } });
        await esperar(400);
        Conferir("desmarcar as caixas devolve os ícones padrão de grupos e pílulas", icones.Caminho(UsoDoIcone.Grupo, "WEB") is null && icones.Caminho(UsoDoIcone.Comando, "git.fetch") is null);
        Conferir("o ícone do app continua valendo sem as caixas", icones.Caminho(UsoDoIcone.App, null) == png);
        preferencias.Alterar(p => p with { Barra = ModeloDaBarra.A, Icones = new IconesPersonalizados() });
        return _verificacoes;
    }

    void Conferir(string nome, bool ok) => _verificacoes.Add($"{(ok ? "ok" : "FALHOU")}: {nome}");

    static IEnumerable<System.Windows.Controls.Image> Imagens(DependencyObject raiz)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var filho = VisualTreeHelper.GetChild(raiz, i);
            if (filho is System.Windows.Controls.Image { Visibility: Visibility.Visible } imagem && imagem.Parent is IconeOuPadrao) yield return imagem;
            foreach (var neto in Imagens(filho)) yield return neto;
        }
    }

    static string Gravar(string caminho, BitmapEncoder codificador, int quadros)
    {
        for (var q = 0; q < quadros; q++)
        {
            var pixels = new byte[32 * 32 * 4];
            for (var i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = ((byte)(q * 60), (byte)(255 - q * 60), 200, 255);
            codificador.Frames.Add(BitmapFrame.Create(BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgra32, null, pixels, 128)));
        }

        using var fluxo = File.Create(caminho);
        codificador.Save(fluxo);
        return caminho;
    }
}
