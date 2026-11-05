using Menage.Core;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Menage.App.Animacoes;
using Menage.App.Tema;
using Menage.Core.Personalizacao;

namespace Menage.App.Modulos.Personalizacao;

/// <summary>Avisa as telas que dependem da aparência (ex.: barra lateral) de que as preferências mudaram.</summary>
public sealed record AparenciaMudou(PreferenciasDeAparencia Preferencias);

/// <summary>O que toca os recursos do WPF (fonte, densidade). Separado para o serviço ser testado sem janela.</summary>
public interface IAplicadorDeMedidas
{
    void Aplicar(PreferenciasDeAparencia preferencias);
}

public sealed class AplicadorDeMedidasWpf : IAplicadorDeMedidas
{
    /// <summary>Troca os tokens no nível da aplicação: eles valem antes dos dicionários mesclados e quem usa DynamicResource acompanha.</summary>
    public void Aplicar(PreferenciasDeAparencia preferencias)
    {
        var recursos = Application.Current.Resources;
        recursos["Fonte.Legenda"] = MedidasDeAparencia.Fonte(12, preferencias.Fonte);
        recursos["Fonte.Corpo"] = MedidasDeAparencia.Fonte(14, preferencias.Fonte);
        recursos["Fonte.Subtitulo"] = MedidasDeAparencia.Fonte(16, preferencias.Fonte);
        recursos["Fonte.Titulo"] = MedidasDeAparencia.Fonte(20, preferencias.Fonte);
        recursos["Altura.Controle"] = MedidasDeAparencia.AlturaDoControle(preferencias.Densidade) * Math.Max(1, MedidasDeAparencia.Escala(preferencias.Fonte));
        recursos["Altura.Linha"] = MedidasDeAparencia.AlturaDaLinha(preferencias.Densidade, preferencias.Fonte);
        recursos["Margem.Cartao"] = new Thickness(MedidasDeAparencia.MargemDoCartao(preferencias.Densidade));
    }
}

/// <summary>
/// Põe as preferências para valer ao vivo, sem reiniciar: modo do tema (claro, escuro, sistema), cor de destaque,
/// fonte, densidade e "reduzir movimento". A barra lateral escuta <see cref="AparenciaMudou"/>; os atalhos são lidos a cada tecla.
/// </summary>
public sealed class ServicoDeAparencia(
    IPreferenciasDeAparencia preferencias, IServicoDeTema tema, ICatalogoDeTemas catalogo, AjusteDeDestaque destaque,
    ServicoDeTransicao transicao, IDetectorDoSistema sistema, IAplicadorDeMedidas medidas, IMessenger mensageiro)
{
    string? _destaqueAplicado;
    bool _trocando;

    public PreferenciasDeAparencia Atual => preferencias.Valor;

    /// <summary>Aplica o que está salvo (sem animar) e passa a acompanhar mudanças. Depois de <see cref="IServicoDeTema.Iniciar"/>.</summary>
    public void Iniciar()
    {
        preferencias.Alterado += p => Aplicar(p, animar: true);
        sistema.Mudou += () => Aplicar(preferencias.Valor, animar: true);
        mensageiro.Register<ServicoDeAparencia, TemaMudou>(this, static (r, _) => r.AoTrocarDeTema());
        Aplicar(preferencias.Valor, animar: false);
    }

    public Resultado<Nada> Alterar(Func<PreferenciasDeAparencia, PreferenciasDeAparencia> mudanca) => preferencias.Alterar(mudanca);

    /// <summary>Lado do par (claro/escuro) que as preferências pedem agora.</summary>
    public bool PedeEscuro(PreferenciasDeAparencia p) => MedidasDeAparencia.UsaEscuro(p.Modo, sistema.Escuro, tema.Escolhido.Escuro);

    void Aplicar(PreferenciasDeAparencia p, bool animar)
    {
        medidas.Aplicar(p);
        transicao.ReduzirMovimento = p.ReduzirMovimento;

        var cor = AjusteDeDestaque.Ler(p.CorDeDestaque);
        var destaqueMudou = !string.Equals(_destaqueAplicado, p.CorDeDestaque, StringComparison.OrdinalIgnoreCase);
        destaque.Cor = cor;
        _destaqueAplicado = p.CorDeDestaque;

        _trocando = true;
        try
        {
            if (p.Modo is not null && PedeEscuro(p) != tema.Escolhido.Escuro) tema.Trocar(catalogo.ParDe(tema.Escolhido).Id, animar);
            else if (destaqueMudou) tema.Reaplicar(animar);
        }
        finally
        {
            _trocando = false;
        }

        mensageiro.Send(new AparenciaMudou(p));
    }

    // Ctrl+T ou "Tema: ..." na paleta: se há um modo escolhido, ele passa a ser o lado que o usuário acabou de pedir.
    void AoTrocarDeTema()
    {
        if (_trocando || preferencias.Valor.Modo is null) return;
        var modo = tema.Escolhido.Escuro ? ModoDeTema.Escuro : ModoDeTema.Claro;
        if (preferencias.Valor.Modo != ModoDeTema.Sistema || PedeEscuro(preferencias.Valor) != tema.Escolhido.Escuro)
            preferencias.Alterar(p => p with { Modo = modo });
    }
}
