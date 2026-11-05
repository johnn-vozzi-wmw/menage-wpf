using System.Windows;
using System.Windows.Controls;
using Menage.Core.Transicoes;
using Menage.App.Tema;

namespace Menage.App.Animacoes;

/// <summary>
/// Área onde as telas (páginas) da janela se revezam. Ligue <see cref="Indice"/> (posição da tela na navegação) e depois
/// <see cref="Destino"/> (a tela): a velha sai e a nova entra, do lado certo conforme a ordem. Pedidos que chegam no meio
/// partem do estado visível, sem pulo (<see cref="AnimadorDeElemento"/>). O conteúdo é trocado uma vez só, no ponto
/// invisível da transição.
/// </summary>
public class HospedeDeTransicao : ContentControl
{
    public static readonly DependencyProperty DestinoProperty =
        DependencyProperty.Register(nameof(Destino), typeof(object), typeof(HospedeDeTransicao),
            new PropertyMetadata(null, (d, e) => ((HospedeDeTransicao)d).Navegar(e.NewValue)));

    public static readonly DependencyProperty IndiceProperty =
        DependencyProperty.Register(nameof(Indice), typeof(int), typeof(HospedeDeTransicao), new PropertyMetadata(0));

    /// <summary>Serviço das transições; a janela (<c>JanelaBase</c>) define e as telas herdam. Sem ele, usa o padrão do sistema.</summary>
    public static readonly DependencyProperty ServicoProperty =
        DependencyProperty.RegisterAttached("Servico", typeof(ServicoDeTransicao), typeof(HospedeDeTransicao),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    public static void SetServico(DependencyObject o, ServicoDeTransicao? v) => o.SetValue(ServicoProperty, v);

    public static ServicoDeTransicao? GetServico(DependencyObject o) => (ServicoDeTransicao?)o.GetValue(ServicoProperty);

    static readonly ServicoDeTransicao Padrao = new();

    AnimadorDeElemento? _animador;
    int _geracao;
    int? _indiceAtual;
    bool _primeira = true;

    public object? Destino
    {
        get => GetValue(DestinoProperty);
        set => SetValue(DestinoProperty, value);
    }

    public int Indice
    {
        get => (int)GetValue(IndiceProperty);
        set => SetValue(IndiceProperty, value);
    }

    ServicoDeTransicao Servico => GetServico(this) ?? Padrao;

    async void Navegar(object? destino)
    {
        var geracao = ++_geracao;
        var sentido = CalculoDeTransicao.Direcao(_indiceAtual, Indice);
        _indiceAtual = Indice;

        if (_primeira || !IsLoaded || Content is null)
        {
            _primeira = false;
            Content = destino;
            return;
        }

        RenderTransformOrigin = new Point(0.5, 0.5);
        _animador ??= Servico.CriarAnimador(this);
        // Sentido da navegação que ainda vale: se outra chegar enquanto esta sai, a de cima usa o seu próprio.
        var saida = CalculoDeTransicao.SaidaDePagina(sentido);
        if (!await _animador.IrAsync(saida, EstadoVisual.Repouso, CalculoDeTransicao.SaidaPaginaMs, Animacao.Suave) || geracao != _geracao) return;

        Content = destino;
        var entrada = CalculoDeTransicao.EntradaDePagina(sentido);
        _animador.Definir(CalculoDeTransicao.Limitar(entrada, Servico.Nivel) with { Opacidade = 0 });
        await _animador.IrAsync(EstadoVisual.Repouso, entrada, CalculoDeTransicao.EntradaPaginaMs, Animacao.Entrada);
    }
}
