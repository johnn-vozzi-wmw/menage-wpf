using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Menage.App.Modulos.Personalizacao;

/// <summary>O Windows está no modo escuro? Fonte do modo "Sistema". Separado para o serviço ser testado sem registro.</summary>
public interface IDetectorDoSistema
{
    bool Escuro { get; }

    /// <summary>Disparado na thread da tela quando o usuário muda o tema do Windows.</summary>
    event Action? Mudou;
}

public sealed class DetectorDoWindows : IDetectorDoSistema, IDisposable
{
    const string Chave = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    bool _ligado;
    Action? _mudou;

    public bool Escuro
    {
        get
        {
            try
            {
                using var chave = Registry.CurrentUser.OpenSubKey(Chave);
                return chave?.GetValue("AppsUseLightTheme") is int claro && claro == 0;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return true;
            }
        }
    }

    public event Action? Mudou
    {
        add
        {
            _mudou += value;
            if (_ligado) return;
            SystemEvents.UserPreferenceChanged += AoMudar;
            _ligado = true;
        }
        remove => _mudou -= value;
    }

    public void Dispose()
    {
        if (_ligado) SystemEvents.UserPreferenceChanged -= AoMudar;
        _ligado = false;
    }

    void AoMudar(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)) return;
        Application.Current?.Dispatcher.BeginInvoke(() => _mudou?.Invoke());
    }
}
