using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Messaging;
using Menage.App.Tema;
using Menage.Core.Personalizacao;

namespace Menage.App.Icones;

public enum UsoDoIcone { App, Comando, Grupo, Projeto }

/// <summary>
/// Resolve e carrega os ícones personalizados. Velocidade: cada arquivo é decodificado uma única vez
/// (chave = caminho + data + tamanho), em 96 px de largura, congelado (<c>Freeze</c>, compartilhável sem custo) e guardado
/// num dicionário concorrente; GIF fica no tamanho original porque quem anima é a biblioteca. Arquivo ausente ou
/// inválido devolve nulo e o ícone padrão continua valendo. O cache é só de imagens; <see cref="Compartilhado"/>
/// existe para os controles, que o XAML cria sem injeção.
/// </summary>
public sealed class ServicoDeIcones
{
    const int LarguraDeDecodificacao = 96;

    /// <summary>Largura da decodificação usada pelo quadro de edição (zoom até 200% sem borrar).</summary>
    public const int LarguraGrande = 400;

    readonly IPreferenciasDeAparencia _preferencias;
    readonly IServicoDeTema _tema;
    readonly ConcurrentDictionary<string, (long Marca, ImageSource? Imagem)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ServicoDeIcones(IPreferenciasDeAparencia preferencias, IServicoDeTema tema, IMessenger mensageiro)
    {
        (_preferencias, _tema) = (preferencias, tema);
        preferencias.Alterado += _ => Alterado?.Invoke();
        mensageiro.Register<ServicoDeIcones, TemaMudou>(this, static (r, _) => r.Alterado?.Invoke());
    }

    public static ServicoDeIcones? Compartilhado { get; set; }

    /// <summary>Preferências de ícones ou o tema mudaram: os controles se refazem.</summary>
    public event Action? Alterado;

    public bool ReduzirMovimento => _preferencias.Valor.ReduzirMovimento;

    public string? Caminho(UsoDoIcone uso, string? id)
    {
        var icones = _preferencias.Valor.Icones;
        return uso switch
        {
            UsoDoIcone.App => icones.DoApp(_tema.Atual.Id),
            UsoDoIcone.Comando when id is not null => icones.De(AlvoDeIcone.Comando, id),
            UsoDoIcone.Grupo when id is not null => icones.De(AlvoDeIcone.Grupo, id),
            UsoDoIcone.Projeto when id is not null => icones.De(AlvoDeIcone.Projeto, id),
            _ => null,
        };
    }

    /// <summary>Zoom (fator, 1 = 100%) do ícone. Só o ícone do app tem zoom; vale como transformação, sem nova decodificação.</summary>
    public double Zoom(UsoDoIcone uso) => uso == UsoDoIcone.App ? _preferencias.Valor.Icones.ZoomDoApp(_tema.Atual.Id) / 100.0 : 1.0;

    public ImageSource? Imagem(string caminho, int largura = LarguraDeDecodificacao)
    {
        FileInfo arquivo;
        try { arquivo = new FileInfo(caminho); if (!arquivo.Exists) return null; }
        catch (Exception) { return null; }

        var marca = arquivo.LastWriteTimeUtc.Ticks ^ arquivo.Length;
        var chave = largura == LarguraDeDecodificacao ? caminho : $"{largura}|{caminho}";
        if (_cache.TryGetValue(chave, out var guardado) && guardado.Marca == marca) return guardado.Imagem;

        var imagem = Decodificar(caminho, largura);
        _cache[chave] = (marca, imagem);
        return imagem;
    }

    static BitmapImage? Decodificar(string caminho, int largura)
    {
        try
        {
            // Pelo fluxo: o arquivo não fica preso (o usuário pode trocá-lo ou apagá-lo) e um arquivo inválido falha aqui, sem vazar alça.
            using var fluxo = File.OpenRead(caminho);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (!IconesPersonalizados.EhGif(caminho)) bmp.DecodePixelWidth = largura;
            bmp.StreamSource = fluxo;
            bmp.EndInit();
            bmp.Freeze();
            return IconesPersonalizados.EhGif(caminho) ? ParaAnimar(caminho) : bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // A biblioteca de GIF relê os quadros pelo UriSource; só chega aqui um GIF que já foi decodificado com sucesso acima.
    static BitmapImage ParaAnimar(string caminho)
    {
        var gif = new BitmapImage();
        gif.BeginInit();
        gif.CacheOption = BitmapCacheOption.OnLoad;
        gif.UriSource = new Uri(caminho, UriKind.Absolute);
        gif.EndInit();
        gif.Freeze();
        return gif;
    }
}
