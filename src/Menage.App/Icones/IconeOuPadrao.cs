using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using WpfAnimatedGif;

namespace Menage.App.Icones;

/// <summary>
/// Mostra o ícone personalizado do <see cref="Uso"/>/<see cref="Id"/> ou, se não houver, o conteúdo padrão (o "M", o símbolo, a bolinha).
/// Escuta só o <see cref="ServicoDeIcones.Alterado"/> enquanto está na tela. GIF anima por WpfAnimatedGif e fica no primeiro
/// quadro quando "reduzir movimento" está ligado.
/// </summary>
[ContentProperty(nameof(Padrao))]
public sealed class IconeOuPadrao : Grid
{
    public static readonly DependencyProperty UsoProperty = DependencyProperty.Register(
        nameof(Uso), typeof(UsoDoIcone), typeof(IconeOuPadrao), new PropertyMetadata(UsoDoIcone.App, (d, _) => ((IconeOuPadrao)d).Atualizar()));

    public static readonly DependencyProperty IdProperty = DependencyProperty.Register(
        nameof(Id), typeof(string), typeof(IconeOuPadrao), new PropertyMetadata(null, (d, _) => ((IconeOuPadrao)d).Atualizar()));

    readonly Image _imagem = new() { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed, SnapsToDevicePixels = true };
    UIElement? _padrao;

    public IconeOuPadrao()
    {
        RenderOptions.SetBitmapScalingMode(_imagem, BitmapScalingMode.HighQuality);
        Children.Add(_imagem);
        Loaded += (_, _) => { if (ServicoDeIcones.Compartilhado is { } s) s.Alterado += Atualizar; Atualizar(); };
        Unloaded += (_, _) => { if (ServicoDeIcones.Compartilhado is { } s) s.Alterado -= Atualizar; };
    }

    public UsoDoIcone Uso { get => (UsoDoIcone)GetValue(UsoProperty); set => SetValue(UsoProperty, value); }

    public string? Id { get => (string?)GetValue(IdProperty); set => SetValue(IdProperty, value); }

    /// <summary>Sem conteúdo padrão (ex.: ícone extra do projeto), some quando não há ícone personalizado.</summary>
    public bool OcultarSemIcone { get; set; }

    public UIElement? Padrao
    {
        get => _padrao;
        set
        {
            if (_padrao is not null) Children.Remove(_padrao);
            _padrao = value;
            if (value is not null) Children.Insert(0, value);
        }
    }

    void Atualizar()
    {
        var servico = ServicoDeIcones.Compartilhado;
        var caminho = servico?.Caminho(Uso, Id);
        var origem = caminho is null ? null : servico!.Imagem(caminho);

        ImageBehavior.SetAnimatedSource(_imagem, null);
        _imagem.Source = null;
        if (origem is not null)
        {
            if (Menage.Core.Personalizacao.IconesPersonalizados.EhGif(caminho!))
            {
                ImageBehavior.SetAutoStart(_imagem, !servico!.ReduzirMovimento);
                ImageBehavior.SetAnimatedSource(_imagem, origem);
            }
            else _imagem.Source = origem;
        }

        _imagem.Visibility = origem is null ? Visibility.Collapsed : Visibility.Visible;
        if (_padrao is not null) _padrao.Visibility = origem is null ? Visibility.Visible : Visibility.Collapsed;
        else if (OcultarSemIcone) Visibility = origem is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
