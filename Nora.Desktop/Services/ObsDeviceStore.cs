using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nora.Desktop.Services;

/// <summary>Credencial do computador guardada neste computador: o segredo nunca sai daqui, a API só guarda o hash.</summary>
public sealed record StoredDevice(string Endpoint, Guid DeviceId, string Secret);

/// <summary>Sessão guardada pelo "Manter conectado": só vale para o mesmo endereço de API e até o vencimento.</summary>
public sealed record StoredSession(string Endpoint, string Token, DateTimeOffset ExpiresAt);

/// <summary>Armazenamento local do computador do OBS (separado para o teste usar uma pasta própria).</summary>
public interface IObsDeviceStore
{
    /// <summary>Último aviso de arquivo ilegível ou de gravação que falhou; lido e limpo por quem mostra a mensagem.</summary>
    string? TakeWarning();
    /// <summary>A última leitura da ativação falhou por E/S transitória (arquivo em uso, acesso negado): o arquivo foi mantido, a ativação segue valendo e a leitura pode ser repetida.</summary>
    bool DeviceReadUnavailable { get; }
    StoredDevice? ReadDevice();
    bool SaveDevice(StoredDevice device);
    void DeleteDevice();
    StoredSession? ReadSession();
    bool SaveSession(StoredSession session);
    void DeleteSession();
    /// <summary>O OBS identifica e ativa o computador (recurso guardado e desligado por padrão). Desligado, o armazenamento serve só para a sessão "manter conectado".</summary>
    bool ComputersEnabled => false;
    StoredDevice? ReadEnrollment(string endpoint) => ReadDevice();
    bool SaveEnrollment(StoredDevice device) => SaveDevice(device);
}

/// <summary>
/// Arquivos em <c>%LOCALAPPDATA%\NORA\OBS\</c>: <c>device.dat</c> e <c>session.dat</c>, ambos protegidos pelo DPAPI do usuário do Windows (nada é gravado em texto).
/// Arquivo ilegível ou corrompido (cifra, JSON ou campos inválidos) é apagado e tratado como ausente, com um aviso para a faixa de mensagens. Falha de E/S (arquivo em uso por antivírus ou indexador, acesso negado) não apaga nada: avisa "não foi possível ler agora" e a leitura é repetida depois.
/// </summary>
public sealed class ObsDeviceStore : IObsDeviceStore
{
    private static readonly byte[] DeviceEntropy = Encoding.UTF8.GetBytes("NORA.OBS.device.v1");
    private static readonly byte[] SessionEntropy = Encoding.UTF8.GetBytes("NORA.OBS.session.v1");
    private readonly string folder;
    private string? warning;

    /// <param name="computersEnabled">Liga a identificação e a ativação do computador do OBS (padrão desligado, decisão de 24/11/2026). Desligado, só a sessão guardada é usada.</param>
    public ObsDeviceStore(string? folder = null, bool computersEnabled = false)
    {
        this.folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NORA", "OBS");
        ComputersEnabled = computersEnabled;
    }

    public bool ComputersEnabled { get; }

    public string DevicePath => Path.Combine(folder, "device.dat");
    public string SessionPath => Path.Combine(folder, "session.dat");

    public string? TakeWarning() { var text = warning; warning = null; return text; }

    public bool DeviceReadUnavailable { get; private set; }

    public StoredDevice? ReadDevice()
    {
        var device = Read<StoredDevice>(DevicePath, DeviceEntropy, "O arquivo que guarda a ativação deste computador estava ilegível e foi descartado; peça um novo código de ativação.",
            device => device.DeviceId != Guid.Empty && !string.IsNullOrWhiteSpace(device.Secret) && !string.IsNullOrWhiteSpace(device.Endpoint), out var unavailable);
        DeviceReadUnavailable = unavailable;
        return device;
    }
    // Gravar ou apagar resolve a dúvida de uma leitura que falhou por E/S: o estado do arquivo passa a ser o que acabou de ser escrito.
    public bool SaveDevice(StoredDevice device) { DeviceReadUnavailable = false; return Write(DevicePath, DeviceEntropy, device, "Não foi possível guardar a ativação neste computador."); }
    public void DeleteDevice() { DeviceReadUnavailable = false; Delete(DevicePath); }

    public StoredSession? ReadSession() => Read<StoredSession>(SessionPath, SessionEntropy, "A sessão salva neste computador estava ilegível e foi descartada; entre com a senha.",
        session => !string.IsNullOrWhiteSpace(session.Token) && !string.IsNullOrWhiteSpace(session.Endpoint), out _);
    public bool SaveSession(StoredSession session) => Write(SessionPath, SessionEntropy, session, "Não foi possível guardar a sessão neste computador; ela vale só até fechar o aplicativo.");
    public void DeleteSession() => Delete(SessionPath);

    public StoredDevice? ReadEnrollment(string endpoint)
    {
        var device = Read<StoredDevice>(EnrollmentPath(endpoint), DeviceEntropy, "Não foi possível restaurar a identificação deste OBS.",
            item => item.DeviceId != Guid.Empty && item.Secret?.Length == 64 && item.Endpoint == endpoint, out var unavailable);
        DeviceReadUnavailable = unavailable;
        return device;
    }

    public bool SaveEnrollment(StoredDevice device) =>
        Write(EnrollmentPath(device.Endpoint), DeviceEntropy, device, "Não foi possível guardar a identificação deste OBS.", overwrite: false);

    private string EnrollmentPath(string endpoint) =>
        Path.Combine(folder, "installation-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint))) + ".dat");

    private T? Read<T>(string path, byte[] entropy, string unreadable, Func<T, bool> valid, out bool unavailable) where T : class
    {
        unavailable = false;
        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), entropy, DataProtectionScope.CurrentUser);
            var value = JsonSerializer.Deserialize<T>(plain);
            if (value is not null && valid(value)) return value;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return null; }
        // Falha de E/S não prova que o arquivo está ruim: mantém o arquivo, avisa e deixa a leitura ser repetida.
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning("NORA: não foi possível ler {0} agora: {1}", Path.GetFileName(path), error.GetType().Name);
            warning = "Não foi possível ler os dados guardados neste computador agora. Tente de novo em instantes.";
            unavailable = true;
            return null;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or NotSupportedException)
        {
            System.Diagnostics.Trace.TraceWarning("NORA: não foi possível ler {0}: {1}", Path.GetFileName(path), error.GetType().Name);
        }
        // Ilegível, de outro usuário do Windows ou sem os campos obrigatórios: o arquivo é apagado e o aviso explica.
        warning = unreadable;
        Delete(path);
        return null;
    }

    private bool Write<T>(string path, byte[] entropy, T value, string failure, bool overwrite = true)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(folder);
            var protectedBytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(value), entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(temporary, protectedBytes);
            File.Move(temporary, path, overwrite);
            return true;
        }
        catch (Exception error) when (error is CryptographicException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            warning = failure;
            System.Diagnostics.Trace.TraceWarning("NORA: falha ao gravar {0}: {1}", Path.GetFileName(path), error.GetType().Name);
            return false;
        }
        finally { Delete(temporary); }
    }

    private void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            warning ??= "Não foi possível apagar um arquivo guardado neste computador. Feche o aplicativo e apague a pasta NORA\\OBS em %LOCALAPPDATA% se o problema continuar.";
            System.Diagnostics.Trace.TraceWarning("NORA: falha ao apagar {0}: {1}", Path.GetFileName(path), error.GetType().Name);
        }
    }
}
