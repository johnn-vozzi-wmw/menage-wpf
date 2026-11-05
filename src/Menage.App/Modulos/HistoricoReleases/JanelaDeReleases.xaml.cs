using Menage.App.Controles;

namespace Menage.App.Modulos.HistoricoReleases;

/// <summary>Linha do tempo das betas e tags. Só liga a tela ao <see cref="JanelaDeReleasesViewModel"/>; ela não altera nada nos projetos.</summary>
public partial class JanelaDeReleases : JanelaBase
{
    public const string Id = "mais.historico";

    public JanelaDeReleases(ContextoDeJanela contexto, JanelaDeReleasesViewModel modelo) : base(contexto)
    {
        Modelo = modelo;
        DataContext = modelo;
        FechaComEsc = true;
        InitializeComponent();
        Loaded += async (_, _) => await modelo.CarregarAsync();
    }

    public JanelaDeReleasesViewModel Modelo { get; }
}
