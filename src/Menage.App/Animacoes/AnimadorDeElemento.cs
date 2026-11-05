using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Menage.Core.Transicoes;

namespace Menage.App.Animacoes;

/// <summary>
/// Anima UM elemento entre <see cref="EstadoVisual"/>s: só <c>Opacity</c> e <c>RenderTransform</c> (translação + escala),
/// por tempo, nunca layout. Um pedido novo cancela o anterior e parte do estado visível naquele instante (sem pulo), com
/// a duração proporcional ao que falta (<see cref="CalculoDeTransicao.Planejar"/>). Durante a animação o elemento fica
/// em <see cref="BitmapCache"/>, que sai ao terminar para o texto voltar a ser desenhado nítido.
/// </summary>
public sealed class AnimadorDeElemento
{
    readonly FrameworkElement _alvo;
    readonly Func<NivelDeMovimento> _nivel;
    readonly bool _vertical;
    readonly TranslateTransform _deslocamento = new();
    readonly ScaleTransform _escala = new(1, 1);
    int _versao;
    TaskCompletionSource<bool>? _pendente;

    public AnimadorDeElemento(FrameworkElement alvo, Func<NivelDeMovimento> nivel, bool vertical = false)
    {
        (_alvo, _nivel, _vertical) = (alvo, nivel, vertical);
        var grupo = new TransformGroup();
        grupo.Children.Add(_escala);
        grupo.Children.Add(_deslocamento);
        alvo.RenderTransform = grupo;
    }

    DependencyProperty EixoDoDeslocamento => _vertical ? TranslateTransform.YProperty : TranslateTransform.XProperty;

    /// <summary>O que o usuário está vendo agora (inclui o valor animado em andamento).</summary>
    public EstadoVisual Atual => new(_alvo.Opacity, _vertical ? _deslocamento.Y : _deslocamento.X, _escala.ScaleX);

    /// <summary>Põe o elemento num estado sem animar, cancelando qualquer transição em curso.</summary>
    public void Definir(EstadoVisual estado)
    {
        Cancelar();
        Aplicar(estado);
    }

    /// <summary>
    /// Vai ao <paramref name="alvo"/> a partir do estado atual. Devolve true se chegou lá, false se outro pedido tomou o lugar.
    /// </summary>
    public Task<bool> IrAsync(EstadoVisual alvo, EstadoVisual origem, int duracaoCheiaMs, IEasingFunction ease)
    {
        var passo = CalculoDeTransicao.Planejar(Atual, alvo, origem, duracaoCheiaMs, _nivel());
        Cancelar();
        if (passo.Instantaneo)
        {
            Aplicar(passo.Para);
            return Task.FromResult(true);
        }

        var versao = _versao;
        var tcs = _pendente = new TaskCompletionSource<bool>();
        Aplicar(passo.De);
        _alvo.CacheMode = new BitmapCache(VisualTreeHelper.GetDpi(_alvo).DpiScaleX);

        var duracao = new Duration(passo.Duracao);
        var opacidade = Criar(passo.De.Opacidade, passo.Para.Opacidade, duracao, ease);
        opacidade.Completed += (_, _) =>
        {
            if (versao != _versao) return;
            _pendente = null;
            Limpar();
            Aplicar(passo.Para);
            tcs.TrySetResult(true);
        };
        _alvo.BeginAnimation(UIElement.OpacityProperty, opacidade);
        if (Muda(passo.De.Deslocamento, passo.Para.Deslocamento))
            _deslocamento.BeginAnimation(EixoDoDeslocamento, Criar(passo.De.Deslocamento, passo.Para.Deslocamento, duracao, ease));
        if (Muda(passo.De.Escala, passo.Para.Escala))
        {
            var escala = Criar(passo.De.Escala, passo.Para.Escala, duracao, ease);
            _escala.BeginAnimation(ScaleTransform.ScaleXProperty, escala);
            _escala.BeginAnimation(ScaleTransform.ScaleYProperty, escala);
        }

        return tcs.Task;
    }

    static bool Muda(double de, double para) => Math.Abs(de - para) > 0.0001;

    static DoubleAnimation Criar(double de, double para, Duration duracao, IEasingFunction ease) =>
        new(de, para, duracao) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };

    /// <summary>Para tudo mantendo o estado visível congelado como valor local (nada de salto), avisando quem esperava.</summary>
    void Cancelar()
    {
        var visivel = Atual;
        _versao++;
        Limpar();
        Aplicar(visivel);
        var pendente = _pendente;
        _pendente = null;
        pendente?.TrySetResult(false);
    }

    void Limpar()
    {
        _alvo.BeginAnimation(UIElement.OpacityProperty, null);
        _deslocamento.BeginAnimation(EixoDoDeslocamento, null);
        _escala.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _escala.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _alvo.CacheMode = null;
    }

    void Aplicar(EstadoVisual estado)
    {
        _alvo.Opacity = estado.Opacidade;
        if (_vertical) _deslocamento.Y = estado.Deslocamento;
        else _deslocamento.X = estado.Deslocamento;
        _escala.ScaleX = _escala.ScaleY = estado.Escala;
    }
}
