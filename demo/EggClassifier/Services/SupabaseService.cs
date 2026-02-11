using System.IO;
using Newtonsoft.Json.Linq;
using Supabase;

namespace EggClassifier.Services;

/// <summary>
/// Supabase 클라이언트 관리 서비스 (Singleton)
/// </summary>
public class SupabaseService
{
    private Client? _client;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <summary>
    /// Supabase 클라이언트 초기화 및 반환
    /// </summary>
    public async Task<Client> GetClientAsync()
    {
        if (_client != null)
            return _client;

        await _initLock.WaitAsync();
        try
        {
            if (_client != null)
                return _client;

            // appsettings.json에서 설정 로드
            var appSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (!File.Exists(appSettingsPath))
            {
                throw new FileNotFoundException("appsettings.json 파일을 찾을 수 없습니다.");
            }

            var json = await File.ReadAllTextAsync(appSettingsPath);
            var config = JObject.Parse(json);

            var url = config["Supabase"]?["Url"]?.ToString();
            var key = config["Supabase"]?["Key"]?.ToString();

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key))
            {
                throw new InvalidOperationException("Supabase URL 또는 Key가 설정되지 않았습니다.");
            }

            // Supabase 클라이언트 초기화
            var options = new SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = false
            };

            _client = new Client(url, key, options);
            await _client.InitializeAsync();

            return _client;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
